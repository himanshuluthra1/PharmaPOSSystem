import { notFound } from "next/navigation";
import { requirePermission } from "@/lib/auth";
import { getSaleDetail } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { DataTable, PageHeader } from "@/components/Ui";
import { fmtDate, inr, paymentMethodLabel } from "@/lib/format";

export const dynamic = "force-dynamic";

export default async function SaleDetailPage({
  params,
}: {
  params: Promise<{ storeId: string; localId: string }>;
}) {
  const user = await requirePermission(PERMISSIONS.sales);
  const { storeId, localId } = await params;
  const detail = await getSaleDetail(user, storeId, Number(localId));
  if (!detail) notFound();

  const { sale, items, payments } = detail;

  return (
    <div>
      <PageHeader
        title={String(sale.invoice_number)}
        subtitle={`${fmtDate(sale.invoice_date as string)} · ${String(sale.billing_customer_name || "Walk-in")}`}
      />
      <div className="mb-4 grid grid-cols-2 gap-3 md:grid-cols-4">
        <div className="rounded-xl border bg-white p-3 text-sm">
          <div className="text-slate-500">Grand total</div>
          <div className="text-lg font-semibold">{inr(Number(sale.grand_total))}</div>
        </div>
        <div className="rounded-xl border bg-white p-3 text-sm">
          <div className="text-slate-500">Paid</div>
          <div className="text-lg font-semibold">{inr(Number(sale.paid_amount))}</div>
        </div>
        <div className="rounded-xl border bg-white p-3 text-sm">
          <div className="text-slate-500">Discount</div>
          <div className="text-lg font-semibold">{inr(Number(sale.discount_amount))}</div>
        </div>
        <div className="rounded-xl border bg-white p-3 text-sm">
          <div className="text-slate-500">Store</div>
          <div className="truncate text-lg font-semibold">{storeId}</div>
        </div>
      </div>

      <h2 className="mb-2 text-sm font-semibold">Items</h2>
      <DataTable
        columns={[
          { key: "item", label: "Item" },
          { key: "batch", label: "Batch" },
          { key: "qty", label: "Qty", className: "text-right" },
          { key: "rate", label: "Rate", className: "text-right" },
          { key: "amount", label: "Amount", className: "text-right" },
        ]}
        rows={items.map((i) => ({
          item: String(i.medicine_name || `#${i.medicine_local_id}`),
          batch: String(i.batch_number || "—"),
          qty: Number(i.quantity).toFixed(2),
          rate: inr(Number(i.unit_price)),
          amount: inr(Number(i.line_total)),
        }))}
      />

      <h2 className="mb-2 mt-6 text-sm font-semibold">Payments</h2>
      <DataTable
        columns={[
          { key: "method", label: "Method" },
          { key: "ref", label: "Reference" },
          { key: "amount", label: "Amount", className: "text-right" },
        ]}
        rows={payments.map((p) => ({
          method: paymentMethodLabel(Number(p.method)),
          ref: String(p.reference_number || "—"),
          amount: inr(Number(p.amount)),
        }))}
      />
    </div>
  );
}
