import { NextRequest } from "next/server";
import { RowDataPacket } from "mysql2";
import { getSession } from "@/lib/auth";
import { query } from "@/lib/db";
import { subscribeRealtime } from "@/lib/realtime";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

async function loadStoreIds(tenantId: number, sessionStoreIds: string[]) {
  try {
    const rows = await query<RowDataPacket[]>(
      `SELECT store_id FROM tenant_stores WHERE tenant_id = ?`,
      [tenantId]
    );
    const ids = rows.map((r) => String(r.store_id));
    return ids.length > 0 ? ids : sessionStoreIds;
  } catch {
    return sessionStoreIds;
  }
}

async function readWatermark(storeIds: string[]): Promise<string> {
  if (storeIds.length === 0) return "0";
  const ph = storeIds.map(() => "?").join(",");
  try {
    const [row] = await query<RowDataPacket[]>(
      `SELECT COALESCE(MAX(id), 0) AS max_id
       FROM sync_events
       WHERE store_id IN (${ph})`,
      storeIds
    );
    return String(Number(row?.max_id || 0));
  } catch {
    // table may not exist yet — fall back to a cheap sales max stamp
    try {
      const [row] = await query<RowDataPacket[]>(
        `SELECT COALESCE(MAX(synced_at_utc), '1970-01-01') AS m
         FROM sales
         WHERE store_id IN (${ph}) AND is_deleted = 0`,
        storeIds
      );
      return String(row?.m || "");
    } catch {
      return "0";
    }
  }
}

export async function GET(req: NextRequest) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    return new Response("Unauthorized", { status: 401 });
  }

  const encoder = new TextEncoder();
  const tenantId = session.user.tenantId;
  let storeIds = await loadStoreIds(tenantId, session.user.storeIds);
  let storeIdSet = new Set(storeIds);

  let cleanup: (() => void) | undefined;
  let heartbeat: ReturnType<typeof setInterval> | undefined;
  let poll: ReturnType<typeof setInterval> | undefined;
  let lastWatermark = await readWatermark(storeIds);

  const stream = new ReadableStream({
    start(controller) {
      const send = (data: unknown) => {
        controller.enqueue(encoder.encode(`data: ${JSON.stringify(data)}\n\n`));
      };

      send({ type: "connected", at: new Date().toISOString() });

      cleanup = subscribeRealtime((event) => {
        if (!storeIdSet.has(event.storeId)) return;
        send({ type: "sync", ...event, at: new Date().toISOString() });
      });

      heartbeat = setInterval(() => {
        try {
          send({ type: "ping", at: new Date().toISOString() });
        } catch {
          // closed
        }
      }, 25000);

      // Poll MySQL so UI refreshes even when HTTP notify misses (wrong URL / HMR bus).
      // Keep this light — aggressive polling exhausted MySQL max_connections under Next.js.
      let pollBusy = false;
      poll = setInterval(async () => {
        if (pollBusy) return;
        pollBusy = true;
        try {
          const next = await readWatermark(storeIds);
          if (next !== lastWatermark) {
            lastWatermark = next;
            storeIds = await loadStoreIds(tenantId, session.user!.storeIds);
            storeIdSet = new Set(storeIds);
            send({
              type: "sync",
              storeId: "poll",
              entityType: "watermark",
              localId: 0,
              at: new Date().toISOString(),
            });
          }
        } catch {
          // keep stream alive
        } finally {
          pollBusy = false;
        }
      }, 15000);

      req.signal.addEventListener("abort", () => {
        if (heartbeat) clearInterval(heartbeat);
        if (poll) clearInterval(poll);
        cleanup?.();
        try {
          controller.close();
        } catch {
          // ignore
        }
      });
    },
    cancel() {
      if (heartbeat) clearInterval(heartbeat);
      if (poll) clearInterval(poll);
      cleanup?.();
    },
  });

  return new Response(stream, {
    headers: {
      "Content-Type": "text/event-stream",
      "Cache-Control": "no-cache, no-transform",
      Connection: "keep-alive",
    },
  });
}
