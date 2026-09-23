import { RowDataPacket } from "mysql2";
import { query } from "@/lib/db";
import { storeFilter, placeholders } from "@/lib/auth";
import type { SessionUser } from "@/lib/session";
import {
  addDays,
  dayRange,
  fyRange,
  monthRange,
  parseYmd,
  toYmd,
  type DateRange,
} from "@/lib/periods";

export type StoreUser = Pick<SessionUser, "storeIds" | "selectedStoreId" | "tenantId">;

export function storeScope(user: StoreUser) {
  const ids = storeFilter(user);
  if (ids.length === 0) return { ids: [] as string[], ph: "NULL" as const };
  return { ids, ph: placeholders(ids.length) };
}

/** Inclusive calendar range → half-open datetime bounds (index-friendly). */
function rangeBounds(range: DateRange) {
  const fromDt = `${range.from} 00:00:00`;
  const toExclusive = toYmd(addDays(parseYmd(range.to), 1)) + " 00:00:00";
  return { fromDt, toExclusive };
}

export async function shopNames(ids: string[], ph: string) {
  if (ids.length === 0) return new Map<string, string>();
  const rows = await query<RowDataPacket[]>(
    `SELECT ts.store_id,
            COALESCE(NULLIF(ts.display_name,''), ts.store_id) AS shop_name
     FROM tenant_stores ts
     WHERE ts.store_id IN (${ph})`,
    ids
  );
  const map = new Map(rows.map((r) => [String(r.store_id), String(r.shop_name)]));
  for (const id of ids) if (!map.has(id)) map.set(id, id);
  return map;
}

export type PeriodMetrics = {
  sale: number;
  saleBills: number;
  saleCost: number;
  grossMargin: number;
  gst: number;
  purchase: number;
  purchaseReturns: number;
  purchaseNet: number;
  collection: number;
  supplierPayment: number;
  expense: number;
  stockCost: number;
  stockMrp: number;
  expired: number;
  nearExpiry: number;
  nearExpiryCost: number;
};

const emptyMetrics = (): PeriodMetrics => ({
  sale: 0,
  saleBills: 0,
  saleCost: 0,
  grossMargin: 0,
  gst: 0,
  purchase: 0,
  purchaseReturns: 0,
  purchaseNet: 0,
  collection: 0,
  supplierPayment: 0,
  expense: 0,
  stockCost: 0,
  stockMrp: 0,
  expired: 0,
  nearExpiry: 0,
  nearExpiryCost: 0,
});

async function stockSnapshot(ids: string[], ph: string) {
  const [stock] = await query<RowDataPacket[]>(
    `SELECT
       COALESCE(SUM(quantity_available * purchase_price),0) AS cost_value,
       COALESCE(SUM(quantity_available * mrp),0) AS mrp_value,
       SUM(CASE WHEN expiry_date IS NOT NULL AND expiry_date < CURDATE()
                 AND quantity_available > 0 THEN 1 ELSE 0 END) AS expired,
       SUM(CASE WHEN expiry_date IS NOT NULL AND expiry_date >= CURDATE()
                 AND expiry_date <= DATE_ADD(CURDATE(), INTERVAL 90 DAY)
                 AND quantity_available > 0 THEN 1 ELSE 0 END) AS near_expiry,
       COALESCE(SUM(CASE WHEN expiry_date IS NOT NULL AND expiry_date >= CURDATE()
                 AND expiry_date <= DATE_ADD(CURDATE(), INTERVAL 90 DAY)
                 AND quantity_available > 0
                 THEN quantity_available * purchase_price ELSE 0 END),0) AS near_cost
     FROM medicine_batches
     WHERE store_id IN (${ph}) AND is_deleted=0`,
    ids
  );
  return {
    stockCost: Number(stock?.cost_value ?? 0),
    stockMrp: Number(stock?.mrp_value ?? 0),
    expired: Number(stock?.expired ?? 0),
    nearExpiry: Number(stock?.near_expiry ?? 0),
    nearExpiryCost: Number(stock?.near_cost ?? 0),
  };
}

export async function metricsForRange(
  user: StoreUser,
  range: DateRange,
  opts?: { includeStock?: boolean }
): Promise<PeriodMetrics> {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return emptyMetrics();
  const { fromDt, toExclusive } = rangeBounds(range);
  const params = [...ids, fromDt, toExclusive];
  const includeStock = opts?.includeStock !== false;

  const expenseParams = [...ids, range.from, toYmd(addDays(parseYmd(range.to), 1))];

  const [saleRows, purchaseRows, returnRows, expenseRows, stock] = await Promise.all([
    query<RowDataPacket[]>(
      `SELECT COALESCE(SUM(grand_total),0) AS amt,
              COUNT(*) AS bills,
              COALESCE(SUM(cgst_amount+sgst_amount+igst_amount),0) AS gst,
              COALESCE(SUM(cogs),0) AS cost,
              COALESCE(SUM(paid_amount),0) AS collected
       FROM sales
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND invoice_date >= ? AND invoice_date < ?`,
      params
    ).catch(() =>
      query<RowDataPacket[]>(
        `SELECT COALESCE(SUM(grand_total),0) AS amt,
                COUNT(*) AS bills,
                COALESCE(SUM(cgst_amount+sgst_amount+igst_amount),0) AS gst,
                0 AS cost,
                COALESCE(SUM(paid_amount),0) AS collected
         FROM sales
         WHERE store_id IN (${ph}) AND is_deleted=0
           AND invoice_date >= ? AND invoice_date < ?`,
        params
      )
    ),
    query<RowDataPacket[]>(
      `SELECT COALESCE(SUM(grand_total),0) AS amt,
              COALESCE(SUM(paid_amount),0) AS paid
       FROM purchases
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND invoice_date >= ? AND invoice_date < ?`,
      params
    ),
    query<RowDataPacket[]>(
      `SELECT COALESCE(SUM(grand_total),0) AS amt
       FROM purchase_returns
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND return_date >= ? AND return_date < ?`,
      params
    ).catch(() => [] as RowDataPacket[]),
    query<RowDataPacket[]>(
      `SELECT COALESCE(SUM(amount),0) AS amt
       FROM expenses
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND expense_date >= ? AND expense_date < ?`,
      expenseParams
    ).catch(() => [] as RowDataPacket[]),
    includeStock
      ? stockSnapshot(ids, ph)
      : Promise.resolve({
          stockCost: 0,
          stockMrp: 0,
          expired: 0,
          nearExpiry: 0,
          nearExpiryCost: 0,
        }),
  ]);

  const sale = saleRows[0];
  const purchase = purchaseRows[0];
  const purchaseReturns = Number(returnRows[0]?.amt ?? 0);
  const expense = Number(expenseRows[0]?.amt ?? 0);

  const saleAmt = Number(sale?.amt ?? 0);
  const saleCost = Number(sale?.cost ?? 0);
  const purchaseAmt = Number(purchase?.amt ?? 0);
  const supplierPayment = Number(purchase?.paid ?? 0);

  return {
    sale: saleAmt,
    saleBills: Number(sale?.bills ?? 0),
    saleCost,
    grossMargin: saleAmt - saleCost,
    gst: Number(sale?.gst ?? 0),
    purchase: purchaseAmt,
    purchaseReturns,
    purchaseNet: purchaseAmt - purchaseReturns,
    collection: Number(sale?.collected ?? 0),
    supplierPayment,
    expense,
    ...stock,
  };
}

export async function paymodeForRange(user: StoreUser, range: DateRange) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const { fromDt, toExclusive } = rangeBounds(range);
  const rows = await query<RowDataPacket[]>(
    `SELECT p.method,
            COUNT(*) AS bills,
            COALESCE(SUM(p.amount),0) AS amount
     FROM sale_payments p
     WHERE p.store_id IN (${ph}) AND p.is_deleted=0
       AND p.payment_date_utc >= ? AND p.payment_date_utc < ?
     GROUP BY p.method
     ORDER BY amount DESC`,
    [...ids, fromDt, toExclusive]
  );
  return rows.map((r) => ({
    method: Number(r.method),
    bills: Number(r.bills),
    amount: Number(r.amount),
  }));
}

export async function monthlySalesPurchase(user: StoreUser, months = 12) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [] as { month: string; sales: number; purchases: number }[];
  const from = toYmd(addDays(new Date(), -Math.max(months, 1) * 31));
  const [sales, purchases] = await Promise.all([
    query<RowDataPacket[]>(
      `SELECT DATE_FORMAT(invoice_date,'%Y-%m') AS ym,
              COALESCE(SUM(grand_total),0) AS amt
       FROM sales
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND invoice_date >= ?
       GROUP BY ym ORDER BY ym`,
      [...ids, from]
    ),
    query<RowDataPacket[]>(
      `SELECT DATE_FORMAT(invoice_date,'%Y-%m') AS ym,
              COALESCE(SUM(grand_total),0) AS amt
       FROM purchases
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND invoice_date >= ?
       GROUP BY ym ORDER BY ym`,
      [...ids, from]
    ),
  ]);
  const map = new Map<string, { month: string; sales: number; purchases: number }>();
  for (const r of sales) {
    map.set(String(r.ym), { month: String(r.ym), sales: Number(r.amt), purchases: 0 });
  }
  for (const r of purchases) {
    const cur = map.get(String(r.ym)) || {
      month: String(r.ym),
      sales: 0,
      purchases: 0,
    };
    cur.purchases = Number(r.amt);
    map.set(String(r.ym), cur);
  }
  return [...map.values()].sort((a, b) => a.month.localeCompare(b.month));
}

export async function vendorOutstanding(user: StoreUser) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return 0;
  try {
    const [r] = await query<RowDataPacket[]>(
      `SELECT COALESCE(SUM(outstanding_balance),0) AS amt
       FROM suppliers
       WHERE store_id IN (${ph}) AND is_deleted=0 AND outstanding_balance > 0`,
      ids
    );
    return Number(r?.amt ?? 0);
  } catch {
    const [r] = await query<RowDataPacket[]>(
      `SELECT COALESCE(SUM(GREATEST(grand_total - paid_amount,0)),0) AS amt
       FROM purchases
       WHERE store_id IN (${ph}) AND is_deleted=0`,
      ids
    );
    return Number(r?.amt ?? 0);
  }
}

export async function shopComparison(user: StoreUser, range: DateRange) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const names = await shopNames(ids, ph);
  const { fromDt, toExclusive } = rangeBounds(range);
  const sales = await query<RowDataPacket[]>(
    `SELECT store_id,
            COALESCE(SUM(grand_total),0) AS revenue,
            COALESCE(SUM(cogs),0) AS cost,
            COUNT(*) AS bills
     FROM sales
     WHERE store_id IN (${ph}) AND is_deleted=0
       AND invoice_date >= ? AND invoice_date < ?
     GROUP BY store_id`,
    [...ids, fromDt, toExclusive]
  );
  const saleMap = new Map(
    sales.map((s) => [
      String(s.store_id),
      { revenue: Number(s.revenue), cost: Number(s.cost), bills: Number(s.bills) },
    ])
  );

  return ids.map((id) => {
    const s = saleMap.get(id) || { revenue: 0, cost: 0, bills: 0 };
    const margin = s.revenue - s.cost;
    return {
      storeId: id,
      shopName: names.get(id) || id,
      revenue: s.revenue,
      margin,
      marginPct: s.revenue > 0 ? Math.round((margin / s.revenue) * 1000) / 10 : 0,
      bills: s.bills,
      avgBill: s.bills > 0 ? s.revenue / s.bills : 0,
      avgBillMargin: s.bills > 0 ? margin / s.bills : 0,
    };
  });
}

export async function topSaleItems(user: StoreUser, range: DateRange, limit = 20) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const { fromDt, toExclusive } = rangeBounds(range);
  return query<RowDataPacket[]>(
    `SELECT si.store_id, si.medicine_local_id,
            COALESCE(m.name,'Item') AS item_name,
            COALESCE(m.brand,'') AS company,
            COALESCE(SUM(si.quantity),0) AS qty,
            COALESCE(SUM(si.line_total),0) AS revenue,
            COUNT(*) AS bills
     FROM sales s
     INNER JOIN sale_items si
       ON si.store_id=s.store_id AND si.sale_local_id=s.local_id AND si.is_deleted=0
     LEFT JOIN medicines m ON m.store_id=si.store_id AND m.local_id=si.medicine_local_id
     WHERE s.store_id IN (${ph}) AND s.is_deleted=0
       AND s.invoice_date >= ? AND s.invoice_date < ?
     GROUP BY si.store_id, si.medicine_local_id, item_name, company
     ORDER BY revenue DESC
     LIMIT ${limit}`,
    [...ids, fromDt, toExclusive]
  );
}

export async function salesByCompany(user: StoreUser, range: DateRange, limit = 10) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const { fromDt, toExclusive } = rangeBounds(range);
  return query<RowDataPacket[]>(
    `SELECT COALESCE(NULLIF(m.brand,''),'Unknown') AS company,
            COALESCE(SUM(si.line_total),0) AS revenue,
            COALESCE(SUM(si.quantity),0) AS qty
     FROM sales s
     INNER JOIN sale_items si
       ON si.store_id=s.store_id AND si.sale_local_id=s.local_id AND si.is_deleted=0
     LEFT JOIN medicines m ON m.store_id=si.store_id AND m.local_id=si.medicine_local_id
     WHERE s.store_id IN (${ph}) AND s.is_deleted=0
       AND s.invoice_date >= ? AND s.invoice_date < ?
     GROUP BY company
     ORDER BY revenue DESC
     LIMIT ${limit}`,
    [...ids, fromDt, toExclusive]
  );
}

export async function topSuppliers(user: StoreUser, limit = 15, range?: DateRange) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const bounds = range ? rangeBounds(range) : null;
  return query<RowDataPacket[]>(
    `SELECT COALESCE(s.name, CONCAT('Supplier #', p.supplier_local_id)) AS supplier,
            COUNT(*) AS bills,
            COALESCE(SUM(p.grand_total),0) AS amount
     FROM purchases p
     LEFT JOIN suppliers s
       ON s.store_id=p.store_id AND s.local_id=p.supplier_local_id AND s.is_deleted=0
     WHERE p.store_id IN (${ph}) AND p.is_deleted=0
       ${bounds ? "AND p.invoice_date >= ? AND p.invoice_date < ?" : ""}
     GROUP BY supplier
     ORDER BY amount DESC
     LIMIT ${limit}`,
    bounds ? [...ids, bounds.fromDt, bounds.toExclusive] : ids
  );
}

export async function topPurchasedItems(user: StoreUser, limit = 20, range?: DateRange) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const bounds = range ? rangeBounds(range) : null;
  return query<RowDataPacket[]>(
    `SELECT pi.store_id, pi.medicine_local_id,
            COALESCE(m.name,'Item') AS item_name,
            COALESCE(m.brand,'') AS company,
            COALESCE(SUM(pi.quantity),0) AS qty,
            COALESCE(SUM(pi.line_total),0) AS amount,
            COALESCE(AVG(pi.purchase_price),0) AS avg_rate,
            COALESCE(AVG(pi.mrp),0) AS avg_mrp
     FROM purchase_items pi
     JOIN purchases p ON p.store_id=pi.store_id AND p.local_id=pi.purchase_local_id AND p.is_deleted=0
     LEFT JOIN medicines m ON m.store_id=pi.store_id AND m.local_id=pi.medicine_local_id
     WHERE pi.store_id IN (${ph}) AND pi.is_deleted=0
       ${bounds ? "AND p.invoice_date >= ? AND p.invoice_date < ?" : ""}
     GROUP BY pi.store_id, pi.medicine_local_id, item_name, company
     ORDER BY amount DESC
     LIMIT ${limit}`,
    bounds ? [...ids, bounds.fromDt, bounds.toExclusive] : ids
  );
}

export async function gstBySlab(user: StoreUser, range: DateRange) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const { fromDt, toExclusive } = rangeBounds(range);
  return query<RowDataPacket[]>(
    `SELECT ROUND(si.gst_percent,0) AS slab,
            COALESCE(SUM(si.taxable_amount),0) AS taxable,
            COALESCE(SUM(si.tax_amount),0) AS gst
     FROM sales s
     INNER JOIN sale_items si
       ON si.store_id=s.store_id AND si.sale_local_id=s.local_id AND si.is_deleted=0
     WHERE s.store_id IN (${ph}) AND s.is_deleted=0
       AND s.invoice_date >= ? AND s.invoice_date < ?
     GROUP BY slab
     ORDER BY slab`,
    [...ids, fromDt, toExclusive]
  );
}

export async function purchaseReturnsMonthly(user: StoreUser, months = 12) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  try {
    const from = toYmd(addDays(new Date(), -Math.max(months, 1) * 31));
    return await query<RowDataPacket[]>(
      `SELECT DATE_FORMAT(return_date,'%Y-%m') AS ym,
              COALESCE(SUM(grand_total),0) AS amt
       FROM purchase_returns
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND return_date >= ?
       GROUP BY ym ORDER BY ym`,
      [...ids, from]
    );
  } catch {
    return [];
  }
}

export async function stockStoreTotals(user: StoreUser) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const names = await shopNames(ids, ph);
  const rows = await query<RowDataPacket[]>(
    `SELECT store_id,
            COALESCE(SUM(quantity_available * purchase_price),0) AS cost_value,
            SUM(CASE WHEN expiry_date < CURDATE() AND quantity_available > 0 THEN 1 ELSE 0 END) AS expired_cnt,
            COALESCE(SUM(CASE WHEN expiry_date < CURDATE() AND quantity_available > 0
              THEN quantity_available * purchase_price ELSE 0 END),0) AS expired_cost,
            SUM(CASE WHEN expiry_date >= CURDATE() AND expiry_date <= DATE_ADD(CURDATE(), INTERVAL 30 DAY)
              AND quantity_available > 0 THEN 1 ELSE 0 END) AS near_1m,
            COALESCE(SUM(CASE WHEN expiry_date >= CURDATE() AND expiry_date <= DATE_ADD(CURDATE(), INTERVAL 30 DAY)
              AND quantity_available > 0 THEN quantity_available * purchase_price ELSE 0 END),0) AS near_1m_cost,
            SUM(CASE WHEN expiry_date > DATE_ADD(CURDATE(), INTERVAL 30 DAY)
              AND expiry_date <= DATE_ADD(CURDATE(), INTERVAL 90 DAY)
              AND quantity_available > 0 THEN 1 ELSE 0 END) AS near_3m,
            COALESCE(SUM(CASE WHEN expiry_date > DATE_ADD(CURDATE(), INTERVAL 30 DAY)
              AND expiry_date <= DATE_ADD(CURDATE(), INTERVAL 90 DAY)
              AND quantity_available > 0 THEN quantity_available * purchase_price ELSE 0 END),0) AS near_3m_cost,
            SUM(CASE WHEN expiry_date > DATE_ADD(CURDATE(), INTERVAL 90 DAY)
              AND expiry_date <= DATE_ADD(CURDATE(), INTERVAL 180 DAY)
              AND quantity_available > 0 THEN 1 ELSE 0 END) AS near_6m,
            COALESCE(SUM(CASE WHEN expiry_date > DATE_ADD(CURDATE(), INTERVAL 90 DAY)
              AND expiry_date <= DATE_ADD(CURDATE(), INTERVAL 180 DAY)
              AND quantity_available > 0 THEN quantity_available * purchase_price ELSE 0 END),0) AS near_6m_cost,
            SUM(CASE WHEN expiry_date > DATE_ADD(CURDATE(), INTERVAL 180 DAY)
              AND expiry_date <= DATE_ADD(CURDATE(), INTERVAL 365 DAY)
              AND quantity_available > 0 THEN 1 ELSE 0 END) AS near_12m,
            COALESCE(SUM(CASE WHEN expiry_date > DATE_ADD(CURDATE(), INTERVAL 180 DAY)
              AND expiry_date <= DATE_ADD(CURDATE(), INTERVAL 365 DAY)
              AND quantity_available > 0 THEN quantity_available * purchase_price ELSE 0 END),0) AS near_12m_cost
     FROM medicine_batches
     WHERE store_id IN (${ph}) AND is_deleted=0
     GROUP BY store_id`,
    ids
  );
  return rows.map((r) => ({
    storeId: String(r.store_id),
    shopName: names.get(String(r.store_id)) || String(r.store_id),
    costValue: Number(r.cost_value),
    expiredCnt: Number(r.expired_cnt),
    expiredCost: Number(r.expired_cost),
    near1m: Number(r.near_1m),
    near1mCost: Number(r.near_1m_cost),
    near3m: Number(r.near_3m),
    near3mCost: Number(r.near_3m_cost),
    near6m: Number(r.near_6m),
    near6mCost: Number(r.near_6m_cost),
    near12m: Number(r.near_12m),
    near12mCost: Number(r.near_12m_cost),
  }));
}

export type ExpiryBucket = "expired" | "1m" | "3m" | "6m";

export async function expiryItems(user: StoreUser, bucket: ExpiryBucket, limit = 500) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  let cond = "b.expiry_date < CURDATE()";
  if (bucket === "1m") {
    cond =
      "b.expiry_date >= CURDATE() AND b.expiry_date <= DATE_ADD(CURDATE(), INTERVAL 30 DAY)";
  } else if (bucket === "3m") {
    cond =
      "b.expiry_date > DATE_ADD(CURDATE(), INTERVAL 30 DAY) AND b.expiry_date <= DATE_ADD(CURDATE(), INTERVAL 90 DAY)";
  } else if (bucket === "6m") {
    cond =
      "b.expiry_date > DATE_ADD(CURDATE(), INTERVAL 90 DAY) AND b.expiry_date <= DATE_ADD(CURDATE(), INTERVAL 180 DAY)";
  }
  return query<RowDataPacket[]>(
    `SELECT b.store_id, b.medicine_local_id, b.local_id AS batch_local_id,
            COALESCE(m.name,'Item') AS item_name,
            COALESCE(m.brand,'') AS company,
            b.quantity_available AS qty,
            b.mrp,
            (b.quantity_available * b.purchase_price) AS cost_value,
            b.expiry_date, b.batch_number
     FROM medicine_batches b
     LEFT JOIN medicines m ON m.store_id=b.store_id AND m.local_id=b.medicine_local_id
     WHERE b.store_id IN (${ph}) AND b.is_deleted=0 AND b.quantity_available > 0
       AND b.expiry_date IS NOT NULL AND ${cond}
     ORDER BY b.expiry_date ASC
     LIMIT ${limit}`,
    ids
  );
}

export async function billsPaymentsSummary(user: StoreUser) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) {
    return {
      bills: 0,
      billAmount: 0,
      paid: 0,
      balance: 0,
      byVendor: [] as RowDataPacket[],
      byShop: [] as {
        store_id: string;
        shop_name: string;
        vendors: number;
        bills: number;
        balance: number;
      }[],
      billsList: [] as RowDataPacket[],
    };
  }
  const names = await shopNames(ids, ph);
  const [sumRows, byVendor, byShopRaw, billsList] = await Promise.all([
    query<RowDataPacket[]>(
      `SELECT COUNT(*) AS bills,
              COALESCE(SUM(grand_total),0) AS bill_amount,
              COALESCE(SUM(paid_amount),0) AS paid,
              COALESCE(SUM(GREATEST(grand_total-paid_amount,0)),0) AS balance
       FROM purchases
       WHERE store_id IN (${ph}) AND is_deleted=0`,
      ids
    ),
    query<RowDataPacket[]>(
      `SELECT COALESCE(s.name, CONCAT('Supplier #', p.supplier_local_id)) AS vendor,
              COALESCE(s.local_id, p.supplier_local_id) AS code,
              COUNT(*) AS bills,
              COUNT(DISTINCT p.store_id) AS shops,
              COALESCE(SUM(GREATEST(p.grand_total-p.paid_amount,0)),0) AS balance
       FROM purchases p
       LEFT JOIN suppliers s
         ON s.store_id=p.store_id AND s.local_id=p.supplier_local_id AND s.is_deleted=0
       WHERE p.store_id IN (${ph}) AND p.is_deleted=0
         AND p.grand_total > p.paid_amount
       GROUP BY vendor, code
       ORDER BY balance DESC
       LIMIT 50`,
      ids
    ).catch(() => [] as RowDataPacket[]),
    query<RowDataPacket[]>(
      `SELECT store_id,
              COUNT(DISTINCT supplier_local_id) AS vendors,
              COUNT(*) AS bills,
              COALESCE(SUM(GREATEST(grand_total-paid_amount,0)),0) AS balance
       FROM purchases
       WHERE store_id IN (${ph}) AND is_deleted=0
       GROUP BY store_id
       ORDER BY balance DESC`,
      ids
    ),
    query<RowDataPacket[]>(
      `SELECT p.store_id, p.local_id, p.invoice_number, p.invoice_date,
              p.supplier_invoice_number AS supplier_bill_number, p.grand_total, p.paid_amount,
              GREATEST(p.grand_total-p.paid_amount,0) AS balance,
              CASE
                WHEN p.paid_amount <= 0 THEN 'unpaid'
                WHEN p.paid_amount >= p.grand_total THEN 'paid'
                ELSE 'partial'
              END AS status,
              COALESCE(s.name, CONCAT('Supplier #', p.supplier_local_id)) AS vendor
       FROM purchases p
       LEFT JOIN suppliers s
         ON s.store_id=p.store_id AND s.local_id=p.supplier_local_id AND s.is_deleted=0
       WHERE p.store_id IN (${ph}) AND p.is_deleted=0
       ORDER BY p.invoice_date DESC
       LIMIT 100`,
      ids
    ),
  ]);
  const sum = sumRows[0];
  const byShop = byShopRaw.map((r) => ({
    store_id: String(r.store_id),
    shop_name: names.get(String(r.store_id)) || String(r.store_id),
    vendors: Number(r.vendors),
    bills: Number(r.bills),
    balance: Number(r.balance),
  }));

  return {
    bills: Number(sum?.bills ?? 0),
    billAmount: Number(sum?.bill_amount ?? 0),
    paid: Number(sum?.paid ?? 0),
    balance: Number(sum?.balance ?? 0),
    byVendor,
    byShop,
    billsList,
  };
}

/** One-shot grouped series for Ledger / P&L / Net Business (avoids N× full scans). */
export async function financeSeries(
  user: StoreUser,
  mode: "daily" | "monthly" | "yearly",
  count: number,
  endOffset = 0
) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];

  const periods: DateRange[] = [];
  for (let i = count - 1; i >= 0; i--) {
    const offset = endOffset - i;
    periods.push(
      mode === "yearly"
        ? fyRange(offset)
        : mode === "monthly"
          ? monthRange(offset)
          : dayRange(offset)
    );
  }
  if (periods.length === 0) return [];

  const fromDt = `${periods[0].from} 00:00:00`;
  const toExclusive =
    toYmd(addDays(parseYmd(periods[periods.length - 1].to), 1)) + " 00:00:00";
  const fmt =
    mode === "yearly"
      ? `CONCAT('FY ', IF(MONTH(invoice_date)>=4, YEAR(invoice_date), YEAR(invoice_date)-1))`
      : mode === "monthly"
        ? `DATE_FORMAT(invoice_date,'%Y-%m')`
        : `DATE(invoice_date)`;

  const [salesRows, purchaseRows, expenseRows, returnRows, stock] = await Promise.all([
    query<RowDataPacket[]>(
      `SELECT ${fmt} AS bucket,
              COALESCE(SUM(grand_total),0) AS sale,
              COUNT(*) AS bills,
              COALESCE(SUM(cgst_amount+sgst_amount+igst_amount),0) AS gst,
              COALESCE(SUM(cogs),0) AS cost,
              COALESCE(SUM(paid_amount),0) AS collection
       FROM sales
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND invoice_date >= ? AND invoice_date < ?
       GROUP BY bucket`,
      [...ids, fromDt, toExclusive]
    ),
    query<RowDataPacket[]>(
      `SELECT ${fmt} AS bucket,
              COALESCE(SUM(grand_total),0) AS purchase,
              COALESCE(SUM(paid_amount),0) AS paid
       FROM purchases
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND invoice_date >= ? AND invoice_date < ?
       GROUP BY bucket`,
      [...ids, fromDt, toExclusive]
    ),
    query<RowDataPacket[]>(
      `SELECT ${
        mode === "yearly"
          ? `CONCAT('FY ', IF(MONTH(expense_date)>=4, YEAR(expense_date), YEAR(expense_date)-1))`
          : mode === "monthly"
            ? `DATE_FORMAT(expense_date,'%Y-%m')`
            : `DATE(expense_date)`
      } AS bucket, COALESCE(SUM(amount),0) AS expense
       FROM expenses
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND expense_date >= ? AND expense_date < ?
       GROUP BY bucket`,
      [...ids, periods[0].from, toYmd(addDays(parseYmd(periods[periods.length - 1].to), 1))]
    ).catch(() => [] as RowDataPacket[]),
    query<RowDataPacket[]>(
      `SELECT ${
        mode === "yearly"
          ? `CONCAT('FY ', IF(MONTH(return_date)>=4, YEAR(return_date), YEAR(return_date)-1))`
          : mode === "monthly"
            ? `DATE_FORMAT(return_date,'%Y-%m')`
            : `DATE(return_date)`
      } AS bucket, COALESCE(SUM(grand_total),0) AS amt
       FROM purchase_returns
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND return_date >= ? AND return_date < ?
       GROUP BY bucket`,
      [...ids, fromDt, toExclusive]
    ).catch(() => [] as RowDataPacket[]),
    stockSnapshot(ids, ph),
  ]);

  const keyFor = (range: DateRange) => {
    if (mode === "yearly") return range.label.startsWith("FY") ? range.label : range.label;
    if (mode === "monthly") return range.from.slice(0, 7);
    return range.from;
  };

  const saleMap = new Map(salesRows.map((r) => [String(r.bucket), r]));
  const purchaseMap = new Map(purchaseRows.map((r) => [String(r.bucket), r]));
  const expenseMap = new Map(expenseRows.map((r) => [String(r.bucket), r]));
  const returnMap = new Map(returnRows.map((r) => [String(r.bucket), r]));

  return periods.map((range) => {
    let key = keyFor(range);
    if (mode === "yearly") {
      const startYear = range.from.slice(0, 4);
      key = `FY ${startYear}`;
    }
    const s = saleMap.get(key);
    const p = purchaseMap.get(key);
    const ex = expenseMap.get(key);
    const pr = returnMap.get(key);
    const sale = Number(s?.sale ?? 0);
    const saleCost = Number(s?.cost ?? 0);
    const purchase = Number(p?.purchase ?? 0);
    const purchaseReturns = Number(pr?.amt ?? 0);
    const collection = Number(s?.collection ?? 0);
    const supplierPayment = Number(p?.paid ?? 0);
    const expense = Number(ex?.expense ?? 0);
    const grossMargin = sale - saleCost;
    const netCash = collection - supplierPayment - expense;
    return {
      label: range.label,
      from: range.from,
      to: range.to,
      sale,
      saleBills: Number(s?.bills ?? 0),
      saleCost,
      grossMargin,
      gst: Number(s?.gst ?? 0),
      purchase,
      purchaseReturns,
      purchaseNet: purchase - purchaseReturns,
      collection,
      supplierPayment,
      expense,
      ...stock,
      netCash,
      netBusiness: netCash,
      netProfit: grossMargin - expense,
      pendingPayment: Math.max(purchase - purchaseReturns - supplierPayment, 0),
    };
  });
}
