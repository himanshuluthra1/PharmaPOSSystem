import { NextRequest, NextResponse } from "next/server";
import { RowDataPacket } from "mysql2";
import { getSession } from "@/lib/auth";
import { query } from "@/lib/db";
import { getPurchaseDetail, getSaleDetail } from "@/lib/data";

export async function GET(req: NextRequest) {
  const session = await getSession();
  if (!session.isLoggedIn || !session.user) {
    return NextResponse.json({ error: "Unauthorized" }, { status: 401 });
  }

  const sp = req.nextUrl.searchParams;
  const kind = String(sp.get("kind") || "");
  const storeId = String(sp.get("storeId") || "");
  const localId = Number(sp.get("localId") || 0);

  if ((kind !== "sale" && kind !== "purchase") || !storeId || !localId) {
    return NextResponse.json({ error: "Invalid request" }, { status: 400 });
  }

  const storeRows = await query<RowDataPacket[]>(
    `SELECT store_id FROM tenant_stores WHERE tenant_id = ?`,
    [session.user.tenantId]
  );
  const storeIds = storeRows.map((s) => String(s.store_id));
  if (!storeIds.includes(storeId)) {
    return NextResponse.json({ error: "Forbidden" }, { status: 403 });
  }

  const user = {
    ...session.user,
    storeIds,
    selectedStoreId: session.user.selectedStoreId,
  };

  try {
    if (kind === "sale") {
      if (
        !user.permissions.includes("sales.view") &&
        !user.permissions.includes("dashboard.view")
      ) {
        return NextResponse.json({ error: "Forbidden" }, { status: 403 });
      }
      const detail = await getSaleDetail(user, storeId, localId);
      if (!detail) return NextResponse.json({ error: "Not found" }, { status: 404 });
      const { sale, items, payments } = detail;
      return NextResponse.json({
        kind: "sale",
        storeId,
        localId,
        header: {
          invoice: String(sale.invoice_number || `#${localId}`),
          date: sale.invoice_date,
          customer: String(sale.billing_customer_name || "Walk-in"),
          grandTotal: Number(sale.grand_total ?? 0),
          paid: Number(sale.paid_amount ?? 0),
          discount: Number(sale.discount_amount ?? 0),
          cogs: Number(sale.cogs ?? 0),
          gst:
            Number(sale.cgst_amount ?? 0) +
            Number(sale.sgst_amount ?? 0) +
            Number(sale.igst_amount ?? 0),
          margin: Number(sale.grand_total ?? 0) - Number(sale.cogs ?? 0),
        },
        items: items.map((i) => ({
          item: String(i.medicine_name || `#${i.medicine_local_id}`),
          medicineLocalId: Number(i.medicine_local_id || 0),
          batch: String(i.batch_number || "—"),
          qty: Number(i.quantity ?? 0),
          rate: Number(i.unit_price ?? 0),
          amount: Number(i.line_total ?? 0),
        })),
        payments: payments.map((p) => ({
          method: Number(p.method ?? 0),
          reference: String(p.reference_number || "—"),
          amount: Number(p.amount ?? 0),
        })),
        href: `/sales/${storeId}/${localId}`,
      });
    }

    if (
      !user.permissions.includes("purchases.view") &&
      !user.permissions.includes("dashboard.view") &&
      !user.permissions.includes("payments.view")
    ) {
      return NextResponse.json({ error: "Forbidden" }, { status: 403 });
    }
    const detail = await getPurchaseDetail(user, storeId, localId);
    if (!detail) return NextResponse.json({ error: "Not found" }, { status: 404 });
    const { purchase, items, supplierName } = detail;
    return NextResponse.json({
      kind: "purchase",
      storeId,
      localId,
      header: {
        invoice: String(purchase.invoice_number || `#${localId}`),
        date: purchase.invoice_date,
        vendor: supplierName || "Supplier",
        vendorBill: String(purchase.supplier_invoice_number || "—"),
        grandTotal: Number(purchase.grand_total ?? 0),
        paid: Number(purchase.paid_amount ?? 0),
        balance: Math.max(
          Number(purchase.grand_total ?? 0) - Number(purchase.paid_amount ?? 0),
          0
        ),
      },
      items: items.map((i) => ({
        item: String(i.medicine_name || `#${i.medicine_local_id}`),
        medicineLocalId: Number(i.medicine_local_id || 0),
        batch: String(i.batch_number || "—"),
        qty: Number(i.quantity ?? 0),
        free: Number(i.free_quantity ?? 0),
        rate: Number(i.purchase_price ?? 0),
        amount: Number(i.line_total ?? 0),
      })),
      href: `/purchases/${storeId}/${localId}`,
    });
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Failed";
    return NextResponse.json({ error: msg }, { status: 500 });
  }
}
