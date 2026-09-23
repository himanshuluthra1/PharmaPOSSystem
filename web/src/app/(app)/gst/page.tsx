import { requirePermission } from "@/lib/auth";
import { PERMISSIONS } from "@/lib/session";
import { Card, DataTable, KpiCard } from "@/components/Ui";
import { DrillValue } from "@/components/DrillDown";
import { SimpleBarChart, SimpleDonut } from "@/components/Charts";
import { rupeeShort } from "@/lib/format";
import { monthBoundsFromYm, monthRange } from "@/lib/periods";
import { gstBySlab, purchaseReturnsMonthly } from "@/lib/medwin/aggregates";

export const dynamic = "force-dynamic";

const COLORS = ["#2563eb", "#059669", "#d97706", "#7c3aed", "#dc2626", "#0891b2"];

export default async function GstPage() {
  const user = await requirePermission(PERMISSIONS.sales);
  const range = monthRange(0);
  const [slabs, returns] = await Promise.all([
    gstBySlab(user, range),
    purchaseReturnsMonthly(user, 12),
  ]);
  const taxable = slabs.reduce((a, s) => a + Number(s.taxable), 0);
  const gst = slabs.reduce((a, s) => a + Number(s.gst), 0);

  return (
    <div className="space-y-5">
      <p className="text-sm text-slate-500">GST analysis · {range.label}</p>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3">
        <KpiCard
          compact
          label="Taxable amount"
          value={taxable}
          tone="primary"
          drill={{ metric: "sale", from: range.from, to: range.to, title: "Sale" }}
        />
        <KpiCard
          compact
          label="GST collected"
          value={gst}
          tone="success"
          drill={{ metric: "gst", from: range.from, to: range.to, title: "GST" }}
        />
        <KpiCard
          compact
          label="Effective %"
          value={`${taxable > 0 ? ((gst / taxable) * 100).toFixed(1) : "0.0"}%`}
          tone="teal"
          drill={{ metric: "gst", from: range.from, to: range.to, title: "GST" }}
        />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="GST by slab">
          <SimpleDonut
            slices={slabs.map((s, i) => ({
              label: `${Number(s.slab)}%`,
              value: Number(s.gst),
              color: COLORS[i % COLORS.length],
            }))}
          />
          <div className="mt-4">
            <DataTable
              dense
              columns={[
                { key: "slab", label: "GST slab" },
                { key: "taxable", label: "Taxable ₹", className: "text-right" },
                { key: "gst", label: "GST ₹", className: "text-right" },
                { key: "pct", label: "Effective %", className: "text-right" },
              ]}
              rows={slabs.map((s) => {
                const t = Number(s.taxable);
                const g = Number(s.gst);
                return {
                  slab: `${Number(s.slab)}%`,
                  taxable: (
                    <DrillValue metric="sale" from={range.from} to={range.to} title="Sale">
                      {rupeeShort(t)}
                    </DrillValue>
                  ),
                  gst: (
                    <DrillValue metric="gst" from={range.from} to={range.to} title="GST">
                      {rupeeShort(g)}
                    </DrillValue>
                  ),
                  pct: (
                    <DrillValue metric="gst" from={range.from} to={range.to} title="GST">
                      {`${t > 0 ? ((g / t) * 100).toFixed(1) : "0.0"}%`}
                    </DrillValue>
                  ),
                };
              })}
            />
          </div>
        </Card>
        <Card title="Purchase returns — monthly" padded={false}>
          <div className="p-4">
            <SimpleBarChart
              labels={returns.map((r) => String(r.ym))}
              series={[
                {
                  name: "Returns",
                  values: returns.map((r) => Number(r.amt)),
                  color: "#dc2626",
                },
              ]}
            />
          </div>
          <DataTable
            embedded
            dense
            columns={[
              { key: "month", label: "Month" },
              { key: "amt", label: "Returns", className: "text-right" },
            ]}
            rows={returns.map((r) => {
              const ym = String(r.ym);
              const b = monthBoundsFromYm(ym);
              return {
                month: ym,
                amt: (
                  <DrillValue metric="purchaseReturns" from={b.from} to={b.to} title="Purchase returns">
                    {rupeeShort(Number(r.amt))}
                  </DrillValue>
                ),
              };
            })}
          />
        </Card>
      </div>
    </div>
  );
}
