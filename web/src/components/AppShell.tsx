"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";

type NavItem = { href: string; label: string; permission?: string };

const NAV: NavItem[] = [
  { href: "/", label: "Dashboard", permission: "dashboard.view" },
  { href: "/sales", label: "Sales", permission: "sales.view" },
  { href: "/purchases", label: "Purchases", permission: "purchases.view" },
  { href: "/stock", label: "Stock", permission: "stock.view" },
  { href: "/payments", label: "Payments", permission: "payments.view" },
  { href: "/returns", label: "Returns", permission: "returns.view" },
  { href: "/admin/users", label: "Users", permission: "stores.manage_users" },
  { href: "/admin/stores", label: "Stores", permission: "stores.manage_stores" },
];

export type ShellUser = {
  fullName: string;
  tenantName: string;
  roleName: string;
  permissions: string[];
  storeIds: string[];
  selectedStoreId: string | "all";
  stores: { store_id: string; display_name: string | null }[];
};

export function AppShell({
  user,
  children,
}: {
  user: ShellUser;
  children: React.ReactNode;
}) {
  const pathname = usePathname();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [live, setLive] = useState<"connecting" | "live" | "offline">("connecting");

  const items = useMemo(
    () =>
      NAV.filter(
        (n) => !n.permission || user.permissions.includes(n.permission)
      ),
    [user.permissions]
  );

  useEffect(() => {
    const es = new EventSource("/api/realtime/stream");
    es.onopen = () => setLive("live");
    es.onerror = () => setLive("offline");
    es.onmessage = (msg) => {
      try {
        const data = JSON.parse(msg.data);
        if (data.type === "sync") {
          router.refresh();
        }
        if (data.type === "connected" || data.type === "ping") {
          setLive("live");
        }
      } catch {
        // ignore
      }
    };
    const poll = setInterval(() => router.refresh(), 30000);
    return () => {
      es.close();
      clearInterval(poll);
    };
  }, [router]);

  async function logout() {
    await fetch("/api/auth/logout", { method: "POST" });
    router.replace("/login");
    router.refresh();
  }

  async function onStoreChange(value: string) {
    await fetch("/api/stores/select", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ storeId: value }),
    });
    router.refresh();
  }

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <div className="flex min-h-screen">
        <aside
          className={`fixed inset-y-0 left-0 z-40 w-64 transform border-r border-slate-200 bg-white transition md:static md:translate-x-0 ${
            open ? "translate-x-0" : "-translate-x-full"
          }`}
        >
          <div className="flex h-14 items-center border-b border-slate-200 px-4">
            <span className="text-lg font-semibold tracking-tight text-teal-700">
              MyPOS
            </span>
          </div>
          <nav className="space-y-1 p-3">
            {items.map((item) => {
              const active =
                item.href === "/"
                  ? pathname === "/"
                  : pathname.startsWith(item.href);
              return (
                <Link
                  key={item.href}
                  href={item.href}
                  onClick={() => setOpen(false)}
                  className={`block rounded-lg px-3 py-2 text-sm font-medium ${
                    active
                      ? "bg-teal-50 text-teal-800"
                      : "text-slate-600 hover:bg-slate-100"
                  }`}
                >
                  {item.label}
                </Link>
              );
            })}
          </nav>
        </aside>

        {open && (
          <button
            className="fixed inset-0 z-30 bg-black/30 md:hidden"
            aria-label="Close menu"
            onClick={() => setOpen(false)}
          />
        )}

        <div className="flex min-w-0 flex-1 flex-col">
          <header className="sticky top-0 z-20 flex h-14 items-center gap-3 border-b border-slate-200 bg-white/95 px-3 backdrop-blur md:px-5">
            <button
              className="rounded-md border border-slate-200 px-2 py-1 text-sm md:hidden"
              onClick={() => setOpen(true)}
            >
              Menu
            </button>
            <div className="min-w-0 flex-1">
              <div className="truncate text-sm font-semibold">{user.tenantName}</div>
              <div className="truncate text-xs text-slate-500">
                {user.fullName} · {user.roleName}
              </div>
            </div>
            <select
              className="max-w-[42vw] rounded-md border border-slate-200 bg-white px-2 py-1.5 text-sm md:max-w-xs"
              value={user.selectedStoreId}
              onChange={(e) => onStoreChange(e.target.value)}
            >
              <option value="all">All shops</option>
              {user.stores.map((s) => (
                <option key={s.store_id} value={s.store_id}>
                  {s.display_name || s.store_id}
                </option>
              ))}
            </select>
            <span
              className={`hidden rounded-full px-2 py-0.5 text-xs font-medium sm:inline ${
                live === "live"
                  ? "bg-emerald-50 text-emerald-700"
                  : live === "connecting"
                    ? "bg-amber-50 text-amber-700"
                    : "bg-slate-100 text-slate-500"
              }`}
            >
              {live === "live" ? "Live" : live === "connecting" ? "Connecting" : "Offline"}
            </span>
            <button
              onClick={logout}
              className="rounded-md border border-slate-200 px-2 py-1 text-sm text-slate-600 hover:bg-slate-50"
            >
              Logout
            </button>
          </header>
          <main className="flex-1 p-3 md:p-6">{children}</main>
        </div>
      </div>
    </div>
  );
}
