using Microsoft.Data.SqlClient;
using PharmaPOS.Application.Common;

const string DefaultSource =
    "Server=localhost;Database=OneMGData;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";
const string DefaultTarget =
    "Server=(localdb)\\MSSQLLocalDB;Database=PharmaPosDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

var sourceConn = DefaultSource;
var targetConn = DefaultTarget;
var dryRun = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--source":
            sourceConn = args[++i];
            break;
        case "--target":
            targetConn = args[++i];
            break;
        case "--dry-run":
            dryRun = true;
            break;
        case "--help":
        case "-h":
            PrintHelp();
            return 0;
    }
}

Console.WriteLine("PharmaPOS pack_info updater (one-time)");
Console.WriteLine("======================================");
Console.WriteLine($"Source : {sourceConn}");
Console.WriteLine($"Target : {targetConn}");
Console.WriteLine($"Mode   : {(dryRun ? "dry-run" : "apply")}");

var packMap = await LoadOneMgPackInfoAsync(sourceConn);
Console.WriteLine($"Loaded {packMap.Count:N0} OneMG pack_info rows.");

await using var target = new SqlConnection(targetConn);
await target.OpenAsync();

var medicines = await LoadTargetMedicinesAsync(target);
Console.WriteLine($"Scanning {medicines.Count:N0} medicines with OneMG-ID...");

var now = DateTime.UtcNow;
int matched = 0, updated = 0, parseFailed = 0, unchanged = 0;

foreach (var med in medicines)
{
    if (!packMap.TryGetValue(med.OneMgId, out var packInfo) || string.IsNullOrWhiteSpace(packInfo))
        continue;

    matched++;
    var packInfoChanged = !string.Equals(med.PackInfo, packInfo, StringComparison.Ordinal);
    var parsedOk = PackInfoParser.TryParse(packInfo, out var parsed);
    if (!parsedOk) parseFailed++;

    var fieldsChanged = parsedOk
        && (med.UnitsPerPack != parsed.UnitsPerPack
            || !string.Equals(med.UnitOfMeasure, parsed.UnitOfMeasure, StringComparison.OrdinalIgnoreCase)
            || med.DosageForm != (int)parsed.DosageForm);

    if (!packInfoChanged && !fieldsChanged)
    {
        unchanged++;
        continue;
    }

    if (!dryRun)
    {
        await using var upd = new SqlCommand("""
            UPDATE Medicines
            SET PackInfo = @PackInfo,
                UnitsPerPack = @Units,
                UnitOfMeasure = @Uom,
                DosageForm = @Form,
                ModifiedAtUtc = @Now
            WHERE Id = @Id
            """, target);
        upd.Parameters.AddWithValue("@Id", med.Id);
        upd.Parameters.AddWithValue("@PackInfo", packInfo);
        upd.Parameters.AddWithValue("@Units", parsedOk ? parsed.UnitsPerPack : med.UnitsPerPack);
        upd.Parameters.AddWithValue("@Uom", parsedOk ? parsed.UnitOfMeasure : med.UnitOfMeasure);
        upd.Parameters.AddWithValue("@Form", parsedOk ? (int)parsed.DosageForm : med.DosageForm);
        upd.Parameters.AddWithValue("@Now", now);
        await upd.ExecuteNonQueryAsync();
    }

    updated++;
    if (updated <= 5)
        Console.WriteLine($"  sample: {med.Name} -> PackInfo={packInfo}");
}

Console.WriteLine($"Matched {matched:N0}, updated {updated:N0}, unchanged {unchanged:N0}, parse failed {parseFailed:N0}.");
if (dryRun) Console.WriteLine("Dry-run only. Re-run without --dry-run to apply.");
return 0;

static async Task<Dictionary<string, string>> LoadOneMgPackInfoAsync(string connectionString)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    await using var conn = new SqlConnection(connectionString);
    await conn.OpenAsync();
    await using var cmd = new SqlCommand(
        "SELECT medicine_id, pack_info FROM dbo.OneMGData_3 WHERE medicine_id IS NOT NULL AND pack_info IS NOT NULL AND pack_info <> ''",
        conn) { CommandTimeout = 600 };
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var id = Convert.ToString(reader.GetValue(0))?.Trim();
        var pack = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(pack)) continue;
        map[id] = pack;
    }

    return map;
}

static async Task<List<TargetMedicine>> LoadTargetMedicinesAsync(SqlConnection target)
{
    var list = new List<TargetMedicine>();
    await using var cmd = new SqlCommand(
        "SELECT Id, Name, UnitsPerPack, UnitOfMeasure, DosageForm, PackInfo, Notes FROM Medicines WHERE IsDeleted = 0 AND Notes LIKE '%OneMG-ID:%'",
        target) { CommandTimeout = 600 };
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var notes = reader.IsDBNull(6) ? null : reader.GetString(6);
        var oneMgId = ParseOneMgId(notes);
        if (oneMgId is null) continue;

        list.Add(new TargetMedicine(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.IsDBNull(3) ? "Nos" : reader.GetString(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            oneMgId));
    }

    return list;
}

static string? ParseOneMgId(string? notes)
{
    if (string.IsNullOrWhiteSpace(notes)) return null;
    const string prefix = "OneMG-ID:";
    var idx = notes.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
    if (idx < 0) return null;
    var tail = notes[(idx + prefix.Length)..];
    var end = tail.IndexOfAny([' ', '|', '\r', '\n']);
    return (end > 0 ? tail[..end] : tail).Trim();
}

static void PrintHelp()
{
    Console.WriteLine("""
        Updates PharmaPOS Medicines.PackInfo, UnitsPerPack, UnitOfMeasure, and DosageForm
        from OneMGData_3.pack_info (matched by OneMG-ID in Medicines.Notes).

        Usage:
          dotnet run --project tools/PharmaPOS.PackInfoUpdate -- [options]

        Options:
          --source <conn>   OneMG SQL connection (default: localhost / OneMGData)
          --target <conn>   PharmaPOS SQL connection (default: LocalDB / PharmaPosDb)
          --dry-run         Preview without writing
          -h, --help        Show help
        """);
}

sealed record TargetMedicine(int Id, string Name, int UnitsPerPack, string UnitOfMeasure, int DosageForm, string? PackInfo, string OneMgId);
