using System.Data.OleDb;
using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

public static class MedWinTransactionImporter
{
    public static async Task ImportSalesAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[sales] Importing sales from salemaster/dsalemaster...");
        if (ctx.MedicineMap.Count == 0)
        {
            await MedWinMasterImporter.LoadExistingMedicineMapAsync(ctx, target);
            if (ctx.MedicineMap.Count == 0)
                throw new InvalidOperationException("Import medicines before sales.");
        }

        using var med = ctx.OpenMedWin();
        med.Open();

        if (!ctx.Force)
        {
            var existing = await MedWinImporter.ScalarIntAsync(target,
                "SELECT COUNT(*) FROM Sales WHERE InvoiceNumber LIKE 'MW-S-%'");
            using var countCmd = new OleDbCommand("SELECT COUNT(*) FROM salemaster WHERE purblno >= 0", med);
            var sourceCount = ImportHelpers.Int(countCmd.ExecuteScalar());
            if (existing >= sourceCount && sourceCount > 0)
            {
                ctx.Log($"  MedWin sales already imported ({existing:N0}). Use --force to import again.");
                return;
            }
        }

        using var headerCmd = new OleDbCommand("""
            SELECT purblno, purbldt, billtime, pactamt, pgrossamt, purtaxam, pcostvalue,
                   cashcustname, cashcustphone, cashcustadd1, cashcustdoctor, paymode, pcheqamt, pcredit
            FROM salemaster
            WHERE purblno >= 0
            ORDER BY purblno
            """, med);
        using var headers = headerCmd.ExecuteReader();

        int imported = 0, skipped = 0;
        while (headers.Read())
        {
            var billNo = ImportHelpers.Int(headers["purblno"]);
            var invoice = $"MW-S-{billNo}";

            var exists = await MedWinImporter.ScalarIntAsync(target,
                "SELECT COUNT(*) FROM Sales WHERE InvoiceNumber = @No", new SqlParameter("@No", invoice));
            if (exists > 0) { skipped++; continue; }

            var invoiceDate = ImportHelpers.CombineDateAndTime(
                ImportHelpers.Date(headers["purbldt"]), Convert.ToString(headers["billtime"]));
            var grandTotal = ImportHelpers.Dec(headers["pactamt"]);
            var paid = grandTotal - ImportHelpers.Dec(headers["pcredit"]);
            if (paid < 0) paid = 0;
            var paymentStatus = paid >= grandTotal ? 2 : (paid > 0 ? 1 : 0);

            await using var insSale = new SqlCommand("""
                INSERT INTO Sales
                    (InvoiceNumber, InvoiceDate, BillingCustomerName, BillingCustomerPhone, BillingCustomerAddress, BillingDoctorName,
                     SubTotal, DiscountAmount, TaxableAmount, CgstAmount, SgstAmount, IgstAmount, RoundOff, GrandTotal,
                     PaidAmount, ChangeReturned, RewardPointsEarned, RewardPointsRedeemed, Status, PaymentStatus, Remarks,
                     BranchId, CreatedAtUtc, IsDeleted)
                OUTPUT INSERTED.Id
                VALUES
                    (@Invoice, @Date, @CustName, @CustPhone, @CustAddr, @Doctor,
                     0, 0, 0, 0, 0, 0, 0, @GrandTotal,
                     @Paid, 0, 0, 0, 2, @PaymentStatus, @Remarks,
                     @BranchId, @Now, 0)
                """, target);
            insSale.Parameters.AddWithValue("@Invoice", invoice);
            insSale.Parameters.AddWithValue("@Date", invoiceDate);
            insSale.Parameters.AddWithValue("@CustName", (object?)ImportHelpers.Trunc(Convert.ToString(headers["cashcustname"]), 200) ?? DBNull.Value);
            insSale.Parameters.AddWithValue("@CustPhone", (object?)ImportHelpers.Trunc(Convert.ToString(headers["cashcustphone"]), 30) ?? DBNull.Value);
            insSale.Parameters.AddWithValue("@CustAddr", (object?)ImportHelpers.Trunc(Convert.ToString(headers["cashcustadd1"]), 500) ?? DBNull.Value);
            insSale.Parameters.AddWithValue("@Doctor", (object?)ImportHelpers.Trunc(Convert.ToString(headers["cashcustdoctor"]), 200) ?? DBNull.Value);
            insSale.Parameters.AddWithValue("@GrandTotal", grandTotal);
            insSale.Parameters.AddWithValue("@Paid", paid);
            insSale.Parameters.AddWithValue("@PaymentStatus", paymentStatus);
            insSale.Parameters.AddWithValue("@Remarks", $"MedWin bill {billNo}");
            insSale.Parameters.AddWithValue("@BranchId", ctx.BranchId);
            insSale.Parameters.AddWithValue("@Now", ctx.NowUtc);
            var saleId = (int)await insSale.ExecuteScalarAsync();
            ctx.SaleMap[billNo] = saleId;

            await ImportSaleLinesAsync(ctx, target, med, billNo, saleId, invoice, invoiceDate);
            await RecalculateImportedSaleHeaderAsync(target, saleId);
            imported++;
            if (imported % 500 == 0) ctx.Log($"  ...{imported:N0} sales");
        }

        ctx.Log($"  Sales imported: {imported:N0} ({skipped:N0} skipped as existing).");
    }

    private static async Task ImportSaleLinesAsync(
        MedWinImportContext ctx, SqlConnection target, OleDbConnection med,
        int billNo, int saleId, string invoiceNumber, DateTime invoiceDate)
    {
        using var cmd = new OleDbCommand("""
            SELECT dpmedcod, dpqty, dpbatch, dpfmrp, mrprate, dpamt, dptax, dtaxamt, dnetamt, dpsize,
                   dpexmon, dpexyear, dpdisc, dpcost, dpfree
            FROM dsalemaster
            WHERE dpurblno = ?
            """, med);
        cmd.Parameters.Add(new OleDbParameter { OleDbType = System.Data.OleDb.OleDbType.Integer, Value = billNo });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var medWinId = ImportHelpers.Int(r["dpmedcod"]);
            if (!ctx.MedicineMap.TryGetValue(medWinId, out var medicineId)) continue;

            var batchNo = ImportHelpers.Trunc(Convert.ToString(r["dpbatch"]), 60) ?? "BATCH";
            int? batchId = await ResolveBatchIdAsync(ctx, target, medWinId, medicineId, batchNo);

            // Keep MedWin units (same as stockmas / item ledger). Do NOT divide by dpsize.
            var qty = ImportHelpers.Dec(r["dpqty"]);
            var free = ImportHelpers.Dec(r["dpfree"]);
            var pack = Math.Max(1, ImportHelpers.Int(r["dpsize"]));

            var mrp = ImportHelpers.Dec(r["mrprate"]);
            if (mrp <= 0) mrp = ImportHelpers.Dec(r["dpfmrp"]);
            // MedWin MRP is usually per pack; store per-unit to match unit qty.
            if (pack > 1 && mrp > 0)
                mrp = Math.Round(mrp / pack, 4);

            var lineTotal = ImportHelpers.Dec(r["dpamt"]);
            if (lineTotal <= 0 && qty > 0) lineTotal = ImportHelpers.Dec(r["dnetamt"]);
            var gstPercent = ImportHelpers.Dec(r["dptax"]);
            var taxAmount = ImportHelpers.Dec(r["dtaxamt"]);
            if (taxAmount <= 0 && gstPercent > 0 && lineTotal != 0)
                taxAmount = Math.Round(lineTotal * gstPercent / (100m + gstPercent), 2);
            var taxable = lineTotal - taxAmount;
            var unitPrice = qty != 0 ? Math.Round(lineTotal / qty, 4) : 0m;
            var discount = ImportHelpers.Dec(r["dpdisc"]);

            DateTime? expiry = ImportHelpers.ParseExpiryMonthYear(
                ImportHelpers.Int(r["dpexyear"]),
                ImportHelpers.Int(r["dpexmon"]));

            await using var ins = new SqlCommand("""
                INSERT INTO SaleItems
                    (SaleId, MedicineId, MedicineBatchId, BatchNumber, ExpiryDate, Quantity, Mrp, UnitPrice,
                     DiscountPercent, DiscountAmount, GstPercent, TaxableAmount, TaxAmount, LineTotal, CreatedAtUtc, IsDeleted)
                VALUES
                    (@SaleId, @MedicineId, @BatchId, @BatchNo, @Expiry, @Qty, @Mrp, @UnitPrice,
                     0, @Discount, @Gst, @Taxable, @Tax, @LineTotal, @Now, 0)
                """, target);
            ins.Parameters.AddWithValue("@SaleId", saleId);
            ins.Parameters.AddWithValue("@MedicineId", medicineId);
            ins.Parameters.AddWithValue("@BatchId", (object?)batchId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@BatchNo", batchNo);
            ins.Parameters.AddWithValue("@Expiry", (object?)expiry ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Qty", qty);
            ins.Parameters.AddWithValue("@Mrp", mrp);
            ins.Parameters.AddWithValue("@UnitPrice", unitPrice);
            ins.Parameters.AddWithValue("@Discount", discount);
            ins.Parameters.AddWithValue("@Gst", gstPercent);
            ins.Parameters.AddWithValue("@Taxable", taxable);
            ins.Parameters.AddWithValue("@Tax", taxAmount);
            ins.Parameters.AddWithValue("@LineTotal", lineTotal);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            await ins.ExecuteNonQueryAsync();
        }
    }

    public static async Task ImportPurchasesAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[purchases] Importing purchases from purchase/dpurchas...");
        if (ctx.SupplierMap.Count == 0)
            await MedWinMasterImporter.ImportSuppliersAsync(ctx, target);
        if (ctx.MedicineMap.Count == 0)
        {
            await MedWinMasterImporter.LoadExistingMedicineMapAsync(ctx, target);
            if (ctx.MedicineMap.Count == 0)
                throw new InvalidOperationException("Import medicines before purchases.");
        }

        using var med = ctx.OpenMedWin();
        med.Open();

        if (!ctx.Force)
        {
            var existing = await MedWinImporter.ScalarIntAsync(target,
                "SELECT COUNT(*) FROM Purchases WHERE InvoiceNumber LIKE 'MW-P-%'");
            using var countCmd = new OleDbCommand("SELECT COUNT(*) FROM purchase WHERE purparty <> 1", med);
            var sourceCount = ImportHelpers.Int(countCmd.ExecuteScalar());
            if (existing >= sourceCount && sourceCount > 0)
            {
                ctx.Log($"  MedWin purchases already imported ({existing:N0}). Use --force to import again.");
                return;
            }
            if (existing > 0 && existing < sourceCount)
                ctx.Log($"  Resuming purchases ({existing:N0}/{sourceCount:N0} already imported)...");
        }

        using var headerCmd = new OleDbCommand("""
            SELECT purblno, purbldt, billtime, purparty, pbillno, pactamt, pgrossamt, purtaxam, pcheqamt, pcredit
            FROM purchase
            WHERE purparty <> 1
            ORDER BY purblno
            """, med);
        using var headers = headerCmd.ExecuteReader();

        int imported = 0, skipped = 0;
        while (headers.Read())
        {
            var billNo = ImportHelpers.Int(headers["purblno"]);
            var invoice = $"MW-P-{billNo}";
            var exists = await MedWinImporter.ScalarIntAsync(target,
                "SELECT COUNT(*) FROM Purchases WHERE InvoiceNumber = @No", new SqlParameter("@No", invoice));
            if (exists > 0) { skipped++; continue; }

            var supplierMedWinId = ImportHelpers.Int(headers["purparty"]);
            if (!ctx.SupplierMap.TryGetValue(supplierMedWinId, out var supplierId))
            {
                skipped++;
                continue;
            }

            var invoiceDate = ImportHelpers.CombineDateAndTime(
                ImportHelpers.Date(headers["purbldt"]), Convert.ToString(headers["billtime"]));
            var grandTotal = ImportHelpers.Dec(headers["pactamt"]);
            var subTotal = ImportHelpers.Dec(headers["pgrossamt"]);
            if (subTotal <= 0) subTotal = grandTotal;
            // MedWin "purtaxam" is usually TAXABLE amount (large). Rarely it holds the tax amount (small).
            var (taxable, taxTotal) = ImportHelpers.ResolveMedWinPurchaseTax(
                grandTotal, subTotal, ImportHelpers.Dec(headers["purtaxam"]));
            var cgst = Math.Round(taxTotal / 2m, 2);
            var sgst = taxTotal - cgst;
            var paid = ImportHelpers.ResolveMedWinPurchasePaidAmount(
                grandTotal,
                ImportHelpers.Dec(headers["pcredit"]),
                ImportHelpers.Dec(headers["pcheqamt"]));
            var paymentStatus = ImportHelpers.ResolveMedWinPurchasePaymentStatus(grandTotal, paid);

            await using var ins = new SqlCommand("""
                INSERT INTO Purchases
                    (InvoiceNumber, SupplierInvoiceNumber, InvoiceDate, SupplierId, SubTotal, DiscountAmount,
                     TaxableAmount, CgstAmount, SgstAmount, IgstAmount, RoundOff, GrandTotal, PaidAmount,
                     Status, PaymentStatus, Remarks, BranchId, CreatedAtUtc, IsDeleted)
                OUTPUT INSERTED.Id
                VALUES
                    (@Invoice, @SupplierInvoice, @Date, @SupplierId, @SubTotal, 0,
                     @Taxable, @Cgst, @Sgst, 0, 0, @GrandTotal, @Paid,
                     3, @PaymentStatus, @Remarks, @BranchId, @Now, 0)
                """, target);
            ins.Parameters.AddWithValue("@Invoice", invoice);
            ins.Parameters.AddWithValue("@SupplierInvoice", (object?)ImportHelpers.Trunc(Convert.ToString(headers["pbillno"]), 60) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Date", invoiceDate);
            ins.Parameters.AddWithValue("@SupplierId", supplierId);
            ins.Parameters.AddWithValue("@SubTotal", subTotal);
            ins.Parameters.AddWithValue("@Taxable", taxable);
            ins.Parameters.AddWithValue("@Cgst", cgst);
            ins.Parameters.AddWithValue("@Sgst", sgst);
            ins.Parameters.AddWithValue("@GrandTotal", grandTotal);
            ins.Parameters.AddWithValue("@Paid", paid);
            ins.Parameters.AddWithValue("@PaymentStatus", paymentStatus);
            ins.Parameters.AddWithValue("@Remarks", $"MedWin purchase {billNo}");
            ins.Parameters.AddWithValue("@BranchId", ctx.BranchId);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            var purchaseId = (int)await ins.ExecuteScalarAsync();

            await ImportPurchaseLinesAsync(ctx, target, med, billNo, purchaseId, invoice, invoiceDate);
            await RecalculatePurchaseHeaderTaxFromLinesAsync(target, purchaseId, ctx.CancellationToken);
            imported++;
        }

        ctx.Log($"  Purchases imported: {imported:N0} ({skipped:N0} skipped).");
    }

    public static async Task ImportPurchaseReturnsAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[purchase-returns] Importing purchase returns from purchase_return/dpurchas_return...");
        if (ctx.SupplierMap.Count == 0)
            await MedWinMasterImporter.ImportSuppliersAsync(ctx, target);
        if (ctx.MedicineMap.Count == 0)
        {
            await MedWinMasterImporter.LoadExistingMedicineMapAsync(ctx, target);
            if (ctx.MedicineMap.Count == 0)
                throw new InvalidOperationException("Import medicines before purchase returns.");
        }

        using var med = ctx.OpenMedWin();
        med.Open();

        if (!ctx.Force)
        {
            var existing = await MedWinImporter.ScalarIntAsync(target,
                "SELECT COUNT(*) FROM PurchaseReturns WHERE ReturnNumber LIKE 'MW-PR-%'");
            using var countCmd = new OleDbCommand("SELECT COUNT(*) FROM purchase_return", med);
            var sourceCount = ImportHelpers.Int(countCmd.ExecuteScalar());
            if (existing >= sourceCount && sourceCount > 0)
            {
                ctx.Log($"  MedWin purchase returns already imported ({existing:N0}). Use --force to import again.");
                return;
            }
        }

        using var headerCmd = new OleDbCommand("""
            SELECT purblno, purbldt, billtime, purparty, pactamt, pgrossamt, purtaxam, pdbnote
            FROM purchase_return
            ORDER BY purblno
            """, med);
        using var headers = headerCmd.ExecuteReader();

        int imported = 0, skipped = 0;
        while (headers.Read())
        {
            var billNo = ImportHelpers.Int(headers["purblno"]);
            var returnNumber = $"MW-PR-{billNo}";

            var exists = await MedWinImporter.ScalarIntAsync(target,
                "SELECT COUNT(*) FROM PurchaseReturns WHERE ReturnNumber = @No",
                new SqlParameter("@No", returnNumber));
            if (exists > 0) { skipped++; continue; }

            var party = ImportHelpers.Int(headers["purparty"]);
            if (!ctx.SupplierMap.TryGetValue(party, out var supplierId))
            {
                skipped++;
                continue;
            }

            var returnDate = ImportHelpers.CombineDateAndTime(
                ImportHelpers.Date(headers["purbldt"]), Convert.ToString(headers["billtime"]));
            var grandTotal = ImportHelpers.Dec(headers["pactamt"]);
            var dbNote = ImportHelpers.Trunc(Convert.ToString(headers["pdbnote"]), 60);

            await using var ins = new SqlCommand("""
                INSERT INTO PurchaseReturns
                    (ReturnNumber, PurchaseId, SupplierId, ReturnDate,
                     SubTotal, DiscountAmount, TaxableAmount, CgstAmount, SgstAmount, RoundOff,
                     GrandTotal, CreditAmount, CreditAppliedAmount, SettlementMode, Status, IsFullReturn,
                     Remarks, SupplierReturnReceiptNumber, BranchId, CreatedAtUtc, IsDeleted)
                OUTPUT INSERTED.Id
                VALUES
                    (@ReturnNumber, NULL, @SupplierId, @ReturnDate,
                     @GrandTotal, 0, @GrandTotal, 0, 0, 0,
                     @GrandTotal, @GrandTotal, 0, 0, 1, 0,
                     @Remarks, @DbNote, @BranchId, @Now, 0)
                """, target);
            ins.Parameters.AddWithValue("@ReturnNumber", returnNumber);
            ins.Parameters.AddWithValue("@SupplierId", supplierId);
            ins.Parameters.AddWithValue("@ReturnDate", returnDate);
            ins.Parameters.AddWithValue("@GrandTotal", grandTotal);
            ins.Parameters.AddWithValue("@Remarks", $"MedWin purchase return {billNo}");
            ins.Parameters.AddWithValue("@DbNote", (object?)dbNote ?? DBNull.Value);
            ins.Parameters.AddWithValue("@BranchId", ctx.BranchId);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            var returnId = (int)await ins.ExecuteScalarAsync();

            await ImportPurchaseReturnLinesAsync(ctx, target, med, billNo, returnId, returnNumber, returnDate);
            imported++;
        }

        ctx.Log($"  Purchase returns imported: {imported:N0} ({skipped:N0} skipped).");
    }

    private static async Task ImportPurchaseReturnLinesAsync(
        MedWinImportContext ctx, SqlConnection target, OleDbConnection med,
        int billNo, int returnId, string returnNumber, DateTime returnDate)
    {
        using var cmd = new OleDbCommand("""
            SELECT dpmedcod, dpqty, dpbatch, dpfree, dpinvrat, dptax, dpamt, dpexmon, dpexyear, dpsize
            FROM dpurchas_return
            WHERE dpurblno = ?
            """, med);
        cmd.Parameters.Add(new OleDbParameter { OleDbType = System.Data.OleDb.OleDbType.Integer, Value = billNo });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var medWinId = ImportHelpers.Int(r["dpmedcod"]);
            if (!ctx.MedicineMap.TryGetValue(medWinId, out var medicineId)) continue;

            var batchNo = ImportHelpers.Trunc(Convert.ToString(r["dpbatch"]), 60) ?? "BATCH";
            int? batchId = await ResolveBatchIdAsync(ctx, target, medWinId, medicineId, batchNo);
            var qty = ImportHelpers.Dec(r["dpqty"]); // units (same as purchase / stock)
            var free = ImportHelpers.Dec(r["dpfree"]);
            var purchasePrice = ImportHelpers.Dec(r["dpinvrat"]);
            var gstPercent = ImportHelpers.Dec(r["dptax"]);
            var lineTotal = ImportHelpers.Dec(r["dpamt"]);
            var taxAmount = 0m;
            if (gstPercent > 0 && lineTotal > 0)
                taxAmount = Math.Round(lineTotal * gstPercent / (100m + gstPercent), 2);
            var taxable = Math.Max(0, lineTotal - taxAmount);
            var expiry = ImportHelpers.ParseExpiryMonthYear(
                ImportHelpers.Int(r["dpexyear"]), ImportHelpers.Int(r["dpexmon"]));

            await using var ins = new SqlCommand("""
                INSERT INTO PurchaseReturnItems
                    (PurchaseReturnId, PurchaseItemId, MedicineId, MedicineBatchId, BatchNumber, ExpiryDate,
                     ReturnedQuantity, ReturnedFreeQuantity, PurchasePrice, DiscountPercent, DiscountAmount,
                     GstPercent, TaxableAmount, TaxAmount, LineTotal, CreatedAtUtc, IsDeleted)
                VALUES
                    (@ReturnId, NULL, @MedicineId, @BatchId, @Batch, @Expiry,
                     @Qty, @Free, @PurchasePrice, 0, 0,
                     @Gst, @Taxable, @Tax, @LineTotal, @Now, 0)
                """, target);
            ins.Parameters.AddWithValue("@ReturnId", returnId);
            ins.Parameters.AddWithValue("@MedicineId", medicineId);
            ins.Parameters.AddWithValue("@BatchId", (object?)batchId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Batch", batchNo);
            ins.Parameters.AddWithValue("@Expiry", (object?)expiry ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Qty", qty);
            ins.Parameters.AddWithValue("@Free", free);
            ins.Parameters.AddWithValue("@PurchasePrice", purchasePrice);
            ins.Parameters.AddWithValue("@Gst", gstPercent);
            ins.Parameters.AddWithValue("@Taxable", taxable);
            ins.Parameters.AddWithValue("@Tax", taxAmount);
            ins.Parameters.AddWithValue("@LineTotal", lineTotal);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            await ins.ExecuteNonQueryAsync();
        }
    }

    private static async Task ImportPurchaseLinesAsync(
        MedWinImportContext ctx, SqlConnection target, OleDbConnection med,
        int billNo, int purchaseId, string invoiceNumber, DateTime invoiceDate)
    {
        using var cmd = new OleDbCommand("""
            SELECT dpmedcod, dpqty, dpbatch, dpfree, dpinvrat, dpfmrp, mrprate, dpamt, dptax,
                   dpsize, dpexmon, dpexyear, dpdisc, manfdate
            FROM dpurchas
            WHERE dpurblno = ?
            """, med);
        cmd.Parameters.Add(new OleDbParameter { OleDbType = System.Data.OleDb.OleDbType.Integer, Value = billNo });
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var medWinId = ImportHelpers.Int(r["dpmedcod"]);
            if (!ctx.MedicineMap.TryGetValue(medWinId, out var medicineId)) continue;

            var batchNo = ImportHelpers.Trunc(Convert.ToString(r["dpbatch"]), 60) ?? "BATCH";
            int? batchId = await ResolveBatchIdAsync(ctx, target, medWinId, medicineId, batchNo);
            // Purchase qty is already in units (matches MedWin item ledger / stockmas).
            var qty = ImportHelpers.Dec(r["dpqty"]);
            var free = ImportHelpers.Dec(r["dpfree"]);
            var purchasePrice = ImportHelpers.Dec(r["dpinvrat"]);
            var mrp = ImportHelpers.Dec(r["mrprate"]);
            if (mrp <= 0) mrp = ImportHelpers.Dec(r["dpfmrp"]);
            var selling = mrp;
            var lineTotal = ImportHelpers.Dec(r["dpamt"]);
            var gstPercent = ImportHelpers.Dec(r["dptax"]);
            var taxAmount = 0m;
            if (gstPercent > 0 && lineTotal > 0)
                taxAmount = Math.Round(lineTotal * gstPercent / (100m + gstPercent), 2);
            var taxable = Math.Max(0, lineTotal - taxAmount);
            var discount = ImportHelpers.Dec(r["dpdisc"]);

            DateTime? expiry = null;
            var y = ImportHelpers.Int(r["dpexyear"]);
            var m = ImportHelpers.Int(r["dpexmon"]);
            expiry = ImportHelpers.ParseExpiryMonthYear(y, m);

            await using var ins = new SqlCommand("""
                INSERT INTO PurchaseItems
                    (PurchaseId, MedicineId, MedicineBatchId, BatchNumber, ManufacturingDate, ExpiryDate, Quantity, FreeQuantity,
                     PurchasePrice, Mrp, SellingPrice, DiscountPercent, DiscountAmount, SchemeDiscount, GstPercent,
                     TaxableAmount, TaxAmount, LineTotal, CreatedAtUtc, IsDeleted)
                VALUES
                    (@PurchaseId, @MedicineId, @BatchId, @Batch, @Mfg, @Expiry, @Qty, @Free,
                     @PurchasePrice, @Mrp, @Selling, 0, @Discount, 0, @Gst,
                     @Taxable, @Tax, @LineTotal, @Now, 0)
                """, target);
            ins.Parameters.AddWithValue("@PurchaseId", purchaseId);
            ins.Parameters.AddWithValue("@MedicineId", medicineId);
            ins.Parameters.AddWithValue("@BatchId", (object?)batchId ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Batch", batchNo);
            ins.Parameters.AddWithValue("@Mfg", (object?)ImportHelpers.Date(r["manfdate"]) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Expiry", (object?)expiry ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Qty", qty);
            ins.Parameters.AddWithValue("@Free", free);
            ins.Parameters.AddWithValue("@PurchasePrice", purchasePrice);
            ins.Parameters.AddWithValue("@Mrp", mrp);
            ins.Parameters.AddWithValue("@Selling", selling);
            ins.Parameters.AddWithValue("@Discount", discount);
            ins.Parameters.AddWithValue("@Gst", gstPercent);
            ins.Parameters.AddWithValue("@Taxable", taxable);
            ins.Parameters.AddWithValue("@Tax", taxAmount);
            ins.Parameters.AddWithValue("@LineTotal", lineTotal);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            await ins.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// Prefer line-level GST breakdown for purchase headers (fixes MedWin purtaxam confusion).
    /// </summary>
    private static async Task RecalculatePurchaseHeaderTaxFromLinesAsync(
        SqlConnection target, int purchaseId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            ;WITH sums AS (
                SELECT
                    ISNULL(SUM(TaxableAmount), 0) AS Taxable,
                    ISNULL(SUM(TaxAmount), 0) AS Tax,
                    ISNULL(SUM(LineTotal), 0) AS LinesTotal
                FROM PurchaseItems
                WHERE PurchaseId = @Id AND IsDeleted = 0
            )
            UPDATE p SET
                TaxableAmount = CASE WHEN s.Taxable > 0 THEN s.Taxable
                                     WHEN p.GrandTotal > s.Tax THEN p.GrandTotal - s.Tax
                                     ELSE p.TaxableAmount END,
                CgstAmount = ROUND(s.Tax / 2.0, 2),
                SgstAmount = s.Tax - ROUND(s.Tax / 2.0, 2),
                IgstAmount = 0,
                SubTotal = CASE WHEN s.LinesTotal > 0 THEN s.LinesTotal ELSE p.SubTotal END
            FROM Purchases p
            CROSS JOIN sums s
            WHERE p.Id = @Id AND s.Tax >= 0 AND (s.Taxable > 0 OR s.Tax > 0 OR s.LinesTotal > 0)
            """, target);
        cmd.Parameters.AddWithValue("@Id", purchaseId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Repair Tax/Taxable on already-imported MedWin purchases from their line items.</summary>
    public static async Task BackfillPurchaseTaxAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[backfill-purchase-tax] Recalculating MedWin purchase tax from line items...");
        ctx.ThrowIfCancellationRequested();

        await using var list = new SqlCommand("""
            SELECT Id FROM Purchases
            WHERE IsDeleted = 0 AND InvoiceNumber LIKE 'MW-P-%'
            """, target);
        var ids = new List<int>();
        await using (var reader = await list.ExecuteReaderAsync(ctx.CancellationToken))
        {
            while (await reader.ReadAsync(ctx.CancellationToken))
                ids.Add(reader.GetInt32(0));
        }

        var updated = 0;
        foreach (var id in ids)
        {
            ctx.ThrowIfCancellationRequested();
            await RecalculatePurchaseHeaderTaxFromLinesAsync(target, id, ctx.CancellationToken);
            updated++;
        }

        ctx.Log($"  Recalculated tax on {updated:N0} MedWin purchase(s).");
    }

    public static async Task ImportPaymentsAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[payments] Importing sale payments...");
        if (ctx.SaleMap.Count == 0)
            await LoadSaleMapAsync(ctx, target);

        using var med = ctx.OpenMedWin();
        med.Open();

        int added = 0;
        using (var cmd = new OleDbCommand("SELECT billno, billdt, cash, pos1, pos2, paytm FROM dsale_payment", med))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var billNo = ImportHelpers.Int(r["billno"]);
                if (!ctx.SaleMap.TryGetValue(billNo, out var saleId)) continue;
                var date = ImportHelpers.Date(r["billdt"]) ?? ctx.NowUtc;

                added += await InsertSalePaymentIfMissingAsync(ctx, target, saleId, 0, ImportHelpers.Dec(r["cash"]), date, null);
                added += await InsertSalePaymentIfMissingAsync(ctx, target, saleId, 1, ImportHelpers.Dec(r["pos1"]), date, null);
                added += await InsertSalePaymentIfMissingAsync(ctx, target, saleId, 1, ImportHelpers.Dec(r["pos2"]), date, "POS2");
                added += await InsertSalePaymentIfMissingAsync(ctx, target, saleId, 2, ImportHelpers.Dec(r["paytm"]), date, "Paytm");
            }
        }

        using (var cmd = new OleDbCommand("SELECT billno, amount, mode, billdt FROM dsale_receipt", med))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var billNo = ImportHelpers.Int(r["billno"]);
                if (!ctx.SaleMap.TryGetValue(billNo, out var saleId)) continue;
                var amount = ImportHelpers.Dec(r["amount"]);
                if (amount <= 0) continue;
                var mode = ImportHelpers.Int(r["mode"]);
                var method = mode switch { 1 => 0, 6 => 2, _ => 0 };
                var date = ImportHelpers.Date(r["billdt"]) ?? ctx.NowUtc;
                added += await InsertSalePaymentIfMissingAsync(ctx, target, saleId, method, amount, date, $"mode-{mode}");
            }
        }

        ctx.Log($"  Sale payment rows added: {added:N0}.");
        ctx.Log("  Purchase receipts are stored on purchase headers (pcheqamt / pcredit).");
    }

    /// <summary>
    /// Repairs PaidAmount / PaymentStatus on imported MW-P purchases from MedWin header fields.
    /// </summary>
    public static async Task BackfillPurchasePaymentsAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[backfill-purchase-payments] Repairing purchase paid amounts from MedWin...");
        using var med = ctx.OpenMedWin();
        med.Open();

        using var headerCmd = new OleDbCommand("""
            SELECT purblno, pactamt, pcheqamt, pcredit
            FROM purchase
            WHERE purparty <> 1
            """, med);
        using var headers = headerCmd.ExecuteReader();

        int updated = 0, examined = 0;
        while (headers.Read())
        {
            examined++;
            var billNo = ImportHelpers.Int(headers["purblno"]);
            var invoice = $"MW-P-{billNo}";
            var grandTotal = ImportHelpers.Dec(headers["pactamt"]);
            var paid = ImportHelpers.ResolveMedWinPurchasePaidAmount(
                grandTotal,
                ImportHelpers.Dec(headers["pcredit"]),
                ImportHelpers.Dec(headers["pcheqamt"]));
            var paymentStatus = ImportHelpers.ResolveMedWinPurchasePaymentStatus(grandTotal, paid);

            await using var upd = new SqlCommand("""
                UPDATE Purchases
                SET PaidAmount = @Paid,
                    PaymentStatus = @PaymentStatus,
                    ModifiedAtUtc = @Now
                WHERE InvoiceNumber = @Invoice
                  AND Status = 3
                  AND (PaidAmount <> @Paid OR PaymentStatus <> @PaymentStatus)
                """, target);
            upd.Parameters.AddWithValue("@Paid", paid);
            upd.Parameters.AddWithValue("@PaymentStatus", paymentStatus);
            upd.Parameters.AddWithValue("@Invoice", invoice);
            upd.Parameters.AddWithValue("@Now", ctx.NowUtc);
            updated += await upd.ExecuteNonQueryAsync();
        }

        await using (var resetSuppliers = new SqlCommand(
                         "UPDATE Suppliers SET OutstandingBalance = 0, ModifiedAtUtc = @Now", target))
        {
            resetSuppliers.Parameters.AddWithValue("@Now", ctx.NowUtc);
            await resetSuppliers.ExecuteNonQueryAsync();
        }

        await using (var syncSuppliers = new SqlCommand("""
            UPDATE s
            SET s.OutstandingBalance = x.Due,
                s.ModifiedAtUtc = @Now
            FROM Suppliers s
            INNER JOIN (
                SELECT SupplierId, SUM(GrandTotal - PaidAmount) AS Due
                FROM Purchases
                WHERE Status = 3 AND GrandTotal > PaidAmount
                GROUP BY SupplierId
            ) x ON x.SupplierId = s.Id
            """, target))
        {
            syncSuppliers.Parameters.AddWithValue("@Now", ctx.NowUtc);
            await syncSuppliers.ExecuteNonQueryAsync();
        }

        ctx.Log($"  Purchase payment rows updated: {updated:N0} ({examined - updated:N0} already correct).");
        ctx.Log("  Supplier outstanding balances recalculated from open purchase dues.");
    }

    private static async Task<int> InsertSalePaymentIfMissingAsync(
        MedWinImportContext ctx, SqlConnection target, int saleId, int method, decimal amount, DateTime date, string? reference)
    {
        if (amount <= 0) return 0;
        await using var ins = new SqlCommand("""
            IF NOT EXISTS (
                SELECT 1 FROM SalePayments
                WHERE SaleId = @SaleId AND Method = @Method AND Amount = @Amount AND ISNULL(ReferenceNumber,'') = ISNULL(@Ref,''))
            INSERT INTO SalePayments (SaleId, Method, Amount, ReferenceNumber, PaymentDateUtc, CreatedAtUtc, IsDeleted)
            VALUES (@SaleId, @Method, @Amount, @Ref, @Date, @Now, 0)
            """, target);
        ins.Parameters.AddWithValue("@SaleId", saleId);
        ins.Parameters.AddWithValue("@Method", method);
        ins.Parameters.AddWithValue("@Amount", amount);
        ins.Parameters.AddWithValue("@Ref", (object?)reference ?? DBNull.Value);
        ins.Parameters.AddWithValue("@Date", date);
        ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
        return await ins.ExecuteNonQueryAsync() > 0 ? 1 : 0;
    }

    private static async Task InsertStockMovementAsync(
        MedWinImportContext ctx,
        SqlConnection target,
        int medicineId,
        int? batchId,
        int movementType,
        decimal quantity,
        decimal unitCost,
        string referenceType,
        int referenceId,
        string referenceNumber,
        string remarks,
        DateTime movementDateUtc)
    {
        if (quantity <= 0) return;

        await using var mv = new SqlCommand("""
            INSERT INTO StockMovements
                (BranchId, MedicineId, MedicineBatchId, MovementType, Quantity, BalanceAfter,
                 UnitCost, ReferenceType, ReferenceId, ReferenceNumber, Remarks, MovementDateUtc,
                 CreatedAtUtc, IsDeleted)
            VALUES
                (@BranchId, @MedicineId, @BatchId, @MovementType, @Qty, 0,
                 @UnitCost, @RefType, @RefId, @RefNo, @Remarks, @MoveDate,
                 @Now, 0)
            """, target);
        mv.Parameters.AddWithValue("@BranchId", ctx.BranchId);
        mv.Parameters.AddWithValue("@MedicineId", medicineId);
        mv.Parameters.AddWithValue("@BatchId", (object?)batchId ?? DBNull.Value);
        mv.Parameters.AddWithValue("@MovementType", movementType);
        mv.Parameters.AddWithValue("@Qty", quantity);
        mv.Parameters.AddWithValue("@UnitCost", unitCost);
        mv.Parameters.AddWithValue("@RefType", referenceType);
        mv.Parameters.AddWithValue("@RefId", referenceId);
        mv.Parameters.AddWithValue("@RefNo", referenceNumber);
        mv.Parameters.AddWithValue("@Remarks", remarks);
        mv.Parameters.AddWithValue("@MoveDate", movementDateUtc);
        mv.Parameters.AddWithValue("@Now", ctx.NowUtc);
        await mv.ExecuteNonQueryAsync();
    }

    private static async Task<int?> ResolveBatchIdAsync(
        MedWinImportContext ctx, SqlConnection target, int medWinId, int medicineId, string batchNo)
    {
        var fromMap = ResolveBatchId(ctx, medWinId, batchNo);
        if (fromMap is > 0) return fromMap;

        await using var cmd = new SqlCommand("""
            SELECT TOP 1 Id FROM MedicineBatches
            WHERE MedicineId = @MedicineId AND BatchNumber = @Batch AND IsDeleted = 0
            ORDER BY Id DESC
            """, target);
        cmd.Parameters.AddWithValue("@MedicineId", medicineId);
        cmd.Parameters.AddWithValue("@Batch", batchNo);
        var result = await cmd.ExecuteScalarAsync();
        return result is int id ? id : result is null or DBNull ? null : Convert.ToInt32(result);
    }

    private static int? ResolveBatchId(MedWinImportContext ctx, int medWinId, string batchNo)
    {
        var prefix = $"{medWinId}:{batchNo}:";
        foreach (var kv in ctx.BatchMap)
            if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                return kv.Value;
        return null;
    }

    private static DateTime ToUtc(DateTime value)
        => value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            : value.ToUniversalTime();

    /// <summary>
    /// Repairs expiry dates on imported sale lines and batches from MedWin month/year fields.
    /// MedWin stores two-digit years (e.g. 29 = 2029) which the first import pass skipped.
    /// </summary>
    public static async Task BackfillExpiryAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[backfill-expiry] Backfilling expiry dates from MedWin stock and sale lines...");
        if (ctx.MedicineMap.Count == 0)
            await MedWinMasterImporter.LoadExistingMedicineMapAsync(ctx, target);

        using var med = ctx.OpenMedWin();
        med.Open();

        var batchRows = 0;
        var saleRows = 0;
        var skipped = 0;

        using (var stockCmd = new OleDbCommand(
                   "SELECT stkcode, stkbatch, stkexyr, stkexmn FROM stockmas WHERE stkexyr > 0 AND stkexmn BETWEEN 1 AND 12", med))
        using (var reader = stockCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var medWinId = ImportHelpers.Int(reader["stkcode"]);
                if (!ctx.MedicineMap.TryGetValue(medWinId, out var medicineId)) continue;

                var batchNo = ImportHelpers.Trunc(Convert.ToString(reader["stkbatch"]), 60) ?? "BATCH";
                var expiry = ImportHelpers.ParseExpiryMonthYear(
                    ImportHelpers.Int(reader["stkexyr"]), ImportHelpers.Int(reader["stkexmn"]));
                if (expiry is null) continue;

                batchRows += await UpdateBatchExpiryAsync(target, medicineId, batchNo, expiry.Value, ctx.NowUtc);
            }
        }

        using (var saleCmd = new OleDbCommand(
                   "SELECT dpurblno, dpmedcod, dpbatch, dpexmon, dpexyear FROM dsalemaster WHERE dpexyear > 0 AND dpexmon BETWEEN 1 AND 12", med))
        using (var reader = saleCmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var billNo = ImportHelpers.Int(reader["dpurblno"]);
                var medWinId = ImportHelpers.Int(reader["dpmedcod"]);
                if (medWinId <= 0)
                {
                    skipped++;
                    continue;
                }

                var batchNo = ImportHelpers.Trunc(Convert.ToString(reader["dpbatch"]), 60) ?? "BATCH";
                var expiry = ImportHelpers.ParseExpiryMonthYear(
                    ImportHelpers.Int(reader["dpexyear"]), ImportHelpers.Int(reader["dpexmon"]));
                if (expiry is null)
                {
                    skipped++;
                    continue;
                }

                saleRows += await UpdateSaleItemExpiryAsync(target, billNo, medWinId, batchNo, expiry.Value);
                batchRows += await UpdateBatchExpiryByMedWinIdAsync(target, medWinId, batchNo, expiry.Value, ctx.NowUtc);
            }
        }

        ctx.Log($"  Sale lines updated: {saleRows:N0}");
        ctx.Log($"  Batch rows updated: {batchRows:N0}");
        ctx.Log($"  Skipped (unmapped): {skipped:N0}");
    }

    private static async Task<int> UpdateSaleItemExpiryAsync(
        SqlConnection target, int billNo, int medWinId, string batchNo, DateTime expiry)
    {
        await using var cmd = new SqlCommand("""
            UPDATE si
            SET si.ExpiryDate = @Expiry
            FROM SaleItems si
            INNER JOIN Sales s ON s.Id = si.SaleId
            INNER JOIN Medicines m ON m.Id = si.MedicineId
            WHERE s.InvoiceNumber = @Invoice
              AND si.BatchNumber = @Batch
              AND si.IsDeleted = 0
              AND (
                    m.Notes LIKE @MedWinNote
                    OR EXISTS (
                        SELECT 1 FROM MedicineMedWinMappings mm
                        WHERE mm.OneMgMedicineId = m.Id AND mm.MedWinMedicineId = @MedWinId)
                  )
              AND (si.ExpiryDate IS NULL OR si.ExpiryDate <> @Expiry)
            """, target);
        cmd.Parameters.AddWithValue("@Invoice", $"MW-S-{billNo}");
        cmd.Parameters.AddWithValue("@Batch", batchNo);
        cmd.Parameters.AddWithValue("@MedWinNote", $"%MedWinId:{medWinId}%");
        cmd.Parameters.AddWithValue("@MedWinId", medWinId);
        cmd.Parameters.AddWithValue("@Expiry", expiry);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> UpdateBatchExpiryByMedWinIdAsync(
        SqlConnection target, int medWinId, string batchNo, DateTime expiry, DateTime nowUtc)
    {
        await using var cmd = new SqlCommand("""
            UPDATE mb
            SET mb.ExpiryDate = @Expiry, mb.ModifiedAtUtc = @Now
            FROM MedicineBatches mb
            INNER JOIN Medicines m ON m.Id = mb.MedicineId
            WHERE mb.BatchNumber = @Batch
              AND mb.IsDeleted = 0
              AND (
                    m.Notes LIKE @MedWinNote
                    OR EXISTS (
                        SELECT 1 FROM MedicineMedWinMappings mm
                        WHERE mm.OneMgMedicineId = m.Id AND mm.MedWinMedicineId = @MedWinId)
                  )
              AND (mb.ExpiryDate IS NULL OR mb.ExpiryDate <> @Expiry)
            """, target);
        cmd.Parameters.AddWithValue("@Batch", batchNo);
        cmd.Parameters.AddWithValue("@MedWinNote", $"%MedWinId:{medWinId}%");
        cmd.Parameters.AddWithValue("@MedWinId", medWinId);
        cmd.Parameters.AddWithValue("@Expiry", expiry);
        cmd.Parameters.AddWithValue("@Now", nowUtc);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> UpdateBatchExpiryAsync(
        SqlConnection target, int medicineId, string batchNo, DateTime expiry, DateTime nowUtc)
    {
        await using var cmd = new SqlCommand("""
            UPDATE MedicineBatches
            SET ExpiryDate = @Expiry, ModifiedAtUtc = @Now
            WHERE MedicineId = @MedicineId
              AND BatchNumber = @Batch
              AND IsDeleted = 0
              AND (ExpiryDate IS NULL OR ExpiryDate <> @Expiry)
            """, target);
        cmd.Parameters.AddWithValue("@MedicineId", medicineId);
        cmd.Parameters.AddWithValue("@Batch", batchNo);
        cmd.Parameters.AddWithValue("@Expiry", expiry);
        cmd.Parameters.AddWithValue("@Now", nowUtc);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task RecalculateImportedSaleHeaderAsync(SqlConnection target, int saleId)
    {
        await using var cmd = new SqlCommand("""
            UPDATE s SET
                s.SubTotal = agg.SubTotal,
                s.DiscountAmount = agg.DiscountAmount,
                s.TaxableAmount = agg.TaxableAmount,
                s.CgstAmount = ROUND(agg.TaxAmount / 2.0, 2),
                s.SgstAmount = agg.TaxAmount - ROUND(agg.TaxAmount / 2.0, 2)
            FROM Sales s
            INNER JOIN (
                SELECT SaleId,
                    SUM(Mrp * Quantity) AS SubTotal,
                    SUM(DiscountAmount) AS DiscountAmount,
                    SUM(TaxableAmount) AS TaxableAmount,
                    SUM(TaxAmount) AS TaxAmount
                FROM SaleItems
                WHERE SaleId = @SaleId AND IsDeleted = 0
                GROUP BY SaleId
            ) agg ON s.Id = agg.SaleId
            WHERE s.Id = @SaleId
            """, target);
        cmd.Parameters.AddWithValue("@SaleId", saleId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task LoadSaleMapAsync(MedWinImportContext ctx, SqlConnection target)
    {
        await using var cmd = new SqlCommand("SELECT Id, InvoiceNumber FROM Sales WHERE InvoiceNumber LIKE 'MW-S-%'", target);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var invoice = reader.GetString(1);
            if (invoice.Length > 5 && int.TryParse(invoice["MW-S-".Length..], out var billNo))
                ctx.SaleMap[billNo] = reader.GetInt32(0);
        }
    }
}
