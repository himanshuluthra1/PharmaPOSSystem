import { requirePermission } from "@/lib/auth";
import { listStock } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { Card, DataTable, KpiCard } from "@/components/Ui";
import { MedicineLink, StockValue } from "@/components/DrillDown";
import { fmtDateOnly, rupeeShort } from "@/lib/format";
import { stockStoreTotals } from "@/lib/medwin/aggregates";

export const dynamic = "force-dynamic";

export default async function StockPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const user = await requirePermission(PERMISSIONS.stock);
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q : undefined;
  const [totals, items] = await Promise.all([
    stockStoreTotals(user),
    listStock(user, "all", q),
  ]);
  const netCost = totals.reduce((a, t) => a + t.costValue, 0);

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <KpiCard
          compact
          label="Net stock (cost)"
          value={
            <StockValue filter="all" title="Stock details">
              {rupeeShort(netCost)}
            </StockValue>
          }
          tone="primary"
        />
        <KpiCard
          compact
          label="Expired batches"
          value={
            <StockValue filter="expired" title="Expired stock">
              {String(totals.reduce((a, t) => a + t.expiredCnt, 0))}
            </StockValue>
          }
          tone="danger"
        />
        <KpiCard
          compact
          label="Near 3m"
          value={
            <StockValue filter="near3m" title="Near expiry — 3 months">
              {String(totals.reduce((a, t) => a + t.near3m, 0))}
            </StockValue>
          }
          tone="warning"
        />
        <KpiCard
          compact
          label="Near 12m"
          value={
            <StockValue filter="near12m" title="Near expiry — 12 months">
              {String(totals.reduce((a, t) => a + t.near12m, 0))}
            </StockValue>
          }
          tone="orange"
        />
      </div>

      <Card title="Stock by store" padded={false}>
        <DataTable
          embedded
          dense
          columns={[
            { key: "n", label: "#" },
            { key: "store", label: "Store" },
            { key: "cost", label: "Net stock (cost)", className: "text-right" },
            { key: "exp", label: "Expired", className: "text-right" },
            { key: "m1", label: "Near 1M", className: "text-right" },
            { key: "m3", label: "Near 3M", className: "text-right" },
            { key: "m6", label: "Near 6M", className: "text-right" },
            { key: "m12", label: "Near 12M", className: "text-right" },
          ]}
          rows={totals.map((t, i) => ({
            n: i + 1,
            store: t.shopName,
            cost: (
              <StockValue filter="all" storeId={t.storeId} title={`Stock — ${t.shopName}`}>
                {rupeeShort(t.costValue)}
              </StockValue>
            ),
            exp: (
              <StockValue filter="expired" storeId={t.storeId} title={`Expired — ${t.shopName}`}>
                {`${t.expiredCnt} · ${rupeeShort(t.expiredCost)}`}
              </StockValue>
            ),
            m1: (
              <StockValue filter="near1m" storeId={t.storeId} title={`Near 1M — ${t.shopName}`}>
                {`${t.near1m} · ${rupeeShort(t.near1mCost)}`}
              </StockValue>
            ),
            m3: (
              <StockValue filter="near3m" storeId={t.storeId} title={`Near 3M — ${t.shopName}`}>
                {`${t.near3m} · ${rupeeShort(t.near3mCost)}`}
              </StockValue>
            ),
            m6: (
              <StockValue filter="near6m" storeId={t.storeId} title={`Near 6M — ${t.shopName}`}>
                {`${t.near6m} · ${rupeeShort(t.near6mCost)}`}
              </StockValue>
            ),
            m12: (
              <StockValue filter="near12m" storeId={t.storeId} title={`Near 12M — ${t.shopName}`}>
                {`${t.near12m} · ${rupeeShort(t.near12mCost)}`}
              </StockValue>
            ),
          }))}
        />
      </Card>

      <Card
        title="Stock items"
        actions={
          <form className="print-hide">
            <input
              name="q"
              defaultValue={q || ""}
              placeholder="Search item / company"
              className="rounded-md border border-slate-200 px-2 py-1 text-sm"
            />
          </form>
        }
      >
        <DataTable
          dense
          columns={[
            { key: "item", label: "Item" },
            { key: "batch", label: "Batch" },
            { key: "expiry", label: "Expiry" },
            { key: "qty", label: "Qty", className: "text-right" },
            { key: "mrp", label: "MRP ₹", className: "text-right" },
            { key: "cost", label: "Cost ₹", className: "text-right" },
            { key: "status", label: "Status" },
          ]}
          rows={items.slice(0, 200).map((b) => {
            const exp = b.expiry_date ? new Date(b.expiry_date as string) : null;
            const today = new Date();
            let status = "OK";
            let cls = "bg-emerald-50 text-emerald-700";
            if (exp && exp < today) {
              status = "Expired";
              cls = "bg-rose-50 text-rose-700";
            } else if (exp && exp <= new Date(today.getTime() + 90 * 86400000)) {
              status = "Near";
              cls = "bg-amber-50 text-amber-700";
            }
            return {
              item: (
                <div>
                  <MedicineLink
                    storeId={String(b.store_id)}
                    medicineLocalId={Number(b.medicine_local_id)}
                  >
                    {String(b.medicine_name || "Item")}
                  </MedicineLink>
                  <div className="text-xs text-slate-400">{String(b.generic_name || "")}</div>
                </div>
              ),
              batch: String(b.batch_number || "—"),
              expiry: fmtDateOnly(b.expiry_date as string),
              qty: Number(b.quantity_available),
              mrp: rupeeShort(Number(b.mrp)),
              cost: (
                <StockValue filter="all" storeId={String(b.store_id)} title="Stock details">
                  {rupeeShort(Number(b.quantity_available) * Number(b.purchase_price))}
                </StockValue>
              ),
              status: (
                <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${cls}`}>
                  {status}
                </span>
              ),
            };
          })}
        />
      </Card>
    </div>
  );
}
