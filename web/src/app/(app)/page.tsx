import Link from "next/link";
import type { ReactNode } from "react";
import { requirePermission } from "@/lib/auth";
import { PERMISSIONS } from "@/lib/session";
import { Card, KpiCard, DataTable } from "@/components/Ui";
import { DrillValue, StockValue } from "@/components/DrillDown";
import { SimpleBarChart, SimpleDonut } from "@/components/Charts";
import { paymentMethodLabel, rupeeShort } from "@/lib/format";
import { dayRange, fyRange, monthBoundsFromYm, monthRange } from "@/lib/periods";
import type { DrillMetric } from "@/lib/drilldown-types";
import {
  metricsForRange,
  monthlySalesPurchase,
  paymodeForRange,
  shopComparison,
  vendorOutstanding,
} from "@/lib/medwin/aggregates";

export const dynamic = "force-dynamic";

function PeriodBox({
  title,
  hrefPrev,
  hrefNext,
  from,
  to,
  m,
}: {
  title: string;
  hrefPrev: string;
  hrefNext: string;
  from: string;
  to: string;
  m: Awaited<ReturnType<typeof metricsForRange>>;
}) {
  const netProfit = m.grossMargin - m.expense;
  const drill = (metric: DrillMetric, label: string) => ({
    metric,
    from,
    to,
    title: label,
  });
  const rows: {
    k: string;
    metric?: DrillMetric;
    value: ReactNode;
    sub?: ReactNode;
  }[] = [
    {
      k: "Sale",
      metric: "sale",
      value: rupeeShort(m.sale),
      sub: (
        <DrillValue metric="saleBills" from={from} to={to} title="Sale bills">
          {`${m.saleBills} bills`}
        </DrillValue>
      ),
    },
    { k: "Profit (excl. GST cost)", metric: "grossMargin", value: rupeeShort(m.grossMargin) },
    { k: "GST", metric: "gst", value: rupeeShort(m.gst) },
    {
      k: "Purchase",
      metric: "purchaseNet",
      value: rupeeShort(m.purchaseNet),
      sub:
        m.purchaseReturns > 0 ? (
          <DrillValue metric="purchaseReturns" from={from} to={to} title="Purchase returns">
            {`Ret ${rupeeShort(m.purchaseReturns)}`}
          </DrillValue>
        ) : undefined,
    },
    { k: "Collection", metric: "collection", value: rupeeShort(m.collection) },
    { k: "Expense", metric: "expense", value: rupeeShort(m.expense) },
    { k: "Net profit", metric: "netProfit", value: rupeeShort(netProfit) },
    {
      k: "Stock (cost)",
      value: (
        <StockValue filter="all" title="Stock details">
          {rupeeShort(m.stockCost)}
        </StockValue>
      ),
      sub: (
        <StockValue filter="all" title="Stock details">
          {`MRP ${rupeeShort(m.stockMrp)}`}
        </StockValue>
      ),
    },
    {
      k: "Near expiry",
      value: (
        <StockValue filter="near3m" title="Near expiry — 3 months">
          {String(m.nearExpiry)}
        </StockValue>
      ),
      sub: (
        <StockValue filter="near3m" title="Near expiry — 3 months">
          {rupeeShort(m.nearExpiryCost)}
        </StockValue>
      ),
    },
  ];
  return (
    <Card
      title={title}
      actions={
        <div className="print-hide flex gap-1 text-sm">
          <Link href={hrefPrev} className="rounded border border-slate-200 px-2 py-0.5 hover:bg-white">
            ‹
          </Link>
          <Link href={hrefNext} className="rounded border border-slate-200 px-2 py-0.5 hover:bg-white">
            ›
          </Link>
        </div>
      }
    >
      <dl className="space-y-2 text-sm">
        {rows.map((row) => (
          <div key={row.k} className="flex items-start justify-between gap-3">
            <dt className="text-slate-500">{row.k}</dt>
            <dd className="text-right">
              <div className="font-semibold text-slate-900">
                {row.metric ? (
                  <DrillValue {...drill(row.metric, row.k)}>{row.value}</DrillValue>
                ) : (
                  row.value
                )}
              </div>
              {row.sub ? <div className="text-xs text-slate-400">{row.sub}</div> : null}
            </dd>
          </div>
        ))}
      </dl>
    </Card>
  );
}

export default async function OverviewPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const user = await requirePermission(PERMISSIONS.dashboard);
  const sp = await searchParams;
  const fyOff = Number(sp.fy ?? 0) || 0;
  const moOff = Number(sp.mo ?? 0) || 0;
  const dayOff = Number(sp.day ?? 0) || 0;

  const fy = fyRange(fyOff);
  const mo = monthRange(moOff);
  const day = dayRange(dayOff);

  const [fyM0, moM0, dayM, monthly, paymode, shops, vendorDue] = await Promise.all([
    metricsForRange(user, fy, { includeStock: false }),
    metricsForRange(user, mo, { includeStock: false }),
    metricsForRange(user, day, { includeStock: true }),
    monthlySalesPurchase(user, 12),
    paymodeForRange(user, mo),
    shopComparison(user, day),
    vendorOutstanding(user),
  ]);

  // Stock is a snapshot — reuse today's stock figures on FY/Month boxes.
  const fyM = {
    ...fyM0,
    stockCost: dayM.stockCost,
    stockMrp: dayM.stockMrp,
    expired: dayM.expired,
    nearExpiry: dayM.nearExpiry,
    nearExpiryCost: dayM.nearExpiryCost,
  };
  const moM = {
    ...moM0,
    stockCost: dayM.stockCost,
    stockMrp: dayM.stockMrp,
    expired: dayM.expired,
    nearExpiry: dayM.nearExpiry,
    nearExpiryCost: dayM.nearExpiryCost,
  };

  const payColors = ["#2563eb", "#059669", "#d97706", "#7c3aed", "#dc2626"];

  return (
    <div className="space-y-5">
      <div className="grid gap-4 lg:grid-cols-3">
        <PeriodBox
          title={fy.label}
          hrefPrev={`/?fy=${fyOff - 1}&mo=${moOff}&day=${dayOff}`}
          hrefNext={`/?fy=${fyOff + 1}&mo=${moOff}&day=${dayOff}`}
          from={fy.from}
          to={fy.to}
          m={fyM}
        />
        <PeriodBox
          title={mo.label}
          hrefPrev={`/?fy=${fyOff}&mo=${moOff - 1}&day=${dayOff}`}
          hrefNext={`/?fy=${fyOff}&mo=${moOff + 1}&day=${dayOff}`}
          from={mo.from}
          to={mo.to}
          m={moM}
        />
        <PeriodBox
          title={day.label}
          hrefPrev={`/?fy=${fyOff}&mo=${moOff}&day=${dayOff - 1}`}
          hrefNext={`/?fy=${fyOff}&mo=${moOff}&day=${dayOff + 1}`}
          from={day.from}
          to={day.to}
          m={dayM}
        />
      </div>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <KpiCard
          label="Vendor outstanding"
          value={vendorDue}
          tone="warning"
          drill={{ metric: "purchase", from: "2015-04-01", to: day.to, title: "Purchase (outstanding)" }}
        />
        <KpiCard
          label="This month expense"
          value={moM.expense}
          tone="orange"
          drill={{ metric: "expense", from: mo.from, to: mo.to, title: "Expense" }}
        />
        <KpiCard
          label="Net profit today"
          value={dayM.grossMargin - dayM.expense}
          tone="success"
          drill={{ metric: "netProfit", from: day.from, to: day.to, title: "Net profit" }}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Monthly revenue vs purchase" padded={false}>
          <div className="p-4">
            <SimpleBarChart
              labels={monthly.map((m) => m.month)}
              series={[
                { name: "Sales", values: monthly.map((m) => m.sales), color: "#2563eb" },
                { name: "Purchase", values: monthly.map((m) => m.purchases), color: "#f59e0b" },
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
        <Card title={`Payment mode — ${mo.label}`}>
          <SimpleDonut
            slices={paymode.map((p, i) => ({
              label: paymentMethodLabel(p.method),
              value: p.amount,
              color: payColors[i % payColors.length],
            }))}
          />
          <div className="mt-4">
            <DataTable
              dense
              columns={[
                { key: "mode", label: "Mode" },
                { key: "amount", label: "Amount", className: "text-right" },
              ]}
              rows={paymode.map((p) => ({
                mode: paymentMethodLabel(p.method),
                amount: (
                  <DrillValue metric="sale" from={mo.from} to={mo.to} title="Sale">
                    {rupeeShort(p.amount)}
                  </DrillValue>
                ),
              }))}
            />
          </div>
        </Card>
      </div>

      <Card title={`Shop comparison — ${day.label}`} padded={false}>
        <DataTable
          embedded
          columns={[
            { key: "shop", label: "Shop" },
            { key: "revenue", label: "Revenue", className: "text-right" },
            { key: "margin", label: "Margin", className: "text-right" },
            { key: "pct", label: "Margin %", className: "text-right" },
            { key: "bills", label: "Bills", className: "text-right" },
            { key: "avg", label: "Avg bill", className: "text-right" },
          ]}
          rows={shops.map((s) => ({
            shop: s.shopName,
            revenue: (
              <DrillValue metric="sale" from={day.from} to={day.to} title="Sale">
                {rupeeShort(s.revenue)}
              </DrillValue>
            ),
            margin: (
              <DrillValue metric="grossMargin" from={day.from} to={day.to} title="Gross margin">
                {rupeeShort(s.margin)}
              </DrillValue>
            ),
            pct: (
              <DrillValue metric="grossMargin" from={day.from} to={day.to} title="Gross margin">
                {`${s.marginPct.toFixed(1)}%`}
              </DrillValue>
            ),
            bills: (
              <DrillValue metric="saleBills" from={day.from} to={day.to} title="Sale bills">
                {s.bills}
              </DrillValue>
            ),
            avg: (
              <DrillValue metric="sale" from={day.from} to={day.to} title="Sale">
                {rupeeShort(s.avgBill)}
              </DrillValue>
            ),
          }))}
        />
      </Card>
    </div>
  );
}
