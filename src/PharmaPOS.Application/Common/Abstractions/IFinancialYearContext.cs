using PharmaPOS.Shared;

namespace PharmaPOS.Application.Common.Abstractions;

/// <summary>
/// Active financial-year view for the shop (from Preferences).
/// Current FY is editable; prior FY is view-only.
/// </summary>
public interface IFinancialYearContext
{
    FinancialYearInfo Active { get; }

    bool CanEditTransactions { get; }

    event Action? Changed;

    Task RefreshAsync(CancellationToken ct = default);
}
