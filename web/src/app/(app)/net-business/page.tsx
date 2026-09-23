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

export default async function NetBusinessPage({
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
  const net = summary.collection - summary.supplierPayment - summary.expense;

  return (
    <div>
      <PeriodToolbar
        mode={mode}
        offset={offset}
        basePath="/net-business"
        from={from}
        to={to}
        showCustom
      />
      <p className="mb-3 text-sm text-slate-500">Period: {range.label}</p>
      <div className="mb-4 grid grid-cols-2 gap-3 md:grid-cols-5">
        <KpiCard
          compact
          label="+ Collection"
          value={summary.collection}
          tone="success"
          drill={{ metric: "collection", from: range.from, to: range.to, title: "Collection" }}
        />
        <KpiCard
          compact
          label="− Supplier payment"
          value={summary.supplierPayment}
          tone="warning"
          drill={{
            metric: "supplierPayment",
            from: range.from,
            to: range.to,
            title: "Supplier payment",
          }}
        />
        <KpiCard
          compact
          label="− Expense"
          value={summary.expense}
          tone="danger"
          drill={{ metric: "expense", from: range.from, to: range.to, title: "Expense" }}
        />
        <KpiCard
          compact
          label="Stock (snapshot)"
          value={
            <StockValue filter="all" title="Stock details">
              {rupeeShort(summary.stockCost)}
            </StockValue>
          }
          tone="purple"
        />
        <KpiCard
          compact
          label="Net business"
          value={net}
          tone="primary"
          drill={{ metric: "netCash", from: range.from, to: range.to, title: "Net business" }}
        />
      </div>
      <Card title="Net Business" padded={false}>
        <DataTable
          embedded
          dense
          columns={[
            { key: "period", label: "Period" },
            { key: "collection", label: "+ Collection", className: "text-right" },
            { key: "supplier", label: "− Supplier payment", className: "text-right" },
            { key: "expense", label: "− Expense", className: "text-right" },
            { key: "net", label: "Net business", className: "text-right" },
          ]}
          rows={rows.map((r) => ({
            period: r.label,
            collection: (
              <DrillValue metric="collection" from={r.from} to={r.to} title="Collection">
                {signedMoney(r.collection)}
              </DrillValue>
            ),
            supplier: (
              <DrillValue metric="supplierPayment" from={r.from} to={r.to} title="Supplier payment">
                {signedMoney(r.supplierPayment)}
              </DrillValue>
            ),
            expense: (
              <DrillValue metric="expense" from={r.from} to={r.to} title="Expense">
                {signedMoney(r.expense)}
              </DrillValue>
            ),
            net: (
              <DrillValue metric="netCash" from={r.from} to={r.to} title="Net business">
                {signedMoney(r.netBusiness)}
              </DrillValue>
            ),
          }))}
        />
      </Card>
    </div>
  );
}
