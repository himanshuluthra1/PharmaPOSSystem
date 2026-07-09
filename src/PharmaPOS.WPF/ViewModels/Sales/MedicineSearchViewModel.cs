using System.Collections.ObjectModel;
using System.Windows;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.ViewModels.Sales;

/// <summary>View model for the medicine search popup (auto-suggest list).</summary>
public class MedicineSearchViewModel : ObservableObject
{
    private readonly ISalesService _salesService;
    private readonly int? _branchId;

    private string _searchText = string.Empty;
    private int _selectedIndex = -1;
    private string? _hint;
    private CancellationTokenSource? _searchCts;
    private readonly SemaphoreSlim _searchGate = new(1, 1);

    public MedicineSearchViewModel(ISalesService salesService, int? branchId)
    {
        _salesService = salesService;
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

        Results.Clear();
        SelectedIndex = -1;

        term = term.Trim();
        if (term.Length < 2)
        {
            Hint = term.Length == 1 ? "Type at least 2 characters..." : "Search by medicine name";
            return;
        }

        Hint = "Searching...";
        try
        {
            await Task.Delay(250, token);
            if (token.IsCancellationRequested) return;

            await _searchGate.WaitAsync(token);
            try
            {
                if (token.IsCancellationRequested) return;

                var rows = await _salesService.SearchMedicinesAsync(term, _branchId, token);
                if (token.IsCancellationRequested) return;

                foreach (var r in rows) Results.Add(r);
                SelectedIndex = rows.Count > 0 ? 0 : -1;
                Hint = rows.Count == 0 ? $"No medicines found for \"{term}\"." : null;
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
