using System.Windows;
using PharmaPOS.WPF.ViewModels.Accounting;

namespace PharmaPOS.WPF.Views;

public partial class PayBillWindow : Window
{
    public PayBillWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireViewModel();
    }

    private PayBillViewModel? ViewModel => DataContext as PayBillViewModel;

    private void WireViewModel()
    {
        if (ViewModel is null) return;
        ViewModel.Paid -= OnPaid;
        ViewModel.Paid += OnPaid;
    }

    private void OnPaid()
    {
        DialogResult = true;
        Close();
    }
}
