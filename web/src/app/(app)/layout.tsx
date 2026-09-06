import { redirect } from "next/navigation";
import { getSession } from "@/lib/auth";
import { listTenantStores } from "@/lib/data";
import { AppShell } from "@/components/AppShell";

export default async function AppLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    redirect("/login");
  }

  const stores = await listTenantStores(session.user.tenantId);

  return (
    <AppShell
      user={{
        fullName: session.user.fullName,
        tenantName: session.user.tenantName,
        roleName: session.user.roleName,
        permissions: session.user.permissions,
        storeIds: session.user.storeIds,
        selectedStoreId: session.user.selectedStoreId,
        stores: stores.map((s) => ({
          store_id: String(s.store_id),
          display_name: (s.display_name as string) || (s.store_code as string) || null,
        })),
      }}
    >
      {children}
    </AppShell>
  );
}
