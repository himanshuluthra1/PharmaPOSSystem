import Link from "next/link";
import type { ReactNode } from "react";
import { DrillValue } from "@/components/DrillDown";
import { inr, rupeeShort } from "@/lib/format";
import type { DrillMetric } from "@/lib/drilldown-types";
import type { PeriodMode } from "@/lib/periods";

export type KpiTone =
  | "primary"
  | "success"
  | "warning"
  | "danger"
  | "teal"
  | "purple"
  | "orange"
  | "slate";

export type KpiDrill = {
  metric: DrillMetric;
  from: string;
  to: string;
  title?: string;
};

const KPI_TONE: Record<KpiTone, string> = {
  primary: "border-blue-200 bg-blue-50/80",
  success: "border-emerald-200 bg-emerald-50/80",
  warning: "border-amber-200 bg-amber-50/80",
  danger: "border-rose-200 bg-rose-50/80",
  teal: "border-teal-200 bg-teal-50/80",
  purple: "border-violet-200 bg-violet-50/80",
  orange: "border-orange-200 bg-orange-50/80",
  slate: "border-slate-200 bg-white",
};

export function KpiCard({
  label,
  value,
  hint,
  href,
  drill,
  tone = "slate",
  compact,
}: {
  label: string;
  value: string | number | ReactNode;
  hint?: string;
  href?: string;
  /** Prefer drill over href for clickable amounts. */
  drill?: KpiDrill;
  tone?: KpiTone;
  compact?: boolean;
}) {
  const display =
    typeof value === "number"
      ? compact
        ? rupeeShort(value)
        : inr(value)
      : value;
  const valueNode = drill ? (
    <DrillValue
      metric={drill.metric}
      from={drill.from}
      to={drill.to}
      title={drill.title || label}
      className="font-semibold text-slate-900"
    >
      {typeof display === "string" || typeof display === "number" ? display : display}
    </DrillValue>
  ) : (
    display
  );

  const content = (
    <div
      className={`rounded-xl border p-3 shadow-sm ${KPI_TONE[tone]} ${
        compact ? "" : "md:p-4"
      }`}
    >
      <div className="text-[11px] font-semibold uppercase tracking-wide text-slate-500">
        {label}
      </div>
      <div
        className={`mt-1 font-semibold text-slate-900 ${
          compact ? "text-lg" : "text-2xl"
        }`}
      >
        {valueNode}
      </div>
      {hint ? <div className="mt-1 text-xs text-slate-500">{hint}</div> : null}
    </div>
  );
  if (href && !drill) {
    return (
      <Link href={href} className="block transition hover:opacity-90">
        {content}
      </Link>
    );
  }
  return content;
}

export function PageHeader({
  title,
  subtitle,
}: {
  title: string;
  subtitle?: string;
}) {
  return (
    <div className="mb-5 print:mb-3">
      <h1 className="text-xl font-semibold text-slate-900 md:text-2xl">{title}</h1>
      {subtitle ? <p className="mt-1 text-sm text-slate-500">{subtitle}</p> : null}
    </div>
  );
}

export function Card({
  title,
  children,
  className = "",
  actions,
  padded = true,
}: {
  title?: ReactNode;
  children: React.ReactNode;
  className?: string;
  actions?: React.ReactNode;
  /** When false, body has no padding (use for full-bleed tables). */
  padded?: boolean;
}) {
  return (
    <div
      className={`w-full overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm ${className}`}
    >
      {title || actions ? (
        <div className="flex items-center justify-between gap-2 border-b border-slate-100 bg-slate-50/80 px-4 py-2.5">
          {title ? (
            <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
          ) : (
            <span />
          )}
          {actions}
        </div>
      ) : null}
      <div className={padded ? "p-4" : ""}>{children}</div>
    </div>
  );
}

export function DataTable({
  columns,
  rows,
  dense,
  embedded,
}: {
  columns: { key: string; label: React.ReactNode; className?: string; width?: string }[];
  rows: Record<string, React.ReactNode>[];
  dense?: boolean;
  /** Drop outer border/radius when already inside a Card. */
  embedded?: boolean;
}) {
  const pad = dense ? "px-3 py-2" : "px-4 py-3";
  return (
    <div
      className={
        embedded
          ? "w-full max-w-full overflow-x-auto"
          : "w-full max-w-full overflow-x-auto rounded-xl border border-slate-200 bg-white shadow-sm"
      }
    >
      <table className="w-full min-w-full table-fixed border-collapse text-left text-sm">
        <colgroup>
          {columns.map((c) => (
            <col key={c.key} style={c.width ? { width: c.width } : undefined} />
          ))}
        </colgroup>
        <thead className="border-b border-slate-200 bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
          <tr>
            {columns.map((c) => (
              <th
                key={c.key}
                className={`${pad} font-medium whitespace-nowrap ${c.className || ""}`}
              >
                {c.label}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.length === 0 ? (
            <tr>
              <td
                colSpan={columns.length}
                className="px-4 py-8 text-center text-slate-500"
              >
                No data
              </td>
            </tr>
          ) : (
            rows.map((row, i) => (
              <tr
                key={i}
                className="border-b border-slate-100 last:border-0 hover:bg-slate-50/70"
              >
                {columns.map((c) => (
                  <td
                    key={c.key}
                    className={`${pad} align-top break-words ${c.className || ""}`}
                  >
                    {row[c.key]}
                  </td>
                ))}
              </tr>
            ))
          )}
        </tbody>
      </table>
    </div>
  );
}

export function StackedHead({
  lines,
}: {
  lines: string[];
}) {
  return (
    <span className="inline-flex flex-col gap-0.5 normal-case tracking-normal">
      {lines.map((l) => (
        <span key={l} className="leading-tight">
          {l}
        </span>
      ))}
    </span>
  );
}

export function PeriodToolbar({
  mode,
  offset,
  basePath,
  from,
  to,
  showCustom,
}: {
  mode: PeriodMode;
  offset: number;
  basePath: string;
  from?: string;
  to?: string;
  showCustom?: boolean;
}) {
  const modes: PeriodMode[] = showCustom
    ? ["daily", "monthly", "yearly", "custom"]
    : ["daily", "monthly", "yearly"];

  function href(next: Partial<{ mode: PeriodMode; offset: number; from: string; to: string }>) {
    const p = new URLSearchParams();
    p.set("mode", next.mode ?? mode);
    p.set("offset", String(next.offset ?? offset));
    if ((next.mode ?? mode) === "custom") {
      if (next.from ?? from) p.set("from", next.from ?? from!);
      if (next.to ?? to) p.set("to", next.to ?? to!);
    }
    return `${basePath}?${p.toString()}`;
  }

  return (
    <div className="print-hide mb-4 flex flex-wrap items-center gap-2">
      <div className="inline-flex overflow-hidden rounded-lg border border-slate-200 bg-white text-sm">
        {modes.map((m) => (
          <Link
            key={m}
            href={href({ mode: m, offset: 0 })}
            className={`px-3 py-1.5 capitalize ${
              mode === m
                ? "bg-blue-600 text-white"
                : "text-slate-600 hover:bg-slate-50"
            }`}
          >
            {m}
          </Link>
        ))}
      </div>
      {mode !== "custom" ? (
        <div className="inline-flex items-center gap-1">
          <Link
            href={href({ offset: offset - 1 })}
            className="rounded-md border border-slate-200 bg-white px-2 py-1 text-sm hover:bg-slate-50"
          >
            ‹
          </Link>
          <Link
            href={href({ offset: offset + 1 })}
            className="rounded-md border border-slate-200 bg-white px-2 py-1 text-sm hover:bg-slate-50"
          >
            ›
          </Link>
        </div>
      ) : null}
      <Link
        href={basePath}
        className="rounded-md border border-slate-200 bg-white px-2 py-1 text-sm text-slate-600 hover:bg-slate-50"
      >
        Reset
      </Link>
    </div>
  );
}

export function signedMoney(n: number, compact = true) {
  const text = compact ? rupeeShort(n) : inr(n);
  if (n > 0) return <span className="font-medium text-emerald-700">{text}</span>;
  if (n < 0) return <span className="font-medium text-rose-700">{text}</span>;
  return <span className="text-slate-700">{text}</span>;
}

export function EmptyState({ title, detail }: { title: string; detail?: string }) {
  return (
    <div className="rounded-xl border border-dashed border-slate-300 bg-slate-50 px-4 py-10 text-center">
      <div className="text-sm font-semibold text-slate-700">{title}</div>
      {detail ? <p className="mt-1 text-sm text-slate-500">{detail}</p> : null}
    </div>
  );
}
