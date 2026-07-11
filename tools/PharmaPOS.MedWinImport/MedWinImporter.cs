using System.Data;
using System.Data.OleDb;
using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

internal static class MedWinImporter
{
    public static async Task RunAsync(MedWinImportContext ctx, IReadOnlyList<string> phases)
    {
        await using var target = await ctx.OpenTargetAsync();
        await LoadTargetContextAsync(ctx, target);

        var set = phases.Count == 0 || phases.Contains("all", StringComparer.OrdinalIgnoreCase)
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "company", "gst", "medicines", "suppliers", "customers", "stock",
                "purchases", "sales", "payments", "users"
            }
            : phases.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (set.Contains("company")) await ImportCompanyAsync(ctx, target);
        if (set.Contains("gst")) await ImportGstCategoriesAsync(ctx, target);
        if (set.Contains("medicines"))
        {
            if (!string.IsNullOrWhiteSpace(ctx.ReportCsvPath))
                await MedicineMatchReporter.ExportAsync(ctx, target, ctx.ReportCsvPath);
            else
                await MedWinMasterImporter.ImportMedicinesAsync(ctx, target);
        }
        if (set.Contains("suppliers")) await MedWinMasterImporter.ImportSuppliersAsync(ctx, target);
        if (set.Contains("customers")) await MedWinMasterImporter.ImportCustomersAsync(ctx, target);
        if (set.Contains("stock")) await MedWinMasterImporter.ImportStockAsync(ctx, target);
        if (set.Contains("purchases")) await MedWinTransactionImporter.ImportPurchasesAsync(ctx, target);
        if (set.Contains("sales")) await MedWinTransactionImporter.ImportSalesAsync(ctx, target);
        if (set.Contains("payments")) await MedWinTransactionImporter.ImportPaymentsAsync(ctx, target);
        if (set.Contains("users")) await ImportUsersAsync(ctx, target);
        if (set.Contains("backfill-expiry")) await MedWinTransactionImporter.BackfillExpiryAsync(ctx, target);
        if (set.Contains("dedupe-onemg")) await OneMgDuplicateCleaner.RunAsync(ctx, target, dryRun: !ctx.Force);
    }

    private static async Task LoadTargetContextAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.BranchId = await ScalarIntAsync(target,
            "SELECT TOP 1 Id FROM Branches WHERE IsHeadOffice = 1 ORDER BY Id");
        if (ctx.BranchId == 0)
            throw new InvalidOperationException("No head-office branch found in PharmaPOS database.");

        ctx.CashierRoleId = await ScalarIntAsync(target,
            "SELECT TOP 1 Id FROM Roles WHERE Name = 'Cashier'");
        if (ctx.CashierRoleId == 0)
            ctx.CashierRoleId = await ScalarIntAsync(target, "SELECT TOP 1 Id FROM Roles ORDER BY Id");
    }

    private static async Task ImportCompanyAsync(MedWinImportContext ctx, SqlConnection target)
    {
        Console.WriteLine("\n[company] Importing company profile...");
        using var med = ctx.OpenMedWin();
        med.Open();
        using var cmd = new OleDbCommand("SELECT TOP 1 * FROM compprof", med);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            Console.WriteLine("  No compprof row found.");
            return;
        }

        var companyName = ImportHelpers.Trunc(Convert.ToString(r["cmpname"]), 200) ?? "Pharmacy";
        var addr1 = Convert.ToString(r["cmpaddr1"])?.Trim();
        var addr2 = Convert.ToString(r["cmpaddr2"])?.Trim();
        var addr3 = Convert.ToString(r["cmpadrr3"])?.Trim();
        var address = ImportHelpers.Trunc(string.Join(", ", new[] { addr1, addr2, addr3 }.Where(s => !string.IsNullOrWhiteSpace(s))), 500);
        var gst = ImportHelpers.ExtractGstin(Convert.ToString(r["saletaxn"]));
        var state = ImportHelpers.Trunc(Convert.ToString(r["state"]), 100);
        var pincode = ImportHelpers.Trunc(Convert.ToString(r["pincode"])?.Trim(), 20);
        var phone = ImportHelpers.Trunc(Convert.ToString(r["cmpphone"]), 100);
        var email = ImportHelpers.Trunc(Convert.ToString(r["companyemail"])?.Trim(), 200);
        var drugLicense = ImportHelpers.Trunc(Convert.ToString(r["druglcno"])?.Trim(), 100);

        const string sql = """
            IF EXISTS (SELECT 1 FROM CompanyProfiles)
                UPDATE CompanyProfiles SET
                    CompanyName = @CompanyName,
                    LegalName = @CompanyName,
                    Address = @Address,
                    State = @State,
                    Pincode = @Pincode,
                    Phone = @Phone,
                    Email = @Email,
                    GstNumber = @GstNumber,
                    DrugLicenseNumber = @DrugLicense,
                    ModifiedAtUtc = @Now
            ELSE
                INSERT INTO CompanyProfiles
                    (CompanyName, LegalName, Address, State, Pincode, Phone, Email, GstNumber, DrugLicenseNumber,
                     Currency, CurrencySymbol, NearExpiryDays, DefaultLowStockThreshold,
                     SalesInvoicePrefix, PurchaseInvoicePrefix, CreatedAtUtc, IsDeleted)
                VALUES
                    (@CompanyName, @CompanyName, @Address, @State, @Pincode, @Phone, @Email, @GstNumber, @DrugLicense,
                     'INR', N'₹', 90, 10, 'INV', 'PO', @Now, 0)
            """;

        await using var update = new SqlCommand(sql, target);
        update.Parameters.AddWithValue("@CompanyName", companyName);
        update.Parameters.AddWithValue("@Address", (object?)address ?? DBNull.Value);
        update.Parameters.AddWithValue("@State", (object?)state ?? DBNull.Value);
        update.Parameters.AddWithValue("@Pincode", (object?)pincode ?? DBNull.Value);
        update.Parameters.AddWithValue("@Phone", (object?)phone ?? DBNull.Value);
        update.Parameters.AddWithValue("@Email", (object?)email ?? DBNull.Value);
        update.Parameters.AddWithValue("@GstNumber", (object?)gst ?? DBNull.Value);
        update.Parameters.AddWithValue("@DrugLicense", (object?)drugLicense ?? DBNull.Value);
        update.Parameters.AddWithValue("@Now", ctx.NowUtc);
        await update.ExecuteNonQueryAsync();
        Console.WriteLine($"  Updated company: {companyName}");
    }

    private static async Task ImportGstCategoriesAsync(MedWinImportContext ctx, SqlConnection target)
    {
        Console.WriteLine("\n[gst] Importing medicine categories with GST slabs...");
        using var med = ctx.OpenMedWin();
        med.Open();

        var existing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new SqlCommand("SELECT Id, Name FROM MedicineCategories WHERE IsDeleted = 0", target))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                existing[reader.GetString(1)] = reader.GetInt32(0);
        }

        using var ole = new OleDbCommand("SELECT catcode, description, cattax, cgst, sgst, igst FROM category", med);
        using var r = ole.ExecuteReader();
        int added = 0;
        while (r.Read())
        {
            var code = Convert.ToString(r["catcode"])?.Trim();
            var desc = ImportHelpers.Trunc(Convert.ToString(r["description"]), 120)
                       ?? ImportHelpers.Trunc(code, 120)
                       ?? "General";
            if (existing.ContainsKey(desc)) { ctx.CategoryMap[code ?? desc] = existing[desc]; continue; }

            var cgst = ImportHelpers.Dec(r["cgst"]);
            var sgst = ImportHelpers.Dec(r["sgst"]);
            var gst = cgst + sgst;
            if (gst <= 0) gst = ImportHelpers.Dec(r["cattax"]);
            if (gst <= 0) gst = ImportHelpers.Dec(r["igst"]);

            await using var ins = new SqlCommand(
                "INSERT INTO MedicineCategories (Name, Status, CreatedAtUtc, IsDeleted) OUTPUT INSERTED.Id VALUES (@Name, 1, @Now, 0)", target);
            ins.Parameters.AddWithValue("@Name", desc);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            var id = (int)await ins.ExecuteScalarAsync();
            existing[desc] = id;
            if (!string.IsNullOrWhiteSpace(code)) ctx.CategoryMap[code] = id;
            added++;
        }

        Console.WriteLine($"  Categories ready ({added} new). GST rates will be applied on medicines from category.");
    }

    private static async Task ImportUsersAsync(MedWinImportContext ctx, SqlConnection target)
    {
        Console.WriteLine("\n[users] Importing MedWin operator accounts...");
        using var med = ctx.OpenMedWin();
        med.Open();

        var codes = new HashSet<int>();
        void Collect(string sql)
        {
            using var cmd = new OleDbCommand(sql, med);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var code = ImportHelpers.Int(r[0]);
                if (code > 0) codes.Add(code);
            }
        }

        Collect("SELECT DISTINCT oprcodeadd FROM salemaster WHERE oprcodeadd IS NOT NULL");
        Collect("SELECT DISTINCT oprcodeadd FROM purchase WHERE oprcodeadd IS NOT NULL");

        var defaultHash = BCrypt.Net.BCrypt.HashPassword("MedWin@123", 12);
        int added = 0;
        foreach (var code in codes.OrderBy(c => c))
        {
            var username = $"op{code}";
            var exists = await ScalarIntAsync(target, "SELECT COUNT(*) FROM Users WHERE Username = @u",
                new SqlParameter("@u", username));
            if (exists > 0) continue;

            await using var ins = new SqlCommand("""
                INSERT INTO Users
                    (Username, PasswordHash, FullName, RoleId, BranchId, Status, MustChangePassword, FailedLoginAttempts, IsLockedOut, CreatedAtUtc, IsDeleted)
                VALUES
                    (@Username, @Hash, @FullName, @RoleId, @BranchId, 1, 1, 0, 0, @Now, 0)
                """, target);
            ins.Parameters.AddWithValue("@Username", username);
            ins.Parameters.AddWithValue("@Hash", defaultHash);
            ins.Parameters.AddWithValue("@FullName", $"MedWin Operator {code}");
            ins.Parameters.AddWithValue("@RoleId", ctx.CashierRoleId);
            ins.Parameters.AddWithValue("@BranchId", ctx.BranchId);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            await ins.ExecuteNonQueryAsync();
            added++;
        }

        Console.WriteLine($"  Added {added} operator users (password: MedWin@123).");
    }

    internal static async Task<int> ScalarIntAsync(SqlConnection conn, string sql, params SqlParameter[] parameters)
    {
        await using var cmd = new SqlCommand(sql, conn);
        if (parameters.Length > 0) cmd.Parameters.AddRange(parameters);
        var result = await cmd.ExecuteScalarAsync();
        return result is int i ? i : Convert.ToInt32(result);
    }

    internal static async Task<bool> TableHasMedWinDataAsync(SqlConnection conn, string table, string invoicePrefix)
    {
        await using var cmd = new SqlCommand($"SELECT COUNT(*) FROM {table} WHERE InvoiceNumber LIKE @p + '%'", conn);
        cmd.Parameters.AddWithValue("@p", invoicePrefix);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }
}
