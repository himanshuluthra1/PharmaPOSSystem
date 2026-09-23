import { requirePermission } from "@/lib/auth";
import { listPurchases } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { Card, DataTable, KpiCard } from "@/components/Ui";
import { BillLink, DrillValue, MedicineLink } from "@/components/DrillDown";
import { SimpleBarChart } from "@/components/Charts";
import { fmtDate, rupeeShort } from "@/lib/format";
import { asYmd, fyRange, monthBoundsFromYm } from "@/lib/periods";
import {
  metricsForRange,
  monthlySalesPurchase,
  topPurchasedItems,
  topSuppliers,
} from "@/lib/medwin/aggregates";

export const dynamic = "force-dynamic";

export default async function PurchasesPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const user = await requirePermission(PERMISSIONS.purchases);
  const sp = await searchParams;
  const q = typeof sp.q === "string" ? sp.q : undefined;
  const fy = fyRange(0);

  const [m, monthly, suppliers, items, bills] = await Promise.all([
    metricsForRange(user, fy, { includeStock: false }),
    monthlySalesPurchase(user, 12),
    topSuppliers(user, 15, fy),
    topPurchasedItems(user, 20, fy),
    listPurchases(user, { q }),
  ]);

  return (
    <div className="space-y-5">
      <p className="text-sm text-slate-500">Purchase analysis · {fy.label}</p>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <KpiCard
          compact
          label="FY purchase"
          value={m.purchase}
          tone="warning"
          drill={{ metric: "purchase", from: fy.from, to: fy.to, title: "Purchase" }}
        />
        <KpiCard
          compact
          label="Purchase returns"
          value={m.purchaseReturns}
          tone="orange"
          drill={{ metric: "purchaseReturns", from: fy.from, to: fy.to, title: "Purchase returns" }}
        />
        <KpiCard
          compact
          label="Purchase net"
          value={m.purchaseNet}
          tone="primary"
          drill={{ metric: "purchaseNet", from: fy.from, to: fy.to, title: "Purchase net" }}
        />
        <KpiCard
          compact
          label="Supplier payments"
          value={m.supplierPayment}
          tone="teal"
          drill={{ metric: "supplierPayment", from: fy.from, to: fy.to, title: "Supplier payment" }}
        />
      </div>

      <Card title="Monthly purchase" padded={false}>
        <div className="p-4">
          <SimpleBarChart
            labels={monthly.map((x) => x.month)}
            series={[
              { name: "Purchase", values: monthly.map((x) => x.purchases), color: "#f59e0b" },
              { name: "Sales", values: monthly.map((x) => x.sales), color: "#2563eb" },
            ]}
          />
        </div>
        <DataTable
          embedded
          dense
          columns={[
            { key: "month", label: "Month" },
            { key: "purchase", label: "Purchase", className: "text-right" },
            { key: "sales", label: "Sales", className: "text-right" },
          ]}
          rows={monthly.map((x) => {
            const b = monthBoundsFromYm(x.month);
            return {
              month: x.month,
              purchase: (
                <DrillValue metric="purchase" from={b.from} to={b.to} title="Purchase">
                  {rupeeShort(x.purchases)}
                </DrillValue>
              ),
              sales: (
                <DrillValue metric="sale" from={b.from} to={b.to} title="Sale">
                  {rupeeShort(x.sales)}
                </DrillValue>
              ),
            };
          })}
        />
      </Card>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Top suppliers" padded={false}>
          <DataTable
            embedded
            dense
            columns={[
              { key: "supplier", label: "Supplier" },
              { key: "bills", label: "Bills", className: "text-right" },
              { key: "amount", label: "Amount ₹", className: "text-right" },
            ]}
            rows={suppliers.map((s) => ({
              supplier: String(s.supplier),
              bills: (
                <DrillValue metric="purchase" from={fy.from} to={fy.to} title="Purchase">
                  {Number(s.bills)}
                </DrillValue>
              ),
              amount: (
                <DrillValue metric="purchase" from={fy.from} to={fy.to} title="Purchase">
                  {rupeeShort(Number(s.amount))}
                </DrillValue>
              ),
            }))}
          />
        </Card>
        <Card title="Top purchased items" padded={false}>
          <DataTable
            embedded
            dense
            columns={[
              { key: "n", label: "#" },
              { key: "item", label: "Item" },
              { key: "company", label: "Company" },
              { key: "qty", label: "Qty", className: "text-right" },
              { key: "amount", label: "Amount ₹", className: "text-right" },
              { key: "rate", label: "Avg rate", className: "text-right" },
              { key: "mrp", label: "Avg MRP", className: "text-right" },
            ]}
            rows={items.map((r, i) => ({
              n: i + 1,
              item: (
                <MedicineLink
                  storeId={String(r.store_id)}
                  medicineLocalId={Number(r.medicine_local_id)}
                >
                  {String(r.item_name)}
                </MedicineLink>
              ),
              company: String(r.company || "—"),
              qty: Number(r.qty),
              amount: (
                <DrillValue metric="purchase" from={fy.from} to={fy.to} title="Purchase">
                  {rupeeShort(Number(r.amount))}
                </DrillValue>
              ),
              rate: (
                <DrillValue metric="purchase" from={fy.from} to={fy.to} title="Purchase">
                  {rupeeShort(Number(r.avg_rate))}
                </DrillValue>
              ),
              mrp: rupeeShort(Number(r.avg_mrp)),
            }))}
          />
        </Card>
      </div>

      <Card
        title="Recent purchase bills"
        actions={
          <form className="print-hide">
            <input
              name="q"
              defaultValue={q || ""}
              placeholder="Search invoice / supplier"
              className="rounded-md border border-slate-200 px-2 py-1 text-sm"
            />
          </form>
        }
      >
        <DataTable
          columns={[
            { key: "invoice", label: "Invoice" },
            { key: "supplier", label: "Supplier" },
            { key: "date", label: "Date" },
            { key: "paid", label: "Paid", className: "text-right" },
            { key: "total", label: "Total", className: "text-right" },
          ]}
          rows={bills.map((p) => {
            const day = asYmd(p.invoice_date as string);
            return {
              invoice: (
                <BillLink
                  kind="purchase"
                  storeId={String(p.store_id)}
                  localId={Number(p.local_id)}
                >
                  {String(p.invoice_number)}
                </BillLink>
              ),
              supplier: String(p.supplier_name || "—"),
              date: fmtDate(p.invoice_date as string),
              paid: (
                <DrillValue metric="supplierPayment" from={day} to={day} title="Supplier payment">
                  {rupeeShort(Number(p.paid_amount))}
                </DrillValue>
              ),
              total: (
                <DrillValue metric="purchase" from={day} to={day} title="Purchase">
                  {rupeeShort(Number(p.grand_total))}
                </DrillValue>
              ),
            };
          })}
        />
      </Card>
    </div>
  );
}
