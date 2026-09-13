using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.WPF.ViewModels.Settings;

namespace PharmaPOS.WPF.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void MedWinImportLog_TargetUpdated(object sender, DataTransferEventArgs e)
    {
        MedWinImportLogScroll?.Dispatcher.BeginInvoke(
            () => MedWinImportLogScroll.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static NewMedicineMappingRowViewModel? RowFromSender(object sender)
        => (sender as FrameworkElement)?.DataContext as NewMedicineMappingRowViewModel;

    private void NewMappingSuggestBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is null) return;

        if (e.Key is Key.Down or Key.Up)
        {
            if (!row.ShowSuggestions) return;
            row.MoveSuggestion(e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Tab)
        {
            if (row.ShowSuggestions)
            {
                row.ConfirmSuggestion();
                e.Handled = e.Key == Key.Enter;
            }
            return;
        }

        if (e.Key == Key.Escape && row.ShowSuggestions)
        {
            row.ClearSuggestions();
            e.Handled = true;
        }
    }

    private void NewMappingSuggestBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Keep popup usable: ignore focus moving into the suggestion list.
        if (e.NewFocus is DependencyObject next
            && sender is DependencyObject box
            && IsDescendantOfPopupList(box, next))
            return;

        var row = RowFromSender(sender);
        row?.ClearSuggestions();
    }

    private static bool IsDescendantOfPopupList(DependencyObject box, DependencyObject? candidate)
    {
        while (candidate is not null)
        {
            if (candidate is ListBox)
                return true;
            candidate = System.Windows.Media.VisualTreeHelper.GetParent(candidate);
        }
        return false;
    }

    private void NewMappingSuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;
        var row = list.DataContext as NewMedicineMappingRowViewModel;
        if (row is null) return;
        if (list.SelectedItem is NewMedicineMappingSuggestionDto suggestion)
            row.SelectSuggestion(suggestion);
    }

    private void NewMappingSuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var row = RowFromSender(sender);
        if (row is null) return;

        if (e.Key == Key.Enter)
        {
            row.ConfirmSuggestion();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            row.ClearSuggestions();
            e.Handled = true;
        }
    }
}
