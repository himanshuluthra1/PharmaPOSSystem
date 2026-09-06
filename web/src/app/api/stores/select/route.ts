import { NextRequest, NextResponse } from "next/server";
import { getSession } from "@/lib/auth";

export async function POST(req: NextRequest) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const body = await req.json().catch(() => null);
  const storeId = String(body?.storeId || "all");
  if (storeId !== "all" && !session.user.storeIds.includes(storeId)) {
    return NextResponse.json({ error: "Store not allowed" }, { status: 403 });
  }

  session.user.selectedStoreId = storeId === "all" ? "all" : storeId;
  await session.save();
  return NextResponse.json({ ok: true, selectedStoreId: session.user.selectedStoreId });
}
