export type DrillMetric =
  | "sale"
  | "saleCost"
  | "grossMargin"
  | "gst"
  | "saleBills"
  | "purchase"
  | "purchaseReturns"
  | "purchaseNet"
  | "collection"
  | "supplierPayment"
  | "expense"
  | "netCash"
  | "netProfit";

export type DrillLevel = "years" | "months" | "days" | "bills";

export type DrillBillDetail = {
  kind: "sale" | "purchase";
  storeId: string;
  localId: number;
};

export type DrillRow = {
  id: string;
  label: string;
  amount: number;
  count?: number;
  extras?: Record<string, number>;
  /** Extra display fields keyed by column key (string or number). */
  cols?: Record<string, string | number | null>;
  nextFrom?: string;
  nextTo?: string;
  nextLevel?: DrillLevel;
  /** Full-page link (optional secondary). Prefer `detail` for in-modal popup. */
  href?: string;
  detail?: DrillBillDetail;
};

export type DrillResult = {
  title: string;
  metric: DrillMetric;
  level: DrillLevel;
  from: string;
  to: string;
  breadcrumb: { label: string; from: string; to: string; level: DrillLevel }[];
  columns: { key: string; label: string; align?: "left" | "right" }[];
  rows: DrillRow[];
};
