import { NextRequest, NextResponse } from "next/server";
import { execute } from "@/lib/db";
import { publishRealtime } from "@/lib/realtime";

export async function POST(req: NextRequest) {
  const secret = req.headers.get("x-realtime-secret") || "";
  const expected = process.env.REALTIME_SECRET || "";
  if (!expected || secret !== expected) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const body = await req.json().catch(() => null);
  const storeId = String(body?.store_id || "").trim();
  const entityType = String(body?.entity_type || "").trim();
  const localId = Number(body?.local_id || 0);

  if (!storeId || !entityType || !localId) {
    return NextResponse.json({ error: "Invalid payload" }, { status: 400 });
  }

  try {
    await execute(
      `INSERT INTO sync_events (store_id, entity_type, local_id) VALUES (?, ?, ?)`,
      [storeId, entityType, localId]
    );
  } catch {
    // table may not exist yet; still broadcast
  }

  publishRealtime({ storeId, entityType, localId });
  return NextResponse.json({ ok: true });
}
