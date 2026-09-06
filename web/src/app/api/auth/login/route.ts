import { NextRequest, NextResponse } from "next/server";
import bcrypt from "bcryptjs";
import { RowDataPacket } from "mysql2";
import { getSession } from "@/lib/auth";
import { query, execute } from "@/lib/db";

export async function POST(req: NextRequest) {
  const body = await req.json().catch(() => null);
  const email = String(body?.email || "")
    .trim()
    .toLowerCase();
  const password = String(body?.password || "");

  if (!email || !password) {
    return NextResponse.json({ error: "Email and password are required." }, { status: 400 });
  }

  const rows = await query<RowDataPacket[]>(
    `SELECT u.id, u.email, u.full_name, u.password_hash, u.status, u.tenant_id, u.role_id,
            t.name AS tenant_name, r.name AS role_name
     FROM dashboard_users u
     JOIN tenants t ON t.id = u.tenant_id
     JOIN dashboard_roles r ON r.id = u.role_id
     WHERE u.email = ?
     LIMIT 1`,
    [email]
  );

  const user = rows[0];
  if (!user || Number(user.status) !== 1) {
    return NextResponse.json({ error: "Invalid email or password." }, { status: 401 });
  }

  const ok = await bcrypt.compare(password, String(user.password_hash));
  if (!ok) {
    return NextResponse.json({ error: "Invalid email or password." }, { status: 401 });
  }

  const perms = await query<RowDataPacket[]>(
    `SELECT permission_key FROM dashboard_role_permissions WHERE role_id = ?`,
    [user.role_id]
  );

  const stores = await query<RowDataPacket[]>(
    `SELECT store_id FROM tenant_stores WHERE tenant_id = ?`,
    [user.tenant_id]
  );

  const session = await getSession();
  session.isLoggedIn = true;
  session.user = {
    id: Number(user.id),
    email: String(user.email),
    fullName: String(user.full_name),
    tenantId: Number(user.tenant_id),
    tenantName: String(user.tenant_name),
    roleId: Number(user.role_id),
    roleName: String(user.role_name),
    permissions: perms.map((p) => String(p.permission_key)),
    storeIds: stores.map((s) => String(s.store_id)),
    selectedStoreId: "all",
  };
  await session.save();

  await execute(
    `UPDATE dashboard_users SET last_login_at_utc = UTC_TIMESTAMP(6) WHERE id = ?`,
    [user.id]
  );

  return NextResponse.json({ ok: true });
}
