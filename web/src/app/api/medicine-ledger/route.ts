import { NextRequest, NextResponse } from "next/server";
import { RowDataPacket } from "mysql2";
import { getSession } from "@/lib/auth";
import { query } from "@/lib/db";
import { getMedicineLedger } from "@/lib/medwin/medicineLedger";

export async function GET(req: NextRequest) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const storeId = String(req.nextUrl.searchParams.get("storeId") || "");
  const medicineLocalId = Number(req.nextUrl.searchParams.get("medicineLocalId") || 0);
  if (!storeId || !medicineLocalId) {
    return NextResponse.json({ error: "Invalid request" }, { status: 400 });
  }

  const storeRows = await query<RowDataPacket[]>(
    `SELECT store_id FROM tenant_stores WHERE tenant_id = ?`,
    [session.user.tenantId]
  );
  const storeIds = storeRows.map((s) => String(s.store_id));
  const user = {
    tenantId: session.user.tenantId,
    storeIds,
    selectedStoreId: session.user.selectedStoreId as string | "all",
  };

  try {
    const data = await getMedicineLedger(user, storeId, medicineLocalId);
    if (!data) return NextResponse.json({ error: "Not found" }, { status: 404 });
    return NextResponse.json(data);
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Failed";
    return NextResponse.json({ error: msg }, { status: 500 });
  }
}
