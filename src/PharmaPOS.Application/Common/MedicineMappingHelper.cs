using System.Text.RegularExpressions;
using PharmaPOS.Application.Features.Masters;

namespace PharmaPOS.Application.Common;

/// <summary>Helpers for manual MedWin ↔ OneMG mapping UI.</summary>
public static class MedicineMappingHelper
{
    private const int MinUniqueAutoMapScore = 400;

    private static readonly Regex StrengthSuffixRegex = new(
        @"^\d+(\.\d+)?(MG|ML|GM|MCG|G)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex StrengthInNameRegex = new(
        @"(\d+(?:\.\d+)?)\s*(MG|ML|GM|MCG|G)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Brand prefix from the first word of a medicine name (strips dose suffixes like -20MG),
    /// normalized for <see cref="Domain.Entities.Masters.Medicine.NameSearchKey"/> prefix matching.
    /// </summary>
    public static string? GetFirstWordPrefix(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var trimmed = name.Trim();
        var end = trimmed.IndexOfAny([' ', '\t']);
        var word = (end < 0 ? trimmed : trimmed[..end]).Trim();
        if (word.Length == 0) return null;

        word = StripStrengthSuffix(word);
        var normalized = SearchQueryExtensions.NormalizeTerm(word).ToUpperInvariant();
        return normalized.Length > 0 ? normalized : null;
    }

    private static string StripStrengthSuffix(string word)
    {
        var dash = word.IndexOf('-');
        if (dash <= 0) return word;

        var after = word[(dash + 1)..];
        return StrengthSuffixRegex.IsMatch(after) ? word[..dash] : word;
    }

    /// <summary>Strength token from a medicine name, e.g. 50MG.</summary>
    public static string? ExtractStrengthKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var match = StrengthInNameRegex.Match(name);
        if (!match.Success) return null;

        return $"{match.Groups[1].Value}{match.Groups[2].Value}".ToUpperInvariant();
    }

    /// <summary>
    /// Picks the best OneMG row for a MedWin medicine using brand prefix, strength,
    /// name, and salt matching. Returns null when no confident unique match exists.
    /// </summary>
    public static MedicineMappingListItemDto? PickBestOneMgMatch(
        string medWinName,
        string? medWinGenericName,
        IReadOnlyList<MedicineMappingListItemDto> candidates)
    {
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return PassesStrengthCheck(medWinName, candidates[0]) ? candidates[0] : null;

        var scored = candidates
            .Select(c => new { Candidate = c, Score = ScoreOneMgMatch(medWinName, medWinGenericName, c) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (scored.Count == 0) return null;

        var bestScore = scored[0].Score;
        if (bestScore < MinUniqueAutoMapScore) return null;

        var top = scored.Where(x => x.Score == bestScore).ToList();
        return top.Count == 1 ? top[0].Candidate : null;
    }

    private static bool PassesStrengthCheck(string medWinName, MedicineMappingListItemDto candidate)
    {
        var medWinStrength = ExtractStrengthKey(medWinName);
        var oneMgStrength = ExtractStrengthKey(candidate.Name);
        if (medWinStrength is null || oneMgStrength is null) return true;
        return medWinStrength.Equals(oneMgStrength, StringComparison.OrdinalIgnoreCase);
    }

    private static int ScoreOneMgMatch(
        string medWinName,
        string? medWinGenericName,
        MedicineMappingListItemDto candidate)
    {
        var medWinKey = SearchQueryExtensions.NormalizeTerm(medWinName).ToUpperInvariant();
        var oneMgKey = SearchQueryExtensions.NormalizeTerm(candidate.Name).ToUpperInvariant();
        if (medWinKey.Length == 0 || oneMgKey.Length == 0) return 0;

        var medWinStrength = ExtractStrengthKey(medWinName);
        var oneMgStrength = ExtractStrengthKey(candidate.Name);
        if (medWinStrength is not null && oneMgStrength is not null
            && !medWinStrength.Equals(oneMgStrength, StringComparison.OrdinalIgnoreCase))
            return 0;

        var score = 100;

        var brandPrefix = GetFirstWordPrefix(medWinName);
        if (brandPrefix is not null && oneMgKey.StartsWith(brandPrefix, StringComparison.Ordinal))
            score += 100;

        if (medWinKey == oneMgKey)
            score += 700;
        else if (oneMgKey.StartsWith(medWinKey, StringComparison.Ordinal)
                 || medWinKey.StartsWith(oneMgKey, StringComparison.Ordinal))
            score += 350;

        if (!string.IsNullOrWhiteSpace(medWinGenericName) && !string.IsNullOrWhiteSpace(candidate.GenericName))
        {
            var medWinGeneric = SearchQueryExtensions.NormalizeTerm(medWinGenericName).ToUpperInvariant();
            var oneMgGeneric = SearchQueryExtensions.NormalizeTerm(candidate.GenericName).ToUpperInvariant();
            if (medWinGeneric.Length > 0 && medWinGeneric == oneMgGeneric)
                score += 250;
        }

        if (medWinStrength is not null && medWinStrength.Equals(oneMgStrength, StringComparison.OrdinalIgnoreCase))
            score += 300;

        return score;
    }
}
