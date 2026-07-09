using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.WPF.Mvvm;
using PharmaPOS.WPF.Services;

namespace PharmaPOS.WPF.ViewModels.Masters;

/// <summary>Shell view model for the Master Data module with one tab per entity type.</summary>
public class MastersViewModel : ObservableObject
{
    private int _selectedTab;

    public MastersViewModel(
        IMastersService masters,
        ICurrentUserService currentUser,
        IDialogService dialog)
    {
        Suppliers = new SupplierTabViewModel(masters, currentUser, dialog);
        Customers = new CustomerTabViewModel(masters, currentUser, dialog);
        Doctors = new DoctorTabViewModel(masters, dialog);
        Manufacturers = new ManufacturerTabViewModel(masters, dialog);
        Employees = new EmployeeTabViewModel(masters, currentUser, dialog);
        Medicines = new MedicineTabViewModel(masters, dialog);
    }

    public SupplierTabViewModel Suppliers { get; }
    public CustomerTabViewModel Customers { get; }
    public DoctorTabViewModel Doctors { get; }
    public ManufacturerTabViewModel Manufacturers { get; }
    public EmployeeTabViewModel Employees { get; }
    public MedicineTabViewModel Medicines { get; }

    public int SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }
}
