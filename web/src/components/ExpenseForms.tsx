"use client";

import { useRouter } from "next/navigation";
import { useState } from "react";

export function ExpenseForms({
  stores,
  heads,
}: {
  stores: { store_id: string; display_name: string | null }[];
  heads: { id: number; name: string }[];
}) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [msg, setMsg] = useState<string | null>(null);
  const defaultStore = stores[0]?.store_id || "";

  async function post(body: Record<string, unknown>) {
    setBusy(true);
    setMsg(null);
    try {
      const res = await fetch("/api/expenses", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(body),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.error || "Request failed");
      setMsg("Saved");
      router.refresh();
    } catch (e) {
      setMsg(e instanceof Error ? e.message : "Failed");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-4">
      {msg ? <p className="text-sm text-slate-600">{msg}</p> : null}

      <details className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <summary className="cursor-pointer text-sm font-semibold">Add expense head</summary>
        <form
          className="mt-3 flex flex-wrap gap-2"
          onSubmit={(e) => {
            e.preventDefault();
            const fd = new FormData(e.currentTarget);
            void post({ action: "head", name: fd.get("name") });
            e.currentTarget.reset();
          }}
        >
          <input
            name="name"
            required
            placeholder="Head name"
            className="rounded-md border border-slate-200 px-2 py-1.5 text-sm"
          />
          <button
            disabled={busy}
            className="rounded-md bg-blue-600 px-3 py-1.5 text-sm text-white disabled:opacity-50"
          >
            Save head
          </button>
        </form>
      </details>

      <details className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm" open>
        <summary className="cursor-pointer text-sm font-semibold">Add expense</summary>
        <form
          className="mt-3 grid gap-2 md:grid-cols-3"
          onSubmit={(e) => {
            e.preventDefault();
            const fd = new FormData(e.currentTarget);
            void post({
              action: "expense",
              storeId: fd.get("storeId"),
              headId: fd.get("headId") || null,
              expenseDate: fd.get("expenseDate"),
              amount: Number(fd.get("amount")),
              notes: fd.get("notes"),
              isRecurring: fd.get("isRecurring") === "on",
            });
            e.currentTarget.reset();
          }}
        >
          <select
            name="storeId"
            defaultValue={defaultStore}
            className="rounded-md border border-slate-200 px-2 py-1.5 text-sm"
          >
            {stores.map((s) => (
              <option key={s.store_id} value={s.store_id}>
                {s.display_name || s.store_id}
              </option>
            ))}
          </select>
          <select name="headId" className="rounded-md border border-slate-200 px-2 py-1.5 text-sm">
            <option value="">No head</option>
            {heads.map((h) => (
              <option key={h.id} value={h.id}>
                {h.name}
              </option>
            ))}
          </select>
          <input
            type="date"
            name="expenseDate"
            required
            defaultValue={new Date().toISOString().slice(0, 10)}
            className="rounded-md border border-slate-200 px-2 py-1.5 text-sm"
          />
          <input
            name="amount"
            type="number"
            step="0.01"
            min="0.01"
            required
            placeholder="Amount"
            className="rounded-md border border-slate-200 px-2 py-1.5 text-sm"
          />
          <input
            name="notes"
            placeholder="Notes"
            className="rounded-md border border-slate-200 px-2 py-1.5 text-sm md:col-span-2"
          />
          <label className="flex items-center gap-2 text-sm text-slate-600">
            <input type="checkbox" name="isRecurring" /> Recurring
          </label>
          <button
            disabled={busy || stores.length === 0}
            className="rounded-md bg-blue-600 px-3 py-1.5 text-sm text-white disabled:opacity-50"
          >
            Save expense
          </button>
        </form>
      </details>

      <details className="rounded-xl border border-slate-200 bg-white p-4 shadow-sm">
        <summary className="cursor-pointer text-sm font-semibold">
          Add collection / payment
        </summary>
        <form
          className="mt-3 grid gap-2 md:grid-cols-3"
          onSubmit={(e) => {
            e.preventDefault();
            const fd = new FormData(e.currentTarget);
            void post({
              action: "collection",
              storeId: fd.get("storeId"),
              entryDate: fd.get("entryDate"),
              entryType: fd.get("entryType"),
              mode: fd.get("mode"),
              amount: Number(fd.get("amount")),
              notes: fd.get("notes"),
            });
            e.currentTarget.reset();
          }}
        >
          <select
            name="storeId"
            defaultValue={defaultStore}
            className="rounded-md border border-slate-200 px-2 py-1.5 text-sm"
          >
            {stores.map((s) => (
              <option key={s.store_id} value={s.store_id}>
                {s.display_name || s.store_id}
              </option>
            ))}
          </select>
          <select name="entryType" className="rounded-md border border-slate-200 px-2 py-1.5 text-sm">
            <option value="collection">Collection</option>
            <option value="payment">Payment</option>
          </select>
          <select name="mode" className="rounded-md border border-slate-200 px-2 py-1.5 text-sm">
            <option>Cash</option>
            <option>UPI</option>
            <option>Card</option>
            <option>Bank</option>
            <option>Other</option>
          </select>
          <input
            type="date"
            name="entryDate"
            required
            defaultValue={new Date().toISOString().slice(0, 10)}
            className="rounded-md border border-slate-200 px-2 py-1.5 text-sm"
          />
          <input
            name="amount"
            type="number"
            step="0.01"
            min="0.01"
            required
            placeholder="Amount"
            className="rounded-md border border-slate-200 px-2 py-1.5 text-sm"
          />
          <input
            name="notes"
            placeholder="Notes"
            className="rounded-md border border-slate-200 px-2 py-1.5 text-sm"
          />
          <button
            disabled={busy || stores.length === 0}
            className="rounded-md bg-blue-600 px-3 py-1.5 text-sm text-white disabled:opacity-50"
          >
            Save entry
          </button>
        </form>
      </details>
    </div>
  );
}

export function DeleteButton({
  action,
  id,
}: {
  action: "delete-expense" | "delete-collection";
  id: number;
}) {
  const router = useRouter();
  return (
    <button
      className="text-xs text-rose-600 hover:underline"
      onClick={async () => {
        await fetch("/api/expenses", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ action, id }),
        });
        router.refresh();
      }}
    >
      Delete
    </button>
  );
}
