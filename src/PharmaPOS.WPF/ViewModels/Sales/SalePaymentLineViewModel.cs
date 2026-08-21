using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Sales;

/// <summary>One tender row on a sale (cash, UPI, card, credit, …).</summary>
public sealed class SalePaymentLineViewModel : ObservableObject
{
    private PaymentMethod _method;
    private decimal _amount;
    private bool _suppress;

    public event Action? Changed;

    public SalePaymentLineViewModel(PaymentMethod method, decimal amount)
    {
        _method = method;
        _amount = amount;
    }

    public PaymentMethod Method
    {
        get => _method;
        set
        {
            if (SetProperty(ref _method, value))
                Changed?.Invoke();
        }
    }

    public decimal Amount
    {
        get => _amount;
        set
        {
            var rounded = Math.Round(value, 2);
            if (SetProperty(ref _amount, rounded) && !_suppress)
                Changed?.Invoke();
        }
    }

    public void SetAmountSilent(decimal amount)
    {
        _suppress = true;
        Amount = amount;
        _suppress = false;
    }
}
