"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { DrillDownProvider } from "@/components/DrillDown";

type NavItem = { href: string; label: string; permission?: string; title?: string };

const NAV: NavItem[] = [
  { href: "/", label: "Overview", title: "Overview", permission: "dashboard.view" },
  { href: "/ledger", label: "Ledger", title: "Ledger", permission: "dashboard.view" },
  { href: "/pnl", label: "P&L", title: "Profit & Loss", permission: "dashboard.view" },
  {
    href: "/net-business",
    label: "Net Business",
    title: "Net Business",
    permission: "dashboard.view",
  },
  { href: "/sales", label: "Sales", title: "Sales Analysis", permission: "sales.view" },
  {
    href: "/purchases",
    label: "Purchase",
    title: "Purchase Analysis",
    permission: "purchases.view",
  },
  {
    href: "/bills-payments",
    label: "Bills & Payments",
    title: "Bills & Payments",
    permission: "payments.view",
  },
  {
    href: "/expenses",
    label: "Expense & Collection",
    title: "Expense & Collection",
    permission: "payments.view",
  },
  {
    href: "/stock",
    label: "Stock & Inventory",
    title: "Stock & Inventory",
    permission: "stock.view",
  },
  {
    href: "/expiry",
    label: "Expiry Tracker",
    title: "Expiry Tracker",
    permission: "stock.view",
  },
  { href: "/gst", label: "GST Analysis", title: "GST Analysis", permission: "sales.view" },
  {
    href: "/audit",
    label: "Audit & Compliance",
    title: "Audit & Compliance",
    permission: "dashboard.view",
  },
  {
    href: "/admin/users",
    label: "User Admin",
    title: "User Administration",
    permission: "stores.manage_users",
  },
  {
    href: "/admin/stores",
    label: "Stores",
    title: "Stores",
    permission: "stores.manage_stores",
  },
];

export type ShellUser = {
  fullName: string;
  tenantName: string;
  roleName: string;
  permissions: string[];
  storeIds: string[];
  selectedStoreId: string | "all";
  stores: { store_id: string; display_name: string | null; has_data?: boolean }[];
};

function pageTitle(pathname: string, items: NavItem[]) {
  const hit = items.find((n) =>
    n.href === "/" ? pathname === "/" : pathname === n.href || pathname.startsWith(n.href + "/")
  );
  return hit?.title || hit?.label || "MyPOS";
}

function syncAgeLabel(iso: string | null | undefined): string | null {
  if (!iso) return "No POS sync yet";
  const ms = Date.now() - new Date(iso).getTime();
  if (Number.isNaN(ms) || ms < 0) return null;
  const hours = ms / 3_600_000;
  if (hours < 36) return null;
  const days = Math.floor(hours / 24);
  if (days < 1) return `Last POS sync ${Math.floor(hours)}h ago`;
  return `Last POS sync ${days} day${days === 1 ? "" : "s"} ago`;
}

export function AppShell({
  user,
  children,
  lastSyncAtUtc,
  resetStoreToAll = false,
}: {
  user: ShellUser;
  children: React.ReactNode;
  lastSyncAtUtc?: string | null;
  /** Persist empty-shop → All shops via Route Handler (cookie write). */
  resetStoreToAll?: boolean;
}) {
  const pathname = usePathname();
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [live, setLive] = useState<"connecting" | "live" | "offline">("connecting");
  const syncWarn = useMemo(() => syncAgeLabel(lastSyncAtUtc), [lastSyncAtUtc]);

  const items = useMemo(
    () =>
      NAV.filter((n) => !n.permission || user.permissions.includes(n.permission)),
    [user.permissions]
  );

  const title = pageTitle(pathname, items);

  useEffect(() => {
    if (!resetStoreToAll) return;
    let cancelled = false;
    (async () => {
      try {
        await fetch("/api/stores/select", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ storeId: "all" }),
        });
        if (!cancelled) router.refresh();
      } catch {
        // ignore — in-memory filter already uses All shops
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [resetStoreToAll, router]);

  useEffect(() => {
    let refreshTimer: ReturnType<typeof setTimeout> | undefined;
    const scheduleRefresh = () => {
      if (typeof document !== "undefined" && document.visibilityState === "hidden") return;
      if (refreshTimer) clearTimeout(refreshTimer);
      // Debounce hard — sync poll + live sales can otherwise re-SSR every few seconds.
      refreshTimer = setTimeout(() => router.refresh(), 4000);
    };

    const es = new EventSource("/api/realtime/stream");
    es.onopen = () => setLive("live");
    es.onerror = () => setLive("offline");
    es.onmessage = (msg) => {
      try {
        const data = JSON.parse(msg.data);
        if (data.type === "sync") scheduleRefresh();
        if (data.type === "connected" || data.type === "ping") setLive("live");
      } catch {
        // ignore
      }
    };
    const onVisible = () => {
      if (document.visibilityState === "visible") setLive((v) => v);
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      es.close();
      document.removeEventListener("visibilitychange", onVisible);
      if (refreshTimer) clearTimeout(refreshTimer);
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

  const today = new Date().toLocaleDateString("en-IN", {
    weekday: "short",
    day: "2-digit",
    month: "short",
    year: "numeric",
  });

  return (
    <DrillDownProvider>
    <div className="min-h-screen bg-[#f1f5f9] text-slate-900">
      <div className="flex min-h-screen">
        <aside
          className={`fixed inset-y-0 left-0 z-40 flex w-64 transform flex-col bg-[#0f172a] text-slate-200 transition md:static md:translate-x-0 ${
            open ? "translate-x-0" : "-translate-x-full"
          }`}
        >
          <div className="border-b border-white/10 px-4 py-4">
            <div className="text-lg font-semibold tracking-tight text-white">
              {user.tenantName || "MyPOS"}
            </div>
            <div className="text-xs text-slate-400">Reporting Dashboard</div>
          </div>
          <nav className="flex-1 space-y-0.5 overflow-y-auto p-2">
            {items.map((item) => {
              const active =
                item.href === "/"
                  ? pathname === "/"
                  : pathname === item.href || pathname.startsWith(item.href + "/");
              return (
                <Link
                  key={item.href}
                  href={item.href}
                  onClick={() => setOpen(false)}
                  className={`block rounded-lg px-3 py-2 text-sm font-medium ${
                    active
                      ? "bg-blue-600 text-white"
                      : "text-slate-300 hover:bg-white/10 hover:text-white"
                  }`}
                >
                  {item.label}
                </Link>
              );
            })}
          </nav>
          <div className="border-t border-white/10 px-4 py-3 text-xs text-slate-400">
            {today}
          </div>
        </aside>

        {open && (
          <button
            className="fixed inset-0 z-30 bg-black/40 md:hidden"
            aria-label="Close menu"
            onClick={() => setOpen(false)}
          />
        )}

        <div className="flex min-w-0 flex-1 flex-col">
          <header className="sticky top-0 z-20 flex h-14 items-center gap-3 border-b border-slate-200 bg-white/95 px-3 backdrop-blur print:hidden md:px-5">
            <button
              className="rounded-md border border-slate-200 px-2 py-1 text-sm md:hidden"
              onClick={() => setOpen(true)}
            >
              Menu
            </button>
            <h1 className="min-w-0 flex-1 truncate text-base font-semibold text-slate-900 md:text-lg">
              {title}
            </h1>
            <div className="hidden text-xs text-slate-500 sm:block">
              {user.fullName} · {user.roleName}
            </div>
            <select
              className="max-w-[40vw] rounded-md border border-slate-200 bg-white px-2 py-1.5 text-sm md:max-w-xs"
              value={user.selectedStoreId}
              onChange={(e) => onStoreChange(e.target.value)}
            >
              <option value="all">All shops</option>
              {user.stores.map((s) => (
                <option key={s.store_id} value={s.store_id}>
                  {s.display_name || s.store_id}
                  {s.has_data === false ? " (no data)" : ""}
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
              onClick={() => router.refresh()}
              className="rounded-md border border-slate-200 px-2 py-1 text-sm text-slate-600 hover:bg-slate-50"
              title="Refresh"
            >
              Refresh
            </button>
            <button
              onClick={logout}
              className="rounded-md border border-slate-200 px-2 py-1 text-sm text-slate-600 hover:bg-slate-50"
            >
              Logout
            </button>
          </header>
          <main className="min-w-0 flex-1 p-3 md:p-6">
            {syncWarn ? (
              <div className="mb-4 rounded-lg border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900">
                {syncWarn}. Today&apos;s sales stay at ₹0 until PharmaPOS on the shop PC is online
                with MySQL reporting sync enabled.
              </div>
            ) : null}
            {children}
          </main>
        </div>
      </div>
    </div>
    </DrillDownProvider>
  );
}
