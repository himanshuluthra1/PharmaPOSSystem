namespace PharmaPOS.Application.Common;

/// <summary>Reads structured values stored in <see cref="Domain.Entities.Masters.Medicine.Notes"/>.</summary>
public static class MedicineNotesHelper
{
    private const string OneMgPrefix = "OneMG-ID:";
    private const string MedWinPrefix = "MedWinId:";

    /// <summary>
    /// Returns the raw OneMG pack_info stored as the first Notes segment
    /// (e.g. "strip of 10 tablets | OneMG-ID:123 | ...").
    /// </summary>
    public static string? ExtractPackInfo(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;

        var first = notes.Split(" | ", 2, StringSplitOptions.TrimEntries)[0];
        if (first.StartsWith(MedWinPrefix, StringComparison.OrdinalIgnoreCase)
            || first.StartsWith(OneMgPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        return first;
    }

    public static string? ParseOneMgId(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;
        var idx = notes.IndexOf(OneMgPrefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var tail = notes[(idx + OneMgPrefix.Length)..];
        var end = tail.IndexOfAny([' ', '|', '\r', '\n']);
        return (end > 0 ? tail[..end] : tail).Trim();
    }

    public static int? ParseMedWinId(string? notes)
    {
        foreach (var id in ParseAllMedWinIds(notes))
            return id;
        return null;
    }

    public static IEnumerable<int> ParseAllMedWinIds(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) yield break;

        var idx = 0;
        while (idx < notes.Length)
        {
            var found = notes.IndexOf(MedWinPrefix, idx, StringComparison.OrdinalIgnoreCase);
            if (found < 0) yield break;

            var tail = notes[(found + MedWinPrefix.Length)..];
            var digitLen = 0;
            while (digitLen < tail.Length && char.IsDigit(tail[digitLen]))
                digitLen++;

            if (digitLen > 0 && int.TryParse(tail[..digitLen], out var id))
                yield return id;

            idx = found + MedWinPrefix.Length + Math.Max(digitLen, 1);
        }
    }

    /// <summary>True when <paramref name="notes"/> contains an exact MedWinId segment (not a longer id prefix).</summary>
    public static bool NotesContainsMedWinId(string? notes, int medWinId)
    {
        var tag = MedWinNote(medWinId);
        if (string.IsNullOrWhiteSpace(notes)) return false;
        if (notes.Equals(tag, StringComparison.OrdinalIgnoreCase)) return true;

        foreach (var segment in notes.Split(" | ", StringSplitOptions.TrimEntries))
        {
            if (segment.Equals(tag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>MedWin-only orphan whose id is not already linked on a catalogue row.</summary>
    public static bool IsPendingMedWinMap(string? notes, IReadOnlySet<int> linkedMedWinIds)
    {
        if (!IsMedWinOnlyOrphan(notes)) return false;
        var id = ParseMedWinId(notes);
        return id.HasValue && !linkedMedWinIds.Contains(id.Value);
    }

    public static bool HasOneMgLink(string? notes)
        => ParseOneMgId(notes) is not null;

    public static bool HasMedWinLink(string? notes)
        => ParseMedWinId(notes) is not null;

    /// <summary>MedWin-only row created when auto-match failed (no OneMG link).</summary>
    public static bool IsMedWinOnlyOrphan(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return false;
        var trimmed = notes.Trim();
        return trimmed.StartsWith(MedWinPrefix, StringComparison.OrdinalIgnoreCase)
               && !trimmed.Contains('|')
               && !trimmed.Contains("OneMG", StringComparison.OrdinalIgnoreCase);
    }

    public static string MedWinNote(int medWinId) => $"{MedWinPrefix}{medWinId}";

    public static string AppendMedWinNote(string? existingNotes, int medWinId)
    {
        var tag = MedWinNote(medWinId);
        if (string.IsNullOrWhiteSpace(existingNotes)) return tag;
        if (existingNotes.Contains(tag, StringComparison.OrdinalIgnoreCase)) return existingNotes;
        return $"{existingNotes.Trim()} | {tag}";
    }
}
