using System.Windows;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels;

namespace PharmaPOS.WPF.Views;

public partial class CounterSelectWindow : Window
{
    public CounterSelectWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += (_, _) => WireViewModel();
    }

    private CounterSelectViewModel? ViewModel => DataContext as CounterSelectViewModel;

    private void WireViewModel()
    {
        if (ViewModel is null) return;
        ViewModel.CounterSelected -= OnCounterSelected;
        ViewModel.CounterSelected += OnCounterSelected;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        await ViewModel.InitializeAsync();
        if (ViewModel.HasResumedSession)
        {
            DialogResult = true;
            Close();
        }
    }

    private void OnCounterSelected()
    {
        DialogResult = true;
        Close();
    }

    private void CountersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel?.OpenCommand.CanExecute(null) == true)
            ViewModel.OpenCommand.Execute(null);
    }
}
