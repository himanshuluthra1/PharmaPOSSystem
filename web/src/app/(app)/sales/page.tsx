import Link from "next/link";
import { requirePermission } from "@/lib/auth";
import { listSales } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { DataTable, PageHeader } from "@/components/Ui";
import { fmtDate, inr } from "@/lib/format";

export const dynamic = "force-dynamic";

export default async function SalesPage({
  searchParams,
}: {
  searchParams: Promise<{ q?: string }>;
}) {
  const user = await requirePermission(PERMISSIONS.sales);
  const sp = await searchParams;
  const rows = await listSales(user, { q: sp.q });

  return (
    <div>
      <PageHeader title="Sales" subtitle="Read-only invoices synced from shops" />
      <form className="mb-4">
        <input
          name="q"
          defaultValue={sp.q || ""}
          placeholder="Search invoice, customer, phone…"
          className="w-full max-w-md rounded-lg border border-slate-300 px-3 py-2 text-sm"
        />
      </form>
      <DataTable
        columns={[
          { key: "invoice", label: "Invoice" },
          { key: "customer", label: "Customer" },
          { key: "date", label: "Date" },
          { key: "paid", label: "Paid", className: "text-right" },
          { key: "total", label: "Total", className: "text-right" },
        ]}
        rows={rows.map((s) => ({
          invoice: (
            <Link
              className="font-medium text-teal-700 hover:underline"
              href={`/sales/${s.store_id}/${s.local_id}`}
            >
              {String(s.invoice_number)}
            </Link>
          ),
          customer: (
            <div>
              <div>{String(s.billing_customer_name || "Walk-in")}</div>
              <div className="text-xs text-slate-500">
                {String(s.billing_customer_phone || "")}
              </div>
            </div>
          ),
          date: fmtDate(s.invoice_date as string),
          paid: inr(Number(s.paid_amount)),
          total: inr(Number(s.grand_total)),
        }))}
      />
    </div>
  );
}
