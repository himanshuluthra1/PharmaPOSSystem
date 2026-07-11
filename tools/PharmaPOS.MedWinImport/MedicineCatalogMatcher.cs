using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

internal sealed record CatalogMedicine(int Id, string Name, string? GenericName, string? Barcode, string? Notes);

internal sealed record MedicineMatchResult(int MedicineId, string MatchMethod, string NormalizedKey, CatalogMedicine Matched);

/// <summary>Matches MedWin medicines to the existing OneMG catalogue in PharmaPOS.</summary>
internal sealed class MedicineCatalogMatcher
{
    private readonly Dictionary<string, List<CatalogMedicine>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _byBarcode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, CatalogMedicine> _byId = new();

    public static async Task<MedicineCatalogMatcher> LoadAsync(SqlConnection target)
    {
        var matcher = new MedicineCatalogMatcher();
        await using var cmd = new SqlCommand("""
            SELECT Id, Name, GenericName, Barcode, Notes
            FROM Medicines
            WHERE IsDeleted = 0
            """, target);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var med = new CatalogMedicine(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4));

            // Skip MedWin-only duplicates so they do not steal barcode/name matches from OneMG catalogue.
            if (IsMedWinOnlyDuplicate(med.Notes)) continue;

            matcher.AddToIndex(med.Name, med);
            if (!string.IsNullOrWhiteSpace(med.GenericName))
                matcher.AddToIndex(med.GenericName, med);
            if (!string.IsNullOrWhiteSpace(med.Barcode))
                matcher._byBarcode[med.Barcode.Trim()] = med.Id;
            matcher._byId[med.Id] = med;
        }

        return matcher;
    }

    private void AddToIndex(string name, CatalogMedicine med)
    {
        var key = ImportHelpers.NormalizeForMatch(name);
        if (key.Length == 0) return;
        if (!_byName.TryGetValue(key, out var list))
        {
            list = new List<CatalogMedicine>();
            _byName[key] = list;
        }
        if (!list.Any(x => x.Id == med.Id))
            list.Add(med);
    }

    public int? TryMatch(string? medName, string? medName1, string? genericName, string? barcode)
        => TryMatchDetailed(medName, medName1, genericName, barcode)?.MedicineId;

    public MedicineMatchResult? TryMatchDetailed(string? medName, string? medName1, string? genericName, string? barcode)
    {
        if (!string.IsNullOrWhiteSpace(barcode))
        {
            var code = barcode.Trim();
            if (_byBarcode.TryGetValue(code, out var byCode) && _byId.TryGetValue(byCode, out var byBarcodeMed))
                return new MedicineMatchResult(byCode, "Barcode", code, byBarcodeMed);
        }

        foreach (var (candidate, method) in new (string? Value, string Method)[]
        {
            (medName1, "medname1"),
            (medName, "medname"),
            (genericName, "generic")
        })
        {
            var key = ImportHelpers.NormalizeForMatch(candidate);
            if (key.Length == 0) continue;
            if (!_byName.TryGetValue(key, out var matches) || matches.Count == 0) continue;

            var best = matches
                .OrderBy(m => ImportHelpers.ParseMedWinMedicineId(m.Notes).HasValue ? 1 : 0)
                .ThenBy(m => m.Id)
                .First();
            return new MedicineMatchResult(best.Id, method, key, best);
        }

        return null;
    }

    public string? GetNotesFor(int medicineId)
        => _byId.TryGetValue(medicineId, out var med) ? med.Notes : null;

    public static string AppendMedWinNote(string? existingNotes, int medWinId)
    {
        var tag = ImportHelpers.MedWinMedicineNote(medWinId);
        if (string.IsNullOrWhiteSpace(existingNotes)) return tag;
        if (existingNotes.Contains(tag, StringComparison.OrdinalIgnoreCase)) return existingNotes;
        return $"{existingNotes.Trim()} | {tag}";
    }

    private static bool IsMedWinOnlyDuplicate(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return false;
        var trimmed = notes.Trim();
        return trimmed.StartsWith("MedWinId:", StringComparison.OrdinalIgnoreCase)
               && !trimmed.Contains('|', StringComparison.Ordinal)
               && !trimmed.Contains("OneMG", StringComparison.OrdinalIgnoreCase);
    }
}
