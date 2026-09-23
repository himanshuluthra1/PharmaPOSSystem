import Link from "next/link";
import { requirePermission } from "@/lib/auth";
import { PERMISSIONS } from "@/lib/session";
import { Card, DataTable } from "@/components/Ui";
import { MedicineLink, StockValue } from "@/components/DrillDown";
import { fmtDateOnly, rupeeShort } from "@/lib/format";
import { expiryItems, type ExpiryBucket } from "@/lib/medwin/aggregates";

export const dynamic = "force-dynamic";

const TABS: { id: ExpiryBucket; label: string; filter: string }[] = [
  { id: "expired", label: "Expired", filter: "expired" },
  { id: "1m", label: "Next 1 month", filter: "near1m" },
  { id: "3m", label: "Next 3 months", filter: "near3m" },
  { id: "6m", label: "Next 6 months", filter: "near6m" },
];

export default async function ExpiryPage({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const user = await requirePermission(PERMISSIONS.stock);
  const sp = await searchParams;
  const tab = (typeof sp.tab === "string" ? sp.tab : "expired") as ExpiryBucket;
  const bucket = TABS.some((t) => t.id === tab) ? tab : "expired";
  const tabMeta = TABS.find((t) => t.id === bucket)!;
  const rows = await expiryItems(user, bucket);

  return (
    <div className="space-y-4">
      <div className="print-hide inline-flex overflow-hidden rounded-lg border border-slate-200 bg-white text-sm">
        {TABS.map((t) => (
          <Link
            key={t.id}
            href={`/expiry?tab=${t.id}`}
            className={`px-3 py-1.5 ${
              bucket === t.id ? "bg-blue-600 text-white" : "text-slate-600 hover:bg-slate-50"
            }`}
          >
            {t.label}
          </Link>
        ))}
      </div>
      <Card
        title={
          <span className="inline-flex items-center gap-2">
            {tabMeta.label} (
            <StockValue filter={tabMeta.filter} title={tabMeta.label}>
              {rows.length}
            </StockValue>
            )
          </span>
        }
        padded={false}
      >
        <DataTable
          embedded
          dense
          columns={[
            { key: "n", label: "#" },
            { key: "item", label: "Item" },
            { key: "company", label: "Company" },
            { key: "qty", label: "Qty", className: "text-right" },
            { key: "mrp", label: "MRP ₹", className: "text-right" },
            { key: "cost", label: "Net cost ₹", className: "text-right" },
            { key: "expiry", label: "Expiry" },
          ]}
          rows={rows.map((r, i) => ({
            n: i + 1,
            item: (
              <MedicineLink
                storeId={String(r.store_id)}
                medicineLocalId={Number(r.medicine_local_id)}
              >
                {String(r.item_name)}
              </MedicineLink>
            ),
            company: String(r.company || "—"),
            qty: Number(r.qty),
            mrp: rupeeShort(Number(r.mrp)),
            cost: (
              <StockValue filter={tabMeta.filter} title={tabMeta.label}>
                {rupeeShort(Number(r.cost_value))}
              </StockValue>
            ),
            expiry: fmtDateOnly(r.expiry_date as string),
          }))}
        />
      </Card>
    </div>
  );
}
