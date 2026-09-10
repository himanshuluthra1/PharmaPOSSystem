using System.Windows;
using System.Windows.Input;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Shared.Constants;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.ViewModels.Masters;

/// <summary>Shared list/search/save workflow for a master-data tab (edit via popup).</summary>
public abstract class MasterTabViewModelBase : ObservableObject
{
    protected readonly IDialogService Dialog;

    private string _searchText = string.Empty;
    private bool _isBusy;
    private string? _statusMessage;
    private CancellationTokenSource? _searchCts;
    private bool _editorOpen;

    protected MasterTabViewModelBase(IDialogService dialog, ICurrentUserService currentUser)
    {
        Dialog = dialog;
        CanEdit = currentUser.HasAnyPermission(
            AppConstants.Permissions.MastersEdit, AppConstants.Permissions.MastersManage);

        NewCommand = new RelayCommand(_ => OpenNewEditor(), _ => CanEdit && !IsBusy);
        EditCommand = new AsyncRelayCommand(p => OpenEditEditorAsync(p), _ => CanEdit && !IsBusy);
        DeleteRowCommand = new AsyncRelayCommand(p => DeleteRowAsync(p), _ => CanEdit && !IsBusy);
        SaveCommand = new AsyncRelayCommand(async _ =>
        {
            if (await SaveAsync())
                CloseEditor(true);
        }, _ => !IsBusy && CanEdit && _editorOpen);
        CancelCommand = new RelayCommand(_ => CloseEditor(false), _ => _editorOpen);
        RefreshCommand = new AsyncRelayCommand(_ => SearchAsync(SearchText));
    }

    public bool CanEdit { get; }

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

    public bool IsEditorOpen
    {
        get => _editorOpen;
        private set => SetProperty(ref _editorOpen, value);
    }

    public ICommand NewCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteRowCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
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
    /// <summary>Persist editor; return true when saved successfully.</summary>
    protected abstract Task<bool> SaveAsync();
    protected abstract void BeginNew();
    protected abstract Task<bool> DeleteItemAsync(int id);
    protected abstract int? GetIdFromRow(object? row);

    private void OpenNewEditor()
    {
        BeginNew();
        StatusMessage = null;
        ShowEditorDialog();
    }

    private async Task OpenEditEditorAsync(object? parameter)
    {
        var id = GetIdFromRow(parameter);
        if (id is null or <= 0) return;
        await LoadItemAsync(id.Value);
        StatusMessage = null;
        ShowEditorDialog();
    }

    private async Task DeleteRowAsync(object? parameter)
    {
        var id = GetIdFromRow(parameter);
        if (id is null or <= 0) return;
        if (!Dialog.Confirm("Delete this record? It will be removed from the list.", "Delete"))
            return;

        await RunBusyAsync(async () =>
        {
            var ok = await DeleteItemAsync(id.Value);
            if (!ok) return;
            StatusMessage = "Record deleted.";
            await SearchAsync(SearchText);
        });
    }

    private void ShowEditorDialog()
    {
        IsEditorOpen = true;
        var window = new MasterEditorWindow { DataContext = this };
        var owner = System.Windows.Application.Current?.MainWindow;
        if (owner is not null && owner.IsLoaded && owner.IsVisible)
        {
            try { window.Owner = owner; } catch { /* ignore */ }
        }

        window.ShowDialog();
        IsEditorOpen = false;
        CommandManager.InvalidateRequerySuggested();
    }

    protected void CloseEditor(bool saved)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;
        foreach (Window w in app.Windows)
        {
            if (w is MasterEditorWindow { IsLoaded: true } editor && ReferenceEquals(editor.DataContext, this))
            {
                try { editor.DialogResult = saved; } catch { /* already closing */ }
                editor.Close();
                break;
            }
        }
    }

    protected async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        try { await action(); }
        finally
        {
            IsBusy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
