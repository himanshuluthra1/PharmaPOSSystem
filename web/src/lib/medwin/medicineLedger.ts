import { RowDataPacket } from "mysql2";
import { query } from "@/lib/db";
import { storeScope, type StoreUser } from "@/lib/medwin/aggregates";

const MOVEMENT_LABEL: Record<number, string> = {
  0: "Purchase In",
  1: "Sale Out",
  2: "Purchase Return",
  3: "Sale Return",
  4: "Adjustment In",
  5: "Adjustment Out",
  6: "Transfer In",
  7: "Transfer Out",
  8: "Damage",
  9: "Expiry",
  10: "Opening Stock",
  11: "Non-Saleable In",
};

export type StockFilter =
  | "all"
  | "expired"
  | "near1m"
  | "near3m"
  | "near6m"
  | "near12m";

function stockFilterSql(filter: StockFilter): string {
  switch (filter) {
    case "expired":
      return "b.expiry_date IS NOT NULL AND b.expiry_date < CURDATE()";
    case "near1m":
      return "b.expiry_date >= CURDATE() AND b.expiry_date <= DATE_ADD(CURDATE(), INTERVAL 30 DAY)";
    case "near3m":
      return "b.expiry_date > DATE_ADD(CURDATE(), INTERVAL 30 DAY) AND b.expiry_date <= DATE_ADD(CURDATE(), INTERVAL 90 DAY)";
    case "near6m":
      return "b.expiry_date > DATE_ADD(CURDATE(), INTERVAL 90 DAY) AND b.expiry_date <= DATE_ADD(CURDATE(), INTERVAL 180 DAY)";
    case "near12m":
      return "b.expiry_date > DATE_ADD(CURDATE(), INTERVAL 180 DAY) AND b.expiry_date <= DATE_ADD(CURDATE(), INTERVAL 365 DAY)";
    default:
      return "1=1";
  }
}

export async function getStockDetails(
  user: StoreUser,
  opts: { filter?: StockFilter; storeId?: string; limit?: number } = {}
) {
  const { ids, ph } = storeScope(user);
  if (ids.length === 0) return { title: "Stock", summary: null, rows: [] as RowDataPacket[] };

  const filter = opts.filter || "all";
  const limit = opts.limit ?? 500;
  const storeId = opts.storeId && ids.includes(opts.storeId) ? opts.storeId : null;
  const scopeIds = storeId ? [storeId] : ids;
  const scopePh = storeId ? "?" : ph;
  const filterSql = stockFilterSql(filter);

  const [sum] = await query<RowDataPacket[]>(
    `SELECT COUNT(*) AS batches,
            COALESCE(SUM(b.quantity_available),0) AS qty,
            COALESCE(SUM(b.quantity_available * b.purchase_price),0) AS cost,
            COALESCE(SUM(b.quantity_available * b.mrp),0) AS mrp
     FROM medicine_batches b
     WHERE b.store_id IN (${scopePh}) AND b.is_deleted=0 AND b.quantity_available > 0
       AND ${filterSql}`,
    scopeIds
  );

  const rows = await query<RowDataPacket[]>(
    `SELECT b.store_id, b.local_id AS batch_local_id, b.medicine_local_id,
            b.batch_number, b.expiry_date, b.quantity_available, b.purchase_price, b.mrp,
            b.selling_price, b.rack_number, b.gst_percent,
            m.name AS medicine_name, m.generic_name, m.brand,
            (b.quantity_available * b.purchase_price) AS cost_value,
            (b.quantity_available * b.mrp) AS mrp_value
     FROM medicine_batches b
     JOIN medicines m ON m.store_id=b.store_id AND m.local_id=b.medicine_local_id AND m.is_deleted=0
     WHERE b.store_id IN (${scopePh}) AND b.is_deleted=0 AND b.quantity_available > 0
       AND ${filterSql}
     ORDER BY m.name, b.expiry_date
     LIMIT ${limit}`,
    scopeIds
  );

  const titles: Record<StockFilter, string> = {
    all: "Stock details",
    expired: "Expired stock",
    near1m: "Near expiry — 1 month",
    near3m: "Near expiry — 3 months",
    near6m: "Near expiry — 6 months",
    near12m: "Near expiry — 12 months",
  };

  return {
    title: titles[filter],
    filter,
    storeId,
    summary: {
      batches: Number(sum?.batches ?? 0),
      qty: Number(sum?.qty ?? 0),
      cost: Number(sum?.cost ?? 0),
      mrp: Number(sum?.mrp ?? 0),
    },
    rows,
  };
}

export async function getMedicineLedger(
  user: StoreUser,
  storeId: string,
  medicineLocalId: number
) {
  const { ids } = storeScope(user);
  if (!ids.includes(storeId)) return null;

  const [med] = await query<RowDataPacket[]>(
    `SELECT local_id, name, generic_name, brand, composition, strength,
            hsn_code, gst_percent, mrp, purchase_price, selling_price, reorder_level
     FROM medicines
     WHERE store_id=? AND local_id=? AND is_deleted=0
     LIMIT 1`,
    [storeId, medicineLocalId]
  );
  if (!med) return null;

  const batches = await query<RowDataPacket[]>(
    `SELECT local_id, batch_number, expiry_date, quantity_available,
            purchase_price, mrp, selling_price, rack_number, gst_percent,
            (quantity_available * purchase_price) AS cost_value
     FROM medicine_batches
     WHERE store_id=? AND medicine_local_id=? AND is_deleted=0
     ORDER BY expiry_date IS NULL, expiry_date, batch_number`,
    [storeId, medicineLocalId]
  );

  const stockQty = batches.reduce((a, b) => a + Number(b.quantity_available ?? 0), 0);
  const stockCost = batches.reduce((a, b) => a + Number(b.cost_value ?? 0), 0);

  // Prefer stock_movements; fall back to sale/purchase document history.
  let movements: RowDataPacket[] = [];
  try {
    movements = await query<RowDataPacket[]>(
      `SELECT sm.local_id, sm.movement_date_utc, sm.movement_type, sm.quantity,
              sm.balance_after, sm.unit_cost, sm.reference_type, sm.reference_id,
              sm.reference_number, sm.remarks, sm.medicine_batch_local_id,
              b.batch_number
       FROM stock_movements sm
       LEFT JOIN medicine_batches b
         ON b.store_id=sm.store_id AND b.local_id=sm.medicine_batch_local_id
       WHERE sm.store_id=? AND sm.medicine_local_id=? AND sm.is_deleted=0
       ORDER BY sm.movement_date_utc DESC, sm.local_id DESC
       LIMIT 800`,
      [storeId, medicineLocalId]
    );
  } catch {
    movements = [];
  }

  type LedgerRow = {
    id: string;
    date: string | Date;
    type: string;
    batch: string;
    qty: number;
    balance: number | null;
    unitCost: number;
    amount: number;
    reference: string;
    party: string;
    href?: string;
  };

  let ledger: LedgerRow[] = [];

  if (movements.length > 0) {
    ledger = movements.map((m) => {
      const qty = Number(m.quantity ?? 0);
      const type = MOVEMENT_LABEL[Number(m.movement_type)] || `Type ${m.movement_type}`;
      let href: string | undefined;
      const refType = String(m.reference_type || "").toLowerCase();
      const refId = Number(m.reference_id || 0);
      if (refId > 0 && (refType.includes("sale") || Number(m.movement_type) === 1)) {
        href = `/sales/${storeId}/${refId}`;
      } else if (refId > 0 && (refType.includes("purchase") || Number(m.movement_type) === 0)) {
        href = `/purchases/${storeId}/${refId}`;
      }
      return {
        id: `m:${m.local_id}`,
        date: m.movement_date_utc,
        type,
        batch: String(m.batch_number || "—"),
        qty,
        balance: m.balance_after != null ? Number(m.balance_after) : null,
        unitCost: Number(m.unit_cost ?? 0),
        amount: Math.abs(qty) * Number(m.unit_cost ?? 0),
        reference: String(m.reference_number || m.remarks || "—"),
        party: "",
        href,
      };
    });
  } else {
    // Document history fallback (MedWin-style when movements missing)
    const [purchases, sales] = await Promise.all([
      query<RowDataPacket[]>(
        `SELECT pi.local_id, p.local_id AS purchase_local_id, p.invoice_number, p.invoice_date,
                pi.batch_number, pi.quantity, pi.free_quantity, pi.purchase_price, pi.line_total,
                COALESCE(s.name, CONCAT('Supplier #', p.supplier_local_id)) AS party
         FROM purchase_items pi
         JOIN purchases p ON p.store_id=pi.store_id AND p.local_id=pi.purchase_local_id AND p.is_deleted=0
         LEFT JOIN suppliers s
           ON s.store_id=p.store_id AND s.local_id=p.supplier_local_id AND s.is_deleted=0
         WHERE pi.store_id=? AND pi.medicine_local_id=? AND pi.is_deleted=0
         ORDER BY p.invoice_date DESC
         LIMIT 400`,
        [storeId, medicineLocalId]
      ).catch(() => [] as RowDataPacket[]),
      query<RowDataPacket[]>(
        `SELECT si.local_id, s.local_id AS sale_local_id, s.invoice_number, s.invoice_date,
                si.batch_number, si.quantity, si.unit_price, si.line_total,
                COALESCE(s.billing_customer_name, 'Walk-in') AS party
         FROM sale_items si
         JOIN sales s ON s.store_id=si.store_id AND s.local_id=si.sale_local_id AND s.is_deleted=0
         WHERE si.store_id=? AND si.medicine_local_id=? AND si.is_deleted=0
         ORDER BY s.invoice_date DESC
         LIMIT 400`,
        [storeId, medicineLocalId]
      ).catch(() => [] as RowDataPacket[]),
    ]);

    ledger = [
      ...purchases.map((r) => ({
        id: `p:${r.local_id}`,
        date: r.invoice_date as string,
        type: "Purchase In",
        batch: String(r.batch_number || "—"),
        qty: Number(r.quantity ?? 0) + Number(r.free_quantity ?? 0),
        balance: null as number | null,
        unitCost: Number(r.purchase_price ?? 0),
        amount: Number(r.line_total ?? 0),
        reference: String(r.invoice_number || "—"),
        party: String(r.party || ""),
        href: `/purchases/${storeId}/${r.purchase_local_id}`,
      })),
      ...sales.map((r) => ({
        id: `s:${r.local_id}`,
        date: r.invoice_date as string,
        type: "Sale Out",
        batch: String(r.batch_number || "—"),
        qty: -Number(r.quantity ?? 0),
        balance: null as number | null,
        unitCost: Number(r.unit_price ?? 0),
        amount: Number(r.line_total ?? 0),
        reference: String(r.invoice_number || "—"),
        party: String(r.party || ""),
        href: `/sales/${storeId}/${r.sale_local_id}`,
      })),
    ].sort((a, b) => String(b.date).localeCompare(String(a.date)));
  }

  return {
    medicine: {
      storeId,
      localId: medicineLocalId,
      name: String(med.name),
      generic: String(med.generic_name || ""),
      brand: String(med.brand || ""),
      composition: String(med.composition || ""),
      strength: String(med.strength || ""),
      hsn: String(med.hsn_code || ""),
      gst: Number(med.gst_percent ?? 0),
      mrp: Number(med.mrp ?? 0),
      purchasePrice: Number(med.purchase_price ?? 0),
      sellingPrice: Number(med.selling_price ?? 0),
      reorderLevel: Number(med.reorder_level ?? 0),
      stockQty,
      stockCost,
    },
    batches,
    ledger,
  };
}
