import { requirePermission } from "@/lib/auth";
import { PERMISSIONS } from "@/lib/session";
import { Card, DataTable, KpiCard, PeriodToolbar, signedMoney } from "@/components/Ui";
import { DrillValue, StockValue } from "@/components/DrillDown";
import { rupeeShort } from "@/lib/format";
import { resolvePeriod, type PeriodMode } from "@/lib/periods";
import { financeSeries, metricsForRange } from "@/lib/medwin/aggregates";

export const dynamic = "force-dynamic";

function parseMode(v: string | undefined): PeriodMode {
  if (v === "monthly" || v === "yearly" || v === "custom" || v === "daily") return v;
  return "monthly";
}

export default async function PnlPage({
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
  const netProfit = summary.grossMargin - summary.expense;

  return (
    <div>
      <PeriodToolbar mode={mode} offset={offset} basePath="/pnl" from={from} to={to} showCustom />
      <p className="mb-3 text-sm text-slate-500">Period: {range.label}</p>
      <div className="mb-4 grid grid-cols-2 gap-3 md:grid-cols-4">
        <KpiCard
          compact
          label="Gross profit"
          value={summary.grossMargin}
          tone="success"
          drill={{ metric: "grossMargin", from: range.from, to: range.to, title: "Gross profit" }}
        />
        <KpiCard
          compact
          label="Sale"
          value={summary.sale}
          tone="primary"
          drill={{ metric: "sale", from: range.from, to: range.to, title: "Sale" }}
        />
        <KpiCard
          compact
          label="Pending payment"
          value={Math.max(summary.purchaseNet - summary.supplierPayment, 0)}
          tone="warning"
          drill={{ metric: "purchaseNet", from: range.from, to: range.to, title: "Purchase net" }}
        />
        <KpiCard
          compact
          label="Net profit"
          value={netProfit}
          tone="teal"
          drill={{ metric: "netProfit", from: range.from, to: range.to, title: "Net profit" }}
        />
      </div>
      <Card title="Profit & Loss" padded={false}>
        <DataTable
          embedded
          dense
          columns={[
            { key: "period", label: "Period" },
            { key: "stock", label: "Stock cost", className: "text-right" },
            { key: "sale", label: "Sale", className: "text-right" },
            { key: "purchase", label: "Purchase", className: "text-right" },
            { key: "paid", label: "Total paid", className: "text-right" },
            { key: "expense", label: "Expense", className: "text-right" },
            { key: "gross", label: "Gross profit", className: "text-right" },
            { key: "net", label: "Net profit", className: "text-right" },
          ]}
          rows={rows.map((r) => ({
            period: r.label,
            stock: (
              <StockValue filter="all" title="Stock details">
                {rupeeShort(r.stockCost)}
              </StockValue>
            ),
            sale: (
              <DrillValue metric="sale" from={r.from} to={r.to} title="Sale">
                {rupeeShort(r.sale)}
              </DrillValue>
            ),
            purchase: (
              <DrillValue metric="purchaseNet" from={r.from} to={r.to} title="Purchase">
                {rupeeShort(r.purchaseNet)}
              </DrillValue>
            ),
            paid: (
              <DrillValue metric="supplierPayment" from={r.from} to={r.to} title="Supplier payment">
                {rupeeShort(r.supplierPayment)}
              </DrillValue>
            ),
            expense: (
              <DrillValue metric="expense" from={r.from} to={r.to} title="Expense">
                {rupeeShort(r.expense)}
              </DrillValue>
            ),
            gross: (
              <DrillValue metric="grossMargin" from={r.from} to={r.to} title="Gross profit">
                {signedMoney(r.grossMargin)}
              </DrillValue>
            ),
            net: (
              <DrillValue metric="netProfit" from={r.from} to={r.to} title="Net profit">
                {signedMoney(r.netProfit)}
              </DrillValue>
            ),
          }))}
        />
      </Card>
    </div>
  );
}
