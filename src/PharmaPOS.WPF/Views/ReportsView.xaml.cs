using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PharmaPOS.WPF.ViewModels.Reports;

namespace PharmaPOS.WPF.Views;

public partial class ReportsView : UserControl
{
    private ReportsViewModel? _subscribedVm;

    public ReportsView()
    {
        InitializeComponent();
    }

    private ReportsViewModel? ViewModel => DataContext as ReportsViewModel;

    private void ReportsView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedVm is not null)
            _subscribedVm.ColumnsChanged -= RebuildColumns;

        _subscribedVm = e.NewValue as ReportsViewModel;
        if (_subscribedVm is not null)
        {
            _subscribedVm.ColumnsChanged += RebuildColumns;
            RebuildColumns();
        }
    }

    private void ReportsView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_subscribedVm is not null)
            _subscribedVm.ColumnsChanged -= RebuildColumns;
        _subscribedVm = null;
    }

    private void RebuildColumns()
    {
        ResultsGrid.Columns.Clear();
        var vm = ViewModel;
        if (vm is null) return;

        foreach (var col in vm.Columns)
        {
            var binding = new Binding($"Values[{col.Key}]")
            {
                Mode = BindingMode.OneWay
            };
            if (!string.IsNullOrWhiteSpace(col.Format))
                binding.StringFormat = col.Format;

            ResultsGrid.Columns.Add(new DataGridTextColumn
            {
                Header = col.Header,
                Binding = binding,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                MinWidth = 80
            });
        }
    }

    private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: ReportRowViewModel row })
            ViewModel?.OpenRowCommand.Execute(row);
    }

    private void ResultsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F7)
        {
            if (ViewModel?.AddToShortageBookCommand.CanExecute(null) == true)
                ViewModel.AddToShortageBookCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter) return;
        if (sender is DataGrid { SelectedItem: ReportRowViewModel row })
        {
            ViewModel?.OpenRowCommand.Execute(row);
            e.Handled = true;
        }
    }
}
