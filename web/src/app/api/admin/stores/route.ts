import { NextRequest, NextResponse } from "next/server";
import { getSession } from "@/lib/auth";
import { execute } from "@/lib/db";
import { PERMISSIONS } from "@/lib/session";

export async function POST(req: NextRequest) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }
  if (!session.user.permissions.includes(PERMISSIONS.manageStores)) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  const body = await req.json().catch(() => null);
  const storeId = String(body?.storeId || "").trim();
  const displayName = String(body?.displayName || "").trim() || null;
  const action = String(body?.action || "add");

  if (!storeId) {
    return NextResponse.json({ error: "storeId required" }, { status: 400 });
  }

  if (action === "remove") {
    await execute(`DELETE FROM tenant_stores WHERE tenant_id=? AND store_id=?`, [
      session.user.tenantId,
      storeId,
    ]);
    session.user.storeIds = session.user.storeIds.filter((s) => s !== storeId);
    if (session.user.selectedStoreId === storeId) session.user.selectedStoreId = "all";
    await session.save();
    return NextResponse.json({ ok: true });
  }

  await execute(
    `INSERT INTO tenant_stores (tenant_id, store_id, display_name)
     VALUES (?, ?, ?)
     ON DUPLICATE KEY UPDATE display_name = VALUES(display_name)`,
    [session.user.tenantId, storeId, displayName]
  );

  if (!session.user.storeIds.includes(storeId)) {
    session.user.storeIds = [...session.user.storeIds, storeId];
    await session.save();
  }

  return NextResponse.json({ ok: true });
}
