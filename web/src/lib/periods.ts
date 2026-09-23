/** Indian FY starts April 1. Dashboard periods use Asia/Kolkata business dates. */

export type PeriodMode = "daily" | "monthly" | "yearly" | "custom";

export type DateRange = {
  from: string; // YYYY-MM-DD
  to: string;
  label: string;
};

const INDIA_TZ = "Asia/Kolkata";

function pad(n: number) {
  return String(n).padStart(2, "0");
}

/** Calendar Y-M-D in India, independent of server TZ (VPS is often UTC). */
export function indiaTodayYmd(ref = new Date()): string {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: INDIA_TZ,
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(ref);
}

/** Local Date at midnight for an India calendar day (for arithmetic only). */
function indiaYmdAsLocalDate(ymd: string): Date {
  const [y, m, d] = ymd.split("-").map(Number);
  return new Date(y, m - 1, d);
}

export function toYmd(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Normalize DB date / ISO string / Date → YYYY-MM-DD. */
export function asYmd(d: Date | string | null | undefined): string {
  if (!d) return toYmd(new Date());
  if (d instanceof Date) {
    if (Number.isNaN(d.getTime())) return toYmd(new Date());
    return toYmd(d);
  }
  const s = String(d).trim();
  if (/^\d{4}-\d{2}-\d{2}/.test(s)) return s.slice(0, 10);
  const parsed = new Date(s);
  if (Number.isNaN(parsed.getTime())) return toYmd(new Date());
  return toYmd(parsed);
}

/** First/last day of a YYYY-MM month key. */
export function monthBoundsFromYm(ym: string): { from: string; to: string } {
  const [y, m] = ym.split("-").map(Number);
  const from = `${y}-${pad(m)}-01`;
  const to = toYmd(new Date(y, m, 0));
  return { from, to };
}

export function parseYmd(s: string): Date {
  const [y, m, d] = s.split("-").map(Number);
  return new Date(y, m - 1, d);
}

export function addDays(d: Date, n: number): Date {
  const x = new Date(d);
  x.setDate(x.getDate() + n);
  return x;
}

export function addMonths(d: Date, n: number): Date {
  const x = new Date(d);
  x.setMonth(x.getMonth() + n);
  return x;
}

/** FY containing India `ref` date, shifted by offset years. */
export function fyRange(offset = 0, ref = new Date()): DateRange {
  const [y, m] = indiaTodayYmd(ref).split("-").map(Number);
  let startYear = m >= 4 ? y : y - 1;
  startYear += offset;
  const from = new Date(startYear, 3, 1);
  const to = new Date(startYear + 1, 2, 31);
  return {
    from: toYmd(from),
    to: toYmd(to),
    label: `FY ${startYear}-${String(startYear + 1).slice(-2)}`,
  };
}

export function monthRange(offset = 0, ref = new Date()): DateRange {
  const base = indiaYmdAsLocalDate(indiaTodayYmd(ref));
  const d = addMonths(new Date(base.getFullYear(), base.getMonth(), 1), offset);
  const from = d;
  const to = new Date(d.getFullYear(), d.getMonth() + 1, 0);
  const label = d.toLocaleString("en-IN", { month: "short", year: "numeric" });
  return { from: toYmd(from), to: toYmd(to), label };
}

export function dayRange(offset = 0, ref = new Date()): DateRange {
  const d = addDays(indiaYmdAsLocalDate(indiaTodayYmd(ref)), offset);
  const s = toYmd(d);
  const label =
    offset === 0
      ? "Today"
      : offset === -1
        ? "Yesterday"
        : d.toLocaleDateString("en-IN", {
            day: "2-digit",
            month: "short",
            year: "numeric",
          });
  return { from: s, to: s, label };
}

export function resolvePeriod(
  mode: PeriodMode,
  offset = 0,
  customFrom?: string,
  customTo?: string
): DateRange {
  if (mode === "custom" && customFrom && customTo) {
    return {
      from: customFrom,
      to: customTo,
      label: `${customFrom} → ${customTo}`,
    };
  }
  if (mode === "yearly") return fyRange(offset);
  if (mode === "monthly") return monthRange(offset);
  return dayRange(offset);
}

export function daysInRange(from: string, to: string): number {
  const a = parseYmd(from).getTime();
  const b = parseYmd(to).getTime();
  return Math.max(1, Math.round((b - a) / 86400000) + 1);
}
