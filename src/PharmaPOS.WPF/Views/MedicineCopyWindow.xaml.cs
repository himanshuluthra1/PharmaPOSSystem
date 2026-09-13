using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.WPF.Views;

public partial class MedicineCopyWindow : Window, INotifyPropertyChanged
{
    private readonly IMastersService _masters;
    private MedicineDetailDto? _sourceTemplate;
    private string _sourceSearchText = string.Empty;
    private CancellationTokenSource? _searchCts;

    public MedicineLookupDto? CreatedMedicine { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SourceSearchText
    {
        get => _sourceSearchText;
        set
        {
            if (_sourceSearchText == value) return;
            _sourceSearchText = value;
            OnPropertyChanged();
            _ = SearchSourcesAsync(value);
        }
    }

    public MedicineCopyWindow(IMastersService masters, int? preselectMedicineId = null, string? suggestedSearch = null)
    {
        InitializeComponent();
        DataContext = this;
        _masters = masters;

        StatusText.Text = "Search and load a medicine to copy, edit the fields, then Save.";

        Loaded += async (_, _) =>
        {
            SourceSearchBox.Focus();
            if (preselectMedicineId is > 0)
            {
                await LoadMedicineAsync(preselectMedicineId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(suggestedSearch))
            {
                SourceSearchText = suggestedSearch.Trim();
            }
        };
    }

    private async Task SearchSourcesAsync(string term)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        SourceList.ItemsSource = null;
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
            return;

        try
        {
            await Task.Delay(250, token);
            var rows = await _masters.SearchMedicinesAsync(term.Trim(), token);
            if (token.IsCancellationRequested) return;

            SourceList.ItemsSource = rows
                .Select(r => new SourceRow(r.Id, r.Name, r.GenericName, $"{r.Name}  ·  {r.GenericName ?? "—"}  ·  MRP {r.Mrp:0.##}"))
                .ToList();
            if (SourceList.Items.Count > 0)
                SourceList.SelectedIndex = 0;
        }
        catch (OperationCanceledException)
        {
            // superseded search
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
        => await LoadSelectedAsync();

    private async void SourceList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => await LoadSelectedAsync();

    private async Task LoadSelectedAsync()
    {
        if (SourceList.SelectedItem is not SourceRow row)
        {
            MessageBox.Show("Select a medicine in the list to copy.", "Copy medicine",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await LoadMedicineAsync(row.Id);
    }

    private async Task LoadMedicineAsync(int medicineId)
    {
        try
        {
            StatusText.Text = "Loading medicine…";
            var detail = await _masters.GetMedicineAsync(medicineId);
            if (detail is null)
            {
                StatusText.Text = "Medicine not found.";
                SaveButton.IsEnabled = false;
                return;
            }

            _sourceTemplate = detail;
            NameBox.Text = detail.Name;
            GenericBox.Text = detail.GenericName ?? string.Empty;
            BrandBox.Text = !string.IsNullOrWhiteSpace(detail.Brand)
                ? detail.Brand
                : (detail.ManufacturerName ?? string.Empty);
            MrpBox.Text = detail.Mrp > 0 ? detail.Mrp.ToString("0.##") : string.Empty;
            GstBox.Text = detail.GstPercent > 0 ? detail.GstPercent.ToString("0.##") : "12";
            SaveButton.IsEnabled = true;
            StatusText.Text = $"Loaded \"{detail.Name}\". Edit details as needed, then Save as a new medicine.";
            NameBox.Focus();
            NameBox.SelectAll();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            SaveButton.IsEnabled = false;
            MessageBox.Show(ex.Message, "Copy medicine", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            var src = _sourceTemplate;
            var dto = new MedicineDetailDto
            {
                Id = 0,
                Name = name,
                GenericName = NullIfBlank(GenericBox.Text),
                Brand = NullIfBlank(BrandBox.Text),
                Barcode = null, // new unique barcode on save
                HsnCode = src?.HsnCode,
                Mrp = mrp,
                SellingPrice = mrp > 0 ? mrp : (src?.SellingPrice ?? 0),
                PurchasePrice = src?.PurchasePrice ?? 0,
                GstPercent = gst,
                DefaultDiscountPercent = src?.DefaultDiscountPercent ?? 0,
                ReorderLevel = src?.ReorderLevel ?? 0,
                ReorderQuantity = src?.ReorderQuantity ?? 0,
                RackNumber = src?.RackNumber,
                BinNumber = src?.BinNumber,
                ScheduleType = src?.ScheduleType ?? ScheduleDrugType.None,
                PrescriptionRequired = src?.PrescriptionRequired ?? false,
                ManufacturerId = src?.ManufacturerId,
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
                0m,
                saved?.RackNumber,
                saved?.BinNumber,
                saved?.Brand,
                saved?.ScheduleType ?? ScheduleDrugType.None,
                null,
                saved?.PurchasePrice ?? 0m,
                saved?.HsnCode,
                saved?.Mrp ?? 0m);

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

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private sealed record SourceRow(int Id, string Name, string? GenericName, string Display);
}
