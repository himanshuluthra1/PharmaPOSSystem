using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using Microsoft.Win32;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Inventory;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Inventory;

public class StockTransferTabViewModel : ObservableObject
{
    private readonly IStockTransferService _transfers;
    private readonly IMedicinePickerService _medicinePicker;
    private readonly IDialogService _dialog;
    private readonly int? _branchId;
    private readonly int? _userId;

    private string _transferNumber = string.Empty;
    private DateTime _transferDate = DateTime.Today;
    private StockTransferBranchOptionDto? _selectedDestination;
    private string? _remarks;
    private bool _isBusy;
    private string? _statusMessage;
    private StockTransferListRowDto? _selectedHistoryRow;
    private string? _selectedTransferSummary;
    private int _detailLoadVersion;

    public StockTransferTabViewModel(
        IStockTransferService transfers,
        ICurrentUserService currentUser,
        IMedicinePickerService medicinePicker,
        IDialogService dialog)
    {
        _transfers = transfers;
        _medicinePicker = medicinePicker;
        _dialog = dialog;
        _branchId = currentUser.CurrentUser?.BranchId;
        _userId = currentUser.CurrentUser?.UserId;

        AddLineCommand = new AsyncRelayCommand(_ => AddLineAsync(), _ => !IsBusy);
        RemoveLineCommand = new RelayCommand(p => RemoveLine(p as StockTransferLineViewModel));
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !IsBusy && CanSave);
        NewCommand = new RelayCommand(_ => ResetForm());
        ImportCommand = new AsyncRelayCommand(_ => ImportAsync(), _ => !IsBusy);
        RefreshHistoryCommand = new AsyncRelayCommand(_ => RefreshHistoryAsync(), _ => !IsBusy);
        ReExportCommand = new AsyncRelayCommand(
            _ => ReExportAsync(),
            _ => !IsBusy && SelectedHistoryRow?.CanReExport == true);
        CancelCommand = new AsyncRelayCommand(
            _ => CancelAsync(),
            _ => !IsBusy && SelectedHistoryRow?.CanCancel == true);

        _ = InitializeAsync();
    }

    public ObservableCollection<StockTransferLineViewModel> Lines { get; } = new();
    public ObservableCollection<StockTransferBranchOptionDto> DestinationBranches { get; } = new();
    public ObservableCollection<StockTransferListRowDto> RecentTransfers { get; } = new();
    public ObservableCollection<StockTransferDetailLineDto> SelectedTransferLines { get; } = new();

    public string TransferNumber
    {
        get => _transferNumber;
        private set => SetProperty(ref _transferNumber, value);
    }

    public DateTime TransferDate
    {
        get => _transferDate;
        set => SetProperty(ref _transferDate, value);
    }

    public StockTransferBranchOptionDto? SelectedDestination
    {
        get => _selectedDestination;
        set
        {
            if (SetProperty(ref _selectedDestination, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? Remarks
    {
        get => _remarks;
        set => SetProperty(ref _remarks, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public StockTransferListRowDto? SelectedHistoryRow
    {
        get => _selectedHistoryRow;
        set
        {
            if (!SetProperty(ref _selectedHistoryRow, value)) return;
            CommandManager.InvalidateRequerySuggested();
            _ = LoadSelectedTransferDetailsAsync(value);
        }
    }

    public string? SelectedTransferSummary
    {
        get => _selectedTransferSummary;
        private set => SetProperty(ref _selectedTransferSummary, value);
    }

    public bool HasSelectedTransferDetails => SelectedTransferLines.Count > 0;

    public bool CanSave => SelectedDestination is not null && Lines.Count > 0;

    public ICommand AddLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand NewCommand { get; }
    public ICommand ImportCommand { get; }
    public ICommand RefreshHistoryCommand { get; }
    public ICommand ReExportCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task InitializeAsync()
    {
        try
        {
            TransferNumber = await _transfers.PreviewNextTransferNumberAsync(_branchId);
            var destinations = await _transfers.ListDestinationBranchesAsync(_branchId);
            DestinationBranches.Clear();
            foreach (var b in destinations)
                DestinationBranches.Add(b);

            if (DestinationBranches.Count == 1)
                SelectedDestination = DestinationBranches[0];

            await RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load transfer screen: {ex.Message}";
        }
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            var selectedId = SelectedHistoryRow?.TransferId;
            var rows = await _transfers.ListRecentTransfersAsync(_branchId);
            RecentTransfers.Clear();
            foreach (var row in rows)
                RecentTransfers.Add(row);

            if (selectedId is int id)
            {
                var match = RecentTransfers.FirstOrDefault(r => r.TransferId == id);
                if (match is not null)
                    SelectedHistoryRow = match;
                else
                    ClearSelectedTransferDetails();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load transfer history: {ex.Message}";
        }
    }

    private async Task LoadSelectedTransferDetailsAsync(StockTransferListRowDto? row)
    {
        var version = ++_detailLoadVersion;
        SelectedTransferLines.Clear();
        SelectedTransferSummary = null;
        OnPropertyChanged(nameof(HasSelectedTransferDetails));

        if (row is null) return;

        try
        {
            var result = await _transfers.GetTransferDetailsAsync(row.TransferId, _branchId);
            if (version != _detailLoadVersion) return;

            if (result.IsFailure || result.Value is null)
            {
                SelectedTransferSummary = result.Error ?? "Could not load transfer details.";
                return;
            }

            var detail = result.Value;
            foreach (var line in detail.Lines)
                SelectedTransferLines.Add(line);

            SelectedTransferSummary =
                $"{detail.TransferNumber}  ·  {detail.DirectionLabel}  ·  " +
                $"{detail.FromBranchName} → {detail.ToBranchName}  ·  " +
                $"{detail.Lines.Count} medicine(s), qty {detail.Lines.Sum(l => l.Quantity):N0}"
                + (string.IsNullOrWhiteSpace(detail.Remarks) ? "" : $"  ·  {detail.Remarks}");

            OnPropertyChanged(nameof(HasSelectedTransferDetails));
        }
        catch (Exception ex)
        {
            if (version != _detailLoadVersion) return;
            SelectedTransferSummary = $"Could not load medicines: {ex.Message}";
        }
    }

    private void ClearSelectedTransferDetails()
    {
        _detailLoadVersion++;
        if (_selectedHistoryRow is not null)
        {
            _selectedHistoryRow = null;
            OnPropertyChanged(nameof(SelectedHistoryRow));
            CommandManager.InvalidateRequerySuggested();
        }

        SelectedTransferLines.Clear();
        SelectedTransferSummary = null;
        OnPropertyChanged(nameof(HasSelectedTransferDetails));
    }

    private async Task AddLineAsync()
    {
        var pick = await _medicinePicker.PickMedicineForAdjustmentAsync();
        if (pick is null) return;

        if (pick.AvailableStock <= 0)
        {
            _dialog.ShowInfo("Selected batch has no available stock to transfer.", "No stock");
            return;
        }

        if (Lines.Any(l => l.BatchId == pick.BatchId))
        {
            _dialog.ShowInfo("This batch is already on the transfer.", "Duplicate batch");
            return;
        }

        Lines.Add(new StockTransferLineViewModel(
            pick.MedicineId,
            pick.BatchId,
            pick.MedicineName,
            pick.BatchNumber,
            pick.AvailableStock,
            pick.ExpiryDate));

        StatusMessage = $"{Lines.Count} line(s) ready to send.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void RemoveLine(StockTransferLineViewModel? line)
    {
        if (line is null) return;
        Lines.Remove(line);
        StatusMessage = Lines.Count == 0 ? null : $"{Lines.Count} line(s) ready to send.";
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveAsync()
    {
        if (SelectedDestination is null)
        {
            _dialog.ShowInfo(
                "Select the destination store.\n\nTip: create the other store under Settings → Branches on this PC (use the same Branch Code as on the other PC).",
                "Transfer");
            return;
        }

        var validLines = Lines.Where(l => l.TransferQuantity > 0).ToList();
        if (validLines.Count == 0)
        {
            _dialog.ShowInfo("Enter transfer quantity on at least one line.", "Transfer");
            return;
        }

        foreach (var line in validLines)
        {
            if (line.TransferQuantity > line.AvailableQuantity)
            {
                _dialog.ShowError(
                    $"Transfer qty for {line.MedicineName} / {line.BatchNumber} " +
                    $"cannot exceed available ({line.AvailableQuantity:N0}).");
                return;
            }
        }

        var totalQty = validLines.Sum(l => l.TransferQuantity);
        if (!_dialog.Confirm(
                $"Send {validLines.Count} line(s), total qty {totalQty:N0}\n" +
                $"to {SelectedDestination.Name}?\n\n" +
                "Stock will leave THIS store now.\n" +
                "A transfer file will be created — send that file to the other store to Import.",
                "Confirm send transfer"))
            return;

        IsBusy = true;
        StatusMessage = "Creating transfer package...";
        try
        {
            var request = new CreateStockTransferRequest
            {
                TransferDate = TransferDate,
                ToBranchId = SelectedDestination.Id,
                Remarks = Remarks,
                Lines = validLines.Select(l => new StockTransferLineRequest
                {
                    MedicineId = l.MedicineId,
                    SourceMedicineBatchId = l.BatchId,
                    Quantity = l.TransferQuantity
                }).ToList()
            };

            var result = await _transfers.CreateOutboundTransferAsync(request, _branchId, _userId);
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not complete transfer.");
                return;
            }

            var r = result.Value;
            var savedPath = SavePackageFile(r.PackageJson!, r.SuggestedFileName);

            _dialog.ShowInfo(
                $"Transfer {r.TransferNumber} sent from stock.\n\n" +
                $"{r.FromBranchName} → {r.ToBranchName}\n" +
                $"{r.LinesTransferred} line(s), qty {r.TotalQuantity:N0}.\n\n" +
                $"Package saved:\n{savedPath}\n\n" +
                "Send this .pharmatrf file to the other store (USB / WhatsApp / email).\n" +
                "They open Inventory → Transfer → Import package.",
                "Transfer package ready");

            ResetForm();
            TransferNumber = await _transfers.PreviewNextTransferNumberAsync(_branchId);
            await RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import stock transfer package",
            Filter = "PharmaPOS transfer (*.pharmatrf)|*.pharmatrf|JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        StatusMessage = "Importing transfer package...";
        try
        {
            var json = await File.ReadAllTextAsync(dialog.FileName);
            var result = await _transfers.ImportPackageAsync(json, _branchId, _userId);
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not import package.");
                return;
            }

            var r = result.Value;
            if (r.LinesTransferred == 0 && r.TotalQuantity == 0)
            {
                _dialog.ShowInfo(
                    $"Cancel notice recorded for package from {r.FromBranchName}.\n\n" +
                    "The original transfer file can no longer be imported here.\n" +
                    "No stock was added.",
                    "Package cancelled by sender");
            }
            else
            {
                _dialog.ShowInfo(
                    $"Transfer received.\n\n" +
                    $"{r.FromBranchName} → {r.ToBranchName}\n" +
                    $"Local ref: {r.TransferNumber}\n" +
                    $"{r.LinesTransferred} line(s), qty {r.TotalQuantity:N0}\n\n" +
                    "Stock is now available to sell on this store.",
                    "Import complete");
            }

            await RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }

    private async Task CancelAsync()
    {
        if (SelectedHistoryRow is null || !SelectedHistoryRow.CanCancel) return;

        var isReceived = !SelectedHistoryRow.IsOutgoing;
        var confirm = isReceived
            ? $"Cancel received transfer {SelectedHistoryRow.TransferNumber}?\n\n" +
              "Imported stock will be removed from THIS store.\n" +
              "A return package file will be created — send it to the sending store " +
              "so they can Import and restore their stock.\n\n" +
              "Cancel is blocked if you already sold some of this stock."
            : $"Cancel sent transfer {SelectedHistoryRow.TransferNumber}?\n\n" +
              "Stock will be restored on THIS store.\n" +
              "A cancel file will be created — send it to the other store if you already shared the original file " +
              "(and they have NOT imported it yet).\n\n" +
              "If they already imported, ask THEM to Cancel the Received row instead.";

        if (!_dialog.Confirm(confirm, "Cancel / rollback transfer"))
            return;

        IsBusy = true;
        StatusMessage = "Cancelling transfer...";
        try
        {
            var result = await _transfers.CancelTransferAsync(
                SelectedHistoryRow.TransferId, _branchId, "Cancelled by user");
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not cancel transfer.");
                return;
            }

            var r = result.Value;
            var path = SavePackageFile(r.PackageJson!, r.SuggestedFileName);

            if (r.IsReturnPackage)
            {
                _dialog.ShowInfo(
                    $"Received transfer {r.TransferNumber} cancelled.\n\n" +
                    $"Removed qty: {r.TotalQuantity:N0}\n\n" +
                    $"Return package:\n{path}\n\n" +
                    "Send this file to the sending store.\n" +
                    "They Import it to put stock back on their POS.",
                    "Import cancelled");
            }
            else
            {
                _dialog.ShowInfo(
                    $"Sent transfer {r.TransferNumber} cancelled.\n\n" +
                    $"Restored qty: {r.TotalQuantity:N0}\n\n" +
                    $"Cancel file:\n{path}\n\n" +
                    "If you already sent the original package, send this cancel file to the other store " +
                    "and ask them to Import it (blocks the old file).\n\n" +
                    "You can now create a new transfer.",
                    "Transfer cancelled");
            }

            TransferNumber = await _transfers.PreviewNextTransferNumberAsync(_branchId);
            await RefreshHistoryAsync();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = null;
        }
    }

    private async Task ReExportAsync()
    {
        if (SelectedHistoryRow is null || !SelectedHistoryRow.CanReExport) return;

        IsBusy = true;
        try
        {
            var result = await _transfers.GetOutboundPackageJsonAsync(SelectedHistoryRow.TransferId, _branchId);
            if (result.IsFailure || result.Value is null)
            {
                _dialog.ShowError(result.Error ?? "Could not export package.");
                return;
            }

            var name = $"{SelectedHistoryRow.TransferNumber}.pharmatrf";
            var path = SavePackageFile(result.Value, name);
            _dialog.ShowInfo($"Package saved again:\n{path}", "Re-export");
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string SavePackageFile(string json, string suggestedFileName)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS", "StockTransfers");
        Directory.CreateDirectory(dir);

        var safeName = string.IsNullOrWhiteSpace(suggestedFileName)
            ? $"transfer_{DateTime.Now:yyyyMMddHHmmss}.pharmatrf"
            : suggestedFileName;
        foreach (var c in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(c, '_');

        var path = Path.Combine(dir, safeName);
        File.WriteAllText(path, json);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            /* ignore */
        }

        return path;
    }

    private void ResetForm()
    {
        Lines.Clear();
        TransferDate = DateTime.Today;
        Remarks = null;
        StatusMessage = null;
        CommandManager.InvalidateRequerySuggested();
    }
}
