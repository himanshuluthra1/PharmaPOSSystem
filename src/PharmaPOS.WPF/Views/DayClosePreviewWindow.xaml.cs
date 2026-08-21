using System.Windows;
using PharmaPOS.Application.Features.Counters;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

public partial class DayClosePreviewWindow : Window
{
    private readonly IInvoicePrintService _printService;
    private readonly CounterDayCloseDto _report;

    public DayClosePreviewWindow(IInvoicePrintService printService, CounterDayCloseDto report)
    {
        InitializeComponent();
        _printService = printService;
        _report = report;
        Viewer.Document = printService.BuildDayCloseDocument(report);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e) => _printService.PrintDayClose(_report);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
