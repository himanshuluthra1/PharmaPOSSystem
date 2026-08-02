using System.Windows;
using System.Windows.Controls;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.Views;

public partial class MedicineFromUrlWindow : Window
{
    private readonly IPharmacyMedicineImportService _import;
    private readonly IMastersService _masters;
    private PharmacyMedicineImportResult? _downloaded;

    public MedicineLookupDto? CreatedMedicine { get; private set; }

    public MedicineFromUrlWindow(
        IPharmacyMedicineImportService import,
        IMastersService masters,
        string? suggestedName = null)
    {
        InitializeComponent();
        _import = import;
        _masters = masters;

        SourceCombo.ItemsSource = _import.Sources.Select(s => s.DisplayName).ToList();
        SourceCombo.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(suggestedName))
            NameBox.Text = suggestedName.Trim();

        StatusText.Text = "Select the website, paste the medicine product URL, then Download.";
    }

    private PharmacyCatalogSource SelectedSource
    {
        get
        {
            var name = SourceCombo.SelectedItem as string ?? "1MG";
            return _import.Sources.First(s => s.DisplayName == name).Source;
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        StatusText.Text = "Downloading medicine details…";
        try
        {
            _downloaded = await _import.DownloadAsync(SelectedSource, UrlBox.Text);
            NameBox.Text = _downloaded.Name;
            GenericBox.Text = _downloaded.GenericName ?? string.Empty;
            BrandBox.Text = _downloaded.Brand ?? string.Empty;
            MrpBox.Text = _downloaded.Mrp > 0 ? _downloaded.Mrp.ToString("0.##") : string.Empty;
            GstBox.Text = _downloaded.GstPercent > 0 ? _downloaded.GstPercent.ToString("0.##") : "12";
            StatusText.Text = $"Downloaded from {_downloaded.SourceLabel}. Review fields, then Save to medicine master.";
            SaveButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _downloaded = null;
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, "Download medicine", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            DownloadButton.IsEnabled = true;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Medicine name is required.", "Save medicine", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        decimal.TryParse(MrpBox.Text.Trim(), out var mrp);
        if (!decimal.TryParse(GstBox.Text.Trim(), out var gst) || gst < 0)
            gst = 12m;

        SaveButton.IsEnabled = false;
        StatusText.Text = "Saving to medicine master…";
        try
        {
            var dto = new MedicineDetailDto
            {
                Id = 0,
                Name = name,
                GenericName = NullIfBlank(GenericBox.Text),
                Brand = NullIfBlank(BrandBox.Text),
                Mrp = mrp,
                SellingPrice = mrp,
                PurchasePrice = 0,
                GstPercent = gst,
                Status = EntityStatus.Active
            };

            var result = await _masters.SaveMedicineAsync(dto);
            if (result.IsFailure)
            {
                MessageBox.Show(result.Error ?? "Could not save medicine.", "Save medicine",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var saved = await _masters.GetMedicineAsync(result.Value);
            CreatedMedicine = new MedicineLookupDto(
                result.Value,
                saved?.Name ?? name,
                saved?.GenericName,
                saved?.Barcode,
                saved?.GstPercent ?? gst,
                saved?.DefaultDiscountPercent ?? 0,
                saved?.PrescriptionRequired ?? false,
                0m);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Save medicine", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
