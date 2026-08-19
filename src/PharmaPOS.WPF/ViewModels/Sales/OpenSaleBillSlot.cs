using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Sales;

/// <summary>One open customer bill slot on the multi-bill sales desk.</summary>
public sealed class OpenSaleBillSlot : ObservableObject
{
    private string _title;
    private int _itemCount;
    private decimal _amount;
    private bool _isActive;

    public OpenSaleBillSlot(int displayNumber)
    {
        DisplayNumber = displayNumber;
        _title = $"Bill {displayNumber}";
    }

    /// <summary>1-based number shown on the chip and used with Ctrl+N.</summary>
    public int DisplayNumber { get; private set; }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public int ItemCount
    {
        get => _itemCount;
        private set => SetProperty(ref _itemCount, value);
    }

    public decimal Amount
    {
        get => _amount;
        private set => SetProperty(ref _amount, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public string ShortcutHint => $"Ctrl+{DisplayNumber}";

    public string ChipLabel =>
        ItemCount > 0
            ? $"{DisplayNumber}. {Title} ({ItemCount})"
            : $"{DisplayNumber}. {Title}";

    internal ParkedSaleBill? Parked { get; set; }

    public void SetDisplayNumber(int number)
    {
        DisplayNumber = number;
        OnPropertyChanged(nameof(DisplayNumber));
        OnPropertyChanged(nameof(ShortcutHint));
        OnPropertyChanged(nameof(ChipLabel));
    }

    public void UpdateSummary(string customerName, int itemCount, decimal amount)
    {
        var label = string.IsNullOrWhiteSpace(customerName) ? $"Bill {DisplayNumber}" : customerName.Trim();
        if (label.Length > 18)
            label = label[..16] + "…";
        Title = label;
        ItemCount = itemCount;
        Amount = amount;
        OnPropertyChanged(nameof(ChipLabel));
    }
}

/// <summary>Frozen cart line used while a bill is parked in another slot.</summary>
internal sealed class ParkedCartLine
{
    public int MedicineId { get; init; }
    public int BatchId { get; init; }
    public string MedicineName { get; init; } = string.Empty;
    public string BatchNumber { get; init; } = string.Empty;
    public DateTime? ExpiryDate { get; init; }
    public decimal Mrp { get; init; }
    public decimal GstPercent { get; init; }
    public decimal AvailableStock { get; init; }
    public decimal Quantity { get; init; }
    public decimal UnitPrice { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal OriginalQuantity { get; init; }
    public bool IsReturnLine { get; init; }
    public string? ReturnNumber { get; init; }
}

internal sealed class ParkedSaleBill
{
    public IReadOnlyList<ParkedCartLine> Lines { get; init; } = Array.Empty<ParkedCartLine>();
    public string CustomerName { get; init; } = string.Empty;
    public string? CustomerMobile { get; init; }
    public string? CustomerAddress { get; init; }
    public string? DoctorName { get; init; }
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Cash;
    public int? EditingSaleId { get; init; }
    public bool IsInvoiceLocked { get; init; }
    public string? LockedBy { get; init; }
    public string? StatusMessage { get; init; }
}
