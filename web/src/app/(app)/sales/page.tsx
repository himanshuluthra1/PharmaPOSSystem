import { requirePermission } from "@/lib/auth";
import { listSales } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { Card, DataTable, KpiCard, PeriodToolbar } from "@/components/Ui";
import { BillLink, DrillValue, MedicineLink } from "@/components/DrillDown";
import { SimpleBarChart, SimpleDonut } from "@/components/Charts";
import { fmtDate, paymentMethodLabel, rupeeShort } from "@/lib/format";
import {
  asYmd,
  daysInRange,
  monthBoundsFromYm,
  resolvePeriod,
  type PeriodMode,
} from "@/lib/periods";
import {
  metricsForRange,
  monthlySalesPurchase,
  paymodeForRange,
  salesByCompany,
  shopComparison,
  topSaleItems,
} from "@/lib/medwin/aggregates";

export const dynamic = "force-dynamic";

function parseMode(v: string | undefined): PeriodMode {
  if (v === "monthly" || v === "yearly" || v === "custom" || v === "daily") return v;
  return "monthly";
}

export default async function SalesPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const user = await requirePermission(PERMISSIONS.sales);
  const sp = await searchParams;
  const mode = parseMode(typeof sp.mode === "string" ? sp.mode : undefined);
  const offset = Number(sp.offset ?? 0) || 0;
  const from = typeof sp.from === "string" ? sp.from : undefined;
  const to = typeof sp.to === "string" ? sp.to : undefined;
  const q = typeof sp.q === "string" ? sp.q : undefined;
  const range = resolvePeriod(mode, offset, from, to);

  const [m, shops, paymode, monthly, topItems, companies, bills] = await Promise.all([
    metricsForRange(user, range, { includeStock: false }),
    shopComparison(user, range),
    paymodeForRange(user, range),
    monthlySalesPurchase(user, 12),
    topSaleItems(user, range, 15),
    salesByCompany(user, range, 10),
    listSales(user, { q }),
  ]);

  const days = daysInRange(range.from, range.to);
  const elapsed = Math.min(days, new Date().getDate());
  const proj = elapsed > 0 ? (m.sale / elapsed) * days : 0;
  const marginProj = elapsed > 0 ? (m.grossMargin / elapsed) * days : 0;
  const colors = ["#2563eb", "#059669", "#d97706", "#7c3aed", "#dc2626"];
  const d = (metric: "sale" | "grossMargin" | "saleBills" | "collection" | "purchase", title: string) => ({
    metric,
    from: range.from,
    to: range.to,
    title,
  });

  return (
    <div className="space-y-5">
      <PeriodToolbar mode={mode} offset={offset} basePath="/sales" from={from} to={to} showCustom />
      <p className="text-sm text-slate-500">Sales analysis · {range.label}</p>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4 lg:grid-cols-8">
        <KpiCard compact label="Sale" value={m.sale} tone="primary" drill={d("sale", "Sale")} />
        <KpiCard
          compact
          label="Gross margin"
          value={m.grossMargin}
          tone="success"
          drill={d("grossMargin", "Gross margin")}
        />
        <KpiCard
          compact
          label="% Margin"
          value={`${m.sale > 0 ? ((m.grossMargin / m.sale) * 100).toFixed(1) : "0.0"}%`}
          tone="teal"
          drill={d("grossMargin", "Gross margin")}
        />
        <KpiCard
          compact
          label="Bills"
          value={String(m.saleBills)}
          tone="slate"
          drill={d("saleBills", "Sale bills")}
        />
        <KpiCard
          compact
          label="Avg bill"
          value={m.saleBills > 0 ? m.sale / m.saleBills : 0}
          tone="purple"
          drill={d("sale", "Sale")}
        />
        <KpiCard
          compact
          label="Avg bill margin"
          value={m.saleBills > 0 ? m.grossMargin / m.saleBills : 0}
          tone="orange"
          drill={d("grossMargin", "Gross margin")}
        />
        <KpiCard compact label="Projection" value={proj} tone="primary" drill={d("sale", "Sale")} />
        <KpiCard
          compact
          label="Margin proj."
          value={marginProj}
          tone="success"
          drill={d("grossMargin", "Gross margin")}
        />
      </div>

      <Card title="Shop comparison" padded={false}>
        <DataTable
          embedded
          columns={[
            { key: "shop", label: "Shop" },
            { key: "revenue", label: "Revenue", className: "text-right" },
            { key: "margin", label: "Margin", className: "text-right" },
            { key: "pct", label: "Margin %", className: "text-right" },
            { key: "bills", label: "Bills", className: "text-right" },
            { key: "avg", label: "Avg bill", className: "text-right" },
            { key: "avgM", label: "Avg bill margin", className: "text-right" },
          ]}
          rows={shops.map((s) => ({
            shop: s.shopName,
            revenue: (
              <DrillValue metric="sale" from={range.from} to={range.to} title="Sale">
                {rupeeShort(s.revenue)}
              </DrillValue>
            ),
            margin: (
              <DrillValue metric="grossMargin" from={range.from} to={range.to} title="Gross margin">
                {rupeeShort(s.margin)}
              </DrillValue>
            ),
            pct: (
              <DrillValue metric="grossMargin" from={range.from} to={range.to} title="Gross margin">
                {`${s.marginPct.toFixed(1)}%`}
              </DrillValue>
            ),
            bills: (
              <DrillValue metric="saleBills" from={range.from} to={range.to} title="Sale bills">
                {s.bills}
              </DrillValue>
            ),
            avg: (
              <DrillValue metric="sale" from={range.from} to={range.to} title="Sale">
                {rupeeShort(s.avgBill)}
              </DrillValue>
            ),
            avgM: (
              <DrillValue metric="grossMargin" from={range.from} to={range.to} title="Gross margin">
                {rupeeShort(s.avgBillMargin)}
              </DrillValue>
            ),
          }))}
        />
      </Card>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Monthly sales vs purchase" padded={false}>
          <div className="p-4">
            <SimpleBarChart
              labels={monthly.map((x) => x.month)}
              series={[
                { name: "Sales", values: monthly.map((x) => x.sales), color: "#2563eb" },
                { name: "Purchase", values: monthly.map((x) => x.purchases), color: "#f59e0b" },
              ]}
            />
          </div>
          <DataTable
            embedded
            dense
            columns={[
              { key: "month", label: "Month" },
              { key: "sales", label: "Sales", className: "text-right" },
              { key: "purchase", label: "Purchase", className: "text-right" },
            ]}
            rows={monthly.map((x) => {
              const b = monthBoundsFromYm(x.month);
              return {
                month: x.month,
                sales: (
                  <DrillValue metric="sale" from={b.from} to={b.to} title="Sale">
                    {rupeeShort(x.sales)}
                  </DrillValue>
                ),
                purchase: (
                  <DrillValue metric="purchase" from={b.from} to={b.to} title="Purchase">
                    {rupeeShort(x.purchases)}
                  </DrillValue>
                ),
              };
            })}
          />
        </Card>
        <Card title="Payment mode">
          <SimpleDonut
            slices={paymode.map((p, i) => ({
              label: paymentMethodLabel(p.method),
              value: p.amount,
              color: colors[i % colors.length],
            }))}
          />
          <div className="mt-4">
            <DataTable
              dense
              columns={[
                { key: "mode", label: "Mode" },
                { key: "bills", label: "Bills", className: "text-right" },
                { key: "sale", label: "Sale ₹", className: "text-right" },
              ]}
              rows={paymode.map((p) => ({
                mode: paymentMethodLabel(p.method),
                bills: (
                  <DrillValue metric="saleBills" from={range.from} to={range.to} title="Sale bills">
                    {p.bills}
                  </DrillValue>
                ),
                sale: (
                  <DrillValue metric="sale" from={range.from} to={range.to} title="Sale">
                    {rupeeShort(p.amount)}
                  </DrillValue>
                ),
              }))}
            />
          </div>
        </Card>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Sales by company (Top 10)" padded={false}>
          <DataTable
            embedded
            dense
            columns={[
              { key: "company", label: "Company" },
              { key: "qty", label: "Qty", className: "text-right" },
              { key: "rev", label: "Revenue ₹", className: "text-right" },
            ]}
            rows={companies.map((c) => ({
              company: String(c.company),
              qty: Number(c.qty),
              rev: (
                <DrillValue metric="sale" from={range.from} to={range.to} title="Sale">
                  {rupeeShort(Number(c.revenue))}
                </DrillValue>
              ),
            }))}
          />
        </Card>
        <Card title="Top selling items" padded={false}>
          <DataTable
            embedded
            dense
            columns={[
              { key: "n", label: "#" },
              { key: "item", label: "Item" },
              { key: "company", label: "Company" },
              { key: "qty", label: "Qty", className: "text-right" },
              { key: "rev", label: "Revenue ₹", className: "text-right" },
              { key: "bills", label: "Bills", className: "text-right" },
            ]}
            rows={topItems.map((r, i) => ({
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
              rev: (
                <DrillValue metric="sale" from={range.from} to={range.to} title="Sale">
                  {rupeeShort(Number(r.revenue))}
                </DrillValue>
              ),
              bills: (
                <DrillValue metric="saleBills" from={range.from} to={range.to} title="Sale bills">
                  {Number(r.bills)}
                </DrillValue>
              ),
            }))}
          />
        </Card>
      </div>

      <Card
        title="Recent bills"
        actions={
          <form className="print-hide">
            <input type="hidden" name="mode" value={mode} />
            <input type="hidden" name="offset" value={offset} />
            <input
              name="q"
              defaultValue={q || ""}
              placeholder="Search invoice / customer"
              className="rounded-md border border-slate-200 px-2 py-1 text-sm"
            />
          </form>
        }
      >
        <DataTable
          columns={[
            { key: "invoice", label: "Invoice" },
            { key: "customer", label: "Customer" },
            { key: "date", label: "Date" },
            { key: "paid", label: "Paid", className: "text-right" },
            { key: "total", label: "Total", className: "text-right" },
          ]}
          rows={bills.map((s) => {
            const day = asYmd(s.invoice_date as string);
            return {
              invoice: (
                <BillLink kind="sale" storeId={String(s.store_id)} localId={Number(s.local_id)}>
                  {String(s.invoice_number)}
                </BillLink>
              ),
              customer: String(s.billing_customer_name || "Walk-in"),
              date: fmtDate(s.invoice_date as string),
              paid: (
                <DrillValue metric="collection" from={day} to={day} title="Collection">
                  {rupeeShort(Number(s.paid_amount))}
                </DrillValue>
              ),
              total: (
                <DrillValue metric="sale" from={day} to={day} title="Sale">
                  {rupeeShort(Number(s.grand_total))}
                </DrillValue>
              ),
            };
          })}
        />
      </Card>
    </div>
  );
}
