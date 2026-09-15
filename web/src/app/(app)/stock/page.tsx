import { requirePermission } from "@/lib/auth";
import { listStock } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { DataTable, PageHeader } from "@/components/Ui";
import { fmtDate, inr } from "@/lib/format";

export const dynamic = "force-dynamic";

export default async function StockPage({
  searchParams,
}: {
  searchParams: Promise<{ filter?: string; q?: string }>;
}) {
  const user = await requirePermission(PERMISSIONS.stock);
  const sp = await searchParams;
  const filter =
    sp.filter === "low" || sp.filter === "near" || sp.filter === "expired"
      ? sp.filter
      : "all";
  const rows = await listStock(user, filter, sp.q);

  return (
    <div>
      <PageHeader title="Stock" subtitle="Batch-wise quantity synced from shops" />
      <div className="mb-4 flex flex-wrap gap-2">
        {[
          ["all", "All"],
          ["low", "Low stock"],
          ["near", "Near expiry"],
          ["expired", "Expired"],
        ].map(([key, label]) => (
          <a
            key={key}
            href={`/stock?filter=${key}${sp.q ? `&q=${encodeURIComponent(sp.q)}` : ""}`}
            className={`rounded-full px-3 py-1 text-sm ${
              filter === key
                ? "bg-teal-700 text-white"
                : "border border-slate-200 bg-white text-slate-600"
            }`}
          >
            {label}
          </a>
        ))}
      </div>
      <form className="mb-4">
        <input type="hidden" name="filter" value={filter} />
        <input
          name="q"
          defaultValue={sp.q || ""}
          placeholder="Search medicine, batch, rack…"
          className="w-full max-w-md rounded-lg border border-slate-300 px-3 py-2 text-sm"
        />
      </form>
      <DataTable
        columns={[
          { key: "medicine", label: "Medicine" },
          { key: "batch", label: "Batch" },
          { key: "expiry", label: "Expiry" },
          { key: "qty", label: "Qty", className: "text-right" },
          { key: "value", label: "Cost value", className: "text-right" },
          { key: "mrpValue", label: "MRP value", className: "text-right" },
          { key: "rack", label: "Rack" },
        ]}
        rows={rows.map((r) => ({
          medicine: (
            <div>
              <div className="font-medium">{String(r.medicine_name)}</div>
              <div className="text-xs text-slate-500">
                {String(r.generic_name || "")}
              </div>
            </div>
          ),
          batch: String(r.batch_number),
          expiry: r.expiry_date
            ? fmtDate(String(r.expiry_date)).split(",")[0]
            : "—",
          qty: Number(r.quantity_available).toFixed(2),
          value: inr(
            Number(r.quantity_available) * Number(r.purchase_price)
          ),
          mrpValue: inr(Number(r.quantity_available) * Number(r.mrp)),
          rack: String(r.rack_number || "—"),
        }))}
      />
    </div>
  );
}
