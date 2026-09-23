import { cache } from "react";
import { redirect } from "next/navigation";
import { getSession, resolveSelectedStoreId } from "@/lib/auth";
import { latestSyncAtUtc, listTenantStores } from "@/lib/data";
import { AppShell } from "@/components/AppShell";

const cachedTenantStores = cache(listTenantStores);

export default async function AppLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    redirect("/login");
  }

  const stores = await cachedTenantStores(session.user.tenantId);
  const storeIds = stores.map((s) => String(s.store_id));
  // In-memory only — do not session.save() here (Server Component).
  const selectedStoreId = await resolveSelectedStoreId(
    storeIds,
    session.user.selectedStoreId
  );
  const sessionStoreId = session.user.selectedStoreId;
  const needsStoreReset =
    selectedStoreId === "all" &&
    sessionStoreId !== "all" &&
    storeIds.includes(sessionStoreId);

  const lastSync = await latestSyncAtUtc();

  return (
    <AppShell
      user={{
        fullName: session.user.fullName,
        tenantName: session.user.tenantName,
        roleName: session.user.roleName,
        permissions: session.user.permissions,
        storeIds,
        selectedStoreId,
        stores: stores.map((s) => ({
          store_id: String(s.store_id),
          display_name: (s.display_name as string) || (s.store_code as string) || null,
          has_data: Number(s.has_data) === 1,
        })),
      }}
      lastSyncAtUtc={lastSync ? lastSync.toISOString() : null}
      resetStoreToAll={needsStoreReset}
    >
      {children}
    </AppShell>
  );
}
