using System.Windows;
using PharmaPOS.WPF.ViewModels;

namespace PharmaPOS.WPF.Views;

public partial class CounterDayCloseWindow : Window
{
    public CounterDayCloseWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => WireViewModel();
    }

    private CounterDayCloseViewModel? ViewModel => DataContext as CounterDayCloseViewModel;

    private void WireViewModel()
    {
        if (ViewModel is null) return;
        ViewModel.ClosedSuccessfully -= OnClosed;
        ViewModel.ClosedSuccessfully += OnClosed;
    }

    private void OnClosed()
    {
        DialogResult = true;
        Close();
    }
}
