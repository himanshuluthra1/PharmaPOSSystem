export function inr(n: number | string | null | undefined) {
  const v = Number(n ?? 0);
  return new Intl.NumberFormat("en-IN", {
    style: "currency",
    currency: "INR",
    maximumFractionDigits: 2,
  }).format(v);
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
