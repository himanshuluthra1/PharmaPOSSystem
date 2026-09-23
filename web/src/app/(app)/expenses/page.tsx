import { requirePermission } from "@/lib/auth";
import { listTenantStores } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { Card, DataTable, EmptyState, KpiCard } from "@/components/Ui";
import { DeleteButton, ExpenseForms } from "@/components/ExpenseForms";
import { DrillValue } from "@/components/DrillDown";
import { fmtDateOnly, rupeeShort } from "@/lib/format";
import { asYmd, monthRange } from "@/lib/periods";
import {
  collectionMonthTotals,
  expenseMonthTotals,
  listCollectionPayments,
  listExpenseHeads,
  listExpenses,
} from "@/lib/medwin/expenses";

export const dynamic = "force-dynamic";

export default async function ExpensesPage() {
  const user = await requirePermission(PERMISSIONS.payments);
  const mo = monthRange(0);
  const [heads, expenses, cps, expTot, cpTot, stores] = await Promise.all([
    listExpenseHeads(user.tenantId),
    listExpenses(user),
    listCollectionPayments(user),
    expenseMonthTotals(user),
    collectionMonthTotals(user),
    listTenantStores(user.tenantId),
  ]);

  const tablesMissing = heads.length === 0 && expenses.length === 0 && cps.length === 0;

  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3 md:grid-cols-3 lg:grid-cols-6">
        <KpiCard
          compact
          label="Expense entries"
          value={String(expTot.entries)}
          tone="slate"
          drill={{ metric: "expense", from: mo.from, to: mo.to, title: "Expense" }}
        />
        <KpiCard
          compact
          label="This month expense"
          value={expTot.monthAmount}
          tone="orange"
          drill={{ metric: "expense", from: mo.from, to: mo.to, title: "Expense" }}
        />
        <KpiCard
          compact
          label="Recurring / month"
          value={expTot.recurring}
          tone="warning"
          drill={{ metric: "expense", from: mo.from, to: mo.to, title: "Expense" }}
        />
        <KpiCard
          compact
          label="C/P entries"
          value={String(cpTot.entries)}
          tone="slate"
          drill={{ metric: "collection", from: mo.from, to: mo.to, title: "Collection" }}
        />
        <KpiCard
          compact
          label="Month collection"
          value={cpTot.collection}
          tone="success"
          drill={{ metric: "collection", from: mo.from, to: mo.to, title: "Collection" }}
        />
        <KpiCard
          compact
          label="Month payment"
          value={cpTot.payment}
          tone="danger"
          drill={{ metric: "supplierPayment", from: mo.from, to: mo.to, title: "Supplier payment" }}
        />
      </div>

      {tablesMissing ? (
        <EmptyState
          title="Expense tables not installed yet"
          detail="Run docs/mysql/expense_collection.sql on pharmapos_reporting, then add heads and entries below."
        />
      ) : null}

      <ExpenseForms
        stores={stores.map((s) => ({
          store_id: String(s.store_id),
          display_name: (s.display_name as string) || (s.store_code as string) || null,
        }))}
        heads={heads.map((h) => ({ id: Number(h.id), name: String(h.name) }))}
      />

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title="Expense heads" padded={false}>
          <DataTable
            embedded
            dense
            columns={[
              { key: "name", label: "Head" },
              { key: "status", label: "Status" },
              { key: "sort", label: "Sort", className: "text-right" },
            ]}
            rows={heads.map((h) => ({
              name: String(h.name),
              status: Number(h.is_active) ? "Active" : "Inactive",
              sort: Number(h.sort_order),
            }))}
          />
        </Card>
        <Card title="Collection & payment" padded={false}>
          <DataTable
            embedded
            dense
            columns={[
              { key: "date", label: "Date" },
              { key: "type", label: "Type" },
              { key: "mode", label: "Mode" },
              { key: "amount", label: "Amount", className: "text-right" },
              { key: "notes", label: "Notes" },
              { key: "act", label: "" },
            ]}
            rows={cps.map((c) => {
              const day = asYmd(c.entry_date as string);
              const isPay = String(c.entry_type) === "payment";
              return {
                date: fmtDateOnly(c.entry_date as string),
                type: String(c.entry_type),
                mode: String(c.mode),
                amount: (
                  <DrillValue
                    metric={isPay ? "supplierPayment" : "collection"}
                    from={day}
                    to={day}
                    title={isPay ? "Supplier payment" : "Collection"}
                  >
                    {rupeeShort(Number(c.amount))}
                  </DrillValue>
                ),
                notes: String(c.notes || "—"),
                act: <DeleteButton action="delete-collection" id={Number(c.id)} />,
              };
            })}
          />
        </Card>
      </div>

      <Card title="Expenses" padded={false}>
        <DataTable
          embedded
          columns={[
            { key: "date", label: "Date" },
            { key: "head", label: "Head" },
            { key: "store", label: "Store" },
            { key: "amount", label: "Amount", className: "text-right" },
            { key: "notes", label: "Notes" },
            { key: "act", label: "" },
          ]}
          rows={expenses.map((e) => {
            const day = asYmd(e.expense_date as string);
            return {
              date: fmtDateOnly(e.expense_date as string),
              head: String(e.head_name || "—"),
              store: String(e.store_id),
              amount: (
                <DrillValue metric="expense" from={day} to={day} title="Expense">
                  {rupeeShort(Number(e.amount))}
                </DrillValue>
              ),
              notes: String(e.notes || "—"),
              act: <DeleteButton action="delete-expense" id={Number(e.id)} />,
            };
          })}
        />
      </Card>
    </div>
  );
}
