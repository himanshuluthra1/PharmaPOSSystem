using System.Collections.ObjectModel;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Mvvm;

namespace PharmaPOS.WPF.ViewModels.Sales;

/// <summary>View model for the medicine search popup (auto-suggest list).</summary>
public class MedicineSearchViewModel : ObservableObject
{
    private const int DebounceMs = 80;
    private const int ResultTake = 25;

    private readonly IMedicineSearchIndex _searchIndex;
    private readonly int? _branchId;

    private string _searchText = string.Empty;
    private int _selectedIndex = -1;
    private string? _hint;
    private CancellationTokenSource? _searchCts;
    private readonly SemaphoreSlim _searchGate = new(1, 1);
    private int _resultVersion;

    public MedicineSearchViewModel(IMedicineSearchIndex searchIndex, int? branchId)
    {
        _searchIndex = searchIndex;
        _branchId = branchId;
    }

    public ObservableCollection<MedicineLookupDto> Results { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = SearchAsync(value);
        }
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetProperty(ref _selectedIndex, value))
                OnPropertyChanged(nameof(SelectedMedicine));
        }
    }

    public MedicineLookupDto? SelectedMedicine =>
        SelectedIndex >= 0 && SelectedIndex < Results.Count ? Results[SelectedIndex] : null;

    public string? Hint
    {
        get => _hint;
        private set => SetProperty(ref _hint, value);
    }

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        if (SelectedIndex < 0)
            SelectedIndex = 0;
        else
            SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Results.Count - 1);
    }

    private async Task SearchAsync(string term)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        var version = Interlocked.Increment(ref _resultVersion);

        term = term.Trim();
        if (term.Length < 2)
        {
            ReplaceResults([]);
            SelectedIndex = -1;
            Hint = term.Length == 1 ? "Type at least 2 characters..." : "Search by medicine name";
            return;
        }

        Hint = _searchIndex.IsWarm ? null : "Loading catalogue...";
        try
        {
            await Task.Delay(DebounceMs, token);
            if (token.IsCancellationRequested || version != _resultVersion) return;

            await _searchGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested || version != _resultVersion) return;

                await _searchIndex.EnsureWarmAsync(token);
                if (token.IsCancellationRequested || version != _resultVersion) return;

                var rows = _searchIndex.Search(term, ResultTake);
                if (token.IsCancellationRequested || version != _resultVersion) return;

                ReplaceResults(rows);
                SelectedIndex = rows.Count > 0 ? 0 : -1;
                Hint = rows.Count == 0
                    ? $"No medicines found for \"{term}\". Click “Not found? Create from website” to add it."
                    : null;

                if (rows.Count > 0)
                    _ = FillStockAsync(rows, version, token);
            }
            finally
            {
                _searchGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Hint = $"Search failed: {ex.Message}";
        }
    }

    private async Task FillStockAsync(
        IReadOnlyList<MedicineLookupDto> rows, int version, CancellationToken token)
    {
        try
        {
            var ids = rows.Select(r => r.Id).ToList();
            var stockMap = await _searchIndex.GetStockByMedicineIdsAsync(ids, _branchId, token);
            if (token.IsCancellationRequested || version != _resultVersion) return;

            for (var i = 0; i < Results.Count; i++)
            {
                var current = Results[i];
                stockMap.TryGetValue(current.Id, out var stock);
                if (current.TotalStock == stock) continue;
                Results[i] = current with { TotalStock = stock };
            }

            OnPropertyChanged(nameof(SelectedMedicine));
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Stock is secondary — leave qty at 0 if the fill fails.
        }
    }

    private void ReplaceResults(IReadOnlyList<MedicineLookupDto> rows)
    {
        Results.Clear();
        foreach (var row in rows)
            Results.Add(row);
    }
}

/// <summary>View model for the batch variant picker popup.</summary>
public class BatchPickerViewModel : ObservableObject
{
    private int _selectedIndex;

    public BatchPickerViewModel(IReadOnlyList<BatchLookupDto> batches, string medicineName)
    {
        MedicineName = medicineName;
        foreach (var b in batches) Batches.Add(b);
        SelectedIndex = batches.Count > 0 ? 0 : -1;
    }

    public string MedicineName { get; }

    public ObservableCollection<BatchLookupDto> Batches { get; } = new();

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetProperty(ref _selectedIndex, value))
                OnPropertyChanged(nameof(SelectedBatch));
        }
    }

    public BatchLookupDto? SelectedBatch =>
        SelectedIndex >= 0 && SelectedIndex < Batches.Count ? Batches[SelectedIndex] : null;

    public void MoveSelection(int delta)
    {
        if (Batches.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        if (SelectedIndex < 0)
            SelectedIndex = 0;
        else
            SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Batches.Count - 1);
    }
}

/// <summary>View model for the same-salt substitute medicine picker (F5).</summary>
public class SubstituteMedicineViewModel : ObservableObject
{
    private int _selectedIndex;

    public SubstituteMedicineViewModel(IReadOnlyList<SubstituteMedicineDto> medicines, int currentMedicineId)
    {
        CurrentMedicineId = currentMedicineId;
        foreach (var medicine in medicines)
            Medicines.Add(new SubstituteMedicineItem(medicine, medicine.Id == currentMedicineId));
        SelectedIndex = medicines.Count > 0 ? 0 : -1;
    }

    public int CurrentMedicineId { get; }

    public ObservableCollection<SubstituteMedicineItem> Medicines { get; } = new();

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetProperty(ref _selectedIndex, value))
                OnPropertyChanged(nameof(SelectedMedicine));
        }
    }

    public SubstituteMedicineDto? SelectedMedicine =>
        SelectedIndex >= 0 && SelectedIndex < Medicines.Count ? Medicines[SelectedIndex].Medicine : null;

    public void MoveSelection(int delta)
    {
        if (Medicines.Count == 0)
        {
            SelectedIndex = -1;
            return;
        }

        if (SelectedIndex < 0)
            SelectedIndex = 0;
        else
            SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Medicines.Count - 1);
    }
}

public record SubstituteMedicineItem(SubstituteMedicineDto Medicine, bool IsCurrent);
