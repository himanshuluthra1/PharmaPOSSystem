import { notFound } from "next/navigation";
import { requirePermission } from "@/lib/auth";
import { getPurchaseDetail } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { DataTable, PageHeader } from "@/components/Ui";
import { fmtDate, inr } from "@/lib/format";

export const dynamic = "force-dynamic";

export default async function PurchaseDetailPage({
  params,
}: {
  params: Promise<{ storeId: string; localId: string }>;
}) {
  const user = await requirePermission(PERMISSIONS.purchases);
  const { storeId, localId } = await params;
  const detail = await getPurchaseDetail(user, storeId, Number(localId));
  if (!detail) notFound();
  const { purchase, items, supplierName } = detail;

  return (
    <div>
      <PageHeader
        title={String(purchase.invoice_number)}
        subtitle={`${fmtDate(purchase.invoice_date as string)} · ${supplierName || "Supplier"}`}
      />
      <div className="mb-4 grid grid-cols-2 gap-3 md:grid-cols-4">
        <div className="rounded-xl border bg-white p-3 text-sm">
          <div className="text-slate-500">Grand total</div>
          <div className="text-lg font-semibold">{inr(Number(purchase.grand_total))}</div>
        </div>
        <div className="rounded-xl border bg-white p-3 text-sm">
          <div className="text-slate-500">Paid</div>
          <div className="text-lg font-semibold">{inr(Number(purchase.paid_amount))}</div>
        </div>
        <div className="rounded-xl border bg-white p-3 text-sm">
          <div className="text-slate-500">Supplier bill</div>
          <div className="truncate text-lg font-semibold">
            {String(purchase.supplier_invoice_number || "—")}
          </div>
        </div>
        <div className="rounded-xl border bg-white p-3 text-sm">
          <div className="text-slate-500">Store</div>
          <div className="truncate text-lg font-semibold">{storeId}</div>
        </div>
      </div>
      <DataTable
        columns={[
          { key: "item", label: "Item" },
          { key: "batch", label: "Batch" },
          { key: "qty", label: "Qty", className: "text-right" },
          { key: "free", label: "Free", className: "text-right" },
          { key: "rate", label: "Cost", className: "text-right" },
          { key: "amount", label: "Amount", className: "text-right" },
        ]}
        rows={items.map((i) => ({
          item: String(i.medicine_name || `#${i.medicine_local_id}`),
          batch: String(i.batch_number || "—"),
          qty: Number(i.quantity).toFixed(2),
          free: Number(i.free_quantity || 0).toFixed(2),
          rate: inr(Number(i.purchase_price)),
          amount: inr(Number(i.line_total)),
        }))}
      />
    </div>
  );
}
