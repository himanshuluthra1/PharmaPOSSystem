import { RowDataPacket } from "mysql2";
import { execute, query } from "@/lib/db";
import { storeFilter, placeholders } from "@/lib/auth";
import type { SessionUser } from "@/lib/session";

type User = Pick<SessionUser, "tenantId" | "storeIds" | "selectedStoreId">;

function scope(user: User) {
  const ids = storeFilter(user);
  return { ids, ph: ids.length ? placeholders(ids.length) : "NULL" };
}

export async function listExpenseHeads(tenantId: number) {
  try {
    return await query<RowDataPacket[]>(
      `SELECT id, name, sort_order, is_active
       FROM expense_heads
       WHERE tenant_id=?
       ORDER BY sort_order, name`,
      [tenantId]
    );
  } catch {
    return [];
  }
}

export async function listExpenses(user: User) {
  const { ids, ph } = scope(user);
  if (ids.length === 0) return [];
  try {
    return await query<RowDataPacket[]>(
      `SELECT e.id, e.store_id, e.expense_date, e.amount, e.notes, e.is_recurring,
              h.name AS head_name
       FROM expenses e
       LEFT JOIN expense_heads h ON h.id=e.head_id
       WHERE e.tenant_id=? AND e.store_id IN (${ph}) AND e.is_deleted=0
       ORDER BY e.expense_date DESC, e.id DESC
       LIMIT 200`,
      [user.tenantId, ...ids]
    );
  } catch {
    return [];
  }
}

export async function listCollectionPayments(user: User) {
  const { ids, ph } = scope(user);
  if (ids.length === 0) return [];
  try {
    return await query<RowDataPacket[]>(
      `SELECT id, store_id, entry_date, entry_type, mode, amount, notes
       FROM collection_payments
       WHERE tenant_id=? AND store_id IN (${ph}) AND is_deleted=0
       ORDER BY entry_date DESC, id DESC
       LIMIT 200`,
      [user.tenantId, ...ids]
    );
  } catch {
    return [];
  }
}

export async function expenseMonthTotals(user: User) {
  const { ids, ph } = scope(user);
  if (ids.length === 0) return { entries: 0, monthAmount: 0, recurring: 0 };
  try {
    const [r] = await query<RowDataPacket[]>(
      `SELECT COUNT(*) AS entries,
              COALESCE(SUM(CASE WHEN expense_date >= DATE_FORMAT(CURDATE(),'%Y-%m-01') THEN amount ELSE 0 END),0) AS month_amt,
              COALESCE(SUM(CASE WHEN is_recurring=1 THEN amount ELSE 0 END),0) AS recurring
       FROM expenses
       WHERE tenant_id=? AND store_id IN (${ph}) AND is_deleted=0`,
      [user.tenantId, ...ids]
    );
    return {
      entries: Number(r?.entries ?? 0),
      monthAmount: Number(r?.month_amt ?? 0),
      recurring: Number(r?.recurring ?? 0),
    };
  } catch {
    return { entries: 0, monthAmount: 0, recurring: 0 };
  }
}

export async function collectionMonthTotals(user: User) {
  const { ids, ph } = scope(user);
  if (ids.length === 0) return { entries: 0, collection: 0, payment: 0 };
  try {
    const [r] = await query<RowDataPacket[]>(
      `SELECT COUNT(*) AS entries,
              COALESCE(SUM(CASE WHEN entry_type='collection' AND entry_date >= DATE_FORMAT(CURDATE(),'%Y-%m-01') THEN amount ELSE 0 END),0) AS collection,
              COALESCE(SUM(CASE WHEN entry_type='payment' AND entry_date >= DATE_FORMAT(CURDATE(),'%Y-%m-01') THEN amount ELSE 0 END),0) AS payment
       FROM collection_payments
       WHERE tenant_id=? AND store_id IN (${ph}) AND is_deleted=0`,
      [user.tenantId, ...ids]
    );
    return {
      entries: Number(r?.entries ?? 0),
      collection: Number(r?.collection ?? 0),
      payment: Number(r?.payment ?? 0),
    };
  } catch {
    return { entries: 0, collection: 0, payment: 0 };
  }
}

export async function createExpenseHead(tenantId: number, name: string) {
  await execute(
    `INSERT INTO expense_heads (tenant_id, name, sort_order, is_active) VALUES (?,?,0,1)`,
    [tenantId, name.trim()]
  );
}

export async function createExpense(input: {
  tenantId: number;
  storeId: string;
  headId?: number | null;
  expenseDate: string;
  amount: number;
  notes?: string;
  isRecurring?: boolean;
}) {
  await execute(
    `INSERT INTO expenses (tenant_id, store_id, head_id, expense_date, amount, notes, is_recurring, is_deleted)
     VALUES (?,?,?,?,?,?,?,0)`,
    [
      input.tenantId,
      input.storeId,
      input.headId ?? null,
      input.expenseDate,
      input.amount,
      input.notes || null,
      input.isRecurring ? 1 : 0,
    ]
  );
}

export async function createCollectionPayment(input: {
  tenantId: number;
  storeId: string;
  entryDate: string;
  entryType: "collection" | "payment";
  mode: string;
  amount: number;
  notes?: string;
}) {
  await execute(
    `INSERT INTO collection_payments (tenant_id, store_id, entry_date, entry_type, mode, amount, notes, is_deleted)
     VALUES (?,?,?,?,?,?,?,0)`,
    [
      input.tenantId,
      input.storeId,
      input.entryDate,
      input.entryType,
      input.mode,
      input.amount,
      input.notes || null,
    ]
  );
}

export async function softDeleteExpense(id: number, tenantId: number) {
  await execute(`UPDATE expenses SET is_deleted=1 WHERE id=? AND tenant_id=?`, [
    id,
    tenantId,
  ]);
}

export async function softDeleteCollectionPayment(id: number, tenantId: number) {
  await execute(
    `UPDATE collection_payments SET is_deleted=1 WHERE id=? AND tenant_id=?`,
    [id, tenantId]
  );
}
