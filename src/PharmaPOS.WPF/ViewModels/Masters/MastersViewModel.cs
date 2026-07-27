using Microsoft.Extensions.DependencyInjection;
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
        IServiceProvider services,
        ICurrentUserService currentUser,
        IDialogService dialog,
        IBarcodeCodec barcodeCodec,
        IBarcodeCameraService barcodeCamera,
        IBarcodeLabelService barcodeLabel)
    {
        // Each tab gets its own MastersService (and DbContext). Constructors kick off
        // parallel searches — sharing one context would throw concurrent-use errors.
        Suppliers = new SupplierTabViewModel(services.GetRequiredService<IMastersService>(), currentUser, dialog);
        Customers = new CustomerTabViewModel(services.GetRequiredService<IMastersService>(), currentUser, dialog);
        Doctors = new DoctorTabViewModel(services.GetRequiredService<IMastersService>(), currentUser, dialog);
        Manufacturers = new ManufacturerTabViewModel(services.GetRequiredService<IMastersService>(), currentUser, dialog);
        Employees = new EmployeeTabViewModel(services.GetRequiredService<IMastersService>(), currentUser, dialog);
        Medicines = new MedicineTabViewModel(
            services.GetRequiredService<IMastersService>(),
            currentUser,
            dialog,
            barcodeCodec,
            barcodeCamera,
            barcodeLabel);
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
