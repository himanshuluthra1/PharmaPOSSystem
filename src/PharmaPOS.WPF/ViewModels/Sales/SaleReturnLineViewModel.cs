using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Sales;

/// <summary>Editable return line bound to the invoice items grid.</summary>
public sealed class SaleReturnLineViewModel : ObservableObject
{
    private bool _isSelected;
    private decimal _returnQuantity;
    private int _returnReasonId;
    private string? _reasonRemarks;
    private bool _sealIntact = true;
    private bool _packagingDamaged;
    private bool _expiryValid = true;
    private bool _isSaleable = true;
    private string? _scannedBatchNumber;
    private bool _batchMismatchApproved;

    public SaleReturnLineViewModel(SaleReturnLineDto source, IReadOnlyList<ReturnReasonDto> reasons)
    {
        SaleItemId = source.SaleItemId;
        MedicineId = source.MedicineId;
        MedicineName = source.MedicineName;
        GenericName = source.GenericName;
        Barcode = source.Barcode;
        MedicineBatchId = source.MedicineBatchId;
        BatchNumber = source.BatchNumber;
        ExpiryDate = source.ExpiryDate;
        Mrp = source.Mrp;
        UnitPrice = source.UnitPrice;
        DiscountPercent = source.DiscountPercent;
        DiscountAmount = source.DiscountAmount;
        GstPercent = source.GstPercent;
        SoldQuantity = source.SoldQuantity;
        AlreadyReturnedQuantity = source.AlreadyReturnedQuantity;
        AvailableReturnQuantity = source.AvailableReturnQuantity;
        ScheduleType = source.ScheduleType;
        PrescriptionRequired = source.PrescriptionRequired;
        IsRefrigerated = source.IsRefrigerated;
        ImagePath = source.ImagePath;
        SoldLineTotal = source.LineTotal;
        SoldTaxable = source.SoldQuantity > 0
            ? Math.Round(source.LineTotal / (1 + source.GstPercent / 100m), 2)
            : 0m;

        _returnReasonId = reasons.FirstOrDefault()?.Id ?? 0;
        RecalculateTotals();
    }

    public int SaleItemId { get; }
    public int MedicineId { get; }
    public string MedicineName { get; }
    public string? GenericName { get; }
    public string? Barcode { get; }
    public int? MedicineBatchId { get; }
    public string? BatchNumber { get; }
    public DateTime? ExpiryDate { get; }
    public decimal Mrp { get; }
    public decimal UnitPrice { get; }
    public decimal DiscountPercent { get; }
    public decimal DiscountAmount { get; }
    public decimal GstPercent { get; }
    public decimal SoldQuantity { get; }
    public decimal AlreadyReturnedQuantity { get; }
    public decimal AvailableReturnQuantity { get; }
    public bool PrescriptionRequired { get; }
    public bool IsRefrigerated { get; }
    public string? ImagePath { get; }
    public ScheduleDrugType ScheduleType { get; }

    public decimal SoldLineTotal { get; }
    public decimal SoldTaxable { get; }

    public string ExpiryLabel => ExpiryDate?.ToString("MM/yyyy") ?? "—";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!SetProperty(ref _isSelected, value)) return;
            if (value && ReturnQuantity <= 0 && AvailableReturnQuantity > 0)
                ReturnQuantity = AvailableReturnQuantity;
            if (!value)
                ReturnQuantity = 0;
        }
    }

    public decimal ReturnQuantity
    {
        get => _returnQuantity;
        set
        {
            if (!SetProperty(ref _returnQuantity, value)) return;
            RecalculateTotals();
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public int ReturnReasonId
    {
        get => _returnReasonId;
        set => SetProperty(ref _returnReasonId, value);
    }

    public string? ReasonRemarks
    {
        get => _reasonRemarks;
        set => SetProperty(ref _reasonRemarks, value);
    }

    public bool SealIntact
    {
        get => _sealIntact;
        set => SetProperty(ref _sealIntact, value);
    }

    public bool PackagingDamaged
    {
        get => _packagingDamaged;
        set => SetProperty(ref _packagingDamaged, value);
    }

    public bool ExpiryValid
    {
        get => _expiryValid;
        set => SetProperty(ref _expiryValid, value);
    }

    public bool IsSaleable
    {
        get => _isSaleable;
        set => SetProperty(ref _isSaleable, value);
    }

    public string? ScannedBatchNumber
    {
        get => _scannedBatchNumber;
        set
        {
            if (!SetProperty(ref _scannedBatchNumber, value)) return;
            OnPropertyChanged(nameof(BatchMismatch));
            OnPropertyChanged(nameof(ValidationMessage));
            OnPropertyChanged(nameof(HasValidationError));
        }
    }

    public bool BatchMismatchApproved
    {
        get => _batchMismatchApproved;
        set => SetProperty(ref _batchMismatchApproved, value);
    }

    public bool BatchMismatch =>
        !string.IsNullOrWhiteSpace(ScannedBatchNumber)
        && !string.Equals(ScannedBatchNumber, BatchNumber, StringComparison.OrdinalIgnoreCase);

    public decimal ProportionalLineTotal { get; private set; }
    public decimal ProportionalDiscount { get; private set; }
    public decimal ProportionalTax { get; private set; }

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationMessage);

    public string? ValidationMessage
    {
        get
        {
            if (ReturnQuantity <= 0) return null;
            if (ReturnQuantity > SoldQuantity)
                return $"Cannot exceed sold qty ({SoldQuantity:N0}).";
            if (ReturnQuantity > AvailableReturnQuantity)
                return $"Max returnable: {AvailableReturnQuantity:N0}.";
            if (BatchMismatch && !BatchMismatchApproved)
                return "Batch mismatch — manager approval required.";
            return null;
        }
    }

    public void RecalculateTotals()
    {
        if (ReturnQuantity <= 0 || SoldQuantity <= 0)
        {
            ProportionalLineTotal = 0;
            ProportionalDiscount = 0;
            ProportionalTax = 0;
        }
        else
        {
            var amounts = SaleReturnPricing.ComputeLineAmounts(
                SoldQuantity, ReturnQuantity, Mrp, UnitPrice,
                DiscountAmount, GstPercent, SoldLineTotal,
                SoldTaxable, SoldLineTotal - SoldTaxable);
            ProportionalLineTotal = amounts.LineTotal;
            ProportionalDiscount = amounts.DiscountAmount;
            ProportionalTax = amounts.TaxAmount;
        }

        OnPropertyChanged(nameof(ProportionalLineTotal));
        OnPropertyChanged(nameof(ProportionalDiscount));
        OnPropertyChanged(nameof(ProportionalTax));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(HasValidationError));
    }

    public CreateSaleReturnLineRequest ToRequest(ReturnReasonDto? reason) => new()
    {
        SaleItemId = SaleItemId,
        ReturnQuantity = ReturnQuantity,
        ReturnReasonId = ReturnReasonId,
        ReasonRemarks = reason?.RequiresRemarks == true ? ReasonRemarks : ReasonRemarks,
        SealIntact = SealIntact,
        PackagingDamaged = PackagingDamaged,
        ExpiryValid = ExpiryValid,
        IsSaleable = IsSaleable,
        ScannedBatchNumber = ScannedBatchNumber,
        BatchMismatchApproved = BatchMismatchApproved
    };
}
