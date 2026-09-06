import { requirePermission } from "@/lib/auth";
import { listRoles, listUsers } from "@/lib/data";
import { PERMISSIONS } from "@/lib/session";
import { CreateUserForm } from "@/components/CreateUserForm";
import { DataTable, PageHeader } from "@/components/Ui";
import { fmtDate } from "@/lib/format";

export const dynamic = "force-dynamic";

export default async function UsersAdminPage() {
  const user = await requirePermission(PERMISSIONS.manageUsers);
  const [users, roles] = await Promise.all([
    listUsers(user.tenantId),
    listRoles(),
  ]);

  return (
    <div>
      <PageHeader
        title="Users"
        subtitle="Create dashboard users and assign roles for this tenant"
      />
      <CreateUserForm
        roles={roles.map((r) => ({ id: Number(r.id), name: String(r.name) }))}
      />
      <DataTable
        columns={[
          { key: "name", label: "Name" },
          { key: "email", label: "Email" },
          { key: "role", label: "Role" },
          { key: "status", label: "Status" },
          { key: "login", label: "Last login" },
        ]}
        rows={users.map((u) => ({
          name: String(u.full_name),
          email: String(u.email),
          role: String(u.role_name),
          status: Number(u.status) === 1 ? "Active" : "Inactive",
          login: fmtDate(u.last_login_at_utc as string),
        }))}
      />
    </div>
  );
}
