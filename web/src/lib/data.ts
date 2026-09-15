import { RowDataPacket } from "mysql2";
import { query } from "./db";
import { storeFilter, placeholders } from "./auth";
import type { SessionUser } from "./session";

type StoreScopedUser = Pick<SessionUser, "storeIds" | "selectedStoreId">;

export type ShopProfitTodayRow = {
  storeId: string;
  shopName: string;
  revenue: number;
  cost: number;
  profit: number;
  marginPct: number;
  bills: number;
  avgBill: number;
  avgBillProfit: number;
};

function stores(user: StoreScopedUser) {
  const ids = storeFilter(user);
  if (ids.length === 0) return { ids: [] as string[], ph: "NULL" };
  return { ids, ph: placeholders(ids.length) };
}

export async function getDashboardKpis(user: StoreScopedUser) {
  const { ids, ph } = stores(user);
  if (ids.length === 0) {
    return {
      todaySales: 0,
      todayBills: 0,
      todayProfit: 0,
      todayCost: 0,
      mtdSales: 0,
      todayPurchases: 0,
      mtdPurchases: 0,
      stockValue: 0,
      stockValueMrp: 0,
      lowStock: 0,
      nearExpiry: 0,
      expired: 0,
      customerDues: 0,
      supplierPayables: 0,
      paymentMix: [] as { method: number; amount: number }[],
      recentSales: [] as RowDataPacket[],
      shopProfitToday: [] as ShopProfitTodayRow[],
      lastSyncAt: null as string | null,
    };
  }

  const [todaySales] = await query<RowDataPacket[]>(
    `SELECT COALESCE(SUM(grand_total),0) AS amount, COUNT(*) AS bills
     FROM sales
     WHERE store_id IN (${ph}) AND is_deleted=0
       AND DATE(invoice_date)=CURDATE()`,
    ids
  );

  const [mtdSales] = await query<RowDataPacket[]>(
    `SELECT COALESCE(SUM(grand_total),0) AS amount
     FROM sales
     WHERE store_id IN (${ph}) AND is_deleted=0
       AND invoice_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')`,
    ids
  );

  const [todayPurchases] = await query<RowDataPacket[]>(
    `SELECT COALESCE(SUM(grand_total),0) AS amount
     FROM purchases
     WHERE store_id IN (${ph}) AND is_deleted=0
       AND DATE(invoice_date)=CURDATE()`,
    ids
  );

  const [mtdPurchases] = await query<RowDataPacket[]>(
    `SELECT COALESCE(SUM(grand_total),0) AS amount
     FROM purchases
     WHERE store_id IN (${ph}) AND is_deleted=0
       AND invoice_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')`,
    ids
  );

  const [stock] = await query<RowDataPacket[]>(
    `SELECT
       COALESCE(SUM(b.quantity_available * b.purchase_price),0) AS cost_value,
       COALESCE(SUM(b.quantity_available * b.mrp),0) AS mrp_value
     FROM medicine_batches b
     WHERE b.store_id IN (${ph}) AND b.is_deleted=0 AND b.quantity_available > 0`,
    ids
  );

  const [alerts] = await query<RowDataPacket[]>(
    `SELECT
       SUM(CASE WHEN m.reorder_level > 0 AND tot.qty <= m.reorder_level THEN 1 ELSE 0 END) AS low_stock,
       SUM(CASE WHEN b.expiry_date IS NOT NULL AND b.expiry_date >= CURDATE()
                 AND b.expiry_date <= DATE_ADD(CURDATE(), INTERVAL 90 DAY)
                 AND b.quantity_available > 0 THEN 1 ELSE 0 END) AS near_expiry,
       SUM(CASE WHEN b.expiry_date IS NOT NULL AND b.expiry_date < CURDATE()
                 AND b.quantity_available > 0 THEN 1 ELSE 0 END) AS expired
     FROM medicine_batches b
     JOIN medicines m ON m.store_id=b.store_id AND m.local_id=b.medicine_local_id AND m.is_deleted=0
     LEFT JOIN (
       SELECT store_id, medicine_local_id, SUM(quantity_available) AS qty
       FROM medicine_batches WHERE is_deleted=0 GROUP BY store_id, medicine_local_id
     ) tot ON tot.store_id=b.store_id AND tot.medicine_local_id=b.medicine_local_id
     WHERE b.store_id IN (${ph}) AND b.is_deleted=0`,
    ids
  );

  const [dues] = await query<RowDataPacket[]>(
    `SELECT COALESCE(SUM(outstanding_balance),0) AS amount
     FROM customers
     WHERE store_id IN (${ph}) AND is_deleted=0 AND outstanding_balance > 0`,
    ids
  );

  let supplierPayables = 0;
  try {
    const [pay] = await query<RowDataPacket[]>(
      `SELECT COALESCE(SUM(outstanding_balance),0) AS amount
       FROM suppliers
       WHERE store_id IN (${ph}) AND is_deleted=0 AND outstanding_balance > 0`,
      ids
    );
    supplierPayables = Number(pay?.amount ?? 0);
  } catch {
    supplierPayables = 0;
  }

  const paymentMix = await query<RowDataPacket[]>(
    `SELECT p.method, COALESCE(SUM(p.amount),0) AS amount
     FROM sale_payments p
     JOIN sales s ON s.store_id=p.store_id AND s.local_id=p.sale_local_id AND s.is_deleted=0
     WHERE p.store_id IN (${ph}) AND p.is_deleted=0
       AND DATE(s.invoice_date)=CURDATE()
     GROUP BY p.method`,
    ids
  );

  const recentSales = await query<RowDataPacket[]>(
    `SELECT store_id, local_id, invoice_number, invoice_date, billing_customer_name,
            grand_total, paid_amount, payment_status
     FROM sales
     WHERE store_id IN (${ph}) AND is_deleted=0
     ORDER BY invoice_date DESC
     LIMIT 15`,
    ids
  );

  const [lastSync] = await query<RowDataPacket[]>(
    `SELECT MAX(synced_at_utc) AS last_sync
     FROM sales WHERE store_id IN (${ph})`,
    ids
  );

  const shopProfitToday = await getShopProfitToday(ids, ph);
  const todayCost = shopProfitToday.reduce((sum, r) => sum + r.cost, 0);
  const todaySalesAmount = Number(todaySales?.amount ?? 0);

  return {
    todaySales: todaySalesAmount,
    todayBills: Number(todaySales?.bills ?? 0),
    todayProfit: todaySalesAmount - todayCost,
    todayCost,
    mtdSales: Number(mtdSales?.amount ?? 0),
    todayPurchases: Number(todayPurchases?.amount ?? 0),
    mtdPurchases: Number(mtdPurchases?.amount ?? 0),
    stockValue: Number(stock?.cost_value ?? 0),
    stockValueMrp: Number(stock?.mrp_value ?? 0),
    lowStock: Number(alerts?.low_stock ?? 0),
    nearExpiry: Number(alerts?.near_expiry ?? 0),
    expired: Number(alerts?.expired ?? 0),
    customerDues: Number(dues?.amount ?? 0),
    supplierPayables,
    paymentMix: paymentMix.map((r) => ({
      method: Number(r.method),
      amount: Number(r.amount),
    })),
    recentSales,
    shopProfitToday,
    lastSyncAt: lastSync?.last_sync
      ? new Date(lastSync.last_sync as Date).toISOString()
      : null,
  };
}

async function getShopProfitToday(ids: string[], ph: string): Promise<ShopProfitTodayRow[]> {
  const names = await query<RowDataPacket[]>(
    `SELECT sa.store_id,
            COALESCE(NULLIF(ts.display_name,''), NULLIF(sa.machine_name,''), sa.store_code, sa.store_id) AS shop_name
     FROM store_activations sa
     LEFT JOIN tenant_stores ts ON ts.store_id = sa.store_id
     WHERE sa.store_id IN (${ph})`,
    ids
  );

  const nameById = new Map(names.map((n) => [String(n.store_id), String(n.shop_name || n.store_id)]));

  // Prefer tenant display names when activation row is missing.
  const tenantNames = await query<RowDataPacket[]>(
    `SELECT store_id, display_name FROM tenant_stores WHERE store_id IN (${ph})`,
    ids
  );
  for (const t of tenantNames) {
    const id = String(t.store_id);
    if (!nameById.has(id) && t.display_name)
      nameById.set(id, String(t.display_name));
  }

  const metrics = await query<RowDataPacket[]>(
    `SELECT s.store_id,
            COALESCE(SUM(s.grand_total),0) AS revenue,
            COUNT(*) AS bills,
            COALESCE(SUM(line_cost.cost_amount),0) AS cost
     FROM sales s
     LEFT JOIN (
       SELECT i.store_id, i.sale_local_id,
              SUM(i.quantity * COALESCE(b.purchase_price, m.purchase_price, 0)) AS cost_amount
       FROM sale_items i
       LEFT JOIN medicine_batches b
         ON b.store_id = i.store_id
        AND b.local_id = i.medicine_batch_local_id
        AND b.is_deleted = 0
       LEFT JOIN medicines m
         ON m.store_id = i.store_id
        AND m.local_id = i.medicine_local_id
        AND m.is_deleted = 0
       WHERE i.store_id IN (${ph}) AND i.is_deleted = 0
       GROUP BY i.store_id, i.sale_local_id
     ) line_cost
       ON line_cost.store_id = s.store_id AND line_cost.sale_local_id = s.local_id
     WHERE s.store_id IN (${ph}) AND s.is_deleted = 0
       AND DATE(s.invoice_date) = CURDATE()
     GROUP BY s.store_id`,
    [...ids, ...ids]
  );

  const byStore = new Map(
    metrics.map((r) => [
      String(r.store_id),
      {
        revenue: Number(r.revenue ?? 0),
        cost: Number(r.cost ?? 0),
        bills: Number(r.bills ?? 0),
      },
    ])
  );

  return ids
    .map((storeId) => {
      const m = byStore.get(storeId) ?? { revenue: 0, cost: 0, bills: 0 };
      const profit = m.revenue - m.cost;
      return {
        storeId,
        shopName: nameById.get(storeId) ?? storeId,
        revenue: m.revenue,
        cost: m.cost,
        profit,
        marginPct: m.revenue > 0 ? Math.round((profit / m.revenue) * 1000) / 10 : 0,
        bills: m.bills,
        avgBill: m.bills > 0 ? m.revenue / m.bills : 0,
        avgBillProfit: m.bills > 0 ? profit / m.bills : 0,
      } satisfies ShopProfitTodayRow;
    })
    .sort((a, b) => b.profit - a.profit || b.revenue - a.revenue);
}

export async function listSales(
  user: StoreScopedUser,
  opts: { q?: string; limit?: number; offset?: number } = {}
) {
  const { ids, ph } = stores(user);
  if (ids.length === 0) return [];
  const limit = opts.limit ?? 50;
  const offset = opts.offset ?? 0;
  const q = (opts.q || "").trim();
  if (q) {
    return query<RowDataPacket[]>(
      `SELECT store_id, local_id, invoice_number, invoice_date, billing_customer_name,
              billing_customer_phone, grand_total, paid_amount, payment_status, status
       FROM sales
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND (invoice_number LIKE ? OR billing_customer_name LIKE ? OR billing_customer_phone LIKE ?)
       ORDER BY invoice_date DESC
       LIMIT ? OFFSET ?`,
      [...ids, `%${q}%`, `%${q}%`, `%${q}%`, limit, offset]
    );
  }
  return query<RowDataPacket[]>(
    `SELECT store_id, local_id, invoice_number, invoice_date, billing_customer_name,
            billing_customer_phone, grand_total, paid_amount, payment_status, status
     FROM sales
     WHERE store_id IN (${ph}) AND is_deleted=0
     ORDER BY invoice_date DESC
     LIMIT ? OFFSET ?`,
    [...ids, limit, offset]
  );
}

export async function getSaleDetail(user: StoreScopedUser, storeId: string, localId: number) {
  if (!user.storeIds.includes(storeId)) return null;
  const [sale] = await query<RowDataPacket[]>(
    `SELECT * FROM sales WHERE store_id=? AND local_id=? AND is_deleted=0 LIMIT 1`,
    [storeId, localId]
  );
  if (!sale) return null;
  const items = await query<RowDataPacket[]>(
    `SELECT i.*, m.name AS medicine_name
     FROM sale_items i
     LEFT JOIN medicines m ON m.store_id=i.store_id AND m.local_id=i.medicine_local_id
     WHERE i.store_id=? AND i.sale_local_id=? AND i.is_deleted=0`,
    [storeId, localId]
  );
  const payments = await query<RowDataPacket[]>(
    `SELECT * FROM sale_payments WHERE store_id=? AND sale_local_id=? AND is_deleted=0`,
    [storeId, localId]
  );
  return { sale, items, payments };
}

export async function listPurchases(
  user: StoreScopedUser,
  opts: { q?: string; limit?: number; offset?: number } = {}
) {
  const { ids, ph } = stores(user);
  if (ids.length === 0) return [];
  const limit = opts.limit ?? 50;
  const offset = opts.offset ?? 0;
  const q = (opts.q || "").trim();
  try {
    if (q) {
      return query<RowDataPacket[]>(
        `SELECT p.store_id, p.local_id, p.invoice_number, p.supplier_invoice_number, p.invoice_date,
                p.grand_total, p.paid_amount, p.payment_status, p.status,
                s.name AS supplier_name
         FROM purchases p
         LEFT JOIN suppliers s ON s.store_id = p.store_id AND s.local_id = p.supplier_local_id
         WHERE p.store_id IN (${ph}) AND p.is_deleted=0
           AND (p.invoice_number LIKE ? OR p.supplier_invoice_number LIKE ? OR s.name LIKE ?)
         ORDER BY p.invoice_date DESC
         LIMIT ? OFFSET ?`,
        [...ids, `%${q}%`, `%${q}%`, `%${q}%`, limit, offset]
      );
    }
    return query<RowDataPacket[]>(
      `SELECT p.store_id, p.local_id, p.invoice_number, p.supplier_invoice_number, p.invoice_date,
              p.grand_total, p.paid_amount, p.payment_status, p.status,
              s.name AS supplier_name
       FROM purchases p
       LEFT JOIN suppliers s ON s.store_id = p.store_id AND s.local_id = p.supplier_local_id
       WHERE p.store_id IN (${ph}) AND p.is_deleted=0
       ORDER BY p.invoice_date DESC
       LIMIT ? OFFSET ?`,
      [...ids, limit, offset]
    );
  } catch {
    return query<RowDataPacket[]>(
      `SELECT store_id, local_id, invoice_number, supplier_invoice_number, invoice_date,
              grand_total, paid_amount, payment_status, status, NULL AS supplier_name
       FROM purchases
       WHERE store_id IN (${ph}) AND is_deleted=0
       ORDER BY invoice_date DESC
       LIMIT ? OFFSET ?`,
      [...ids, limit, offset]
    );
  }
}

export async function getPurchaseDetail(user: StoreScopedUser, storeId: string, localId: number) {
  if (!user.storeIds.includes(storeId)) return null;
  const [purchase] = await query<RowDataPacket[]>(
    `SELECT * FROM purchases WHERE store_id=? AND local_id=? AND is_deleted=0 LIMIT 1`,
    [storeId, localId]
  );
  if (!purchase) return null;
  const items = await query<RowDataPacket[]>(
    `SELECT i.*, m.name AS medicine_name
     FROM purchase_items i
     LEFT JOIN medicines m ON m.store_id=i.store_id AND m.local_id=i.medicine_local_id
     WHERE i.store_id=? AND i.purchase_local_id=? AND i.is_deleted=0`,
    [storeId, localId]
  );
  let supplierName: string | null = null;
  try {
    const [s] = await query<RowDataPacket[]>(
      `SELECT name FROM suppliers WHERE store_id=? AND local_id=? LIMIT 1`,
      [storeId, purchase.supplier_local_id]
    );
    supplierName = (s?.name as string) ?? null;
  } catch {
    supplierName = null;
  }
  return { purchase, items, supplierName };
}

export async function listStock(
  user: StoreScopedUser,
  filter: "all" | "low" | "near" | "expired" = "all",
  q?: string
) {
  const { ids, ph } = stores(user);
  if (ids.length === 0) return [];
  const params: unknown[] = [...ids];
  let where = `b.store_id IN (${ph}) AND b.is_deleted=0`;
  if (filter === "near") {
    where += ` AND b.expiry_date IS NOT NULL AND b.expiry_date >= CURDATE()
               AND b.expiry_date <= DATE_ADD(CURDATE(), INTERVAL 90 DAY) AND b.quantity_available > 0`;
  } else if (filter === "expired") {
    where += ` AND b.expiry_date IS NOT NULL AND b.expiry_date < CURDATE() AND b.quantity_available > 0`;
  } else if (filter === "low") {
    where += ` AND m.reorder_level > 0 AND tot.qty <= m.reorder_level`;
  }
  if (q?.trim()) {
    where += ` AND (m.name LIKE ? OR b.batch_number LIKE ? OR b.rack_number LIKE ?)`;
    params.push(`%${q.trim()}%`, `%${q.trim()}%`, `%${q.trim()}%`);
  }
  return query<RowDataPacket[]>(
    `SELECT b.store_id, b.local_id, b.batch_number, b.expiry_date, b.quantity_available,
            b.purchase_price, b.mrp, b.rack_number, m.name AS medicine_name, m.generic_name,
            m.reorder_level, tot.qty AS medicine_total_qty
     FROM medicine_batches b
     JOIN medicines m ON m.store_id=b.store_id AND m.local_id=b.medicine_local_id AND m.is_deleted=0
     LEFT JOIN (
       SELECT store_id, medicine_local_id, SUM(quantity_available) AS qty
       FROM medicine_batches WHERE is_deleted=0 GROUP BY store_id, medicine_local_id
     ) tot ON tot.store_id=b.store_id AND tot.medicine_local_id=b.medicine_local_id
     WHERE ${where}
     ORDER BY m.name, b.expiry_date
     LIMIT 500`,
    params
  );
}

export async function listPayments(user: StoreScopedUser) {
  const { ids, ph } = stores(user);
  if (ids.length === 0) return { mix: [], recent: [], dues: [] };
  const mix = await query<RowDataPacket[]>(
    `SELECT p.method, COALESCE(SUM(p.amount),0) AS amount, COUNT(*) AS cnt
     FROM sale_payments p
     JOIN sales s ON s.store_id=p.store_id AND s.local_id=p.sale_local_id AND s.is_deleted=0
     WHERE p.store_id IN (${ph}) AND p.is_deleted=0
       AND s.invoice_date >= DATE_FORMAT(CURDATE(), '%Y-%m-01')
     GROUP BY p.method`,
    ids
  );
  const recent = await query<RowDataPacket[]>(
    `SELECT p.store_id, p.local_id, p.method, p.amount, p.payment_date_utc, p.reference_number,
            s.invoice_number, s.billing_customer_name
     FROM sale_payments p
     JOIN sales s ON s.store_id=p.store_id AND s.local_id=p.sale_local_id AND s.is_deleted=0
     WHERE p.store_id IN (${ph}) AND p.is_deleted=0
     ORDER BY COALESCE(p.payment_date_utc, s.invoice_date) DESC
     LIMIT 100`,
    ids
  );
  const dues = await query<RowDataPacket[]>(
    `SELECT store_id, local_id, name, phone, outstanding_balance
     FROM customers
     WHERE store_id IN (${ph}) AND is_deleted=0 AND outstanding_balance > 0.009
     ORDER BY outstanding_balance DESC
     LIMIT 100`,
    ids
  );
  return { mix, recent, dues };
}

export async function listReturns(user: StoreScopedUser) {
  const { ids, ph } = stores(user);
  if (ids.length === 0) return { saleReturns: [], purchaseReturns: [] };
  const saleReturns = await query<RowDataPacket[]>(
    `SELECT store_id, local_id, return_number, return_date, grand_total, refund_amount, status
     FROM sale_returns
     WHERE store_id IN (${ph}) AND is_deleted=0
     ORDER BY return_date DESC
     LIMIT 100`,
    ids
  );
  const purchaseReturns = await query<RowDataPacket[]>(
    `SELECT store_id, local_id, return_number, return_date, grand_total, status
     FROM purchase_returns
     WHERE store_id IN (${ph}) AND is_deleted=0
     ORDER BY return_date DESC
     LIMIT 100`,
    ids
  );
  return { saleReturns, purchaseReturns };
}

export async function listTenantStores(tenantId: number) {
  return query<RowDataPacket[]>(
    `SELECT ts.store_id, ts.display_name, sa.store_code, sa.machine_name, sa.is_approved
     FROM tenant_stores ts
     LEFT JOIN store_activations sa ON sa.store_id = ts.store_id
     WHERE ts.tenant_id=?
     ORDER BY COALESCE(ts.display_name, ts.store_id)`,
    [tenantId]
  );
}

export async function listAvailableStores() {
  return query<RowDataPacket[]>(
    `SELECT store_id, store_code, machine_name, is_approved
     FROM store_activations
     ORDER BY requested_at_utc DESC`
  );
}

export async function listUsers(tenantId: number) {
  return query<RowDataPacket[]>(
    `SELECT u.id, u.email, u.full_name, u.status, u.last_login_at_utc, r.name AS role_name, u.role_id
     FROM dashboard_users u
     JOIN dashboard_roles r ON r.id=u.role_id
     WHERE u.tenant_id=?
     ORDER BY u.email`,
    [tenantId]
  );
}

export async function listRoles() {
  return query<RowDataPacket[]>(`SELECT id, name FROM dashboard_roles ORDER BY id`);
}
