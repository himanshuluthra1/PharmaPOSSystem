using System.Data;
using System.Data.OleDb;
using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

public static class MedWinMasterImporter
{
    public static async Task ImportMedicinesAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[medicines] Importing active MedWin medicines (in stock or sold), matching OneMG catalogue...");
        await LoadCategoriesAsync(ctx, target);
        await LoadManufacturersAsync(ctx, target);

        if (ctx.ForceMedicines)
            await RemoveOrphanMedWinMedicinesAsync(ctx, target);
        else
            await LoadExistingMedicineMapAsync(ctx, target);

        var matcher = await MedicineCatalogMatcher.LoadAsync(target);

        using var med = ctx.OpenMedWin();
        med.Open();

        var activeIds = LoadActiveMedicineIds(med);
        ctx.Log($"  Active MedWin medicines (stock or sold): {activeIds.Count:N0}");

        var stockSelling = LoadStockSellingPrices(med);
        var saleSelling = LoadSaleSellingPrices(med);
        // Access Jet rejects multi LEFT JOIN without nested parentheses — load lookups separately.
        var saltByGroup = LoadItemGroupSalts(med);
        var gstByCategory = LoadCategoryGst(med);

        using var cmd = new OleDbCommand("""
            SELECT numbercd, medcode, medname, medname1, mcomp, mgamma, medgrp, mstrngth, medsize, sizefact,
                   mrprate, fpurrat, purrate, wrate, specialrate, medloct, item_catcode, remarks
            FROM mednmas
            """, med);
        using var reader = cmd.ExecuteReader();

        var table = CreateMedicineTable();
        var pendingUpdates = new List<MedicinePriceUpdate>();
        int read = 0, skipped = 0, matched = 0, inserted = 0;

        while (reader.Read())
        {
            read++;
            var medWinId = ImportHelpers.Int(reader["numbercd"]);
            if (medWinId <= 0 || !activeIds.Contains(medWinId)) { skipped++; continue; }
            var name = ImportHelpers.Trunc(Convert.ToString(reader["medname"]), 200);
            if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }

            var medName1 = Convert.ToString(reader["medname1"]);
            // mgamma is a salt-group code (ATO/MIN/...), not the actual salt.
            // Real salt/composition is itemgrp.itemgrds via medgrp.
            var saltGroupCode = Convert.ToString(reader["mgamma"]);
            var medGrp = (Convert.ToString(reader["medgrp"]) ?? "").Trim();
            saltByGroup.TryGetValue(medGrp, out var saltName);
            var genericName = !string.IsNullOrWhiteSpace(saltName) ? saltName : saltGroupCode;
            var barcode = Convert.ToString(reader["medcode"]);
            var packInfo = ImportHelpers.Trunc(Convert.ToString(reader["medsize"]), 200);

            var mrp = ImportHelpers.Dec(reader["mrprate"]);
            var purchase = ImportHelpers.Dec(reader["fpurrat"]);
            if (purchase <= 0) purchase = ImportHelpers.Dec(reader["purrate"]);
            var selling = ResolveSellingPrice(medWinId, reader, stockSelling, saleSelling, mrp);

            if (ctx.MedicineMap.TryGetValue(medWinId, out var alreadyMapped))
            {
                pendingUpdates.Add(new MedicinePriceUpdate(alreadyMapped, medWinId, mrp, purchase, selling, null));
                if (pendingUpdates.Count >= 1000) { await FlushMedicineUpdatesAsync(ctx, target, pendingUpdates); pendingUpdates.Clear(); }
                continue;
            }

            var existingId = matcher.TryMatch(name, medName1, genericName, barcode);
            if (existingId.HasValue)
            {
                pendingUpdates.Add(new MedicinePriceUpdate(existingId.Value, medWinId, mrp, purchase, selling, matcher.GetNotesFor(existingId.Value)));
                ctx.MedicineMap[medWinId] = existingId.Value;
                matched++;
                if (pendingUpdates.Count >= 1000) { await FlushMedicineUpdatesAsync(ctx, target, pendingUpdates); pendingUpdates.Clear(); }
                continue;
            }

            var mcomp = Convert.ToString(reader["mcomp"])?.Trim();
            int? manufacturerId = null;
            if (!string.IsNullOrWhiteSpace(mcomp) && ctx.ManufacturerMap.TryGetValue(mcomp, out var mid))
                manufacturerId = mid;

            var catCode = Convert.ToString(reader["item_catcode"])?.Trim();
            int? categoryId = null;
            if (!string.IsNullOrWhiteSpace(catCode) && ctx.CategoryMap.TryGetValue(catCode, out var cid))
                categoryId = cid;

            var gst = 12m;
            if (!string.IsNullOrWhiteSpace(catCode) && gstByCategory.TryGetValue(catCode, out var catGst) && catGst > 0)
                gst = catGst;

            var row = table.NewRow();
            row["Name"] = name;
            row["GenericName"] = (object?)ImportHelpers.Trunc(genericName, 200) ?? DBNull.Value;
            row["Brand"] = (object?)ImportHelpers.Trunc(mcomp, 200) ?? DBNull.Value;
            row["Strength"] = (object?)ImportHelpers.Trunc(Convert.ToString(reader["mstrngth"]), 100) ?? DBNull.Value;
            row["Composition"] = (object?)ImportHelpers.Trunc(genericName, 500) ?? DBNull.Value;
            row["DosageForm"] = 0;
            row["CategoryId"] = (object?)categoryId ?? DBNull.Value;
            row["ManufacturerId"] = (object?)manufacturerId ?? DBNull.Value;
            row["HsnCode"] = "3004";
            row["GstPercent"] = gst;
            row["IsBatchEnabled"] = true;
            row["IsExpiryEnabled"] = true;
            row["Barcode"] = (object?)ImportHelpers.Trunc(barcode, 64) ?? DBNull.Value;
            row["Mrp"] = mrp;
            row["PurchasePrice"] = purchase;
            row["SellingPrice"] = selling;
            row["DefaultDiscountPercent"] = 0m;
            row["RackNumber"] = (object?)ImportHelpers.Trunc(Convert.ToString(reader["medloct"]), 50) ?? DBNull.Value;
            row["ScheduleType"] = 0;
            row["PrescriptionRequired"] = false;
            row["UnitsPerPack"] = Math.Max(1, ImportHelpers.Int(reader["sizefact"]));
            row["UnitOfMeasure"] = "Nos";
            row["ReorderLevel"] = 0;
            row["ReorderQuantity"] = 0;
            row["PackInfo"] = (object?)packInfo ?? DBNull.Value;
            row["Notes"] = ImportHelpers.MedWinMedicineNote(medWinId);
            row["Status"] = 1;
            row["CreatedAtUtc"] = ctx.NowUtc;
            row["IsDeleted"] = false;
            table.Rows.Add(row);
            inserted++;

            if (table.Rows.Count >= 2000)
            {
                ctx.Log($"  …flushing {table.Rows.Count:N0} new medicine row(s)…");
                await FlushMedicinesAsync(target, table);
                table.Clear();
            }
        }

        if (table.Rows.Count > 0)
            await FlushMedicinesAsync(target, table);
        if (pendingUpdates.Count > 0)
            await FlushMedicineUpdatesAsync(ctx, target, pendingUpdates);

        await LoadExistingMedicineMapAsync(ctx, target);
        ctx.Log($"  Active scanned {read:N0}: matched OneMG {matched:N0}, new inserts {inserted:N0}, mapped {ctx.MedicineMap.Count:N0}, skipped {skipped:N0}.");
    }

    /// <summary>
    /// Re-applies real salt (itemgrp.itemgrds), pack (medsize), and strength onto existing MedWin-only medicine rows.
    /// </summary>
    public static async Task BackfillSaltsAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[backfill-salts] Refreshing GenericName/Composition/PackInfo from MedWin itemgrp...");
        ctx.ThrowIfCancellationRequested();

        var groups = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var byMw = new Dictionary<int, (string Salt, string? Pack, string? Strength)>();

        using (var med = ctx.OpenMedWin())
        {
            med.Open();
            using (var gcmd = new OleDbCommand("SELECT itemgrcd, itemgrds FROM itemgrp", med))
            using (var gr = gcmd.ExecuteReader())
            {
                while (gr.Read())
                {
                    var code = (Convert.ToString(gr["itemgrcd"]) ?? "").Trim();
                    var name = (Convert.ToString(gr["itemgrds"]) ?? "").Trim();
                    if (code.Length > 0 && name.Length > 0)
                        groups[code] = name;
                }
            }

            using var mcmd = new OleDbCommand("SELECT numbercd, medgrp, medsize, mstrngth FROM mednmas", med);
            using var mr = mcmd.ExecuteReader();
            while (mr.Read())
            {
                var id = Convert.ToInt32(Convert.ToDecimal(mr["numbercd"]));
                var grp = (Convert.ToString(mr["medgrp"]) ?? "").Trim();
                groups.TryGetValue(grp, out var salt);
                byMw[id] = (
                    salt ?? "",
                    string.IsNullOrWhiteSpace(Convert.ToString(mr["medsize"])) ? null : Convert.ToString(mr["medsize"])!.Trim(),
                    string.IsNullOrWhiteSpace(Convert.ToString(mr["mstrngth"])) ? null : Convert.ToString(mr["mstrngth"])!.Trim());
            }
        }

        await using var setOpts = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;", target);
        await setOpts.ExecuteNonQueryAsync(ctx.CancellationToken);

        await using var list = new SqlCommand("""
            SELECT Id, Notes FROM Medicines
            WHERE IsDeleted = 0 AND Notes LIKE 'MedWinId:%' AND Notes NOT LIKE '%|%'
            """, target);
        var rows = new List<(int Id, int Mw)>();
        await using (var reader = await list.ExecuteReaderAsync(ctx.CancellationToken))
        {
            while (await reader.ReadAsync(ctx.CancellationToken))
            {
                var notes = reader.GetString(1).Trim();
                if (!int.TryParse(notes.AsSpan("MedWinId:".Length), out var mw)) continue;
                rows.Add((reader.GetInt32(0), mw));
            }
        }

        var updated = 0;
        await using var tx = (SqlTransaction)await target.BeginTransactionAsync(ctx.CancellationToken);
        foreach (var row in rows)
        {
            ctx.ThrowIfCancellationRequested();
            if (!byMw.TryGetValue(row.Mw, out var info)) continue;
            if (info.Salt.Length == 0 && info.Pack is null && info.Strength is null) continue;

            await using var upd = new SqlCommand("""
                UPDATE Medicines SET
                  GenericName = CASE WHEN @hasSalt = 1 THEN @salt ELSE GenericName END,
                  Composition = CASE WHEN @hasSalt = 1 THEN @salt ELSE Composition END,
                  PackInfo = COALESCE(@pack, PackInfo),
                  Strength = COALESCE(@strength, Strength)
                WHERE Id = @id
                """, target, tx);
            upd.Parameters.AddWithValue("@hasSalt", info.Salt.Length > 0 ? 1 : 0);
            upd.Parameters.AddWithValue("@salt", info.Salt.Length > 0 ? info.Salt : (object)DBNull.Value);
            upd.Parameters.AddWithValue("@pack", (object?)info.Pack ?? DBNull.Value);
            upd.Parameters.AddWithValue("@strength", (object?)info.Strength ?? DBNull.Value);
            upd.Parameters.AddWithValue("@id", row.Id);
            updated += await upd.ExecuteNonQueryAsync(ctx.CancellationToken);
        }

        await tx.CommitAsync(ctx.CancellationToken);
        ctx.Log($"  Salt/pack backfill updated {updated:N0} of {rows.Count:N0} MedWin medicine(s).");
    }

    private sealed record MedicinePriceUpdate(int MedicineId, int MedWinId, decimal Mrp, decimal Purchase, decimal Selling, string? ExistingNotes);

    public static HashSet<int> LoadActiveMedicineIds(OleDbConnection med)
    {
        var ids = new HashSet<int>();
        using (var cmd = new OleDbCommand("SELECT DISTINCT stkcode FROM stockmas", med))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) { var id = ImportHelpers.Int(r[0]); if (id > 0) ids.Add(id); }
        using (var cmd = new OleDbCommand("SELECT DISTINCT dpmedcod FROM dsalemaster", med))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) { var id = ImportHelpers.Int(r[0]); if (id > 0) ids.Add(id); }
        return ids;
    }

    /// <summary>itemgrp.itemgrcd → itemgrds (real salt). Keys are trimmed.</summary>
    public static Dictionary<string, string> LoadItemGroupSalts(OleDbConnection med)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new OleDbCommand("SELECT itemgrcd, itemgrds FROM itemgrp", med);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var code = (Convert.ToString(r["itemgrcd"]) ?? "").Trim();
            var name = (Convert.ToString(r["itemgrds"]) ?? "").Trim();
            if (code.Length > 0 && name.Length > 0)
                map[code] = name;
        }
        return map;
    }

    /// <summary>category.catcode → GST % (cgst+sgst, else cattax, else igst).</summary>
    public static Dictionary<string, decimal> LoadCategoryGst(OleDbConnection med)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        using var cmd = new OleDbCommand("SELECT catcode, cgst, sgst, cattax, igst FROM category", med);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var code = (Convert.ToString(r["catcode"]) ?? "").Trim();
            if (code.Length == 0) continue;
            var gst = ImportHelpers.Dec(r["cgst"]) + ImportHelpers.Dec(r["sgst"]);
            if (gst <= 0) gst = ImportHelpers.Dec(r["cattax"]);
            if (gst <= 0) gst = ImportHelpers.Dec(r["igst"]);
            if (gst > 0) map[code] = gst;
        }
        return map;
    }

    private static async Task FlushMedicineUpdatesAsync(MedWinImportContext ctx, SqlConnection target, List<MedicinePriceUpdate> updates)
    {
        foreach (var u in updates)
        {
            var notes = MedicineCatalogMatcher.AppendMedWinNote(u.ExistingNotes, u.MedWinId);
            await using var upd = new SqlCommand("""
                UPDATE Medicines SET
                    Mrp = CASE WHEN @Mrp > 0 THEN @Mrp ELSE Mrp END,
                    PurchasePrice = CASE WHEN @Purchase > 0 THEN @Purchase ELSE PurchasePrice END,
                    SellingPrice = CASE WHEN @Selling > 0 THEN @Selling ELSE SellingPrice END,
                    Notes = @Notes,
                    ModifiedAtUtc = @Now
                WHERE Id = @Id
                """, target);
            upd.Parameters.AddWithValue("@Id", u.MedicineId);
            upd.Parameters.AddWithValue("@Mrp", u.Mrp);
            upd.Parameters.AddWithValue("@Purchase", u.Purchase);
            upd.Parameters.AddWithValue("@Selling", u.Selling);
            upd.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
            upd.Parameters.AddWithValue("@Now", ctx.NowUtc);
            await upd.ExecuteNonQueryAsync();
        }
    }

    public static decimal ResolveSellingPrice(
        int medWinId,
        OleDbDataReader reader,
        Dictionary<int, decimal> stockSelling,
        Dictionary<int, decimal> saleSelling,
        decimal mrp)
    {
        if (stockSelling.TryGetValue(medWinId, out var fromStock) && fromStock > 0) return fromStock;

        var wrate = ImportHelpers.Dec(reader["wrate"]);
        if (wrate > 0) return wrate;

        var special = ImportHelpers.Dec(reader["specialrate"]);
        if (special > 0) return special;

        if (saleSelling.TryGetValue(medWinId, out var fromSale) && fromSale > 0) return fromSale;

        return mrp;
    }

    public static Dictionary<int, decimal> LoadStockSellingPrices(OleDbConnection med)
    {
        var map = new Dictionary<int, decimal>();
        using var cmd = new OleDbCommand("""
            SELECT stkcode, MAX(stkinvrate) AS sell_rate
            FROM stockmas
            WHERE stkinvrate > 0
            GROUP BY stkcode
            """, med);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = ImportHelpers.Int(r["stkcode"]);
            var rate = ImportHelpers.Dec(r["sell_rate"]);
            if (id > 0 && rate > 0) map[id] = rate;
        }
        return map;
    }

    public static Dictionary<int, decimal> LoadSaleSellingPrices(OleDbConnection med)
    {
        var map = new Dictionary<int, decimal>();
        using var cmd = new OleDbCommand("""
            SELECT dpmedcod, dpqty, dpamt, mrprate, dpfmrp
            FROM dsalemaster
            WHERE dpqty > 0 AND dpamt > 0
            """, med);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = ImportHelpers.Int(r["dpmedcod"]);
            if (id <= 0) continue;
            var qty = ImportHelpers.Dec(r["dpqty"]);
            var amt = ImportHelpers.Dec(r["dpamt"]);
            var unit = qty > 0 ? Math.Round(amt / qty, 2) : 0m;
            if (unit <= 0)
            {
                unit = ImportHelpers.Dec(r["mrprate"]);
                if (unit <= 0) unit = ImportHelpers.Dec(r["dpfmrp"]);
            }
            if (unit > 0) map[id] = unit;
        }
        return map;
    }

    public static async Task ImportSuppliersAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[suppliers] Importing suppliers from subgroup...");
        using var med = ctx.OpenMedWin();
        med.Open();
        using var cmd = new OleDbCommand("""
            SELECT acctno, subdesc, subadd1, subadd2, subadd3, subphone, mobileno, emailid,
                   city, state, pincode, tinno, sublcno, subopbal, balanceamt
            FROM subgroup
            WHERE subgrpty = 'SC' AND acctno > 1
            """, med);
        using var reader = cmd.ExecuteReader();

        int added = 0;
        while (reader.Read())
        {
            var medWinId = ImportHelpers.Int(reader["acctno"]);
            var name = ImportHelpers.Trunc(Convert.ToString(reader["subdesc"]), 200);
            if (medWinId <= 0 || string.IsNullOrWhiteSpace(name)) continue;

            var existing = await MedWinImporter.ScalarIntAsync(target,
                "SELECT TOP 1 Id FROM Suppliers WHERE Name = @Name AND BranchId = @BranchId",
                new SqlParameter("@Name", name),
                new SqlParameter("@BranchId", ctx.BranchId));
            if (existing > 0)
            {
                ctx.SupplierMap[medWinId] = existing;
                continue;
            }

            var address = string.Join(", ", new[]
            {
                Convert.ToString(reader["subadd1"])?.Trim(),
                Convert.ToString(reader["subadd2"])?.Trim(),
                Convert.ToString(reader["subadd3"])?.Trim()
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            await using var ins = new SqlCommand("""
                INSERT INTO Suppliers
                    (Name, GstNumber, DrugLicenseNumber, Address, City, State, Pincode, Phone, Email,
                     PaymentTermsDays, OpeningBalance, OutstandingBalance, Status, BranchId, CreatedAtUtc, IsDeleted)
                OUTPUT INSERTED.Id
                VALUES
                    (@Name, @Gst, @Dl, @Address, @City, @State, @Pincode, @Phone, @Email,
                     30, @Opening, @Outstanding, 1, @BranchId, @Now, 0)
                """, target);
            ins.Parameters.AddWithValue("@Name", name);
            ins.Parameters.AddWithValue("@Gst", (object?)ImportHelpers.Trunc(Convert.ToString(reader["tinno"]), 20) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Dl", (object?)ImportHelpers.Trunc(Convert.ToString(reader["sublcno"]), 60) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Address", (object?)ImportHelpers.Trunc(address, 500) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@City", (object?)ImportHelpers.Trunc(Convert.ToString(reader["city"]), 100) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@State", (object?)ImportHelpers.Trunc(Convert.ToString(reader["state"]), 100) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Pincode", (object?)ImportHelpers.Trunc(Convert.ToString(reader["pincode"]), 20) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Phone", (object?)ImportHelpers.Trunc(Convert.ToString(reader["mobileno"]) ?? Convert.ToString(reader["subphone"]), 30) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Email", (object?)ImportHelpers.Trunc(Convert.ToString(reader["emailid"]), 200) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Opening", ImportHelpers.Dec(reader["subopbal"]));
            ins.Parameters.AddWithValue("@Outstanding", ImportHelpers.Dec(reader["balanceamt"]));
            ins.Parameters.AddWithValue("@BranchId", ctx.BranchId);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            var id = (int)await ins.ExecuteScalarAsync();
            ctx.SupplierMap[medWinId] = id;
            added++;
        }

        ctx.Log($"  Suppliers ready ({added} new, {ctx.SupplierMap.Count:N0} mapped).");
    }

    public static async Task ImportCustomersAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[customers] Importing customers...");
        using var med = ctx.OpenMedWin();
        med.Open();

        int added = 0;
        async Task<int> EnsureCustomerAsync(string? name, string? phone, CustomerKind kind, int? medWinId = null)
        {
            name = ImportHelpers.Trunc(name, 200);
            if (string.IsNullOrWhiteSpace(name) || name.Equals("MS", StringComparison.OrdinalIgnoreCase))
                return 0;

            if (medWinId.HasValue && ctx.CustomerMap.TryGetValue(medWinId.Value, out var mapped))
                return mapped;

            await using var find = new SqlCommand(
                "SELECT TOP 1 Id FROM Customers WHERE BranchId = @BranchId AND Name = @Name AND ISNULL(Phone,'') = ISNULL(@Phone,'')",
                target);
            find.Parameters.AddWithValue("@BranchId", ctx.BranchId);
            find.Parameters.AddWithValue("@Name", name);
            find.Parameters.AddWithValue("@Phone", (object?)ImportHelpers.Trunc(phone, 30) ?? DBNull.Value);
            var existing = await find.ExecuteScalarAsync();
            if (existing is int eid)
            {
                if (medWinId.HasValue) ctx.CustomerMap[medWinId.Value] = eid;
                return eid;
            }

            await using var ins = new SqlCommand("""
                INSERT INTO Customers (Name, Type, Phone, BranchId, CreditLimit, OutstandingBalance, RewardPoints, IsMember, Status, CreatedAtUtc, IsDeleted)
                OUTPUT INSERTED.Id VALUES (@Name, @Type, @Phone, @BranchId, 0, 0, 0, 0, 1, @Now, 0)
                """, target);
            ins.Parameters.AddWithValue("@Name", name);
            ins.Parameters.AddWithValue("@Type", (int)kind);
            ins.Parameters.AddWithValue("@Phone", (object?)ImportHelpers.Trunc(phone, 30) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@BranchId", ctx.BranchId);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            var id = (int)await ins.ExecuteScalarAsync();
            if (medWinId.HasValue) ctx.CustomerMap[medWinId.Value] = id;
            added++;
            return id;
        }

        using (var cmd = new OleDbCommand("SELECT acctno, subdesc, mobileno FROM subgroup WHERE subgrpty = 'SD'", med))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                var id = ImportHelpers.Int(r["acctno"]);
                await EnsureCustomerAsync(Convert.ToString(r["subdesc"]), Convert.ToString(r["mobileno"]), CustomerKind.Regular, id);
            }
        }

        using (var cmd = new OleDbCommand("SELECT patient FROM patient_master WHERE patient IS NOT NULL AND patient <> ''", med))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                await EnsureCustomerAsync(Convert.ToString(r["patient"]), null, CustomerKind.Retail);
        }

        using (var cmd = new OleDbCommand("""
            SELECT DISTINCT cashcustname, cashcustphone
            FROM salemaster
            WHERE cashcustname IS NOT NULL AND cashcustname <> '' AND cashcustname <> 'MS'
            """, med))
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
                await EnsureCustomerAsync(Convert.ToString(r["cashcustname"]), Convert.ToString(r["cashcustphone"]), CustomerKind.Retail);
        }

        ctx.Log($"  Customers ready ({added} new).");
    }

    public static async Task ImportStockAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[stock] Importing stock batches from stockmas...");
        if (ctx.MedicineMap.Count == 0)
            await LoadExistingMedicineMapAsync(ctx, target);

        var gstByMedicine = new Dictionary<int, decimal>();
        await using (var gstCmd = new SqlCommand(
                         "SELECT Id, GstPercent FROM Medicines WHERE IsDeleted = 0", target))
        await using (var gstReader = await gstCmd.ExecuteReaderAsync())
        {
            while (await gstReader.ReadAsync())
                gstByMedicine[gstReader.GetInt32(0)] = gstReader.GetDecimal(1);
        }

        using var med = ctx.OpenMedWin();
        med.Open();
        using var cmd = new OleDbCommand("""
            SELECT stkcode, stkbatch, stkqty, stkfpurt, stkfnmrp, stkinvrate, mrprate,
                   stkexyr, stkexmn, stksize, stockno, manfdate
            FROM stockmas
            WHERE stkqty > 0
            """, med);
        using var reader = cmd.ExecuteReader();

        int added = 0, skipped = 0;
        while (reader.Read())
        {
            var medWinId = ImportHelpers.Int(reader["stkcode"]);
            if (!ctx.MedicineMap.TryGetValue(medWinId, out var medicineId)) { skipped++; continue; }

            var batchNo = ImportHelpers.Trunc(Convert.ToString(reader["stkbatch"]), 60) ?? "BATCH";
            var stockNo = ImportHelpers.Int(reader["stockno"]);
            var key = $"{medWinId}:{batchNo}:{stockNo}";
            if (ctx.BatchMap.ContainsKey(key)) continue;

            var expiryYear = ImportHelpers.Int(reader["stkexyr"]);
            var expiryMonth = ImportHelpers.Int(reader["stkexmn"]);
            var expiry = ImportHelpers.ParseExpiryMonthYear(expiryYear, expiryMonth);

            var mrp = ImportHelpers.Dec(reader["mrprate"]);
            if (mrp <= 0) mrp = ImportHelpers.Dec(reader["stkfnmrp"]);
            var purchase = ImportHelpers.Dec(reader["stkfpurt"]);
            var selling = ImportHelpers.Dec(reader["stkinvrate"]);
            if (selling <= 0) selling = mrp;
            var qty = ImportHelpers.Dec(reader["stkqty"]);

            var gst = gstByMedicine.TryGetValue(medicineId, out var g) ? g : 12m;

            await using var ins = new SqlCommand("""
                INSERT INTO MedicineBatches
                    (MedicineId, BatchNumber, ManufacturingDate, ExpiryDate, QuantityAvailable,
                     PurchasePrice, Mrp, SellingPrice, GstPercent, BranchId, CreatedAtUtc, IsDeleted)
                OUTPUT INSERTED.Id
                VALUES
                    (@MedicineId, @Batch, @Mfg, @Expiry, @Qty, @Purchase, @Mrp, @Selling, @Gst, @BranchId, @Now, 0)
                """, target);
            ins.Parameters.AddWithValue("@MedicineId", medicineId);
            ins.Parameters.AddWithValue("@Batch", batchNo);
            ins.Parameters.AddWithValue("@Mfg", (object?)ImportHelpers.Date(reader["manfdate"]) ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Expiry", (object?)expiry ?? DBNull.Value);
            ins.Parameters.AddWithValue("@Qty", qty);
            ins.Parameters.AddWithValue("@Purchase", purchase);
            ins.Parameters.AddWithValue("@Mrp", mrp);
            ins.Parameters.AddWithValue("@Selling", selling);
            ins.Parameters.AddWithValue("@Gst", gst);
            ins.Parameters.AddWithValue("@BranchId", ctx.BranchId);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            var batchId = (int)await ins.ExecuteScalarAsync();
            ctx.BatchMap[key] = batchId;

            await using var mv = new SqlCommand("""
                INSERT INTO StockMovements
                    (BranchId, MedicineId, MedicineBatchId, MovementType, Quantity, BalanceAfter,
                     UnitCost, ReferenceType, ReferenceNumber, Remarks, MovementDateUtc,
                     CreatedAtUtc, IsDeleted)
                VALUES
                    (@BranchId, @MedicineId, @BatchId, @MovementType, @Qty, @Qty,
                     @UnitCost, N'MedWinImport', N'STOCK', N'MedWin current stock snapshot', @Now,
                     @Now, 0)
                """, target);
            mv.Parameters.AddWithValue("@BranchId", ctx.BranchId);
            mv.Parameters.AddWithValue("@MedicineId", medicineId);
            mv.Parameters.AddWithValue("@BatchId", batchId);
            mv.Parameters.AddWithValue("@MovementType", 10); // StockMovementType.OpeningStock
            mv.Parameters.AddWithValue("@Qty", qty);
            mv.Parameters.AddWithValue("@UnitCost", purchase);
            mv.Parameters.AddWithValue("@Now", ctx.NowUtc);
            await mv.ExecuteNonQueryAsync();

            added++;
        }

        ctx.Log($"  Stock batches imported: {added:N0} ({skipped:N0} skipped — medicine not mapped).");
    }

    private static async Task LoadCategoriesAsync(MedWinImportContext ctx, SqlConnection target)
    {
        if (ctx.CategoryMap.Count > 0) return;
        await using var cmd = new SqlCommand("SELECT Id, Name FROM MedicineCategories WHERE IsDeleted = 0", target);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ctx.CategoryMap[reader.GetString(1)] = reader.GetInt32(0);
    }

    private static async Task LoadManufacturersAsync(MedWinImportContext ctx, SqlConnection target)
    {
        await using (var cmd = new SqlCommand("SELECT Id, Name FROM Manufacturers WHERE IsDeleted = 0", target))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                ctx.ManufacturerMap[reader.GetString(1)] = reader.GetInt32(0);
        }

        using var med = ctx.OpenMedWin();
        med.Open();
        using var ole = new OleDbCommand("SELECT DISTINCT compcode, compname FROM compmas WHERE compname IS NOT NULL AND compname <> ''", med);
        using var r = ole.ExecuteReader();
        var newNames = new List<string>();
        while (r.Read())
        {
            var code = Convert.ToString(r["compcode"])?.Trim();
            var name = ImportHelpers.Trunc(Convert.ToString(r["compname"]), 200);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) continue;
            if (!ctx.ManufacturerMap.ContainsKey(name))
                newNames.Add(name);
            else
                ctx.ManufacturerMap[code] = ctx.ManufacturerMap[name];
        }

        foreach (var name in newNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await using var ins = new SqlCommand(
                "INSERT INTO Manufacturers (Name, Status, CreatedAtUtc, IsDeleted) OUTPUT INSERTED.Id VALUES (@Name, 1, @Now, 0)", target);
            ins.Parameters.AddWithValue("@Name", name);
            ins.Parameters.AddWithValue("@Now", ctx.NowUtc);
            var id = (int)await ins.ExecuteScalarAsync();
            ctx.ManufacturerMap[name] = id;
        }

        using var ole2 = new OleDbCommand("SELECT compcode, compname FROM compmas WHERE compname IS NOT NULL AND compname <> ''", med);
        using var r2 = ole2.ExecuteReader();
        while (r2.Read())
        {
            var code = Convert.ToString(r2["compcode"])?.Trim();
            var name = ImportHelpers.Trunc(Convert.ToString(r2["compname"]), 200);
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name)) continue;
            if (ctx.ManufacturerMap.TryGetValue(name, out var id))
                ctx.ManufacturerMap[code] = id;
        }
    }

    private static async Task RemoveOrphanMedWinMedicinesAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("  --force: removing prior MedWin-only medicine rows for rematch...");
        await using var cmd = new SqlCommand("""
            UPDATE Medicines SET IsDeleted = 1, DeletedAtUtc = @Now
            WHERE IsDeleted = 0
              AND Notes LIKE 'MedWinId:%'
              AND Notes NOT LIKE '%|%'
              AND Notes NOT LIKE '%OneMG%'
            """, target);
        cmd.Parameters.AddWithValue("@Now", ctx.NowUtc);
        var removed = await cmd.ExecuteNonQueryAsync();
        ctx.Log($"  Soft-deleted {removed:N0} orphan MedWin-only medicines.");
        ctx.MedicineMap.Clear();
    }

    public static async Task LoadExistingMedicineMapAsync(MedWinImportContext ctx, SqlConnection target)
    {
        await using var cmd = new SqlCommand("SELECT Id, Notes FROM Medicines WHERE Notes LIKE '%MedWinId:%' AND IsDeleted = 0", target);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var medWinId = ImportHelpers.ParseMedWinMedicineId(reader.IsDBNull(1) ? null : reader.GetString(1));
            if (medWinId.HasValue)
                ctx.MedicineMap[medWinId.Value] = reader.GetInt32(0);
        }
    }

    private static async Task<decimal> GetMedicineGstAsync(SqlConnection target, int medicineId)
    {
        await using var cmd = new SqlCommand("SELECT GstPercent FROM Medicines WHERE Id = @Id", target);
        cmd.Parameters.AddWithValue("@Id", medicineId);
        var result = await cmd.ExecuteScalarAsync();
        return result is decimal d ? d : 12m;
    }

    private static DataTable CreateMedicineTable()
    {
        var t = new DataTable();
        t.Columns.Add("Name", typeof(string));
        t.Columns.Add("GenericName", typeof(string));
        t.Columns.Add("Brand", typeof(string));
        t.Columns.Add("Strength", typeof(string));
        t.Columns.Add("Composition", typeof(string));
        t.Columns.Add("DosageForm", typeof(int));
        t.Columns.Add("CategoryId", typeof(int));
        t.Columns.Add("ManufacturerId", typeof(int));
        t.Columns.Add("HsnCode", typeof(string));
        t.Columns.Add("GstPercent", typeof(decimal));
        t.Columns.Add("IsBatchEnabled", typeof(bool));
        t.Columns.Add("IsExpiryEnabled", typeof(bool));
        t.Columns.Add("Barcode", typeof(string));
        t.Columns.Add("Mrp", typeof(decimal));
        t.Columns.Add("PurchasePrice", typeof(decimal));
        t.Columns.Add("SellingPrice", typeof(decimal));
        t.Columns.Add("DefaultDiscountPercent", typeof(decimal));
        t.Columns.Add("RackNumber", typeof(string));
        t.Columns.Add("ScheduleType", typeof(int));
        t.Columns.Add("PrescriptionRequired", typeof(bool));
        t.Columns.Add("UnitsPerPack", typeof(int));
        t.Columns.Add("UnitOfMeasure", typeof(string));
        t.Columns.Add("ReorderLevel", typeof(int));
        t.Columns.Add("ReorderQuantity", typeof(int));
        t.Columns.Add("PackInfo", typeof(string));
        t.Columns.Add("Notes", typeof(string));
        t.Columns.Add("Status", typeof(int));
        t.Columns.Add("CreatedAtUtc", typeof(DateTime));
        t.Columns.Add("IsDeleted", typeof(bool));
        return t;
    }

    private static async Task FlushMedicinesAsync(SqlConnection target, DataTable table)
    {
        using var bulk = new SqlBulkCopy(target)
        {
            DestinationTableName = "Medicines",
            BulkCopyTimeout = 600,
            BatchSize = 1000
        };
        foreach (DataColumn col in table.Columns)
            bulk.ColumnMappings.Add(col.ColumnName, col.ColumnName);
        await bulk.WriteToServerAsync(table);
    }

    private enum CustomerKind
    {
        Retail = 0,
        Regular = 1
    }
}
