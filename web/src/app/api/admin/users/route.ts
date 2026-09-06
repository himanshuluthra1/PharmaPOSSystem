import { NextRequest, NextResponse } from "next/server";
import bcrypt from "bcryptjs";
import { getSession } from "@/lib/auth";
import { execute } from "@/lib/db";
import { PERMISSIONS } from "@/lib/session";

export async function POST(req: NextRequest) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }
  if (!session.user.permissions.includes(PERMISSIONS.manageUsers)) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  const body = await req.json().catch(() => null);
  const email = String(body?.email || "")
    .trim()
    .toLowerCase();
  const fullName = String(body?.fullName || "").trim();
  const password = String(body?.password || "");
  const roleId = Number(body?.roleId || 0);

  if (!email || !fullName || password.length < 6 || roleId <= 0) {
    return NextResponse.json({ error: "Invalid user details." }, { status: 400 });
  }

  const hash = await bcrypt.hash(password, 10);
  try {
    await execute(
      `INSERT INTO dashboard_users (tenant_id, role_id, email, full_name, password_hash, status)
       VALUES (?, ?, ?, ?, ?, 1)`,
      [session.user.tenantId, roleId, email, fullName, hash]
    );
  } catch (e) {
    return NextResponse.json(
      { error: e instanceof Error ? e.message : "Could not create user." },
      { status: 400 }
    );
  }

  return NextResponse.json({ ok: true });
}
