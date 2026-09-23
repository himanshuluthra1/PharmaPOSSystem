import { NextRequest, NextResponse } from "next/server";
import { RowDataPacket } from "mysql2";
import { getSession } from "@/lib/auth";
import { query } from "@/lib/db";
import {
  getDrillDown,
  isDrillMetric,
  type DrillLevel,
} from "@/lib/drilldown";

const LEVELS = new Set<DrillLevel>(["years", "months", "days", "bills"]);

export async function GET(req: NextRequest) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const perms = session.user.permissions || [];
  if (
    !perms.includes("dashboard.view") &&
    !perms.includes("sales.view") &&
    !perms.includes("purchases.view") &&
    !perms.includes("payments.view")
  ) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  const sp = req.nextUrl.searchParams;
  const metric = String(sp.get("metric") || "");
  const from = String(sp.get("from") || "");
  const to = String(sp.get("to") || "");
  const levelRaw = sp.get("level");
  const title = sp.get("title") || undefined;

  if (!isDrillMetric(metric)) {
    return NextResponse.json({ error: "Invalid metric" }, { status: 400 });
  }
  if (!/^\d{4}-\d{2}-\d{2}$/.test(from) || !/^\d{4}-\d{2}-\d{2}$/.test(to) || from > to) {
    return NextResponse.json({ error: "Invalid date range" }, { status: 400 });
  }

  let level: DrillLevel | undefined;
  if (levelRaw) {
    if (!LEVELS.has(levelRaw as DrillLevel)) {
      return NextResponse.json({ error: "Invalid level" }, { status: 400 });
    }
    level = levelRaw as DrillLevel;
  }

  // Refresh store list so newly linked stores appear without re-login.
  const storeRows = await query<RowDataPacket[]>(
    `SELECT store_id FROM tenant_stores WHERE tenant_id = ?`,
    [session.user.tenantId]
  );
  const storeIds = storeRows.map((s) => String(s.store_id));
  const selected =
    session.user.selectedStoreId !== "all" && storeIds.includes(session.user.selectedStoreId)
      ? session.user.selectedStoreId
      : "all";

  const user = {
    tenantId: session.user.tenantId,
    storeIds,
    selectedStoreId: selected as string | "all",
  };

  try {
    const result = await getDrillDown(user, {
      metric,
      from,
      to,
      level,
      title: title ? String(title) : undefined,
    });
    return NextResponse.json(result);
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Failed";
    return NextResponse.json({ error: msg }, { status: 500 });
  }
}
