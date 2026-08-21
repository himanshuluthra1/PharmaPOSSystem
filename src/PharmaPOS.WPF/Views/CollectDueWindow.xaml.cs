using System.Windows;
using PharmaPOS.Application.Features.Accounting;
using PharmaPOS.WPF.ViewModels.Accounting;

namespace PharmaPOS.WPF.Views;

public partial class CollectDueWindow : Window
{
    public CollectDueWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireViewModel();
    }

    public CustomerCollectionReceiptDto? ResultReceipt { get; private set; }

    private CollectDueViewModel? ViewModel => DataContext as CollectDueViewModel;

    private void WireViewModel()
    {
        if (ViewModel is null) return;
        ViewModel.Collected -= OnCollected;
        ViewModel.Collected += OnCollected;
    }

    private void OnCollected(CustomerCollectionReceiptDto receipt)
    {
        ResultReceipt = receipt;
        DialogResult = true;
        Close();
    }
}
