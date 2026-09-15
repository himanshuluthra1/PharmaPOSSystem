import Link from "next/link";
import { requirePermission } from "@/lib/auth";
import { getDashboardKpis } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { DataTable, KpiCard, PageHeader } from "@/components/Ui";
import { fmtDate, inr, paymentMethodLabel } from "@/lib/format";

export const dynamic = "force-dynamic";

function profitClass(n: number) {
  if (n > 0) return "text-emerald-700 font-semibold";
  if (n < 0) return "text-rose-700 font-semibold";
  return "font-semibold text-slate-700";
}

export default async function DashboardPage() {
  const user = await requirePermission(PERMISSIONS.dashboard);
  const kpis = await getDashboardKpis(user);
  const shopRows = kpis.shopProfitToday;
  const maxRevenue = Math.max(...shopRows.map((s) => s.revenue), 1);

  const totals = shopRows.reduce(
    (acc, s) => {
      acc.revenue += s.revenue;
      acc.cost += s.cost;
      acc.profit += s.profit;
      acc.bills += s.bills;
      return acc;
    },
    { revenue: 0, cost: 0, profit: 0, bills: 0 }
  );
  const totalMarginPct =
    totals.revenue > 0 ? Math.round((totals.profit / totals.revenue) * 1000) / 10 : 0;

  return (
    <div>
      <PageHeader
        title="Dashboard"
        subtitle={
          kpis.lastSyncAt
            ? `Last sale sync ${fmtDate(kpis.lastSyncAt)}`
            : "Waiting for store sync"
        }
      />

      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <KpiCard
          label="Today sales"
          value={kpis.todaySales}
          hint={`${kpis.todayBills} bills`}
          href="/sales"
        />
        <KpiCard
          label="Profit today"
          value={kpis.todayProfit}
          hint={`Cost ${inr(kpis.todayCost)} · margin ${
            kpis.todaySales > 0
              ? `${((kpis.todayProfit / kpis.todaySales) * 100).toFixed(1)}%`
              : "—"
          }`}
          href="/sales"
        />
        <KpiCard label="MTD sales" value={kpis.mtdSales} href="/sales" />
        <KpiCard
          label="Today purchase"
          value={kpis.todayPurchases}
          href="/purchases"
        />
        <KpiCard label="MTD purchase" value={kpis.mtdPurchases} href="/purchases" />
        <KpiCard
          label="Stock value (cost)"
          value={kpis.stockValue}
          hint={`At purchase cost · MRP ${inr(kpis.stockValueMrp)}`}
          href="/stock"
        />
        <KpiCard
          label="Low / near / expired"
          value={`${kpis.lowStock} / ${kpis.nearExpiry} / ${kpis.expired}`}
          href="/stock"
        />
        <KpiCard label="Customer dues" value={kpis.customerDues} href="/payments" />
        <KpiCard
          label="Supplier payables"
          value={kpis.supplierPayables}
          href="/payments"
        />
      </div>

      <div className="mt-6">
        <div className="mb-3 flex flex-wrap items-end justify-between gap-2">
          <div>
            <h2 className="text-sm font-semibold text-slate-800">
              Profit today — all shops
            </h2>
            <p className="text-xs text-slate-500">
              Sale − stock cost (batch purchase price). Same layout as Medwin shop comparison.
            </p>
          </div>
        </div>
        <DataTable
          columns={[
            { key: "shop", label: "Shop" },
            { key: "revenue", label: "Revenue", className: "text-right" },
            { key: "profit", label: "Profit", className: "text-right" },
            { key: "margin", label: "Margin %", className: "text-right" },
            { key: "bills", label: "Bills", className: "text-right" },
            { key: "avgBill", label: "Avg bill", className: "text-right" },
            { key: "avgProfit", label: "Avg bill profit", className: "text-right" },
          ]}
          rows={[
            ...shopRows.map((s) => {
              const barW = Math.round((s.revenue / maxRevenue) * 100);
              return {
                shop: (
                  <div>
                    <div className="font-medium text-slate-800">{s.shopName}</div>
                    <div className="mt-1 h-1 w-28 overflow-hidden rounded bg-slate-100">
                      <div
                        className="h-full rounded bg-teal-600"
                        style={{ width: `${barW}%` }}
                      />
                    </div>
                  </div>
                ),
                revenue: inr(s.revenue),
                profit: <span className={profitClass(s.profit)}>{inr(s.profit)}</span>,
                margin: (
                  <span
                    className={
                      s.marginPct >= 20
                        ? "text-emerald-700"
                        : s.marginPct >= 10
                          ? "text-amber-600"
                          : "text-rose-600"
                    }
                  >
                    {s.marginPct.toFixed(1)}%
                  </span>
                ),
                bills: s.bills,
                avgBill: inr(s.avgBill),
                avgProfit: (
                  <span className={profitClass(s.avgBillProfit)}>{inr(s.avgBillProfit)}</span>
                ),
              };
            }),
            ...(shopRows.length > 1
              ? [
                  {
                    shop: <span className="font-semibold">All shops</span>,
                    revenue: <span className="font-semibold">{inr(totals.revenue)}</span>,
                    profit: (
                      <span className={`font-semibold ${profitClass(totals.profit)}`}>
                        {inr(totals.profit)}
                      </span>
                    ),
                    margin: (
                      <span className="font-semibold">{totalMarginPct.toFixed(1)}%</span>
                    ),
                    bills: <span className="font-semibold">{totals.bills}</span>,
                    avgBill: (
                      <span className="font-semibold">
                        {inr(totals.bills > 0 ? totals.revenue / totals.bills : 0)}
                      </span>
                    ),
                    avgProfit: (
                      <span className={`font-semibold ${profitClass(totals.bills > 0 ? totals.profit / totals.bills : 0)}`}>
                        {inr(totals.bills > 0 ? totals.profit / totals.bills : 0)}
                      </span>
                    ),
                  },
                ]
              : []),
          ]}
        />
      </div>

      <div className="mt-6 grid gap-4 lg:grid-cols-3">
        <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm lg:col-span-1">
          <h2 className="mb-3 text-sm font-semibold text-slate-800">
            Today payment mix
          </h2>
          <ul className="space-y-2 text-sm">
            {kpis.paymentMix.length === 0 ? (
              <li className="text-slate-500">No payments today</li>
            ) : (
              kpis.paymentMix.map((p) => (
                <li key={p.method} className="flex justify-between">
                  <span>{paymentMethodLabel(p.method)}</span>
                  <span className="font-medium">{inr(p.amount)}</span>
                </li>
              ))
            )}
          </ul>
        </div>
        <div className="lg:col-span-2">
          <h2 className="mb-3 text-sm font-semibold text-slate-800">
            Recent sales
          </h2>
          <DataTable
            columns={[
              { key: "invoice", label: "Invoice" },
              { key: "customer", label: "Customer" },
              { key: "date", label: "Date" },
              { key: "amount", label: "Amount", className: "text-right" },
            ]}
            rows={kpis.recentSales.map((s) => ({
              invoice: (
                <Link
                  className="font-medium text-teal-700 hover:underline"
                  href={`/sales/${s.store_id}/${s.local_id}`}
                >
                  {String(s.invoice_number)}
                </Link>
              ),
              customer: String(s.billing_customer_name || "Walk-in"),
              date: fmtDate(s.invoice_date as string),
              amount: inr(Number(s.grand_total)),
            }))}
          />
        </div>
      </div>
    </div>
  );
}
