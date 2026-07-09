using PharmaPOS.Application.Features.Sales;

namespace PharmaPOS.WPF.Services;

/// <summary>Opens the bill search popup and returns the selected bill, if any.</summary>
public interface IBillSearchService
{
    Task<SaleListItemDto?> PickBillAsync();
}
