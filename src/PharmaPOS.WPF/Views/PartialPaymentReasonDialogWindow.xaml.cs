using System.Windows;
using PharmaPOS.WPF.ViewModels.Purchases;

namespace PharmaPOS.WPF.Views;

public partial class PartialPaymentReasonDialogWindow : Window
{
    public PartialPaymentReasonDialogWindow(PartialPaymentReasonDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += () =>
        {
            DialogResult = viewModel.DialogAccepted;
            Close();
        };
    }
}
