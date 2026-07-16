using PharmaPOS.Application.Features.SaleReturns;

namespace PharmaPOS.WPF.Services;

/// <summary>Opens an in-place sale return dialog for a loaded invoice.</summary>
public interface ISaleReturnDialogService
{
    /// <summary>
    /// Shows return UI for <paramref name="saleId"/>.
    /// <see cref="SaleReturnDialogResult.DialogShown"/> is false when the invoice could not be loaded.
    /// </summary>
    Task<SaleReturnDialogResult> ShowForSaleAsync(int saleId);
}

public sealed record SaleReturnDialogResult(bool DialogShown, bool ReturnPosted, SaleReturnReceiptDto? Receipt);
