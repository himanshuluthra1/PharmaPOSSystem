export function inr(n: number | string | null | undefined) {
  const v = Number(n ?? 0);
  return new Intl.NumberFormat("en-IN", {
    style: "currency",
    currency: "INR",
    maximumFractionDigits: 2,
  }).format(v);
}

/** Medwin-style compact money: Cr / L / plain. */
export function fmtShort(n: number | string | null | undefined): string {
  const v = Number(n ?? 0);
  if (Math.abs(v) >= 1e7) return `${(v / 1e7).toFixed(2)} Cr`;
  if (Math.abs(v) >= 1e5) return `${(v / 1e5).toFixed(2)} L`;
  return new Intl.NumberFormat("en-IN", { maximumFractionDigits: 0 }).format(v);
}

export function rupeeShort(n: number | string | null | undefined): string {
  return `₹${fmtShort(n)}`;
}

export function fmtDate(d: Date | string | null | undefined) {
  if (!d) return "—";
  const date = typeof d === "string" ? new Date(d) : d;
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("en-IN", {
    day: "2-digit",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  }).format(date);
}

export function fmtDateOnly(d: Date | string | null | undefined) {
  if (!d) return "—";
  const date = typeof d === "string" ? new Date(d) : d;
  if (Number.isNaN(date.getTime())) return "—";
  return new Intl.DateTimeFormat("en-IN", {
    day: "2-digit",
    month: "short",
    year: "numeric",
  }).format(date);
}

export function paymentMethodLabel(method: number) {
  switch (method) {
    case 0:
      return "Cash";
    case 1:
      return "Card";
    case 2:
      return "UPI";
    case 3:
      return "Credit";
    case 4:
      return "Bank transfer";
    default:
      return `Method ${method}`;
  }
}
