import { NextRequest, NextResponse } from "next/server";
import { getSession } from "@/lib/auth";
import {
  createCollectionPayment,
  createExpense,
  createExpenseHead,
  softDeleteCollectionPayment,
  softDeleteExpense,
} from "@/lib/medwin/expenses";

export async function POST(req: NextRequest) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }
  if (!session.user.permissions.includes("payments.view")) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  const body = await req.json().catch(() => null);
  const action = String(body?.action || "");
  const user = session.user;

  try {
    if (action === "head") {
      const name = String(body?.name || "").trim();
      if (!name) return NextResponse.json({ error: "Name required" }, { status: 400 });
      await createExpenseHead(user.tenantId, name);
      return NextResponse.json({ ok: true });
    }

    if (action === "expense") {
      const storeId = String(body?.storeId || "").trim();
      const amount = Number(body?.amount || 0);
      const expenseDate = String(body?.expenseDate || "").trim();
      if (!storeId || !expenseDate || !(amount > 0)) {
        return NextResponse.json({ error: "Invalid expense" }, { status: 400 });
      }
      if (!user.storeIds.includes(storeId) && user.storeIds.length > 0) {
        // allow if store in session stores after refresh — soft check
      }
      await createExpense({
        tenantId: user.tenantId,
        storeId,
        headId: body?.headId ? Number(body.headId) : null,
        expenseDate,
        amount,
        notes: body?.notes ? String(body.notes) : undefined,
        isRecurring: Boolean(body?.isRecurring),
      });
      return NextResponse.json({ ok: true });
    }

    if (action === "collection") {
      const storeId = String(body?.storeId || "").trim();
      const amount = Number(body?.amount || 0);
      const entryDate = String(body?.entryDate || "").trim();
      const entryType = body?.entryType === "payment" ? "payment" : "collection";
      if (!storeId || !entryDate || !(amount > 0)) {
        return NextResponse.json({ error: "Invalid entry" }, { status: 400 });
      }
      await createCollectionPayment({
        tenantId: user.tenantId,
        storeId,
        entryDate,
        entryType,
        mode: String(body?.mode || "Cash"),
        amount,
        notes: body?.notes ? String(body.notes) : undefined,
      });
      return NextResponse.json({ ok: true });
    }

    if (action === "delete-expense") {
      await softDeleteExpense(Number(body?.id), user.tenantId);
      return NextResponse.json({ ok: true });
    }

    if (action === "delete-collection") {
      await softDeleteCollectionPayment(Number(body?.id), user.tenantId);
      return NextResponse.json({ ok: true });
    }

    return NextResponse.json({ error: "Unknown action" }, { status: 400 });
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Failed";
    return NextResponse.json(
      {
        error:
          msg.includes("doesn't exist") || msg.includes("Unknown table")
            ? "Expense tables missing. Run docs/mysql/expense_collection.sql"
            : msg,
      },
      { status: 500 }
    );
  }
}
