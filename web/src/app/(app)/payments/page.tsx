import { requirePermission } from "@/lib/auth";
import { listPayments } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { DataTable, PageHeader } from "@/components/Ui";
import { fmtDate, inr, paymentMethodLabel } from "@/lib/format";

export const dynamic = "force-dynamic";

export default async function PaymentsPage() {
  const user = await requirePermission(PERMISSIONS.payments);
  const data = await listPayments(user);

  return (
    <div>
      <PageHeader
        title="Payments"
        subtitle="Sale tenders this month, recent payments, and customer dues"
      />

      <div className="mb-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
        {data.mix.map((m) => (
          <div
            key={String(m.method)}
            className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm"
          >
            <div className="text-xs uppercase text-slate-500">
              {paymentMethodLabel(Number(m.method))}
            </div>
            <div className="mt-1 text-xl font-semibold">{inr(Number(m.amount))}</div>
            <div className="text-xs text-slate-500">{Number(m.cnt)} payments</div>
          </div>
        ))}
      </div>

      <h2 className="mb-2 text-sm font-semibold">Recent payments</h2>
      <DataTable
        columns={[
          { key: "invoice", label: "Invoice" },
          { key: "customer", label: "Customer" },
          { key: "method", label: "Method" },
          { key: "date", label: "Date" },
          { key: "amount", label: "Amount", className: "text-right" },
        ]}
        rows={data.recent.map((p) => ({
          invoice: String(p.invoice_number),
          customer: String(p.billing_customer_name || "—"),
          method: paymentMethodLabel(Number(p.method)),
          date: fmtDate((p.payment_date_utc as string) || null),
          amount: inr(Number(p.amount)),
        }))}
      />

      <h2 className="mb-2 mt-6 text-sm font-semibold">Customer dues</h2>
      <DataTable
        columns={[
          { key: "name", label: "Customer" },
          { key: "phone", label: "Phone" },
          { key: "due", label: "Outstanding", className: "text-right" },
        ]}
        rows={data.dues.map((d) => ({
          name: String(d.name),
          phone: String(d.phone || "—"),
          due: inr(Number(d.outstanding_balance)),
        }))}
      />
    </div>
  );
}
