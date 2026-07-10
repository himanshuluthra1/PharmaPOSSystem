namespace PharmaPOS.WPF.Services;

/// <summary>Opens the purchase search popup and returns the selected purchase, if any.</summary>
public interface IPurchaseSearchService
{
    Task<PharmaPOS.Application.Features.Purchases.PurchaseListItemDto?> PickPurchaseAsync();
}
