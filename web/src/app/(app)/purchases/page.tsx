import Link from "next/link";
import { requirePermission } from "@/lib/auth";
import { listPurchases } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { DataTable, PageHeader } from "@/components/Ui";
import { fmtDate, inr } from "@/lib/format";

export const dynamic = "force-dynamic";

export default async function PurchasesPage({
  searchParams,
}: {
  searchParams: Promise<{ q?: string }>;
}) {
  const user = await requirePermission(PERMISSIONS.purchases);
  const sp = await searchParams;
  const rows = await listPurchases(user, { q: sp.q });

  return (
    <div>
      <PageHeader title="Purchases" subtitle="Goods received from suppliers" />
      <form className="mb-4">
        <input
          name="q"
          defaultValue={sp.q || ""}
          placeholder="Search invoice or supplier…"
          className="w-full max-w-md rounded-lg border border-slate-300 px-3 py-2 text-sm"
        />
      </form>
      <DataTable
        columns={[
          { key: "invoice", label: "Invoice" },
          { key: "supplier", label: "Supplier" },
          { key: "date", label: "Date" },
          { key: "paid", label: "Paid", className: "text-right" },
          { key: "total", label: "Total", className: "text-right" },
        ]}
        rows={rows.map((p) => ({
          invoice: (
            <Link
              className="font-medium text-teal-700 hover:underline"
              href={`/purchases/${p.store_id}/${p.local_id}`}
            >
              {String(p.invoice_number)}
            </Link>
          ),
          supplier: String(p.supplier_name || p.supplier_invoice_number || "—"),
          date: fmtDate(p.invoice_date as string),
          paid: inr(Number(p.paid_amount)),
          total: inr(Number(p.grand_total)),
        }))}
      />
    </div>
  );
}
