using System.Data.OleDb;
using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

internal static class MedWinTransactionImporter
{
    public static async Task ImportSalesAsync(MedWinImportContext ctx, SqlConnection target)
    {
        Console.WriteLine("\n[sales] Importing sales from salemaster/dsalemaster...");
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
                Console.WriteLine($"  MedWin sales already imported ({existing:N0}). Use --force to import again.");
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

            await ImportSaleLinesAsync(ctx, target, med, billNo, saleId);
            await RecalculateImportedSaleHeaderAsync(target, saleId);
            imported++;
            if (imported % 500 == 0) Console.WriteLine($"  ...{imported:N0} sales");
        }

        Console.WriteLine($"  Sales imported: {imported:N0} ({skipped:N0} skipped as existing).");
    }

    private static async Task ImportSaleLinesAsync(MedWinImportContext ctx, SqlConnection target, OleDbConnection med, int billNo, int saleId)
    {
        using var cmd = new OleDbCommand("""
            SELECT dpmedcod, dpqty, dpbatch, dpfmrp, mrprate, dpamt, dptax, dtaxamt, dnetamt, dpsize,
                   dpexmon, dpexyear, dpdisc, dpcost
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
            int? batchId = ResolveBatchId(ctx, medWinId, batchNo);

            var qty = ImportHelpers.Dec(r["dpqty"]);
            var pack = Math.Max(1, ImportHelpers.Int(r["dpsize"]));
            if (pack > 1) qty = qty / pack;

            var mrp = ImportHelpers.Dec(r["mrprate"]);
            if (mrp <= 0) mrp = ImportHelpers.Dec(r["dpfmrp"]);
            var lineTotal = ImportHelpers.Dec(r["dpamt"]);
            if (lineTotal <= 0) lineTotal = ImportHelpers.Dec(r["dnetamt"]);
            var gstPercent = ImportHelpers.Dec(r["dptax"]);
            var taxAmount = ImportHelpers.Dec(r["dtaxamt"]);
            if (taxAmount <= 0 && gstPercent > 0 && lineTotal > 0)
                taxAmount = Math.Round(lineTotal * gstPercent / (100m + gstPercent), 2);
            var taxable = Math.Max(0, lineTotal - taxAmount);
            var unitPrice = qty > 0 ? Math.Round(lineTotal / qty, 2) : lineTotal;
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
        Console.WriteLine("\n[purchases] Importing purchases from purchase/dpurchas...");
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
                Console.WriteLine($"  MedWin purchases already imported ({existing:N0}). Use --force to import again.");
                return;
            }
            if (existing > 0 && existing < sourceCount)
                Console.WriteLine($"  Resuming purchases ({existing:N0}/{sourceCount:N0} already imported)...");
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
            var taxTotal = ImportHelpers.Dec(headers["purtaxam"]);
            var taxable = Math.Max(0, grandTotal - taxTotal);
            var cgst = taxTotal / 2m;
            var sgst = taxTotal - cgst;
            var paid = grandTotal - ImportHelpers.Dec(headers["pcredit"]);
            if (paid < 0) paid = 0;
            var paymentStatus = paid >= grandTotal ? 2 : (paid > 0 ? 1 : 0);

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

            await ImportPurchaseLinesAsync(ctx, target, med, billNo, purchaseId);
            imported++;
        }

        Console.WriteLine($"  Purchases imported: {imported:N0} ({skipped:N0} skipped).");
    }

    private static async Task ImportPurchaseLinesAsync(MedWinImportContext ctx, SqlConnection target, OleDbConnection med, int billNo, int purchaseId)
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
                    (PurchaseId, MedicineId, BatchNumber, ManufacturingDate, ExpiryDate, Quantity, FreeQuantity,
                     PurchasePrice, Mrp, SellingPrice, DiscountPercent, DiscountAmount, SchemeDiscount, GstPercent,
                     TaxableAmount, TaxAmount, LineTotal, CreatedAtUtc, IsDeleted)
                VALUES
                    (@PurchaseId, @MedicineId, @Batch, @Mfg, @Expiry, @Qty, @Free,
                     @PurchasePrice, @Mrp, @Selling, 0, @Discount, 0, @Gst,
                     @Taxable, @Tax, @LineTotal, @Now, 0)
                """, target);
            ins.Parameters.AddWithValue("@PurchaseId", purchaseId);
            ins.Parameters.AddWithValue("@MedicineId", medicineId);
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

    public static async Task ImportPaymentsAsync(MedWinImportContext ctx, SqlConnection target)
    {
        Console.WriteLine("\n[payments] Importing sale payments...");
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

        Console.WriteLine($"  Sale payment rows added: {added:N0}.");
        Console.WriteLine("  Purchase receipts (purrcpt) are reflected in purchase PaidAmount during purchase import.");
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

    private static int? ResolveBatchId(MedWinImportContext ctx, int medWinId, string batchNo)
    {
        var prefix = $"{medWinId}:{batchNo}:";
        foreach (var kv in ctx.BatchMap)
            if (kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                return kv.Value;
        return null;
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
