"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";

export function CreateUserForm({
  roles,
}: {
  roles: { id: number; name: string }[];
}) {
  const router = useRouter();
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    const fd = new FormData(e.currentTarget);
    const res = await fetch("/api/admin/users", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        email: fd.get("email"),
        fullName: fd.get("fullName"),
        password: fd.get("password"),
        roleId: Number(fd.get("roleId")),
      }),
    });
    const data = await res.json().catch(() => ({}));
    setBusy(false);
    if (!res.ok) {
      setError(data.error || "Failed");
      return;
    }
    e.currentTarget.reset();
    router.refresh();
  }

  return (
    <form
      onSubmit={onSubmit}
      className="mb-6 grid gap-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm md:grid-cols-2"
    >
      <input
        name="fullName"
        required
        placeholder="Full name"
        className="rounded-lg border px-3 py-2 text-sm"
      />
      <input
        name="email"
        type="email"
        required
        placeholder="Email"
        className="rounded-lg border px-3 py-2 text-sm"
      />
      <input
        name="password"
        type="password"
        required
        minLength={6}
        placeholder="Password"
        className="rounded-lg border px-3 py-2 text-sm"
      />
      <select name="roleId" className="rounded-lg border px-3 py-2 text-sm" required>
        {roles.map((r) => (
          <option key={r.id} value={r.id}>
            {r.name}
          </option>
        ))}
      </select>
      {error ? (
        <div className="md:col-span-2 text-sm text-red-600">{error}</div>
      ) : null}
      <button
        disabled={busy}
        className="rounded-lg bg-teal-700 px-3 py-2 text-sm font-semibold text-white md:col-span-2"
      >
        {busy ? "Saving…" : "Add user"}
      </button>
    </form>
  );
}
