import { requirePermission } from "@/lib/auth";
import { PERMISSIONS } from "@/lib/session";
import { Card, DataTable, KpiCard, PeriodToolbar, StackedHead, signedMoney } from "@/components/Ui";
import { DrillValue, StockValue } from "@/components/DrillDown";
import { rupeeShort } from "@/lib/format";
import { resolvePeriod, type PeriodMode } from "@/lib/periods";
import { financeSeries, metricsForRange } from "@/lib/medwin/aggregates";

export const dynamic = "force-dynamic";

function parseMode(v: string | undefined): PeriodMode {
  if (v === "monthly" || v === "yearly" || v === "custom" || v === "daily") return v;
  return "monthly";
}

export default async function LedgerPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const user = await requirePermission(PERMISSIONS.dashboard);
  const sp = await searchParams;
  const mode = parseMode(typeof sp.mode === "string" ? sp.mode : undefined);
  const offset = Number(sp.offset ?? 0) || 0;
  const from = typeof sp.from === "string" ? sp.from : undefined;
  const to = typeof sp.to === "string" ? sp.to : undefined;
  const seriesMode = mode === "custom" ? "monthly" : mode;
  const range = resolvePeriod(mode, offset, from, to);
  const [summary, rows] = await Promise.all([
    metricsForRange(user, range, { includeStock: false }),
    financeSeries(user, seriesMode, mode === "daily" ? 7 : mode === "yearly" ? 4 : 6, offset),
  ]);

  const netCash = summary.collection - summary.supplierPayment - summary.expense;
  const stockCost = rows[0]?.stockCost ?? 0;
  const d = (metric: "sale" | "collection" | "purchaseNet" | "supplierPayment" | "expense" | "netCash", title: string) => ({
    metric,
    from: range.from,
    to: range.to,
    title,
  });

  return (
    <div>
      <PeriodToolbar mode={mode} offset={offset} basePath="/ledger" from={from} to={to} showCustom />
      <p className="mb-3 text-sm text-slate-500">Period: {range.label}</p>
      <div className="mb-4 grid grid-cols-2 gap-3 md:grid-cols-4 lg:grid-cols-7">
        <KpiCard compact label="Sale" value={summary.sale} tone="primary" drill={d("sale", "Sale")} />
        <KpiCard compact label="Collection" value={summary.collection} tone="success" drill={d("collection", "Collection")} />
        <KpiCard compact label="Purchase net" value={summary.purchaseNet} tone="warning" drill={d("purchaseNet", "Purchase net")} />
        <KpiCard compact label="Supplier payment" value={summary.supplierPayment} tone="orange" drill={d("supplierPayment", "Supplier payment")} />
        <KpiCard compact label="Expense" value={summary.expense} tone="danger" drill={d("expense", "Expense")} />
        <KpiCard compact label="Net cash" value={netCash} tone="teal" drill={d("netCash", "Net cash")} />
        <KpiCard
          compact
          label="Stock (cost)"
          value={
            <StockValue filter="all" title="Stock details">
              {rupeeShort(stockCost)}
            </StockValue>
          }
          tone="purple"
        />
      </div>
      <Card title="Ledger" padded={false}>
        <DataTable
          embedded
          dense
          columns={[
            { key: "period", label: "Period" },
            {
              key: "stock",
              label: <StackedHead lines={["Stock", "Cost"]} />,
              className: "text-right",
            },
            {
              key: "sale",
              label: <StackedHead lines={["Sale", "Cost", "Margin"]} />,
              className: "text-right",
            },
            {
              key: "purchase",
              label: <StackedHead lines={["Purchase", "Return", "Net"]} />,
              className: "text-right",
            },
            {
              key: "money",
              label: <StackedHead lines={["Collection", "Sup. pay", "Expense"]} />,
              className: "text-right",
            },
            {
              key: "net",
              label: <StackedHead lines={["Net cash"]} />,
              className: "text-right",
            },
          ]}
          rows={rows.map((r) => ({
            period: r.label,
            stock: (
              <StockValue filter="all" title="Stock details">
                {rupeeShort(r.stockCost)}
              </StockValue>
            ),
            sale: (
              <span className="inline-flex flex-col text-right">
                <DrillValue metric="sale" from={r.from} to={r.to} title="Sale">
                  {rupeeShort(r.sale)}
                </DrillValue>
                <DrillValue metric="saleCost" from={r.from} to={r.to} title="Sale cost" className="text-xs text-slate-400">
                  {rupeeShort(r.saleCost)}
                </DrillValue>
                <DrillValue metric="grossMargin" from={r.from} to={r.to} title="Gross margin">
                  {signedMoney(r.grossMargin)}
                </DrillValue>
              </span>
            ),
            purchase: (
              <span className="inline-flex flex-col text-right">
                <DrillValue metric="purchase" from={r.from} to={r.to} title="Purchase">
                  {rupeeShort(r.purchase)}
                </DrillValue>
                <DrillValue metric="purchaseReturns" from={r.from} to={r.to} title="Purchase returns" className="text-xs text-slate-400">
                  {rupeeShort(r.purchaseReturns)}
                </DrillValue>
                <DrillValue metric="purchaseNet" from={r.from} to={r.to} title="Purchase net">
                  {rupeeShort(r.purchaseNet)}
                </DrillValue>
              </span>
            ),
            money: (
              <span className="inline-flex flex-col text-right">
                <DrillValue metric="collection" from={r.from} to={r.to} title="Collection">
                  {rupeeShort(r.collection)}
                </DrillValue>
                <DrillValue metric="supplierPayment" from={r.from} to={r.to} title="Supplier payment">
                  {rupeeShort(r.supplierPayment)}
                </DrillValue>
                <DrillValue metric="expense" from={r.from} to={r.to} title="Expense">
                  {rupeeShort(r.expense)}
                </DrillValue>
              </span>
            ),
            net: (
              <DrillValue metric="netCash" from={r.from} to={r.to} title="Net cash">
                {signedMoney(r.netCash)}
              </DrillValue>
            ),
          }))}
        />
      </Card>
    </div>
  );
}
