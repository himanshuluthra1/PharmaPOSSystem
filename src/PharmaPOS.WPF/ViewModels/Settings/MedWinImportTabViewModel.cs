using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32;
using PharmaPOS.MedWinImport;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;
using WpfApp = System.Windows.Application;

namespace PharmaPOS.WPF.ViewModels.Settings;

public sealed class MedWinImportPhaseItem : ObservableObject
{
    private bool _isSelected;

    public MedWinImportPhaseItem(string id, string label, bool isSelected)
    {
        Id = id;
        Label = label;
        _isSelected = isSelected;
    }

    public string Id { get; }
    public string Label { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>Settings tab: migrate MedWin Access (.mdb) data into the local PharmaPOS database.</summary>
public sealed class MedWinImportTabViewModel : ObservableObject
{
    private readonly IConfiguration _configuration;
    private readonly IDialogService _dialog;
    private readonly StringBuilder _logBuilder = new();
    private CancellationTokenSource? _cts;

    private string _mdbPath = MedWinMigrationOptions.DefaultMdbPath;
    private string _mdbPassword = MedWinMigrationOptions.DefaultPassword;
    private bool _force;
    private bool _clearExistingTransactionalData = true;
    private bool _isRunning;
    private string _logText = string.Empty;
    private string? _statusMessage;

    public MedWinImportTabViewModel(IConfiguration configuration, IDialogService dialog)
    {
        _configuration = configuration;
        _dialog = dialog;

        foreach (var phase in MedWinMigrationRunner.AvailablePhases)
            Phases.Add(new MedWinImportPhaseItem(phase.Id, phase.Label, phase.InFullImport));

        BrowseMdbCommand = new RelayCommand(_ => BrowseMdb(), _ => !IsRunning);
        SelectFullImportCommand = new RelayCommand(_ => SelectFullImport(), _ => !IsRunning);
        SelectNoneCommand = new RelayCommand(_ => SelectNone(), _ => !IsRunning);
        ClearLogCommand = new RelayCommand(_ => ClearLog(), _ => !IsRunning);
        RunImportCommand = new AsyncRelayCommand(RunImportAsync, () => !IsRunning);
        RunFullImportCommand = new AsyncRelayCommand(RunFullImportAsync, () => !IsRunning);
        CancelImportCommand = new RelayCommand(_ => CancelImport(), _ => IsRunning);
    }

    public ObservableCollection<MedWinImportPhaseItem> Phases { get; } = new();

    public string MdbPath
    {
        get => _mdbPath;
        set => SetProperty(ref _mdbPath, value ?? string.Empty);
    }

    public string MdbPassword
    {
        get => _mdbPassword;
        set => SetProperty(ref _mdbPassword, value ?? string.Empty);
    }

    public bool Force
    {
        get => _force;
        set => SetProperty(ref _force, value);
    }

    /// <summary>
    /// Wipe existing POS sales/purchases/stock before importing MedWin data (masters kept).
    /// </summary>
    public bool ClearExistingTransactionalData
    {
        get => _clearExistingTransactionalData;
        set => SetProperty(ref _clearExistingTransactionalData, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand BrowseMdbCommand { get; }
    public ICommand SelectFullImportCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand RunImportCommand { get; }
    public ICommand RunFullImportCommand { get; }
    public ICommand CancelImportCommand { get; }

    private void BrowseMdb()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select MedWin data.mdb",
            Filter = "Access Database (*.mdb)|*.mdb|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (!string.IsNullOrWhiteSpace(MdbPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(MdbPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    dlg.InitialDirectory = dir;
                dlg.FileName = Path.GetFileName(MdbPath);
            }
            catch { /* ignore path parse */ }
        }

        if (dlg.ShowDialog() == true)
            MdbPath = dlg.FileName;
    }

    private void SelectFullImport()
    {
        var full = MedWinMigrationRunner.DefaultFullPhases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var phase in Phases)
            phase.IsSelected = full.Contains(phase.Id);
    }

    private Task RunFullImportAsync()
    {
        SelectFullImport();
        return RunImportAsync();
    }

    private void SelectNone()
    {
        foreach (var phase in Phases)
            phase.IsSelected = false;
    }

    private void ClearLog()
    {
        _logBuilder.Clear();
        LogText = string.Empty;
        StatusMessage = null;
    }

    private void CancelImport() => _cts?.Cancel();

    private bool ConfirmClearAndImport(string path)
    {
        if (ClearExistingTransactionalData)
        {
            if (!_dialog.Confirm(
                    "CLEAR EXISTING TRANSACTIONAL DATA?\n\n" +
                    "This permanently deletes from PharmaPOS:\n" +
                    "• All sales, sale returns, payments\n" +
                    "• All purchases, purchase returns, POs\n" +
                    "• Stock batches, stock movements, adjustments, transfers\n" +
                    "• Related journal entries and sync outbox\n\n" +
                    "KEPT: medicines, suppliers, customers, categories, users, company, roles.\n\n" +
                    "Then MedWin masters + selected transactional phases will be imported.\n\n" +
                    "Backup LocalDB before continuing. This cannot be undone.",
                    "Confirm wipe transactional data"))
                return false;

            if (!_dialog.Confirm(
                    "Final confirmation\n\n" +
                    "Wipe all existing sales/purchases/stock now, then import from:\n" +
                    path +
                    "\n\nContinue?",
                    "Final confirmation — wipe then import"))
                return false;
        }
        else
        {
            var warn = Force
                ? "Force rematch is ON — medicines may be rematched to OneMG.\n\n"
                : "";
            if (!_dialog.Confirm(
                    $"{warn}Import selected phases from:\n{path}\n\ninto the local PharmaPOS database?\n\n" +
                    "Existing POS sales/purchases will NOT be cleared.\nBackup recommended before large imports.",
                    "Confirm MedWin import"))
                return false;
        }

        return true;
    }

    private async Task RunImportAsync()
    {
        var path = MdbPath.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _dialog.ShowError("Select a valid MedWin data.mdb file first.");
            return;
        }

        var selected = Phases.Where(p => p.IsSelected).Select(p => p.Id).ToList();
        if (selected.Count == 0)
        {
            _dialog.ShowError("Select at least one import phase.");
            return;
        }

        var target = _configuration.GetConnectionString("PharmaPosDb");
        if (string.IsNullOrWhiteSpace(target))
        {
            _dialog.ShowError("ConnectionStrings:PharmaPosDb is missing from appsettings.");
            return;
        }

        if (!ConfirmClearAndImport(path))
            return;

        IsRunning = true;
        ClearLog();
        AppendLog($"MedWin import started: {path}");
        AppendLog($"Phases: {string.Join(", ", selected)}");
        if (ClearExistingTransactionalData)
            AppendLog("Clear existing transactional data: YES");
        StatusMessage = ClearExistingTransactionalData
            ? "Clearing transactional data, then importing…"
            : "Import running…";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        try
        {
            await Task.Run(async () =>
            {
                await MedWinMigrationRunner.RunAsync(new MedWinMigrationOptions
                {
                    MedWinPath = path,
                    MedWinPassword = MdbPassword,
                    TargetConnectionString = target,
                    Force = Force,
                    ClearExistingTransactionalData = ClearExistingTransactionalData,
                    Phases = selected,
                    LogSink = AppendLog,
                    CancellationToken = token
                });
            }, token);

            AppendLog("Import completed successfully.");
            StatusMessage = "Import completed successfully.";
            _dialog.ShowInfo(
                "MedWin import finished.\n\nNext: use Settings → Medicine Mapping to link unmatched MedWin medicines to the OneMG catalogue.",
                "MedWin import");
        }
        catch (OperationCanceledException)
        {
            AppendLog("Import cancelled.");
            StatusMessage = "Import cancelled.";
        }
        catch (Exception ex)
        {
            AppendLog("Import failed:");
            AppendLog(ex.ToString());
            StatusMessage = "Import failed.";
            _dialog.ShowError($"MedWin import failed:\n\n{ex.Message}");
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsRunning = false;
        }
    }

    private void AppendLog(string message)
    {
        void Write()
        {
            if (_logBuilder.Length > 0) _logBuilder.AppendLine();
            _logBuilder.Append(message);
            if (_logBuilder.Length > 200_000)
                _logBuilder.Remove(0, _logBuilder.Length - 150_000);
            LogText = _logBuilder.ToString();
        }

        var dispatcher = WpfApp.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            Write();
        else
            dispatcher.BeginInvoke(Write);
    }
}
