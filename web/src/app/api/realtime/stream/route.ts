import { NextRequest } from "next/server";
import { getSession } from "@/lib/auth";
import { subscribeRealtime } from "@/lib/realtime";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

export async function GET(req: NextRequest) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    return new Response("Unauthorized", { status: 401 });
  }

  const storeIds = new Set(session.user.storeIds);
  const encoder = new TextEncoder();

  let cleanup: (() => void) | undefined;
  let heartbeat: ReturnType<typeof setInterval> | undefined;

  const stream = new ReadableStream({
    start(controller) {
      const send = (data: unknown) => {
        controller.enqueue(encoder.encode(`data: ${JSON.stringify(data)}\n\n`));
      };

      send({ type: "connected", at: new Date().toISOString() });

      cleanup = subscribeRealtime((event) => {
        if (!storeIds.has(event.storeId)) return;
        send({ type: "sync", ...event, at: new Date().toISOString() });
      });

      heartbeat = setInterval(() => {
        try {
          send({ type: "ping", at: new Date().toISOString() });
        } catch {
          // closed
        }
      }, 25000);

      req.signal.addEventListener("abort", () => {
        if (heartbeat) clearInterval(heartbeat);
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
