using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace PharmaPOS.WPF.Services;

public interface IUiLayoutService
{
    UiLayoutSettings Current { get; }
    void Load();
    void Save();
    void ScheduleSave();
    void ResetToDefaults();
    void SetSidePanelWidth(string viewKey, double width);
    void SetGridColumnWidths(string viewKey, IReadOnlyDictionary<string, double> widths);
    IReadOnlyDictionary<string, double> GetGridColumnWidths(string viewKey);
    double GetSidePanelWidth(string viewKey);
}

/// <summary>
/// Loads/saves Sales &amp; Purchase layout widths to a local JSON file so drag-resized
/// sections and DataGrid columns survive restarts on this machine.
/// </summary>
public sealed class UiLayoutService : IUiLayoutService
{
    public const string SalesKey = "Sales";
    public const string PurchaseKey = "Purchase";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private DispatcherTimer? _saveTimer;
    private UiLayoutSettings _current = new();

    public UiLayoutService()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "ui-layout.json");
        Load();
    }

    public UiLayoutSettings Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath))
            {
                _current = new UiLayoutSettings();
                return;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<UiLayoutSettings>(json, JsonOptions);
                _current = Normalize(loaded ?? new UiLayoutSettings());
            }
            catch
            {
                _current = new UiLayoutSettings();
            }
        }
    }

    public void Save()
    {
        UiLayoutSettings snapshot;
        lock (_gate)
        {
            snapshot = Clone(_current);
        }

        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Layout persistence must not break billing UI.
        }
    }

    public void ScheduleSave()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            Save();
            return;
        }

        dispatcher.Invoke(() =>
        {
            _saveTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _saveTimer.Tick -= OnSaveTick;
            _saveTimer.Tick += OnSaveTick;
            _saveTimer.Stop();
            _saveTimer.Start();
        });
    }

    private void OnSaveTick(object? sender, EventArgs e)
    {
        if (_saveTimer is not null)
        {
            _saveTimer.Stop();
            _saveTimer.Tick -= OnSaveTick;
        }
        Save();
    }

    public void ResetToDefaults()
    {
        lock (_gate)
        {
            _current = new UiLayoutSettings();
        }
        Save();
    }

    public void SetSidePanelWidth(string viewKey, double width)
    {
        width = ClampSideWidth(width);
        lock (_gate)
        {
            if (IsSales(viewKey))
                _current.SalesSidePanelWidth = width;
            else if (IsPurchase(viewKey))
                _current.PurchaseSidePanelWidth = width;
        }
        ScheduleSave();
    }

    public double GetSidePanelWidth(string viewKey)
    {
        lock (_gate)
        {
            if (IsSales(viewKey)) return ClampSideWidth(_current.SalesSidePanelWidth);
            if (IsPurchase(viewKey)) return ClampSideWidth(_current.PurchaseSidePanelWidth);
            return 250;
        }
    }

    public void SetGridColumnWidths(string viewKey, IReadOnlyDictionary<string, double> widths)
    {
        if (string.IsNullOrWhiteSpace(viewKey)) return;

        var copy = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (key, value) in widths)
        {
            if (value > 20)
                copy[key] = Math.Round(value, 1);
        }

        lock (_gate)
        {
            _current.GridColumnsByView[viewKey] = copy;

            // Keep legacy fields in sync for older readers / partial upgrades.
            if (IsSales(viewKey))
                _current.SalesGridColumns = new Dictionary<string, double>(copy, StringComparer.Ordinal);
            else if (IsPurchase(viewKey))
                _current.PurchaseGridColumns = new Dictionary<string, double>(copy, StringComparer.Ordinal);
        }
        ScheduleSave();
    }

    public IReadOnlyDictionary<string, double> GetGridColumnWidths(string viewKey)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(viewKey)
                && _current.GridColumnsByView.TryGetValue(viewKey, out var byView)
                && byView.Count > 0)
            {
                return new Dictionary<string, double>(byView, StringComparer.Ordinal);
            }

            // Legacy fallback before GridColumnsByView existed.
            var source = IsSales(viewKey) ? _current.SalesGridColumns
                : IsPurchase(viewKey) ? _current.PurchaseGridColumns
                : null;
            return source is null || source.Count == 0
                ? new Dictionary<string, double>(StringComparer.Ordinal)
                : new Dictionary<string, double>(source, StringComparer.Ordinal);
        }
    }

    private static bool IsSales(string key) =>
        string.Equals(key, SalesKey, StringComparison.OrdinalIgnoreCase);

    private static bool IsPurchase(string key) =>
        string.Equals(key, PurchaseKey, StringComparison.OrdinalIgnoreCase);

    private static double ClampSideWidth(double width) =>
        Math.Clamp(double.IsFinite(width) ? width : 250, 180, 480);

    private static UiLayoutSettings Normalize(UiLayoutSettings s)
    {
        var byView = new Dictionary<string, Dictionary<string, double>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var (viewKey, cols) in s.GridColumnsByView ?? new())
        {
            if (string.IsNullOrWhiteSpace(viewKey) || cols is null || cols.Count == 0) continue;
            byView[viewKey] = new Dictionary<string, double>(cols, StringComparer.Ordinal);
        }

        // Migrate legacy Sales / Purchase maps into the unified dictionary.
        if ((s.SalesGridColumns?.Count ?? 0) > 0 && !byView.ContainsKey(SalesKey))
            byView[SalesKey] = new Dictionary<string, double>(s.SalesGridColumns!, StringComparer.Ordinal);
        if ((s.PurchaseGridColumns?.Count ?? 0) > 0 && !byView.ContainsKey(PurchaseKey))
            byView[PurchaseKey] = new Dictionary<string, double>(s.PurchaseGridColumns!, StringComparer.Ordinal);

        return new UiLayoutSettings
        {
            SalesSidePanelWidth = ClampSideWidth(s.SalesSidePanelWidth),
            PurchaseSidePanelWidth = ClampSideWidth(s.PurchaseSidePanelWidth),
            SalesGridColumns = byView.TryGetValue(SalesKey, out var sales)
                ? new Dictionary<string, double>(sales, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal),
            PurchaseGridColumns = byView.TryGetValue(PurchaseKey, out var purchase)
                ? new Dictionary<string, double>(purchase, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal),
            GridColumnsByView = byView
        };
    }

    private static UiLayoutSettings Clone(UiLayoutSettings s) => Normalize(s);
}
