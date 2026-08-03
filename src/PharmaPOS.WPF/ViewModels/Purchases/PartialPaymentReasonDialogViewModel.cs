using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Purchases;

public sealed class PartialPaymentReasonChoice
{
    public PartialPaymentReasonChoice(PurchasePartialPaymentReason reason, string label)
    {
        Reason = reason;
        Label = label;
    }

    public PurchasePartialPaymentReason Reason { get; }
    public string Label { get; }
}

public sealed class PartialPaymentReasonResult
{
    public required PurchasePartialPaymentReason Reason { get; init; }
    public string? Notes { get; init; }
    public int? LinkedPurchaseReturnId { get; init; }
}

public sealed class PartialPaymentReasonDialogViewModel : ObservableObject
{
    private PartialPaymentReasonChoice _selectedReason;
    private string _notes = string.Empty;
    private OpenPurchaseReturnCreditDto? _selectedReturn;
    private bool _dialogResult;

    public PartialPaymentReasonDialogViewModel(
        decimal balanceDue,
        IReadOnlyList<OpenPurchaseReturnCreditDto> openReturns)
    {
        BalanceDue = balanceDue;
        ReasonChoices =
        [
            new(PurchasePartialPaymentReason.CreditPayLater, "Credit / pay later"),
            new(PurchasePartialPaymentReason.AgainstPurchaseReturn, "Against purchase return"),
            new(PurchasePartialPaymentReason.Other, "Other")
        ];
        _selectedReason = ReasonChoices[0];

        foreach (var r in openReturns)
            OpenReturns.Add(r);
        _selectedReturn = OpenReturns.FirstOrDefault();

        OkCommand = new RelayCommand(_ => Confirm(), _ => CanConfirm());
        CancelCommand = new RelayCommand(_ => { DialogAccepted = false; RequestClose?.Invoke(); });
    }

    public decimal BalanceDue { get; }
    public IReadOnlyList<PartialPaymentReasonChoice> ReasonChoices { get; }
    public ObservableCollection<OpenPurchaseReturnCreditDto> OpenReturns { get; } = new();

    public event Action? RequestClose;

    public bool DialogAccepted
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public PartialPaymentReasonChoice SelectedReason
    {
        get => _selectedReason;
        set
        {
            if (!SetProperty(ref _selectedReason, value)) return;
            OnPropertyChanged(nameof(ShowReturnPicker));
            OnPropertyChanged(nameof(ShowNotesHint));
            OnPropertyChanged(nameof(HasNoOpenReturns));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string Notes
    {
        get => _notes;
        set
        {
            if (SetProperty(ref _notes, value ?? string.Empty))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public OpenPurchaseReturnCreditDto? SelectedReturn
    {
        get => _selectedReturn;
        set
        {
            if (SetProperty(ref _selectedReturn, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool ShowReturnPicker => SelectedReason.Reason == PurchasePartialPaymentReason.AgainstPurchaseReturn;
    public bool ShowNotesHint => SelectedReason.Reason == PurchasePartialPaymentReason.Other;
    public bool HasNoOpenReturns => ShowReturnPicker && OpenReturns.Count == 0;

    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    public PartialPaymentReasonResult? BuildResult()
    {
        if (!DialogAccepted) return null;
        return new PartialPaymentReasonResult
        {
            Reason = SelectedReason.Reason,
            Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim(),
            LinkedPurchaseReturnId = ShowReturnPicker ? SelectedReturn?.PurchaseReturnId : null
        };
    }

    private bool CanConfirm()
    {
        if (SelectedReason.Reason == PurchasePartialPaymentReason.Other
            && string.IsNullOrWhiteSpace(Notes))
            return false;
        if (SelectedReason.Reason == PurchasePartialPaymentReason.AgainstPurchaseReturn
            && SelectedReturn is null)
            return false;
        return true;
    }

    private void Confirm()
    {
        if (!CanConfirm()) return;
        DialogAccepted = true;
        RequestClose?.Invoke();
    }
}
