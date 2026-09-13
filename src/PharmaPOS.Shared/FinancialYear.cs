namespace PharmaPOS.Shared;

/// <summary>Indian financial year: 1 April → 31 March.</summary>
public sealed record FinancialYearInfo(
    int StartYear,
    DateTime Start,
    DateTime EndExclusive,
    bool IsCurrent)
{
    /// <summary>e.g. 2025-26</summary>
    public string Label => $"{StartYear}-{(StartYear + 1) % 100:D2}";

    /// <summary>e.g. FY 2025-26</summary>
    public string DisplayLabel => $"FY {Label}";
}

/// <summary>Helpers for Indian FY (1 Apr – 31 Mar).</summary>
public static class FinancialYearHelper
{
    public static int GetStartYear(DateTime localDate)
        => localDate.Month >= 4 ? localDate.Year : localDate.Year - 1;

    public static FinancialYearInfo FromLocalDate(DateTime localDate)
        => ForStartYear(GetStartYear(localDate), localDate);

    public static FinancialYearInfo ForStartYear(int startYear, DateTime? todayLocal = null)
    {
        var today = todayLocal ?? DateTime.Today;
        if (today.Kind == DateTimeKind.Unspecified)
            today = DateTime.SpecifyKind(today, DateTimeKind.Local);

        var start = new DateTime(startYear, 4, 1, 0, 0, 0, DateTimeKind.Local);
        var endExclusive = start.AddYears(1);
        var currentStart = GetStartYear(today);
        return new FinancialYearInfo(startYear, start, endExclusive, startYear == currentStart);
    }

    /// <summary>
    /// Resolves the view FY from a stored preference. Null or current year ⇒ current FY.
    /// </summary>
    public static FinancialYearInfo Resolve(int? viewStartYear, DateTime todayLocal)
    {
        var current = FromLocalDate(todayLocal);
        if (viewStartYear is null || viewStartYear == current.StartYear)
            return current;
        return ForStartYear(viewStartYear.Value, todayLocal);
    }

    public static IReadOnlyList<FinancialYearOption> ListOptions(DateTime todayLocal, int count = 8)
    {
        var currentStart = GetStartYear(todayLocal);
        var list = new List<FinancialYearOption>(count);
        for (var i = 0; i < count; i++)
        {
            var y = currentStart - i;
            var info = ForStartYear(y, todayLocal);
            list.Add(new FinancialYearOption(
                y,
                info.IsCurrent ? $"{info.Label} (Current)" : info.Label,
                info.IsCurrent));
        }
        return list;
    }
}

public sealed record FinancialYearOption(int StartYear, string Label, bool IsCurrent);
