import { SessionOptions } from "iron-session";

export type SessionUser = {
  id: number;
  email: string;
  fullName: string;
  tenantId: number;
  tenantName: string;
  roleId: number;
  roleName: string;
  permissions: string[];
  storeIds: string[];
  selectedStoreId: string | "all";
};

export type SessionData = {
  user?: SessionUser;
  isLoggedIn: boolean;
};

export const sessionOptions: SessionOptions = {
  password:
    process.env.AUTH_SECRET ||
    "complex_password_at_least_32_characters_long_for_dev",
  cookieName: process.env.SESSION_COOKIE_NAME || "pharmapos_dash_session",
  cookieOptions: {
    secure: process.env.NODE_ENV === "production",
    httpOnly: true,
    sameSite: "lax",
    maxAge: 60 * 60 * 12,
  },
};

export const PERMISSIONS = {
  dashboard: "dashboard.view",
  sales: "sales.view",
  purchases: "purchases.view",
  stock: "stock.view",
  payments: "payments.view",
  returns: "returns.view",
  manageUsers: "stores.manage_users",
  manageStores: "stores.manage_stores",
} as const;
