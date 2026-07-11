using System.Data.OleDb;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

internal static class MedicineMatchReporter
{
    public static async Task ExportAsync(MedWinImportContext ctx, SqlConnection target, string csvPath)
    {
        Console.WriteLine($"\n[medicine-match] Generating match report (no DB changes)...");
        await MedWinMasterImporter.LoadExistingMedicineMapAsync(ctx, target);
        var matcher = await MedicineCatalogMatcher.LoadAsync(target);

        using var med = ctx.OpenMedWin();
        med.Open();

        var activeIds = MedWinMasterImporter.LoadActiveMedicineIds(med);
        var stockSelling = MedWinMasterImporter.LoadStockSellingPrices(med);
        var saleSelling = MedWinMasterImporter.LoadSaleSellingPrices(med);

        Console.WriteLine($"  Active MedWin medicines: {activeIds.Count:N0}");

        using var cmd = new OleDbCommand("""
            SELECT m.numbercd, m.medcode, m.medname, m.medname1, m.mgamma, m.mrprate, m.fpurrat, m.purrate, m.wrate, m.specialrate
            FROM mednmas m
            """, med);
        using var reader = cmd.ExecuteReader();

        var dir = Path.GetDirectoryName(csvPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        await using var writer = new StreamWriter(csvPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        await writer.WriteLineAsync(string.Join(",",
            "MedWinId", "MedWinCode", "MedWinName", "MedWinName1", "MedWinGeneric",
            "NormalizedKey", "ProposedMatchMethod", "ProposedAction",
            "ProposedMatchedId", "ProposedMatchedName", "ProposedMatchedBarcode",
            "CurrentDbMappedId", "WouldChange", "Mrp", "PurchasePrice", "SellingPrice"));

        int rows = 0, proposedMatch = 0, proposedInsert = 0, wouldChange = 0;

        while (reader.Read())
        {
            var medWinId = ImportHelpers.Int(reader["numbercd"]);
            if (medWinId <= 0 || !activeIds.Contains(medWinId)) continue;

            var medName = Convert.ToString(reader["medname"]);
            var medName1 = Convert.ToString(reader["medname1"]);
            var genericName = Convert.ToString(reader["mgamma"]);
            var barcode = Convert.ToString(reader["medcode"]);

            var mrp = ImportHelpers.Dec(reader["mrprate"]);
            var purchase = ImportHelpers.Dec(reader["fpurrat"]);
            if (purchase <= 0) purchase = ImportHelpers.Dec(reader["purrate"]);
            var selling = MedWinMasterImporter.ResolveSellingPrice(medWinId, reader, stockSelling, saleSelling, mrp);

            var match = matcher.TryMatchDetailed(medName, medName1, genericName, barcode);
            var proposedAction = match is not null ? "MatchExisting" : "InsertNew";
            if (match is not null) proposedMatch++; else proposedInsert++;

            int? currentMappedId = ctx.MedicineMap.TryGetValue(medWinId, out var mapped) ? mapped : null;
            var proposedId = match?.MedicineId;
            var changes = currentMappedId != proposedId;
            if (changes) wouldChange++;

            var normalizedKey = match?.NormalizedKey ?? FirstNormalizedKey(medName1, medName, genericName);

            await writer.WriteLineAsync(string.Join(",",
                Csv(medWinId),
                Csv(barcode),
                Csv(medName),
                Csv(medName1),
                Csv(genericName),
                Csv(normalizedKey),
                Csv(match?.MatchMethod ?? ""),
                Csv(proposedAction),
                Csv(proposedId),
                Csv(match?.Matched.Name),
                Csv(match?.Matched.Barcode),
                Csv(currentMappedId),
                Csv(changes ? "Yes" : "No"),
                Csv(mrp),
                Csv(purchase),
                Csv(selling)));

            rows++;
        }

        Console.WriteLine($"  Report rows: {rows:N0} (proposed match {proposedMatch:N0}, insert {proposedInsert:N0}, would change {wouldChange:N0})");
        Console.WriteLine($"  CSV written: {Path.GetFullPath(csvPath)}");
    }

    private static string FirstNormalizedKey(params string?[] names)
    {
        foreach (var name in names)
        {
            var key = ImportHelpers.NormalizeForMatch(name);
            if (key.Length > 0) return key;
        }
        return string.Empty;
    }

    private static string Csv(object? value)
    {
        if (value is null) return "";
        var text = value switch
        {
            decimal d => d.ToString(CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? ""
        };
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
            return $"\"{text.Replace("\"", "\"\"")}\"";
        return text;
    }
}
