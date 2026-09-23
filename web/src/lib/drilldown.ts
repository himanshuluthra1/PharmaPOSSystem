import { RowDataPacket } from "mysql2";
import { query } from "@/lib/db";
import {
  addDays,
  addMonths,
  daysInRange,
  fyRange,
  parseYmd,
  toYmd,
  type DateRange,
} from "@/lib/periods";
import { storeScope, type StoreUser } from "@/lib/medwin/aggregates";
import type {
  DrillBillDetail,
  DrillLevel,
  DrillMetric,
  DrillResult,
  DrillRow,
} from "@/lib/drilldown-types";

export type {
  DrillBillDetail,
  DrillLevel,
  DrillMetric,
  DrillResult,
  DrillRow,
} from "@/lib/drilldown-types";

const METRIC_TITLE: Record<DrillMetric, string> = {
  sale: "Sale",
  saleCost: "Sale cost",
  grossMargin: "Gross margin",
  gst: "GST",
  saleBills: "Sale bills",
  purchase: "Purchase",
  purchaseReturns: "Purchase returns",
  purchaseNet: "Purchase net",
  collection: "Collection",
  supplierPayment: "Supplier payment",
  expense: "Expense",
  netCash: "Net cash",
  netProfit: "Net profit",
};

const ALL_METRICS = new Set<string>(Object.keys(METRIC_TITLE));

export function isDrillMetric(v: string): v is DrillMetric {
  return ALL_METRICS.has(v);
}

function rangeBounds(from: string, to: string) {
  const fromDt = `${from} 00:00:00`;
  const toExclusive = toYmd(addDays(parseYmd(to), 1)) + " 00:00:00";
  return { fromDt, toExclusive };
}

function expenseBounds(from: string, to: string) {
  return {
    fromDate: from,
    toExclusive: toYmd(addDays(parseYmd(to), 1)),
  };
}

function clampRange(from: string, to: string, window: DateRange): DateRange {
  return {
    from: from > window.from ? from : window.from,
    to: to < window.to ? to : window.to,
    label: window.label,
  };
}

function overlappingFys(from: string, to: string): DateRange[] {
  const start = parseYmd(from);
  const end = parseYmd(to);
  const fy = start.getMonth() >= 3 ? start.getFullYear() : start.getFullYear() - 1;
  const endFy = end.getMonth() >= 3 ? end.getFullYear() : end.getFullYear() - 1;
  const out: DateRange[] = [];
  for (let y = fy; y <= endFy; y++) {
    const full = fyRange(0, new Date(y, 3, 1));
    const clamped = clampRange(from, to, full);
    if (clamped.from <= clamped.to) out.push({ ...clamped, label: full.label });
  }
  return out;
}

function overlappingMonths(from: string, to: string): DateRange[] {
  const start = new Date(parseYmd(from).getFullYear(), parseYmd(from).getMonth(), 1);
  const end = parseYmd(to);
  const out: DateRange[] = [];
  let d = start;
  while (d <= end) {
    const mFrom = toYmd(d);
    const mTo = toYmd(new Date(d.getFullYear(), d.getMonth() + 1, 0));
    const label = d.toLocaleString("en-IN", { month: "short", year: "numeric" });
    const clamped = clampRange(from, to, { from: mFrom, to: mTo, label });
    if (clamped.from <= clamped.to) out.push(clamped);
    d = addMonths(d, 1);
  }
  return out;
}

function overlappingDays(from: string, to: string): DateRange[] {
  const out: DateRange[] = [];
  let d = parseYmd(from);
  const end = parseYmd(to);
  while (d <= end) {
    const s = toYmd(d);
    out.push({
      from: s,
      to: s,
      label: d.toLocaleDateString("en-IN", {
        day: "2-digit",
        month: "short",
        year: "numeric",
      }),
    });
    d = addDays(d, 1);
  }
  return out;
}

export function pickStartLevel(from: string, to: string): DrillLevel {
  if (from === to) return "bills";
  if (overlappingFys(from, to).length > 1) return "years";
  if (from.slice(0, 7) !== to.slice(0, 7)) return "months";
  return "days";
}

function nextLevelFor(level: DrillLevel): DrillLevel | undefined {
  if (level === "years") return "months";
  if (level === "months") return "days";
  if (level === "days") return "bills";
  return undefined;
}

function fyBucketExpr(col: string) {
  return `CONCAT('FY ', IF(MONTH(${col})>=4, YEAR(${col}), YEAR(${col})-1))`;
}

function monthBucketExpr(col: string) {
  return `DATE_FORMAT(${col},'%Y-%m')`;
}

function dayBucketExpr(col: string) {
  return `DATE_FORMAT(${col},'%Y-%m-%d')`;
}

function bucketExpr(level: DrillLevel, col: string) {
  if (level === "years") return fyBucketExpr(col);
  if (level === "months") return monthBucketExpr(col);
  return dayBucketExpr(col);
}

type AggRow = { bucket: string; amount: number; count: number; extras?: Record<string, number> };

async function aggregateSales(
  user: StoreUser,
  from: string,
  to: string,
  level: DrillLevel,
  metric: DrillMetric
): Promise<AggRow[]> {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const { fromDt, toExclusive } = rangeBounds(from, to);
  const fmt = bucketExpr(level, "invoice_date");

  const rows = await query<RowDataPacket[]>(
    `SELECT ${fmt} AS bucket,
            COALESCE(SUM(grand_total),0) AS sale,
            COALESCE(SUM(cogs),0) AS cost,
            COALESCE(SUM(grand_total - cogs),0) AS margin,
            COALESCE(SUM(cgst_amount+sgst_amount+igst_amount),0) AS gst,
            COALESCE(SUM(paid_amount),0) AS collection,
            COUNT(*) AS cnt
     FROM sales
     WHERE store_id IN (${ph}) AND is_deleted=0
       AND invoice_date >= ? AND invoice_date < ?
     GROUP BY bucket
     ORDER BY bucket`,
    [...ids, fromDt, toExclusive]
  );
  return rows.map((r) => {
    const sale = Number(r.sale ?? 0);
    const cost = Number(r.cost ?? 0);
    const margin = Number(r.margin ?? 0);
    const gst = Number(r.gst ?? 0);
    const collection = Number(r.collection ?? 0);
    const count = Number(r.cnt ?? 0);
    let amount = sale;
    if (metric === "saleCost") amount = cost;
    else if (metric === "grossMargin") amount = margin;
    else if (metric === "gst") amount = gst;
    else if (metric === "saleBills") amount = count;
    else if (metric === "collection") amount = collection;
    return {
      bucket: normalizeBucket(level, r.bucket),
      amount,
      count,
      extras: { sale, cost, margin, gst, collection, bills: count },
    };
  });
}

/** mysql2 may return DATE columns as Date objects — normalize to bucket keys. */
function normalizeBucket(level: DrillLevel, raw: unknown): string {
  if (raw instanceof Date) {
    if (level === "days") return toYmd(raw);
    if (level === "months") return `${raw.getFullYear()}-${String(raw.getMonth() + 1).padStart(2, "0")}`;
    const y = raw.getMonth() >= 3 ? raw.getFullYear() : raw.getFullYear() - 1;
    return `FY ${y}`;
  }
  const s = String(raw ?? "");
  // DATE() sometimes arrives as "YYYY-MM-DDTHH:mm:ss.sssZ"
  if (level === "days" && /^\d{4}-\d{2}-\d{2}/.test(s)) return s.slice(0, 10);
  return s;
}

async function aggregatePurchases(
  user: StoreUser,
  from: string,
  to: string,
  level: DrillLevel,
  metric: "purchase" | "supplierPayment"
): Promise<AggRow[]> {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const { fromDt, toExclusive } = rangeBounds(from, to);
  const fmt = bucketExpr(level, "invoice_date");

  const rows = await query<RowDataPacket[]>(
    `SELECT ${fmt} AS bucket,
            COALESCE(SUM(grand_total),0) AS purchase,
            COALESCE(SUM(paid_amount),0) AS paid,
            COUNT(*) AS cnt
     FROM purchases
     WHERE store_id IN (${ph}) AND is_deleted=0
       AND invoice_date >= ? AND invoice_date < ?
     GROUP BY bucket
     ORDER BY bucket`,
    [...ids, fromDt, toExclusive]
  );
  return rows.map((r) => {
    const purchase = Number(r.purchase ?? 0);
    const paid = Number(r.paid ?? 0);
    const count = Number(r.cnt ?? 0);
    return {
      bucket: normalizeBucket(level, r.bucket),
      amount: metric === "supplierPayment" ? paid : purchase,
      count,
      extras: { purchase, paid, bills: count, balance: Math.max(purchase - paid, 0) },
    };
  });
}

async function aggregatePurchaseReturns(
  user: StoreUser,
  from: string,
  to: string,
  level: DrillLevel
): Promise<AggRow[]> {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const { fromDt, toExclusive } = rangeBounds(from, to);
  const fmt = bucketExpr(level, "return_date");
  try {
    const rows = await query<RowDataPacket[]>(
      `SELECT ${fmt} AS bucket,
              COALESCE(SUM(grand_total),0) AS amount,
              COUNT(*) AS cnt
       FROM purchase_returns
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND return_date >= ? AND return_date < ?
       GROUP BY bucket
       ORDER BY bucket`,
      [...ids, fromDt, toExclusive]
    );
    return rows.map((r) => ({
      bucket: normalizeBucket(level, r.bucket),
      amount: Number(r.amount ?? 0),
      count: Number(r.cnt ?? 0),
    }));
  } catch {
    return [];
  }
}

async function aggregateExpenses(
  user: StoreUser,
  from: string,
  to: string,
  level: DrillLevel
): Promise<AggRow[]> {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const { fromDate, toExclusive } = expenseBounds(from, to);
  const fmt = bucketExpr(level, "expense_date");
  try {
    const rows = await query<RowDataPacket[]>(
      `SELECT ${fmt} AS bucket,
              COALESCE(SUM(amount),0) AS amount,
              COUNT(*) AS cnt
       FROM expenses
       WHERE store_id IN (${ph}) AND is_deleted=0
         AND expense_date >= ? AND expense_date < ?
       GROUP BY bucket
       ORDER BY bucket`,
      [...ids, fromDate, toExclusive]
    );
    return rows.map((r) => ({
      bucket: normalizeBucket(level, r.bucket),
      amount: Number(r.amount ?? 0),
      count: Number(r.cnt ?? 0),
    }));
  } catch {
    return [];
  }
}

async function aggregatePurchaseNet(
  user: StoreUser,
  from: string,
  to: string,
  level: DrillLevel
): Promise<AggRow[]> {
  const [purchases, returns] = await Promise.all([
    aggregatePurchases(user, from, to, level, "purchase"),
    aggregatePurchaseReturns(user, from, to, level),
  ]);
  const map = new Map<string, AggRow>();
  for (const p of purchases) {
    map.set(p.bucket, {
      bucket: p.bucket,
      amount: p.amount,
      count: p.count,
      extras: { purchase: p.amount, returns: 0 },
    });
  }
  for (const r of returns) {
    const cur = map.get(r.bucket) || {
      bucket: r.bucket,
      amount: 0,
      count: 0,
      extras: { purchase: 0, returns: 0 },
    };
    cur.extras = {
      purchase: cur.extras?.purchase ?? 0,
      returns: r.amount,
    };
    cur.amount = (cur.extras.purchase ?? 0) - r.amount;
    cur.count += r.count;
    map.set(r.bucket, cur);
  }
  return [...map.values()].sort((a, b) => a.bucket.localeCompare(b.bucket));
}

async function aggregateNetCash(
  user: StoreUser,
  from: string,
  to: string,
  level: DrillLevel
): Promise<AggRow[]> {
  const [collection, supplier, expense] = await Promise.all([
    aggregateSales(user, from, to, level, "collection"),
    aggregatePurchases(user, from, to, level, "supplierPayment"),
    aggregateExpenses(user, from, to, level),
  ]);
  const keys = new Set([
    ...collection.map((r) => r.bucket),
    ...supplier.map((r) => r.bucket),
    ...expense.map((r) => r.bucket),
  ]);
  const cMap = new Map(collection.map((r) => [r.bucket, r]));
  const sMap = new Map(supplier.map((r) => [r.bucket, r]));
  const eMap = new Map(expense.map((r) => [r.bucket, r]));
  return [...keys]
    .sort()
    .map((bucket) => {
      const c = cMap.get(bucket)?.amount ?? 0;
      const s = sMap.get(bucket)?.amount ?? 0;
      const e = eMap.get(bucket)?.amount ?? 0;
      return {
        bucket,
        amount: c - s - e,
        count:
          (cMap.get(bucket)?.count ?? 0) +
          (sMap.get(bucket)?.count ?? 0) +
          (eMap.get(bucket)?.count ?? 0),
        extras: { collection: c, supplierPayment: s, expense: e },
      };
    });
}

async function aggregateNetProfit(
  user: StoreUser,
  from: string,
  to: string,
  level: DrillLevel
): Promise<AggRow[]> {
  const [margin, expense] = await Promise.all([
    aggregateSales(user, from, to, level, "grossMargin"),
    aggregateExpenses(user, from, to, level),
  ]);
  const keys = new Set([...margin.map((r) => r.bucket), ...expense.map((r) => r.bucket)]);
  const mMap = new Map(margin.map((r) => [r.bucket, r]));
  const eMap = new Map(expense.map((r) => [r.bucket, r]));
  return [...keys]
    .sort()
    .map((bucket) => {
      const g = mMap.get(bucket)?.amount ?? 0;
      const e = eMap.get(bucket)?.amount ?? 0;
      return {
        bucket,
        amount: g - e,
        count: (mMap.get(bucket)?.count ?? 0) + (eMap.get(bucket)?.count ?? 0),
        extras: { grossMargin: g, expense: e },
      };
    });
}

function periodsForLevel(level: DrillLevel, from: string, to: string): DateRange[] {
  if (level === "years") return overlappingFys(from, to);
  if (level === "months") return overlappingMonths(from, to);
  return overlappingDays(from, to);
}

function bucketKeyForPeriod(level: DrillLevel, range: DateRange): string {
  if (level === "years") {
    const y = parseYmd(range.from);
    const fy = y.getMonth() >= 3 ? y.getFullYear() : y.getFullYear() - 1;
    return `FY ${fy}`;
  }
  if (level === "months") return range.from.slice(0, 7);
  return range.from;
}

async function periodRows(
  user: StoreUser,
  metric: DrillMetric,
  from: string,
  to: string,
  level: DrillLevel
): Promise<DrillRow[]> {
  let aggs: AggRow[] = [];
  if (
    metric === "sale" ||
    metric === "saleCost" ||
    metric === "grossMargin" ||
    metric === "gst" ||
    metric === "saleBills" ||
    metric === "collection"
  ) {
    aggs = await aggregateSales(user, from, to, level, metric);
  } else if (metric === "purchase") {
    aggs = await aggregatePurchases(user, from, to, level, "purchase");
  } else if (metric === "supplierPayment") {
    aggs = await aggregatePurchases(user, from, to, level, "supplierPayment");
  } else if (metric === "purchaseReturns") {
    aggs = await aggregatePurchaseReturns(user, from, to, level);
  } else if (metric === "purchaseNet") {
    aggs = await aggregatePurchaseNet(user, from, to, level);
  } else if (metric === "expense") {
    aggs = await aggregateExpenses(user, from, to, level);
  } else if (metric === "netCash") {
    aggs = await aggregateNetCash(user, from, to, level);
  } else if (metric === "netProfit") {
    aggs = await aggregateNetProfit(user, from, to, level);
  }

  const map = new Map(aggs.map((a) => [a.bucket, a]));
  const next = nextLevelFor(level);
  const periods = periodsForLevel(level, from, to);

  return periods
    .map((p) => {
      const key = bucketKeyForPeriod(level, p);
      const a = map.get(key);
      const extras = a?.extras;
      return {
        id: key,
        label: p.label,
        amount: a?.amount ?? 0,
        count: a?.count,
        extras,
        cols: {
          label: p.label,
          ...(extras
            ? Object.fromEntries(Object.entries(extras).map(([k, v]) => [k, v]))
            : {}),
        },
        nextFrom: p.from,
        nextTo: p.to,
        nextLevel: next,
      } satisfies DrillRow;
    })
    .filter((r) => r.amount !== 0 || (r.count ?? 0) > 0 || Object.values(r.extras || {}).some((v) => v !== 0));
}

async function billRows(
  user: StoreUser,
  metric: DrillMetric,
  from: string,
  to: string
): Promise<DrillRow[]> {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return [];
  const { fromDt, toExclusive } = rangeBounds(from, to);
  const { fromDate, toExclusive: expTo } = expenseBounds(from, to);

  const ymd = (d: unknown) => {
    if (!d) return "";
    if (d instanceof Date) return toYmd(d);
    const s = String(d);
    return /^\d{4}-\d{2}-\d{2}/.test(s) ? s.slice(0, 10) : s;
  };

  if (
    metric === "sale" ||
    metric === "saleCost" ||
    metric === "grossMargin" ||
    metric === "gst" ||
    metric === "saleBills" ||
    metric === "collection"
  ) {
    const rows = await query<RowDataPacket[]>(
      `SELECT s.store_id, s.local_id, s.invoice_number, s.invoice_date,
              s.billing_customer_name, s.grand_total, s.cogs, s.paid_amount,
              (s.cgst_amount+s.sgst_amount+s.igst_amount) AS gst,
              (s.grand_total - s.cogs) AS margin
       FROM sales s
       WHERE s.store_id IN (${ph}) AND s.is_deleted=0
         AND s.invoice_date >= ? AND s.invoice_date < ?
       ORDER BY s.invoice_date DESC, s.local_id DESC
       LIMIT 500`,
      [...ids, fromDt, toExclusive]
    );
    return rows.map((r) => {
      const sale = Number(r.grand_total ?? 0);
      const cost = Number(r.cogs ?? 0);
      const margin = Number(r.margin ?? 0);
      const gst = Number(r.gst ?? 0);
      const paid = Number(r.paid_amount ?? 0);
      let amount = sale;
      if (metric === "saleCost") amount = cost;
      else if (metric === "grossMargin") amount = margin;
      else if (metric === "gst") amount = gst;
      else if (metric === "saleBills") amount = 1;
      else if (metric === "collection") amount = paid;
      const inv = String(r.invoice_number || `#${r.local_id}`);
      const customer = String(r.billing_customer_name || "Walk-in");
      const detail: DrillBillDetail = {
        kind: "sale",
        storeId: String(r.store_id),
        localId: Number(r.local_id),
      };
      return {
        id: `${r.store_id}:${r.local_id}`,
        label: `${inv} · ${customer}`,
        amount,
        cols: {
          date: ymd(r.invoice_date),
          invoice: inv,
          customer,
          sale,
          cost,
          margin,
          gst,
          paid,
          balance: Math.max(sale - paid, 0),
        },
        detail,
        href: `/sales/${r.store_id}/${r.local_id}`,
      };
    });
  }

  if (metric === "purchase" || metric === "supplierPayment" || metric === "purchaseNet") {
    const rows = await query<RowDataPacket[]>(
      `SELECT p.store_id, p.local_id, p.invoice_number, p.invoice_date,
              p.supplier_invoice_number, p.grand_total, p.paid_amount,
              COALESCE(s.name, CONCAT('Supplier #', p.supplier_local_id)) AS vendor
       FROM purchases p
       LEFT JOIN suppliers s
         ON s.store_id=p.store_id AND s.local_id=p.supplier_local_id AND s.is_deleted=0
       WHERE p.store_id IN (${ph}) AND p.is_deleted=0
         AND p.invoice_date >= ? AND p.invoice_date < ?
       ORDER BY p.invoice_date DESC, p.local_id DESC
       LIMIT 500`,
      [...ids, fromDt, toExclusive]
    );
    return rows.map((r) => {
      const total = Number(r.grand_total ?? 0);
      const paid = Number(r.paid_amount ?? 0);
      const amount = metric === "supplierPayment" ? paid : total;
      const inv = String(r.invoice_number || `#${r.local_id}`);
      const vendor = String(r.vendor);
      const detail: DrillBillDetail = {
        kind: "purchase",
        storeId: String(r.store_id),
        localId: Number(r.local_id),
      };
      return {
        id: `${r.store_id}:${r.local_id}`,
        label: `${inv} · ${vendor}`,
        amount,
        cols: {
          date: ymd(r.invoice_date),
          invoice: inv,
          vendorBill: String(r.supplier_invoice_number || "—"),
          vendor,
          purchase: total,
          paid,
          balance: Math.max(total - paid, 0),
        },
        detail,
        href: `/purchases/${r.store_id}/${r.local_id}`,
      };
    });
  }

  if (metric === "purchaseReturns") {
    try {
      const rows = await query<RowDataPacket[]>(
        `SELECT store_id, local_id, return_number, return_date, grand_total, status
         FROM purchase_returns
         WHERE store_id IN (${ph}) AND is_deleted=0
           AND return_date >= ? AND return_date < ?
         ORDER BY return_date DESC, local_id DESC
         LIMIT 500`,
        [...ids, fromDt, toExclusive]
      );
      return rows.map((r) => ({
        id: `${r.store_id}:${r.local_id}`,
        label: String(r.return_number || `#${r.local_id}`),
        amount: Number(r.grand_total ?? 0),
        cols: {
          date: ymd(r.return_date),
          invoice: String(r.return_number || `#${r.local_id}`),
          status: String(r.status || "—"),
          amount: Number(r.grand_total ?? 0),
        },
      }));
    } catch {
      return [];
    }
  }

  if (metric === "expense") {
    try {
      const rows = await query<RowDataPacket[]>(
        `SELECT e.id, e.store_id, e.expense_date, e.amount, e.notes,
                COALESCE(h.name, '—') AS head_name
         FROM expenses e
         LEFT JOIN expense_heads h ON h.id=e.head_id
         WHERE e.store_id IN (${ph}) AND e.is_deleted=0
           AND e.expense_date >= ? AND e.expense_date < ?
         ORDER BY e.expense_date DESC, e.id DESC
         LIMIT 500`,
        [...ids, fromDate, expTo]
      );
      return rows.map((r) => ({
        id: `exp:${r.id}`,
        label: `${r.head_name} · ${r.notes || r.store_id}`,
        amount: Number(r.amount ?? 0),
        cols: {
          date: ymd(r.expense_date),
          head: String(r.head_name),
          store: String(r.store_id),
          notes: String(r.notes || "—"),
          amount: Number(r.amount ?? 0),
        },
      }));
    } catch {
      return [];
    }
  }

  if (metric === "netCash") {
    const [sales, purchases, expenses] = await Promise.all([
      query<RowDataPacket[]>(
        `SELECT s.store_id, s.local_id, s.invoice_number, s.invoice_date,
                s.billing_customer_name, s.paid_amount
         FROM sales s
         WHERE s.store_id IN (${ph}) AND s.is_deleted=0
           AND s.invoice_date >= ? AND s.invoice_date < ?
           AND s.paid_amount > 0
         ORDER BY s.invoice_date DESC
         LIMIT 200`,
        [...ids, fromDt, toExclusive]
      ),
      query<RowDataPacket[]>(
        `SELECT p.store_id, p.local_id, p.invoice_number, p.invoice_date, p.paid_amount,
                COALESCE(s.name, CONCAT('Supplier #', p.supplier_local_id)) AS vendor
         FROM purchases p
         LEFT JOIN suppliers s
           ON s.store_id=p.store_id AND s.local_id=p.supplier_local_id AND s.is_deleted=0
         WHERE p.store_id IN (${ph}) AND p.is_deleted=0
           AND p.invoice_date >= ? AND p.invoice_date < ?
           AND p.paid_amount > 0
         ORDER BY p.invoice_date DESC
         LIMIT 200`,
        [...ids, fromDt, toExclusive]
      ),
      query<RowDataPacket[]>(
        `SELECT e.id, e.expense_date, e.amount, e.store_id, COALESCE(h.name,'Expense') AS head_name
         FROM expenses e
         LEFT JOIN expense_heads h ON h.id=e.head_id
         WHERE e.store_id IN (${ph}) AND e.is_deleted=0
           AND e.expense_date >= ? AND e.expense_date < ?
         ORDER BY e.expense_date DESC
         LIMIT 200`,
        [...ids, fromDate, expTo]
      ).catch(() => [] as RowDataPacket[]),
    ]);
    const rows: DrillRow[] = [
      ...sales.map((r) => ({
        id: `c:${r.store_id}:${r.local_id}`,
        label: `Collection · ${r.invoice_number || `#${r.local_id}`}`,
        amount: Number(r.paid_amount ?? 0),
        cols: {
          type: "Collection",
          date: ymd(r.invoice_date),
          ref: String(r.invoice_number || `#${r.local_id}`),
          party: String(r.billing_customer_name || "Walk-in"),
          amount: Number(r.paid_amount ?? 0),
        },
        detail: {
          kind: "sale" as const,
          storeId: String(r.store_id),
          localId: Number(r.local_id),
        },
        href: `/sales/${r.store_id}/${r.local_id}`,
      })),
      ...purchases.map((r) => ({
        id: `p:${r.store_id}:${r.local_id}`,
        label: `Supplier pay · ${r.invoice_number || `#${r.local_id}`}`,
        amount: -Number(r.paid_amount ?? 0),
        cols: {
          type: "Supplier pay",
          date: ymd(r.invoice_date),
          ref: String(r.invoice_number || `#${r.local_id}`),
          party: String(r.vendor),
          amount: -Number(r.paid_amount ?? 0),
        },
        detail: {
          kind: "purchase" as const,
          storeId: String(r.store_id),
          localId: Number(r.local_id),
        },
        href: `/purchases/${r.store_id}/${r.local_id}`,
      })),
      ...expenses.map((r) => ({
        id: `e:${r.id}`,
        label: `Expense · ${r.head_name}`,
        amount: -Number(r.amount ?? 0),
        cols: {
          type: "Expense",
          date: ymd(r.expense_date),
          ref: String(r.head_name),
          party: String(r.store_id),
          amount: -Number(r.amount ?? 0),
        },
      })),
    ];
    return rows;
  }

  if (metric === "netProfit") {
    const [sales, expenses] = await Promise.all([
      query<RowDataPacket[]>(
        `SELECT s.store_id, s.local_id, s.invoice_number, s.invoice_date,
                s.billing_customer_name, s.grand_total, s.cogs,
                (s.grand_total - s.cogs) AS amount
         FROM sales s
         WHERE s.store_id IN (${ph}) AND s.is_deleted=0
           AND s.invoice_date >= ? AND s.invoice_date < ?
         ORDER BY s.invoice_date DESC
         LIMIT 300`,
        [...ids, fromDt, toExclusive]
      ),
      query<RowDataPacket[]>(
        `SELECT e.id, e.expense_date, e.amount, e.store_id, COALESCE(h.name,'Expense') AS head_name
         FROM expenses e
         LEFT JOIN expense_heads h ON h.id=e.head_id
         WHERE e.store_id IN (${ph}) AND e.is_deleted=0
           AND e.expense_date >= ? AND e.expense_date < ?
         ORDER BY e.expense_date DESC
         LIMIT 200`,
        [...ids, fromDate, expTo]
      ).catch(() => [] as RowDataPacket[]),
    ]);
    return [
      ...sales.map((r) => ({
        id: `s:${r.store_id}:${r.local_id}`,
        label: `Margin · ${r.invoice_number || `#${r.local_id}`} · ${r.billing_customer_name || "Walk-in"}`,
        amount: Number(r.amount ?? 0),
        cols: {
          type: "Sale margin",
          date: ymd(r.invoice_date),
          ref: String(r.invoice_number || `#${r.local_id}`),
          party: String(r.billing_customer_name || "Walk-in"),
          sale: Number(r.grand_total ?? 0),
          cost: Number(r.cogs ?? 0),
          amount: Number(r.amount ?? 0),
        },
        detail: {
          kind: "sale" as const,
          storeId: String(r.store_id),
          localId: Number(r.local_id),
        },
        href: `/sales/${r.store_id}/${r.local_id}`,
      })),
      ...expenses.map((r) => ({
        id: `e:${r.id}`,
        label: `Expense · ${r.head_name}`,
        amount: -Number(r.amount ?? 0),
        cols: {
          type: "Expense",
          date: ymd(r.expense_date),
          ref: String(r.head_name),
          party: String(r.store_id),
          sale: null,
          cost: null,
          amount: -Number(r.amount ?? 0),
        },
      })),
    ];
  }

  return [];
}

function columnsFor(metric: DrillMetric, level: DrillLevel): DrillResult["columns"] {
  const L = (key: string, label: string): DrillResult["columns"][number] => ({
    key,
    label,
    align: "left",
  });
  const R = (key: string, label: string): DrillResult["columns"][number] => ({
    key,
    label,
    align: "right",
  });

  if (level === "bills") {
    if (
      metric === "sale" ||
      metric === "saleCost" ||
      metric === "grossMargin" ||
      metric === "gst" ||
      metric === "saleBills" ||
      metric === "collection"
    ) {
      return [
        L("date", "Date"),
        L("invoice", "Invoice"),
        L("customer", "Customer"),
        R("sale", "Sale"),
        R("cost", "Cost"),
        R("margin", "Margin"),
        R("gst", "GST"),
        R("paid", "Paid"),
        R("balance", "Balance"),
      ];
    }
    if (metric === "purchase" || metric === "supplierPayment" || metric === "purchaseNet") {
      return [
        L("date", "Date"),
        L("invoice", "POS bill"),
        L("vendorBill", "Vendor bill"),
        L("vendor", "Vendor"),
        R("purchase", "Amount"),
        R("paid", "Paid"),
        R("balance", "Balance"),
      ];
    }
    if (metric === "purchaseReturns") {
      return [L("date", "Date"), L("invoice", "Return #"), L("status", "Status"), R("amount", "Amount")];
    }
    if (metric === "expense") {
      return [
        L("date", "Date"),
        L("head", "Head"),
        L("store", "Store"),
        L("notes", "Notes"),
        R("amount", "Amount"),
      ];
    }
    if (metric === "netCash") {
      return [
        L("type", "Type"),
        L("date", "Date"),
        L("ref", "Ref"),
        L("party", "Party"),
        R("amount", "Amount"),
      ];
    }
    if (metric === "netProfit") {
      return [
        L("type", "Type"),
        L("date", "Date"),
        L("ref", "Ref"),
        L("party", "Party"),
        R("sale", "Sale"),
        R("cost", "Cost"),
        R("amount", "Amount"),
      ];
    }
    return [L("label", "Entry"), R("amount", "Amount")];
  }

  if (metric === "netCash") {
    return [
      L("label", "Period"),
      R("collection", "Collection"),
      R("supplierPayment", "Sup. pay"),
      R("expense", "Expense"),
      R("amount", "Net cash"),
    ];
  }
  if (metric === "netProfit") {
    return [
      L("label", "Period"),
      R("grossMargin", "Gross"),
      R("expense", "Expense"),
      R("amount", "Net profit"),
    ];
  }
  if (metric === "purchaseNet") {
    return [
      L("label", "Period"),
      R("purchase", "Purchase"),
      R("returns", "Returns"),
      R("amount", "Net"),
      R("count", "Bills"),
    ];
  }
  if (metric === "purchase" || metric === "supplierPayment") {
    return [
      L("label", "Period"),
      R("purchase", "Purchase"),
      R("paid", "Paid"),
      R("balance", "Balance"),
      R("bills", "Bills"),
    ];
  }
  if (metric === "purchaseReturns") {
    return [L("label", "Period"), R("amount", "Returns"), R("count", "Count")];
  }
  if (metric === "expense") {
    return [L("label", "Period"), R("amount", "Expense"), R("count", "Entries")];
  }
  // sale family
  return [
    L("label", "Period"),
    R("sale", "Sale"),
    R("cost", "Cost"),
    R("margin", "Margin"),
    R("gst", "GST"),
    R("collection", "Collection"),
    R("bills", "Bills"),
  ];
}

export async function getDrillDown(
  user: StoreUser,
  opts: {
    metric: DrillMetric;
    from: string;
    to: string;
    level?: DrillLevel;
    title?: string;
  }
): Promise<DrillResult> {
  const { metric, from, to } = opts;
  const level = opts.level || pickStartLevel(from, to);
  const title = opts.title || METRIC_TITLE[metric];

  const rows =
    level === "bills"
      ? await billRows(user, metric, from, to)
      : await periodRows(user, metric, from, to, level);

  const spanLabel =
    from === to
      ? from
      : daysInRange(from, to) > 366
        ? `${from} → ${to}`
        : `${from} → ${to}`;

  return {
    title: `${title} · ${spanLabel}`,
    metric,
    level,
    from,
    to,
    breadcrumb: [{ label: title, from, to, level }],
    columns: columnsFor(metric, level),
    rows,
  };
}
