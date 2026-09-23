import { requirePermission } from "@/lib/auth";
import { PERMISSIONS } from "@/lib/session";
import { Card, DataTable, EmptyState, KpiCard } from "@/components/Ui";

export const dynamic = "force-dynamic";

export default async function AuditPage() {
  await requirePermission(PERMISSIONS.dashboard);

  return (
    <div className="space-y-5">
      <div className="print-hide flex flex-wrap gap-2 text-sm">
        <select className="rounded-md border border-slate-200 bg-white px-2 py-1.5" defaultValue="30">
          <option value="7">Last 7 days</option>
          <option value="30">Last 30 days</option>
          <option value="90">Last 90 days</option>
        </select>
        <select className="rounded-md border border-slate-200 bg-white px-2 py-1.5" defaultValue="all">
          <option value="all">All severity</option>
          <option value="high">High</option>
          <option value="medium">Medium</option>
          <option value="low">Low</option>
        </select>
        <select className="rounded-md border border-slate-200 bg-white px-2 py-1.5" defaultValue="all">
          <option value="all">All sources</option>
          <option value="sales">Sales</option>
          <option value="purchase">Purchase</option>
        </select>
      </div>

      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-6">
        <KpiCard compact label="Post-edit changes" value="0" tone="slate" />
        <KpiCard compact label="High severity" value="0" tone="danger" />
        <KpiCard compact label="Bill deletions" value="0" tone="warning" />
        <KpiCard compact label="Line removals" value="0" tone="orange" />
        <KpiCard compact label="Amount decreases" value="0" tone="purple" />
        <KpiCard compact label="Payment changes" value="0" tone="teal" />
      </div>

      <EmptyState
        title="Audit sync not available yet"
        detail="Medwin-style change logs require POS invoice-edit audit sync into MySQL. The layout matches Medwin; data will appear here once audit events are published from PharmaPOS."
      />

      <Card title="Change log" padded={false}>
        <DataTable
          embedded
          columns={[
            { key: "when", label: "When" },
            { key: "sev", label: "Severity" },
            { key: "source", label: "Source" },
            { key: "bill", label: "Bill #" },
            { key: "date", label: "Date" },
            { key: "event", label: "Event" },
            { key: "old", label: "Old" },
            { key: "neu", label: "New" },
            { key: "delta", label: "Δ" },
          ]}
          rows={[]}
        />
      </Card>

      <Card title="Shrinkage" padded={false}>
        <DataTable
          embedded
          columns={[
            { key: "when", label: "When" },
            { key: "bill", label: "Bill #" },
            { key: "event", label: "Event" },
            { key: "detail", label: "Detail" },
            { key: "delta", label: "Δ" },
          ]}
          rows={[]}
        />
      </Card>
    </div>
  );
}
