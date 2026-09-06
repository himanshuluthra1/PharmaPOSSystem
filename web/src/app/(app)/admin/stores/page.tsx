import { requirePermission } from "@/lib/auth";
import { listAvailableStores, listTenantStores } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { StoreAdminActions } from "@/components/StoreAdminActions";
import { PageHeader } from "@/components/Ui";

export const dynamic = "force-dynamic";

export default async function StoresAdminPage() {
  const user = await requirePermission(PERMISSIONS.manageStores);
  const [assigned, available] = await Promise.all([
    listTenantStores(user.tenantId),
    listAvailableStores(),
  ]);

  const assignedIds = new Set(assigned.map((a) => String(a.store_id)));
  const availableFiltered = available
    .filter((s) => Number(s.is_approved) === 1 && !assignedIds.has(String(s.store_id)))
    .map((s) => ({
      store_id: String(s.store_id),
      store_code: s.store_code ? String(s.store_code) : undefined,
      machine_name: s.machine_name ? String(s.machine_name) : undefined,
    }));

  return (
    <div>
      <PageHeader
        title="Stores"
        subtitle="Attach POS store_id values to this tenant for multi-shop dashboards"
      />
      <StoreAdminActions
        available={availableFiltered}
        assigned={assigned.map((a) => ({
          store_id: String(a.store_id),
          display_name: (a.display_name as string) || (a.store_code as string) || null,
        }))}
      />
    </div>
  );
}
