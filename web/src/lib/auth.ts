import { getIronSession } from "iron-session";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { SessionData, sessionOptions } from "./session";

export async function getSession() {
  return getIronSession<SessionData>(await cookies(), sessionOptions);
}

export async function requireUser() {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    redirect("/login");
  }
  return session.user;
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
