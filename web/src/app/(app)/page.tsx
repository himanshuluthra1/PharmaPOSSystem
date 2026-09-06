import Link from "next/link";
import { requirePermission } from "@/lib/auth";
import { getDashboardKpis } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { DataTable, KpiCard, PageHeader } from "@/components/Ui";
import { fmtDate, inr, paymentMethodLabel } from "@/lib/format";

export const dynamic = "force-dynamic";

export default async function DashboardPage() {
  const user = await requirePermission(PERMISSIONS.dashboard);
  const kpis = await getDashboardKpis(user);

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
        <KpiCard label="MTD sales" value={kpis.mtdSales} href="/sales" />
        <KpiCard
          label="Today purchase"
          value={kpis.todayPurchases}
          href="/purchases"
        />
        <KpiCard label="MTD purchase" value={kpis.mtdPurchases} href="/purchases" />
        <KpiCard label="Stock value" value={kpis.stockValue} href="/stock" />
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
