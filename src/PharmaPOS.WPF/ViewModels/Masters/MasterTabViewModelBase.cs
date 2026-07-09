using System.Collections.ObjectModel;
using System.Windows.Input;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Masters;

/// <summary>Shared list/search/save workflow for a master-data tab.</summary>
public abstract class MasterTabViewModelBase : ObservableObject
{
    protected readonly IDialogService Dialog;

    private string _searchText = string.Empty;
    private bool _isBusy;
    private string? _statusMessage;
    private CancellationTokenSource? _searchCts;

    protected MasterTabViewModelBase(IDialogService dialog)
    {
        Dialog = dialog;
        NewCommand = new RelayCommand(_ => BeginNew());
        SaveCommand = new AsyncRelayCommand(_ => SaveAsync(), _ => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(_ => SearchAsync(SearchText));
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = DebouncedSearchAsync();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        protected set => SetProperty(ref _statusMessage, value);
    }

    public ICommand NewCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RefreshCommand { get; }

    public abstract string EditorTitle { get; }
    public abstract bool IsNewRecord { get; }

    protected async Task DebouncedSearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        try
        {
            await Task.Delay(300, token);
            await SearchAsync(SearchText, token);
        }
        catch (OperationCanceledException) { }
    }

    protected abstract Task SearchAsync(string term, CancellationToken ct = default);
    protected abstract Task LoadItemAsync(int id);
    protected abstract Task SaveAsync();
    protected abstract void BeginNew();

    protected async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        try { await action(); }
        finally { IsBusy = false; }
    }
}
