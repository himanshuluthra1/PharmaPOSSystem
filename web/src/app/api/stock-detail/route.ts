import { NextRequest, NextResponse } from "next/server";
import { RowDataPacket } from "mysql2";
import { getSession } from "@/lib/auth";
import { query } from "@/lib/db";
import { getStockDetails, type StockFilter } from "@/lib/medwin/medicineLedger";

const FILTERS = new Set<StockFilter>([
  "all",
  "expired",
  "near1m",
  "near3m",
  "near6m",
  "near12m",
]);

export async function GET(req: NextRequest) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const filterRaw = String(req.nextUrl.searchParams.get("filter") || "all");
  const filter = (FILTERS.has(filterRaw as StockFilter) ? filterRaw : "all") as StockFilter;
  const storeId = req.nextUrl.searchParams.get("storeId") || undefined;

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
    const data = await getStockDetails(user, { filter, storeId: storeId || undefined });
    return NextResponse.json(data);
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Failed";
    return NextResponse.json({ error: msg }, { status: 500 });
  }
}
