using System.Windows;
using PharmaPOS.Application.Features.Reports;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

public partial class ScheduleRegisterPreviewWindow : Window
{
    private readonly IInvoicePrintService _printService;
    private readonly ScheduleRegisterReportDto _report;

    public ScheduleRegisterPreviewWindow(IInvoicePrintService printService, ScheduleRegisterReportDto report)
    {
        InitializeComponent();
        _printService = printService;
        _report = report;
        Title = $"{report.FilterLabel} Register";
        Viewer.Document = printService.BuildScheduleRegisterDocument(report);
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e) =>
        _printService.PrintScheduleRegister(_report);

    private void PdfButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _printService.ExportScheduleRegisterPdf(_report);
        MessageBox.Show($"PDF saved to:\n{path}", "Schedule register", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
