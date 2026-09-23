import { requirePermission } from "@/lib/auth";
import { PERMISSIONS } from "@/lib/session";
import { Card, DataTable, KpiCard } from "@/components/Ui";
import { BillLink, DrillValue } from "@/components/DrillDown";
import { fmtDateOnly, rupeeShort } from "@/lib/format";
import { asYmd, toYmd } from "@/lib/periods";
import { billsPaymentsSummary } from "@/lib/medwin/aggregates";

export const dynamic = "force-dynamic";

export default async function BillsPaymentsPage() {
  const user = await requirePermission(PERMISSIONS.payments);
  const data = await billsPaymentsSummary(user);
  const from = "2015-04-01";
  const to = toYmd(new Date());

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <KpiCard
          compact
          label="Bills"
          value={String(data.bills)}
          tone="slate"
          drill={{ metric: "purchase", from, to, title: "Purchase bills" }}
        />
        <KpiCard
          compact
          label="Bill amount"
          value={data.billAmount}
          tone="primary"
          drill={{ metric: "purchase", from, to, title: "Purchase" }}
        />
        <KpiCard
          compact
          label="Supplier payment"
          value={data.paid}
          tone="success"
          drill={{ metric: "supplierPayment", from, to, title: "Supplier payment" }}
        />
        <KpiCard
          compact
          label="Balance due"
          value={data.balance}
          tone="warning"
          drill={{ metric: "purchase", from, to, title: "Purchase (balance)" }}
        />
      </div>

      <details open className="rounded-xl border border-slate-200 bg-white shadow-sm">
        <summary className="cursor-pointer px-4 py-3 text-sm font-semibold text-slate-800">
          Balance by vendor
        </summary>
        <div className="border-t border-slate-100 p-4">
          <DataTable
            dense
            columns={[
              { key: "vendor", label: "Vendor" },
              { key: "code", label: "Code" },
              { key: "bills", label: "Bills", className: "text-right" },
              { key: "shops", label: "Shops", className: "text-right" },
              { key: "balance", label: "Balance", className: "text-right" },
            ]}
            rows={data.byVendor.map((v) => ({
              vendor: String(v.vendor),
              code: String(v.code),
              bills: (
                <DrillValue metric="purchase" from={from} to={to} title="Purchase">
                  {Number(v.bills)}
                </DrillValue>
              ),
              shops: Number(v.shops),
              balance: (
                <DrillValue metric="purchase" from={from} to={to} title="Purchase">
                  {rupeeShort(Number(v.balance))}
                </DrillValue>
              ),
            }))}
          />
        </div>
      </details>

      <details open className="rounded-xl border border-slate-200 bg-white shadow-sm">
        <summary className="cursor-pointer px-4 py-3 text-sm font-semibold text-slate-800">
          Balance by shop
        </summary>
        <div className="border-t border-slate-100 p-4">
          <DataTable
            dense
            columns={[
              { key: "shop", label: "Shop" },
              { key: "vendors", label: "Vendors", className: "text-right" },
              { key: "bills", label: "Bills", className: "text-right" },
              { key: "balance", label: "Balance", className: "text-right" },
            ]}
            rows={data.byShop.map((s) => ({
              shop: s.shop_name,
              vendors: s.vendors,
              bills: (
                <DrillValue metric="purchase" from={from} to={to} title="Purchase">
                  {s.bills}
                </DrillValue>
              ),
              balance: (
                <DrillValue metric="purchase" from={from} to={to} title="Purchase">
                  {rupeeShort(s.balance)}
                </DrillValue>
              ),
            }))}
          />
        </div>
      </details>

      <Card title="Vendor bills" padded={false}>
        <DataTable
          embedded
          columns={[
            { key: "date", label: "Date" },
            { key: "vendor", label: "Vendor" },
            { key: "vendorBill", label: "Vendor bill no." },
            { key: "bill", label: "POS bill no." },
            { key: "amount", label: "Amount", className: "text-right" },
            { key: "paid", label: "Payment made", className: "text-right" },
            { key: "balance", label: "Balance", className: "text-right" },
            { key: "status", label: "Status" },
          ]}
          rows={data.billsList.map((b) => {
            const day = asYmd(b.invoice_date as string);
            return {
              date: fmtDateOnly(b.invoice_date as string),
              vendor: String(b.vendor),
              vendorBill: String(b.supplier_bill_number || "—"),
              bill: (
                <BillLink
                  kind="purchase"
                  storeId={String(b.store_id)}
                  localId={Number(b.local_id)}
                >
                  {String(b.invoice_number)}
                </BillLink>
              ),
              amount: (
                <DrillValue metric="purchase" from={day} to={day} title="Purchase">
                  {rupeeShort(Number(b.grand_total))}
                </DrillValue>
              ),
              paid: (
                <DrillValue metric="supplierPayment" from={day} to={day} title="Supplier payment">
                  {rupeeShort(Number(b.paid_amount))}
                </DrillValue>
              ),
              balance: (
                <DrillValue metric="purchase" from={day} to={day} title="Purchase">
                  {rupeeShort(Number(b.balance))}
                </DrillValue>
              ),
              status: (
                <span
                  className={`rounded-full px-2 py-0.5 text-xs font-medium ${
                    b.status === "paid"
                      ? "bg-emerald-50 text-emerald-700"
                      : b.status === "partial"
                        ? "bg-amber-50 text-amber-700"
                        : "bg-rose-50 text-rose-700"
                  }`}
                >
                  {String(b.status)}
                </span>
              ),
            };
          })}
        />
      </Card>
    </div>
  );
}
