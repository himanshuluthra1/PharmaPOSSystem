import { getIronSession } from "iron-session";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { RowDataPacket } from "mysql2";
import { SessionData, sessionOptions } from "./session";
import { query } from "./db";

export async function getSession() {
  return getIronSession<SessionData>(await cookies(), sessionOptions);
}

/** True when the store has any synced sales, purchases, or stock. */
export async function storeHasSyncedData(storeId: string): Promise<boolean> {
  const rows = await query<RowDataPacket[]>(
    `SELECT (
       EXISTS(SELECT 1 FROM sales WHERE store_id=? AND is_deleted=0 LIMIT 1)
       OR EXISTS(SELECT 1 FROM purchases WHERE store_id=? AND is_deleted=0 LIMIT 1)
       OR EXISTS(SELECT 1 FROM medicine_batches WHERE store_id=? AND is_deleted=0 LIMIT 1)
     ) AS has_data`,
    [storeId, storeId, storeId]
  );
  return Number(rows[0]?.has_data) === 1;
}

/**
 * Resolve shop filter. Empty linked shops (no synced POS data) fall back to
 * "all" so the dashboard does not show an all-zero view by accident.
 */
export async function resolveSelectedStoreId(
  storeIds: string[],
  selectedStoreId: string | "all"
): Promise<string | "all"> {
  const selected =
    selectedStoreId !== "all" && storeIds.includes(selectedStoreId)
      ? selectedStoreId
      : "all";
  if (selected === "all") return "all";
  const hasData = await storeHasSyncedData(selected);
  return hasData ? selected : "all";
}

export async function requireUser() {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    redirect("/login");
  }

  // Always use current tenant_stores so newly linked POS stores appear without re-login.
  const storeRows = await query<RowDataPacket[]>(
    `SELECT store_id FROM tenant_stores WHERE tenant_id = ?`,
    [session.user.tenantId]
  );
  const storeIds = storeRows.map((s) => String(s.store_id));
  // Correct empty-shop filter in memory only — cookie writes are not allowed
  // from Server Components (layout/pages); AppShell persists via /api/stores/select.
  const selected = await resolveSelectedStoreId(storeIds, session.user.selectedStoreId);

  return {
    ...session.user,
    storeIds,
    selectedStoreId: selected,
  };
}

export async function requirePermission(permission: string) {
  const user = await requireUser();
  if (!user.permissions.includes(permission)) {
    redirect("/?error=forbidden");
  }
  return user;
}

export function storeFilter(user: { storeIds: string[]; selectedStoreId: string | "all" }) {
  if (user.selectedStoreId !== "all" && user.storeIds.includes(user.selectedStoreId)) {
    return [user.selectedStoreId];
  }
  return user.storeIds;
}

export function placeholders(n: number) {
  return Array(n).fill("?").join(",");
}
