"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

export function StoreAdminActions({
  available,
  assigned,
}: {
  available: { store_id: string; store_code?: string; machine_name?: string }[];
  assigned: { store_id: string; display_name?: string | null }[];
}) {
  const router = useRouter();
  const [storeId, setStoreId] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function addStore() {
    setError(null);
    const res = await fetch("/api/admin/stores", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        action: "add",
        storeId: storeId || undefined,
        displayName,
      }),
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok) {
      setError(data.error || "Failed");
      return;
    }
    setStoreId("");
    setDisplayName("");
    router.refresh();
  }

  async function removeStore(id: string) {
    await fetch("/api/admin/stores", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action: "remove", storeId: id }),
    });
    router.refresh();
  }

  return (
    <div className="space-y-4">
      <div className="rounded-2xl border bg-white p-4 shadow-sm">
        <h2 className="mb-3 text-sm font-semibold">Assign store to this tenant</h2>
        <div className="grid gap-2 md:grid-cols-3">
          <select
            value={storeId}
            onChange={(e) => setStoreId(e.target.value)}
            className="rounded-lg border px-3 py-2 text-sm"
          >
            <option value="">Select approved store…</option>
            {available.map((s) => (
              <option key={s.store_id} value={s.store_id}>
                {(s.store_code || s.store_id) +
                  (s.machine_name ? ` · ${s.machine_name}` : "")}
              </option>
            ))}
          </select>
          <input
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            placeholder="Display name (optional)"
            className="rounded-lg border px-3 py-2 text-sm"
          />
          <button
            onClick={addStore}
            className="rounded-lg bg-teal-700 px-3 py-2 text-sm font-semibold text-white"
          >
            Add store
          </button>
        </div>
        {error ? <div className="mt-2 text-sm text-red-600">{error}</div> : null}
      </div>

      <div className="rounded-2xl border bg-white p-4 shadow-sm">
        <h2 className="mb-3 text-sm font-semibold">Assigned stores</h2>
        <ul className="divide-y">
          {assigned.map((s) => (
            <li
              key={s.store_id}
              className="flex items-center justify-between py-2 text-sm"
            >
              <div>
                <div className="font-medium">
                  {s.display_name || s.store_id}
                </div>
                <div className="text-xs text-slate-500">{s.store_id}</div>
              </div>
              <button
                onClick={() => removeStore(s.store_id)}
                className="rounded border px-2 py-1 text-xs text-red-600"
              >
                Remove
              </button>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}
