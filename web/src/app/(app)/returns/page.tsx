import { requirePermission } from "@/lib/auth";
import { listReturns } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { DataTable, PageHeader } from "@/components/Ui";
import { fmtDate, inr } from "@/lib/format";

export const dynamic = "force-dynamic";

export default async function ReturnsPage() {
  const user = await requirePermission(PERMISSIONS.returns);
  const data = await listReturns(user);

  return (
    <div>
      <PageHeader title="Returns" subtitle="Sale and purchase returns" />
      <h2 className="mb-2 text-sm font-semibold">Sale returns</h2>
      <DataTable
        columns={[
          { key: "number", label: "Return #" },
          { key: "date", label: "Date" },
          { key: "refund", label: "Refund", className: "text-right" },
          { key: "total", label: "Total", className: "text-right" },
        ]}
        rows={data.saleReturns.map((r) => ({
          number: String(r.return_number),
          date: fmtDate(r.return_date as string),
          refund: inr(Number(r.refund_amount)),
          total: inr(Number(r.grand_total)),
        }))}
      />
      <h2 className="mb-2 mt-6 text-sm font-semibold">Purchase returns</h2>
      <DataTable
        columns={[
          { key: "number", label: "Return #" },
          { key: "date", label: "Date" },
          { key: "total", label: "Total", className: "text-right" },
        ]}
        rows={data.purchaseReturns.map((r) => ({
          number: String(r.return_number),
          date: fmtDate(r.return_date as string),
          total: inr(Number(r.grand_total)),
        }))}
      />
    </div>
  );
}
