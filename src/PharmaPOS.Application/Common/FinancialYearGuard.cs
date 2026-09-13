using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Common;

/// <summary>Blocks mutating transactions when viewing a prior financial year.</summary>
public static class FinancialYearGuard
{
    public static string ViewOnlyMessage(IFinancialYearContext fy)
        => $"Financial year {fy.Active.Label} is view-only. Switch to the current year in Settings → Preferences to make changes.";

    public static Result EnsureEditable(IFinancialYearContext fy)
        => fy.CanEditTransactions
            ? Result.Success()
            : Result.Failure(ViewOnlyMessage(fy));

    public static Result<T> FailIfReadOnly<T>(IFinancialYearContext fy)
        => Result.Failure<T>(ViewOnlyMessage(fy));
}
