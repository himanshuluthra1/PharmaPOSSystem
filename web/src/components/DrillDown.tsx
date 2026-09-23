"use client";

import Link from "next/link";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from "react";
import { fmtDate, fmtDateOnly, inr, paymentMethodLabel, rupeeShort } from "@/lib/format";
import type {
  DrillBillDetail,
  DrillLevel,
  DrillMetric,
  DrillResult,
  DrillRow,
} from "@/lib/drilldown-types";

export type DrillOpenArgs = {
  metric: DrillMetric;
  from: string;
  to: string;
  title?: string;
  level?: DrillLevel;
};

type StackFrame = DrillOpenArgs & { label?: string };

type DrillCtx = {
  open: (args: DrillOpenArgs) => void;
  openBill: (detail: DrillBillDetail) => void;
  openMedicine: (args: { storeId: string; medicineLocalId: number }) => void;
  openStock: (args?: { filter?: string; storeId?: string; title?: string }) => void;
  close: () => void;
};

const Ctx = createContext<DrillCtx | null>(null);

export function useDrillDown() {
  const ctx = useContext(Ctx);
  if (!ctx) {
    return {
      open: () => {},
      openBill: () => {},
      openMedicine: () => {},
      openStock: () => {},
      close: () => {},
    };
  }
  return ctx;
}

const MONEY_KEYS = new Set([
  "amount",
  "sale",
  "cost",
  "margin",
  "gst",
  "collection",
  "paid",
  "balance",
  "purchase",
  "returns",
  "supplierPayment",
  "expense",
  "grossMargin",
]);

const COUNT_KEYS = new Set(["count", "bills"]);

function cellValue(key: string, row: DrillRow, metric?: DrillMetric): ReactNode {
  if (key === "label") return row.label || row.cols?.label || "—";

  const raw =
    row.cols && key in row.cols
      ? row.cols[key]
      : key === "amount"
        ? row.amount
        : key === "count"
          ? row.count
          : row.extras?.[key];

  if (raw === null || raw === undefined || raw === "") return "—";
  if (key === "date") return fmtDateOnly(String(raw));
  if (typeof raw === "number") {
    if (COUNT_KEYS.has(key) || (key === "amount" && metric === "saleBills" && !(row.cols && "sale" in row.cols))) {
      return String(Math.round(raw));
    }
    if (MONEY_KEYS.has(key) || key === "amount") return rupeeShort(raw);
    return String(raw);
  }
  return String(raw);
}

type SaleDetailJson = {
  kind: "sale";
  storeId: string;
  header: {
    invoice: string;
    date: string;
    customer: string;
    grandTotal: number;
    paid: number;
    discount: number;
    cogs: number;
    gst: number;
    margin: number;
  };
  items: {
    item: string;
    medicineLocalId: number;
    batch: string;
    qty: number;
    rate: number;
    amount: number;
  }[];
  payments: { method: number; reference: string; amount: number }[];
  href: string;
};

type PurchaseDetailJson = {
  kind: "purchase";
  storeId: string;
  header: {
    invoice: string;
    date: string;
    vendor: string;
    vendorBill: string;
    grandTotal: number;
    paid: number;
    balance: number;
  };
  items: {
    item: string;
    medicineLocalId: number;
    batch: string;
    qty: number;
    free: number;
    rate: number;
    amount: number;
  }[];
  href: string;
};

type BillDetailJson = SaleDetailJson | PurchaseDetailJson;

function BillDetailView({
  detail,
  onBack,
}: {
  detail: DrillBillDetail;
  onBack: () => void;
}) {
  const [data, setData] = useState<BillDetailJson | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    const q = new URLSearchParams({
      kind: detail.kind,
      storeId: detail.storeId,
      localId: String(detail.localId),
    });
    fetch(`/api/drilldown/detail?${q}`)
      .then(async (res) => {
        const json = await res.json();
        if (!res.ok) throw new Error(json.error || "Failed");
        if (!cancelled) setData(json as BillDetailJson);
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : "Failed");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [detail]);

  if (loading) {
    return <div className="py-12 text-center text-sm text-slate-500">Loading bill…</div>;
  }
  if (error || !data) {
    return (
      <div className="space-y-3 py-8 text-center">
        <div className="text-sm text-rose-600">{error || "Not found"}</div>
        <button
          type="button"
          onClick={onBack}
          className="rounded-md border border-slate-200 px-3 py-1.5 text-sm hover:bg-slate-50"
        >
          Back to list
        </button>
      </div>
    );
  }

  if (data.kind === "sale") {
    const h = data.header;
    return (
      <div className="space-y-4">
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <div className="text-base font-semibold text-slate-900">{h.invoice}</div>
            <div className="text-sm text-slate-500">
              {fmtDate(h.date)} · {h.customer}
            </div>
          </div>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={onBack}
              className="rounded-md border border-slate-200 px-2 py-1 text-sm hover:bg-slate-50"
            >
              ← Back
            </button>
            <Link
              href={data.href}
              className="rounded-md border border-slate-200 px-2 py-1 text-sm text-blue-700 hover:bg-blue-50"
            >
              Open page
            </Link>
          </div>
        </div>
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-6">
          {[
            ["Sale", h.grandTotal],
            ["Cost", h.cogs],
            ["Margin", h.margin],
            ["GST", h.gst],
            ["Paid", h.paid],
            ["Discount", h.discount],
          ].map(([label, val]) => (
            <div key={String(label)} className="rounded-lg border border-slate-100 bg-slate-50 px-3 py-2">
              <div className="text-[11px] font-medium uppercase text-slate-500">{label}</div>
              <div className="text-sm font-semibold tabular-nums">{inr(Number(val))}</div>
            </div>
          ))}
        </div>
        <div>
          <h3 className="mb-2 text-sm font-semibold text-slate-800">Items</h3>
          <div className="overflow-x-auto rounded-lg border border-slate-200">
            <table className="w-full border-collapse text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase text-slate-500">
                <tr>
                  <th className="px-3 py-2">Item</th>
                  <th className="px-3 py-2">Batch</th>
                  <th className="px-3 py-2 text-right">Qty</th>
                  <th className="px-3 py-2 text-right">Rate</th>
                  <th className="px-3 py-2 text-right">Amount</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((i, idx) => (
                  <tr key={idx} className="border-t border-slate-100">
                    <td className="px-3 py-2">
                      {i.medicineLocalId > 0 ? (
                        <MedicineLink storeId={data.storeId} medicineLocalId={i.medicineLocalId}>
                          {i.item}
                        </MedicineLink>
                      ) : (
                        i.item
                      )}
                    </td>
                    <td className="px-3 py-2">{i.batch}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{i.qty.toFixed(2)}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{inr(i.rate)}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{inr(i.amount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
        <div>
          <h3 className="mb-2 text-sm font-semibold text-slate-800">Payments</h3>
          {data.payments.length === 0 ? (
            <div className="text-sm text-slate-500">No payments</div>
          ) : (
            <div className="overflow-x-auto rounded-lg border border-slate-200">
              <table className="w-full border-collapse text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase text-slate-500">
                  <tr>
                    <th className="px-3 py-2">Method</th>
                    <th className="px-3 py-2">Reference</th>
                    <th className="px-3 py-2 text-right">Amount</th>
                  </tr>
                </thead>
                <tbody>
                  {data.payments.map((p, idx) => (
                    <tr key={idx} className="border-t border-slate-100">
                      <td className="px-3 py-2">{paymentMethodLabel(p.method)}</td>
                      <td className="px-3 py-2">{p.reference}</td>
                      <td className="px-3 py-2 text-right tabular-nums">{inr(p.amount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    );
  }

  const h = data.header;
  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <div className="text-base font-semibold text-slate-900">{h.invoice}</div>
          <div className="text-sm text-slate-500">
            {fmtDate(h.date)} · {h.vendor} · Vendor bill {h.vendorBill}
          </div>
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={onBack}
            className="rounded-md border border-slate-200 px-2 py-1 text-sm hover:bg-slate-50"
          >
            ← Back
          </button>
          <Link
            href={data.href}
            className="rounded-md border border-slate-200 px-2 py-1 text-sm text-blue-700 hover:bg-blue-50"
          >
            Open page
          </Link>
        </div>
      </div>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
        {[
          ["Amount", h.grandTotal],
          ["Paid", h.paid],
          ["Balance", h.balance],
        ].map(([label, val]) => (
          <div key={String(label)} className="rounded-lg border border-slate-100 bg-slate-50 px-3 py-2">
            <div className="text-[11px] font-medium uppercase text-slate-500">{label}</div>
            <div className="text-sm font-semibold tabular-nums">{inr(Number(val))}</div>
          </div>
        ))}
      </div>
      <div>
        <h3 className="mb-2 text-sm font-semibold text-slate-800">Items</h3>
        <div className="overflow-x-auto rounded-lg border border-slate-200">
          <table className="w-full border-collapse text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase text-slate-500">
              <tr>
                <th className="px-3 py-2">Item</th>
                <th className="px-3 py-2">Batch</th>
                <th className="px-3 py-2 text-right">Qty</th>
                <th className="px-3 py-2 text-right">Free</th>
                <th className="px-3 py-2 text-right">Cost</th>
                <th className="px-3 py-2 text-right">Amount</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((i, idx) => (
                <tr key={idx} className="border-t border-slate-100">
                  <td className="px-3 py-2">
                    {i.medicineLocalId > 0 ? (
                      <MedicineLink storeId={data.storeId} medicineLocalId={i.medicineLocalId}>
                        {i.item}
                      </MedicineLink>
                    ) : (
                      i.item
                    )}
                  </td>
                  <td className="px-3 py-2">{i.batch}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{i.qty.toFixed(2)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{i.free.toFixed(2)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{inr(i.rate)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{inr(i.amount)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function DrillDownModal({
  stack,
  setStack,
  onClose,
}: {
  stack: StackFrame[];
  setStack: (s: StackFrame[]) => void;
  onClose: () => void;
}) {
  const current = stack[stack.length - 1];
  const [data, setData] = useState<DrillResult | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [bill, setBill] = useState<DrillBillDetail | null>(null);

  useEffect(() => {
    if (!current) return;
    let cancelled = false;
    setLoading(true);
    setError(null);
    setBill(null);
    const q = new URLSearchParams({
      metric: current.metric,
      from: current.from,
      to: current.to,
    });
    if (current.level) q.set("level", current.level);
    if (current.title) q.set("title", current.title);

    fetch(`/api/drilldown?${q}`)
      .then(async (res) => {
        const json = await res.json();
        if (!res.ok) throw new Error(json.error || "Failed");
        if (!cancelled) setData(json as DrillResult);
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : "Failed");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [current]);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") {
        if (bill) setBill(null);
        else onClose();
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose, bill]);

  function goTo(row: DrillRow) {
    if (row.detail) {
      setBill(row.detail);
      return;
    }
    if (row.nextLevel && row.nextFrom && row.nextTo && current) {
      setStack([
        ...stack,
        {
          metric: current.metric,
          from: row.nextFrom,
          to: row.nextTo,
          level: row.nextLevel,
          title: current.title,
          label: row.label,
        },
      ]);
    }
  }

  return (
    <div className="fixed inset-0 z-[100] flex items-end justify-center sm:items-center">
      <button
        type="button"
        className="absolute inset-0 bg-slate-900/50"
        aria-label="Close"
        onClick={onClose}
      />
      <div
        role="dialog"
        aria-modal="true"
        className="relative z-10 flex max-h-[92vh] w-full max-w-5xl flex-col rounded-t-2xl border border-slate-200 bg-white shadow-xl sm:rounded-2xl"
      >
        <div className="flex items-start justify-between gap-3 border-b border-slate-100 px-4 py-3">
          <div className="min-w-0">
            <div className="text-sm font-semibold text-slate-900">
              {bill
                ? bill.kind === "sale"
                  ? "Sale bill"
                  : "Purchase bill"
                : data?.title || current?.title || "Drill-down"}
            </div>
            {!bill ? (
              <div className="mt-1 flex flex-wrap items-center gap-1 text-xs text-slate-500">
                {stack.map((f, i) => (
                  <span key={i} className="inline-flex items-center gap-1">
                    {i > 0 ? <span className="text-slate-300">/</span> : null}
                    <button
                      type="button"
                      className={`rounded px-1 py-0.5 hover:bg-slate-100 ${
                        i === stack.length - 1
                          ? "font-medium text-slate-800"
                          : "text-blue-700"
                      }`}
                      onClick={() => setStack(stack.slice(0, i + 1))}
                    >
                      {f.label || f.title || f.metric}
                      {f.level ? ` (${f.level})` : ""}
                    </button>
                  </span>
                ))}
              </div>
            ) : null}
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-slate-200 px-2 py-1 text-sm text-slate-600 hover:bg-slate-50"
          >
            Close
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-auto p-4">
          {bill ? (
            <BillDetailView detail={bill} onBack={() => setBill(null)} />
          ) : loading ? (
            <div className="py-12 text-center text-sm text-slate-500">Loading…</div>
          ) : error ? (
            <div className="py-12 text-center text-sm text-rose-600">{error}</div>
          ) : !data || data.rows.length === 0 ? (
            <div className="py-12 text-center text-sm text-slate-500">No entries</div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[640px] border-collapse text-left text-sm">
                <thead className="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                  <tr>
                    {data.columns.map((c) => (
                      <th
                        key={c.key}
                        className={`px-3 py-2 font-medium whitespace-nowrap ${
                          (c.align || (c.key === "label" ? "left" : "right")) === "right"
                            ? "text-right"
                            : "text-left"
                        }`}
                      >
                        {c.label}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {data.rows.map((row) => {
                    const clickable = Boolean(row.detail || row.nextLevel);
                    return (
                      <tr
                        key={row.id}
                        className={`border-b border-slate-100 ${
                          clickable ? "cursor-pointer hover:bg-blue-50/60" : ""
                        }`}
                        onClick={() => clickable && goTo(row)}
                      >
                        {data.columns.map((c) => {
                          const align =
                            c.align || (c.key === "label" || ["date", "invoice", "customer", "vendor", "vendorBill", "head", "store", "notes", "type", "ref", "party", "status"].includes(c.key)
                              ? "left"
                              : "right");
                          const isInvoice = c.key === "invoice" || c.key === "ref";
                          return (
                            <td
                              key={c.key}
                              className={`px-3 py-2 whitespace-nowrap ${
                                align === "right" ? "text-right tabular-nums" : ""
                              } ${isInvoice && row.detail ? "font-medium text-blue-700" : ""}`}
                            >
                              {cellValue(c.key, row, data.metric)}
                            </td>
                          );
                        })}
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function PopupShell({
  title,
  onClose,
  children,
}: {
  title: string;
  onClose: () => void;
  children: ReactNode;
}) {
  return (
    <div className="fixed inset-0 z-[100] flex items-end justify-center sm:items-center">
      <button
        type="button"
        className="absolute inset-0 bg-slate-900/50"
        aria-label="Close"
        onClick={onClose}
      />
      <div
        role="dialog"
        aria-modal="true"
        className="relative z-10 flex max-h-[92vh] w-full max-w-5xl flex-col rounded-t-2xl border border-slate-200 bg-white shadow-xl sm:rounded-2xl"
      >
        <div className="flex items-center justify-between gap-3 border-b border-slate-100 px-4 py-3">
          <div className="truncate text-sm font-semibold text-slate-900">{title}</div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-md border border-slate-200 px-2 py-1 text-sm text-slate-600 hover:bg-slate-50"
          >
            Close
          </button>
        </div>
        <div className="min-h-0 flex-1 overflow-auto p-4">{children}</div>
      </div>
    </div>
  );
}

function MedicineLedgerView({
  storeId,
  medicineLocalId,
}: {
  storeId: string;
  medicineLocalId: number;
}) {
  const { openBill } = useDrillDown();
  const [data, setData] = useState<{
    medicine: {
      name: string;
      generic: string;
      brand: string;
      composition: string;
      strength: string;
      hsn: string;
      gst: number;
      mrp: number;
      purchasePrice: number;
      sellingPrice: number;
      reorderLevel: number;
      stockQty: number;
      stockCost: number;
    };
    batches: {
      batch_number: string;
      expiry_date: string;
      quantity_available: number;
      purchase_price: number;
      mrp: number;
      rack_number: string;
      cost_value: number;
    }[];
    ledger: {
      id: string;
      date: string;
      type: string;
      batch: string;
      qty: number;
      balance: number | null;
      unitCost: number;
      amount: number;
      reference: string;
      party: string;
      href?: string;
    }[];
  } | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    const q = new URLSearchParams({
      storeId,
      medicineLocalId: String(medicineLocalId),
    });
    fetch(`/api/medicine-ledger?${q}`)
      .then(async (res) => {
        const json = await res.json();
        if (!res.ok) throw new Error(json.error || "Failed");
        if (!cancelled) setData(json);
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : "Failed");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [storeId, medicineLocalId]);

  if (loading) return <div className="py-12 text-center text-sm text-slate-500">Loading ledger…</div>;
  if (error || !data) {
    return <div className="py-12 text-center text-sm text-rose-600">{error || "Not found"}</div>;
  }

  const m = data.medicine;
  return (
    <div className="space-y-4">
      <div>
        <div className="text-base font-semibold text-slate-900">{m.name}</div>
        <div className="text-sm text-slate-500">
          {[m.brand, m.generic, m.strength, m.composition].filter(Boolean).join(" · ") || "—"}
        </div>
      </div>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4 md:grid-cols-6">
        {[
          ["Stock qty", m.stockQty.toFixed(2)],
          ["Stock cost", inr(m.stockCost)],
          ["MRP", inr(m.mrp)],
          ["Purchase", inr(m.purchasePrice)],
          ["Selling", inr(m.sellingPrice)],
          ["GST %", `${m.gst}`],
          ["HSN", m.hsn || "—"],
          ["Reorder", String(m.reorderLevel)],
        ].map(([label, val]) => (
          <div key={String(label)} className="rounded-lg border border-slate-100 bg-slate-50 px-3 py-2">
            <div className="text-[11px] font-medium uppercase text-slate-500">{label}</div>
            <div className="truncate text-sm font-semibold">{val}</div>
          </div>
        ))}
      </div>

      <div>
        <h3 className="mb-2 text-sm font-semibold text-slate-800">
          Batches ({data.batches.length})
        </h3>
        <div className="overflow-x-auto rounded-lg border border-slate-200">
          <table className="w-full border-collapse text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase text-slate-500">
              <tr>
                <th className="px-3 py-2">Batch</th>
                <th className="px-3 py-2">Expiry</th>
                <th className="px-3 py-2">Rack</th>
                <th className="px-3 py-2 text-right">Qty</th>
                <th className="px-3 py-2 text-right">Cost</th>
                <th className="px-3 py-2 text-right">MRP</th>
                <th className="px-3 py-2 text-right">Value</th>
              </tr>
            </thead>
            <tbody>
              {data.batches.length === 0 ? (
                <tr>
                  <td colSpan={7} className="px-3 py-6 text-center text-slate-500">
                    No batches
                  </td>
                </tr>
              ) : (
                data.batches.map((b, i) => (
                  <tr key={i} className="border-t border-slate-100">
                    <td className="px-3 py-2">{b.batch_number || "—"}</td>
                    <td className="px-3 py-2">{fmtDateOnly(b.expiry_date)}</td>
                    <td className="px-3 py-2">{b.rack_number || "—"}</td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {Number(b.quantity_available).toFixed(2)}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">{inr(b.purchase_price)}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{inr(b.mrp)}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{inr(b.cost_value)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>

      <div>
        <h3 className="mb-2 text-sm font-semibold text-slate-800">
          Ledger ({data.ledger.length})
        </h3>
        <div className="overflow-x-auto rounded-lg border border-slate-200">
          <table className="w-full min-w-[720px] border-collapse text-left text-sm">
            <thead className="bg-slate-50 text-xs uppercase text-slate-500">
              <tr>
                <th className="px-3 py-2">Date</th>
                <th className="px-3 py-2">Type</th>
                <th className="px-3 py-2">Batch</th>
                <th className="px-3 py-2">Ref / Party</th>
                <th className="px-3 py-2 text-right">Qty</th>
                <th className="px-3 py-2 text-right">Balance</th>
                <th className="px-3 py-2 text-right">Rate</th>
                <th className="px-3 py-2 text-right">Amount</th>
              </tr>
            </thead>
            <tbody>
              {data.ledger.length === 0 ? (
                <tr>
                  <td colSpan={8} className="px-3 py-6 text-center text-slate-500">
                    No movements
                  </td>
                </tr>
              ) : (
                data.ledger.map((r) => {
                  const href = r.href || "";
                  const saleMatch = href.match(/^\/sales\/([^/]+)\/(\d+)/);
                  const purchaseMatch = href.match(/^\/purchases\/([^/]+)\/(\d+)/);
                  return (
                    <tr key={r.id} className="border-t border-slate-100">
                      <td className="px-3 py-2 whitespace-nowrap">{fmtDate(r.date)}</td>
                      <td className="px-3 py-2 whitespace-nowrap">{r.type}</td>
                      <td className="px-3 py-2">{r.batch}</td>
                      <td className="px-3 py-2">
                        {saleMatch ? (
                          <button
                            type="button"
                            className="font-medium text-blue-700 hover:underline"
                            onClick={() =>
                              openBill({
                                kind: "sale",
                                storeId: saleMatch[1],
                                localId: Number(saleMatch[2]),
                              })
                            }
                          >
                            {r.reference}
                            {r.party ? ` · ${r.party}` : ""}
                          </button>
                        ) : purchaseMatch ? (
                          <button
                            type="button"
                            className="font-medium text-blue-700 hover:underline"
                            onClick={() =>
                              openBill({
                                kind: "purchase",
                                storeId: purchaseMatch[1],
                                localId: Number(purchaseMatch[2]),
                              })
                            }
                          >
                            {r.reference}
                            {r.party ? ` · ${r.party}` : ""}
                          </button>
                        ) : (
                          <span>
                            {r.reference}
                            {r.party ? ` · ${r.party}` : ""}
                          </span>
                        )}
                      </td>
                      <td
                        className={`px-3 py-2 text-right tabular-nums ${
                          r.qty < 0 ? "text-rose-700" : r.qty > 0 ? "text-emerald-700" : ""
                        }`}
                      >
                        {r.qty.toFixed(2)}
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums">
                        {r.balance == null ? "—" : r.balance.toFixed(2)}
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums">{inr(r.unitCost)}</td>
                      <td className="px-3 py-2 text-right tabular-nums">{inr(r.amount)}</td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function StockDetailView({
  filter,
  storeId,
}: {
  filter?: string;
  storeId?: string;
}) {
  const { openMedicine } = useDrillDown();
  const [data, setData] = useState<{
    title: string;
    summary: { batches: number; qty: number; cost: number; mrp: number } | null;
    rows: {
      store_id: string;
      medicine_local_id: number;
      medicine_name: string;
      generic_name: string;
      brand: string;
      batch_number: string;
      expiry_date: string;
      quantity_available: number;
      purchase_price: number;
      mrp: number;
      rack_number: string;
      cost_value: number;
      mrp_value: number;
    }[];
  } | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    const q = new URLSearchParams();
    if (filter) q.set("filter", filter);
    if (storeId) q.set("storeId", storeId);
    fetch(`/api/stock-detail?${q}`)
      .then(async (res) => {
        const json = await res.json();
        if (!res.ok) throw new Error(json.error || "Failed");
        if (!cancelled) setData(json);
      })
      .catch((e) => {
        if (!cancelled) setError(e instanceof Error ? e.message : "Failed");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [filter, storeId]);

  if (loading) return <div className="py-12 text-center text-sm text-slate-500">Loading stock…</div>;
  if (error || !data) {
    return <div className="py-12 text-center text-sm text-rose-600">{error || "Not found"}</div>;
  }

  const s = data.summary;
  return (
    <div className="space-y-4">
      {s ? (
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
          {[
            ["Batches", String(s.batches)],
            ["Qty", s.qty.toFixed(2)],
            ["Cost value", inr(s.cost)],
            ["MRP value", inr(s.mrp)],
          ].map(([label, val]) => (
            <div key={String(label)} className="rounded-lg border border-slate-100 bg-slate-50 px-3 py-2">
              <div className="text-[11px] font-medium uppercase text-slate-500">{label}</div>
              <div className="text-sm font-semibold">{val}</div>
            </div>
          ))}
        </div>
      ) : null}
      <div className="overflow-x-auto rounded-lg border border-slate-200">
        <table className="w-full min-w-[800px] border-collapse text-left text-sm">
          <thead className="bg-slate-50 text-xs uppercase text-slate-500">
            <tr>
              <th className="px-3 py-2">Item</th>
              <th className="px-3 py-2">Company</th>
              <th className="px-3 py-2">Batch</th>
              <th className="px-3 py-2">Expiry</th>
              <th className="px-3 py-2">Rack</th>
              <th className="px-3 py-2 text-right">Qty</th>
              <th className="px-3 py-2 text-right">Cost</th>
              <th className="px-3 py-2 text-right">MRP</th>
              <th className="px-3 py-2 text-right">Value</th>
            </tr>
          </thead>
          <tbody>
            {data.rows.length === 0 ? (
              <tr>
                <td colSpan={9} className="px-3 py-6 text-center text-slate-500">
                  No stock rows
                </td>
              </tr>
            ) : (
              data.rows.map((r, i) => (
                <tr key={i} className="border-t border-slate-100">
                  <td className="px-3 py-2">
                    <button
                      type="button"
                      className="text-left font-medium text-blue-700 hover:underline"
                      onClick={() =>
                        openMedicine({
                          storeId: String(r.store_id),
                          medicineLocalId: Number(r.medicine_local_id),
                        })
                      }
                    >
                      {r.medicine_name}
                    </button>
                    {r.generic_name ? (
                      <div className="text-xs text-slate-400">{r.generic_name}</div>
                    ) : null}
                  </td>
                  <td className="px-3 py-2">{r.brand || "—"}</td>
                  <td className="px-3 py-2">{r.batch_number || "—"}</td>
                  <td className="px-3 py-2 whitespace-nowrap">{fmtDateOnly(r.expiry_date)}</td>
                  <td className="px-3 py-2">{r.rack_number || "—"}</td>
                  <td className="px-3 py-2 text-right tabular-nums">
                    {Number(r.quantity_available).toFixed(2)}
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums">{inr(r.purchase_price)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{inr(r.mrp)}</td>
                  <td className="px-3 py-2 text-right tabular-nums">{inr(r.cost_value)}</td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export function DrillDownProvider({ children }: { children: ReactNode }) {
  const [stack, setStack] = useState<StackFrame[] | null>(null);
  const [standaloneBill, setStandaloneBill] = useState<DrillBillDetail | null>(null);
  const [medicine, setMedicine] = useState<{ storeId: string; medicineLocalId: number } | null>(
    null
  );
  const [stock, setStock] = useState<{ filter?: string; storeId?: string; title?: string } | null>(
    null
  );

  const close = useCallback(() => {
    setStack(null);
    setStandaloneBill(null);
    setMedicine(null);
    setStock(null);
  }, []);

  const open = useCallback((args: DrillOpenArgs) => {
    setStandaloneBill(null);
    setMedicine(null);
    setStock(null);
    setStack([{ ...args, label: args.title || args.metric }]);
  }, []);

  const openBill = useCallback((detail: DrillBillDetail) => {
    setStack(null);
    setMedicine(null);
    setStock(null);
    setStandaloneBill(detail);
  }, []);

  const openMedicine = useCallback((args: { storeId: string; medicineLocalId: number }) => {
    setStack(null);
    setStandaloneBill(null);
    setStock(null);
    setMedicine(args);
  }, []);

  const openStock = useCallback(
    (args?: { filter?: string; storeId?: string; title?: string }) => {
      setStack(null);
      setStandaloneBill(null);
      setMedicine(null);
      setStock(args || { filter: "all" });
    },
    []
  );

  useEffect(() => {
    if (!standaloneBill && !medicine && !stock) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") close();
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [standaloneBill, medicine, stock, close]);

  return (
    <Ctx.Provider value={{ open, openBill, openMedicine, openStock, close }}>
      {children}
      {stack && stack.length > 0 ? (
        <DrillDownModal stack={stack} setStack={setStack} onClose={close} />
      ) : null}
      {standaloneBill ? (
        <PopupShell
          title={standaloneBill.kind === "sale" ? "Sale bill" : "Purchase bill"}
          onClose={close}
        >
          <BillDetailView detail={standaloneBill} onBack={close} />
        </PopupShell>
      ) : null}
      {medicine ? (
        <PopupShell title="Medicine ledger" onClose={close}>
          <MedicineLedgerView
            storeId={medicine.storeId}
            medicineLocalId={medicine.medicineLocalId}
          />
        </PopupShell>
      ) : null}
      {stock ? (
        <PopupShell title={stock.title || "Stock details"} onClose={close}>
          <StockDetailView filter={stock.filter} storeId={stock.storeId} />
        </PopupShell>
      ) : null}
    </Ctx.Provider>
  );
}

export function BillLink({
  kind,
  storeId,
  localId,
  children,
  className = "",
}: {
  kind: "sale" | "purchase";
  storeId: string;
  localId: number;
  children: ReactNode;
  className?: string;
}) {
  const { openBill } = useDrillDown();
  return (
    <button
      type="button"
      className={`font-medium text-blue-700 hover:underline ${className}`}
      onClick={(e) => {
        e.preventDefault();
        e.stopPropagation();
        openBill({ kind, storeId, localId });
      }}
    >
      {children}
    </button>
  );
}

export function MedicineLink({
  storeId,
  medicineLocalId,
  children,
  className = "",
}: {
  storeId: string;
  medicineLocalId: number;
  children: ReactNode;
  className?: string;
}) {
  const { openMedicine } = useDrillDown();
  return (
    <button
      type="button"
      className={`font-medium text-blue-700 hover:underline ${className}`}
      onClick={(e) => {
        e.preventDefault();
        e.stopPropagation();
        openMedicine({ storeId, medicineLocalId });
      }}
    >
      {children}
    </button>
  );
}

export function StockValue({
  children,
  filter = "all",
  storeId,
  title,
  className = "",
}: {
  children: ReactNode;
  filter?: string;
  storeId?: string;
  title?: string;
  className?: string;
}) {
  const { openStock } = useDrillDown();
  return (
    <button
      type="button"
      title="Click for stock details"
      className={`cursor-pointer rounded text-left underline decoration-dotted decoration-slate-300 underline-offset-2 transition hover:text-blue-700 hover:decoration-blue-400 ${className}`}
      onClick={(e) => {
        e.preventDefault();
        e.stopPropagation();
        openStock({ filter, storeId, title });
      }}
    >
      {children}
    </button>
  );
}

export function DrillValue({
  metric,
  from,
  to,
  title,
  level,
  children,
  className = "",
  compact,
}: {
  metric: DrillMetric;
  from: string;
  to: string;
  title?: string;
  level?: DrillLevel;
  children: ReactNode;
  className?: string;
  compact?: boolean;
}) {
  const { open } = useDrillDown();
  return (
    <button
      type="button"
      title="Click to drill down"
      onClick={(e) => {
        e.preventDefault();
        e.stopPropagation();
        open({ metric, from, to, title, level });
      }}
      className={`cursor-pointer rounded text-left underline decoration-dotted decoration-slate-300 underline-offset-2 transition hover:text-blue-700 hover:decoration-blue-400 ${
        compact ? "" : ""
      } ${className}`}
    >
      {children}
    </button>
  );
}

/** Format helper for server-rendered amounts wrapped later by DrillValue. */
export function formatDrillAmount(n: number, compact = true) {
  return compact ? rupeeShort(n) : inr(n);
}
