using System.Collections.ObjectModel;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.Domain.Enums;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Masters;

public class SupplierTabViewModel : MasterTabViewModelBase
{
    private readonly IMastersService _masters;
    private readonly Func<int?> _branchId;
    private SupplierListDto? _selected;
    private SupplierDetailDto _editor = new();

    public SupplierTabViewModel(IMastersService masters, ICurrentUserService currentUser, IDialogService dialog)
        : base(dialog, currentUser)
    {
        _masters = masters;
        _branchId = () => currentUser.CurrentUser?.BranchId;
        _ = SearchAsync(string.Empty);
    }

    public ObservableCollection<SupplierListDto> Items { get; } = new();
    public Array EntityStatuses => Enum.GetValues(typeof(EntityStatus));

    public SupplierListDto? SelectedItem
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                _ = LoadItemAsync(value.Id);
        }
    }

    public SupplierDetailDto Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(EditorTitle));
                OnPropertyChanged(nameof(IsNewRecord));
            }
        }
    }

    public override string EditorTitle => Editor.Id > 0 ? $"Edit: {Editor.Name}" : "New Supplier";
    public override bool IsNewRecord => Editor.Id == 0;

    protected override async Task SearchAsync(string term, CancellationToken ct = default)
    {
        var rows = await _masters.SearchSuppliersAsync(term, _branchId(), ct);
        Items.Clear();
        foreach (var r in rows) Items.Add(r);
    }

    protected override async Task LoadItemAsync(int id)
    {
        var detail = await _masters.GetSupplierAsync(id);
        if (detail is not null) Editor = detail;
    }

    protected override void BeginNew()
    {
        SelectedItem = null;
        Editor = new SupplierDetailDto();
    }

    protected override async Task SaveAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _masters.SaveSupplierAsync(Editor, _branchId());
            if (result.IsFailure)
            {
                Dialog.ShowError(result.Error ?? "Could not save supplier.");
                return;
            }
            Editor.Id = result.Value;
            StatusMessage = "Supplier saved.";
            await SearchAsync(SearchText);
        });
    }
}

public class CustomerTabViewModel : MasterTabViewModelBase
{
    private readonly IMastersService _masters;
    private readonly Func<int?> _branchId;
    private CustomerListDto? _selected;
    private CustomerDetailDto _editor = new();

    public CustomerTabViewModel(IMastersService masters, ICurrentUserService currentUser, IDialogService dialog)
        : base(dialog, currentUser)
    {
        _masters = masters;
        _branchId = () => currentUser.CurrentUser?.BranchId;
        _ = SearchAsync(string.Empty);
    }

    public ObservableCollection<CustomerListDto> Items { get; } = new();
    public Array EntityStatuses => Enum.GetValues(typeof(EntityStatus));
    public Array CustomerTypes => Enum.GetValues(typeof(CustomerType));

    public CustomerListDto? SelectedItem
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                _ = LoadItemAsync(value.Id);
        }
    }

    public CustomerDetailDto Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(EditorTitle));
                OnPropertyChanged(nameof(IsNewRecord));
            }
        }
    }

    public override string EditorTitle => Editor.Id > 0 ? $"Edit: {Editor.Name}" : "New Customer";
    public override bool IsNewRecord => Editor.Id == 0;

    protected override async Task SearchAsync(string term, CancellationToken ct = default)
    {
        var rows = await _masters.SearchCustomersAsync(term, _branchId(), ct);
        Items.Clear();
        foreach (var r in rows) Items.Add(r);
    }

    protected override async Task LoadItemAsync(int id)
    {
        var detail = await _masters.GetCustomerAsync(id);
        if (detail is not null) Editor = detail;
    }

    protected override void BeginNew()
    {
        SelectedItem = null;
        Editor = new CustomerDetailDto();
    }

    protected override async Task SaveAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _masters.SaveCustomerAsync(Editor, _branchId());
            if (result.IsFailure)
            {
                Dialog.ShowError(result.Error ?? "Could not save customer.");
                return;
            }
            Editor.Id = result.Value;
            StatusMessage = "Customer saved.";
            await SearchAsync(SearchText);
        });
    }
}

public class DoctorTabViewModel : MasterTabViewModelBase
{
    private readonly IMastersService _masters;
    private DoctorListDto? _selected;
    private DoctorDetailDto _editor = new();

    public DoctorTabViewModel(IMastersService masters, ICurrentUserService currentUser, IDialogService dialog)
        : base(dialog, currentUser)
    {
        _masters = masters;
        _ = SearchAsync(string.Empty);
    }

    public ObservableCollection<DoctorListDto> Items { get; } = new();
    public Array EntityStatuses => Enum.GetValues(typeof(EntityStatus));

    public DoctorListDto? SelectedItem
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                _ = LoadItemAsync(value.Id);
        }
    }

    public DoctorDetailDto Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(EditorTitle));
                OnPropertyChanged(nameof(IsNewRecord));
            }
        }
    }

    public override string EditorTitle => Editor.Id > 0 ? $"Edit: {Editor.Name}" : "New Doctor";
    public override bool IsNewRecord => Editor.Id == 0;

    protected override async Task SearchAsync(string term, CancellationToken ct = default)
    {
        var rows = await _masters.SearchDoctorsAsync(term, ct);
        Items.Clear();
        foreach (var r in rows) Items.Add(r);
    }

    protected override async Task LoadItemAsync(int id)
    {
        var detail = await _masters.GetDoctorAsync(id);
        if (detail is not null) Editor = detail;
    }

    protected override void BeginNew()
    {
        SelectedItem = null;
        Editor = new DoctorDetailDto();
    }

    protected override async Task SaveAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _masters.SaveDoctorAsync(Editor);
            if (result.IsFailure)
            {
                Dialog.ShowError(result.Error ?? "Could not save doctor.");
                return;
            }
            Editor.Id = result.Value;
            StatusMessage = "Doctor saved.";
            await SearchAsync(SearchText);
        });
    }
}

public class ManufacturerTabViewModel : MasterTabViewModelBase
{
    private readonly IMastersService _masters;
    private ManufacturerListDto? _selected;
    private ManufacturerDetailDto _editor = new();

    public ManufacturerTabViewModel(IMastersService masters, ICurrentUserService currentUser, IDialogService dialog)
        : base(dialog, currentUser)
    {
        _masters = masters;
        _ = SearchAsync(string.Empty);
    }

    public ObservableCollection<ManufacturerListDto> Items { get; } = new();
    public Array EntityStatuses => Enum.GetValues(typeof(EntityStatus));

    public ManufacturerListDto? SelectedItem
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                _ = LoadItemAsync(value.Id);
        }
    }

    public ManufacturerDetailDto Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(EditorTitle));
                OnPropertyChanged(nameof(IsNewRecord));
            }
        }
    }

    public override string EditorTitle => Editor.Id > 0 ? $"Edit: {Editor.Name}" : "New Manufacturer";
    public override bool IsNewRecord => Editor.Id == 0;

    protected override async Task SearchAsync(string term, CancellationToken ct = default)
    {
        var rows = await _masters.SearchManufacturersAsync(term, ct);
        Items.Clear();
        foreach (var r in rows) Items.Add(r);
    }

    protected override async Task LoadItemAsync(int id)
    {
        var detail = await _masters.GetManufacturerAsync(id);
        if (detail is not null) Editor = detail;
    }

    protected override void BeginNew()
    {
        SelectedItem = null;
        Editor = new ManufacturerDetailDto();
    }

    protected override async Task SaveAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _masters.SaveManufacturerAsync(Editor);
            if (result.IsFailure)
            {
                Dialog.ShowError(result.Error ?? "Could not save manufacturer.");
                return;
            }
            Editor.Id = result.Value;
            StatusMessage = "Manufacturer saved.";
            await SearchAsync(SearchText);
        });
    }
}

public class EmployeeTabViewModel : MasterTabViewModelBase
{
    private readonly IMastersService _masters;
    private readonly Func<int?> _branchId;
    private EmployeeListDto? _selected;
    private EmployeeDetailDto _editor = new();

    public EmployeeTabViewModel(IMastersService masters, ICurrentUserService currentUser, IDialogService dialog)
        : base(dialog, currentUser)
    {
        _masters = masters;
        _branchId = () => currentUser.CurrentUser?.BranchId;
        _ = SearchAsync(string.Empty);
    }

    public ObservableCollection<EmployeeListDto> Items { get; } = new();
    public Array EntityStatuses => Enum.GetValues(typeof(EntityStatus));

    public EmployeeListDto? SelectedItem
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                _ = LoadItemAsync(value.Id);
        }
    }

    public EmployeeDetailDto Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(EditorTitle));
                OnPropertyChanged(nameof(IsNewRecord));
            }
        }
    }

    public override string EditorTitle => Editor.Id > 0 ? $"Edit: {Editor.Name}" : "New Employee";
    public override bool IsNewRecord => Editor.Id == 0;

    protected override async Task SearchAsync(string term, CancellationToken ct = default)
    {
        var rows = await _masters.SearchEmployeesAsync(term, _branchId(), ct);
        Items.Clear();
        foreach (var r in rows) Items.Add(r);
    }

    protected override async Task LoadItemAsync(int id)
    {
        var detail = await _masters.GetEmployeeAsync(id);
        if (detail is not null) Editor = detail;
    }

    protected override void BeginNew()
    {
        SelectedItem = null;
        Editor = new EmployeeDetailDto();
    }

    protected override async Task SaveAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _masters.SaveEmployeeAsync(Editor, _branchId());
            if (result.IsFailure)
            {
                Dialog.ShowError(result.Error ?? "Could not save employee.");
                return;
            }
            Editor.Id = result.Value;
            StatusMessage = "Employee saved.";
            await SearchAsync(SearchText);
        });
    }
}

public class MedicineTabViewModel : MasterTabViewModelBase
{
    private readonly IMastersService _masters;
    private MedicineListDto? _selected;
    private MedicineDetailDto _editor = new();
    private string? _searchHint;

    public MedicineTabViewModel(IMastersService masters, ICurrentUserService currentUser, IDialogService dialog)
        : base(dialog, currentUser)
    {
        _masters = masters;
    }

    public ObservableCollection<MedicineListDto> Items { get; } = new();
    public Array EntityStatuses => Enum.GetValues(typeof(EntityStatus));

    public string? SearchHint
    {
        get => _searchHint;
        private set => SetProperty(ref _searchHint, value);
    }

    public MedicineListDto? SelectedItem
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value) && value is not null)
                _ = LoadItemAsync(value.Id);
        }
    }

    public MedicineDetailDto Editor
    {
        get => _editor;
        private set
        {
            if (SetProperty(ref _editor, value))
            {
                OnPropertyChanged(nameof(EditorTitle));
                OnPropertyChanged(nameof(IsNewRecord));
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => Editor.Id > 0;
    public override string EditorTitle => Editor.Id > 0 ? Editor.Name : "Select a medicine";
    public override bool IsNewRecord => false;

    protected override async Task SearchAsync(string term, CancellationToken ct = default)
    {
        term = term.Trim();
        Items.Clear();
        SelectedItem = null;
        Editor = new MedicineDetailDto();

        if (term.Length < 2)
        {
            SearchHint = term.Length == 1 ? "Type at least 2 characters to search..." : "Search the medicine catalogue (min 2 chars).";
            return;
        }

        SearchHint = "Searching...";
        var rows = await _masters.SearchMedicinesAsync(term, ct);
        foreach (var r in rows) Items.Add(r);
        SearchHint = rows.Count == 0 ? $"No medicines found for \"{term}\"." : null;
    }

    protected override async Task LoadItemAsync(int id)
    {
        var detail = await _masters.GetMedicineAsync(id);
        if (detail is not null) Editor = detail;
    }

    protected override void BeginNew()
    {
        Dialog.ShowInfo("Medicines are imported in bulk. Search and edit an existing record.");
    }

    protected override async Task SaveAsync()
    {
        if (Editor.Id <= 0)
        {
            Dialog.ShowError("Select a medicine to update.");
            return;
        }

        await RunBusyAsync(async () =>
        {
            var result = await _masters.SaveMedicineAsync(Editor);
            if (result.IsFailure)
            {
                Dialog.ShowError(result.Error ?? "Could not save medicine.");
                return;
            }
            StatusMessage = "Medicine updated.";
            await SearchAsync(SearchText);
        });
    }
}
