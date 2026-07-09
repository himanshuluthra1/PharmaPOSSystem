using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Masters;

/// <summary>CRUD operations for master data entities.</summary>
public interface IMastersService
{
    // Suppliers
    Task<List<SupplierListDto>> SearchSuppliersAsync(string term, int? branchId, CancellationToken ct = default);
    Task<SupplierDetailDto?> GetSupplierAsync(int id, CancellationToken ct = default);
    Task<Result<int>> SaveSupplierAsync(SupplierDetailDto dto, int? branchId, CancellationToken ct = default);

    // Customers
    Task<List<CustomerListDto>> SearchCustomersAsync(string term, int? branchId, CancellationToken ct = default);
    Task<CustomerDetailDto?> GetCustomerAsync(int id, CancellationToken ct = default);
    Task<Result<int>> SaveCustomerAsync(CustomerDetailDto dto, int? branchId, CancellationToken ct = default);

    // Doctors
    Task<List<DoctorListDto>> SearchDoctorsAsync(string term, CancellationToken ct = default);
    Task<DoctorDetailDto?> GetDoctorAsync(int id, CancellationToken ct = default);
    Task<Result<int>> SaveDoctorAsync(DoctorDetailDto dto, CancellationToken ct = default);

    // Manufacturers
    Task<List<ManufacturerListDto>> SearchManufacturersAsync(string term, CancellationToken ct = default);
    Task<ManufacturerDetailDto?> GetManufacturerAsync(int id, CancellationToken ct = default);
    Task<Result<int>> SaveManufacturerAsync(ManufacturerDetailDto dto, CancellationToken ct = default);

    // Employees
    Task<List<EmployeeListDto>> SearchEmployeesAsync(string term, int? branchId, CancellationToken ct = default);
    Task<EmployeeDetailDto?> GetEmployeeAsync(int id, CancellationToken ct = default);
    Task<Result<int>> SaveEmployeeAsync(EmployeeDetailDto dto, int? branchId, CancellationToken ct = default);

    // Medicines (search + update key fields; catalogue is bulk-imported)
    Task<List<MedicineListDto>> SearchMedicinesAsync(string term, CancellationToken ct = default);
    Task<MedicineDetailDto?> GetMedicineAsync(int id, CancellationToken ct = default);
    Task<Result<int>> SaveMedicineAsync(MedicineDetailDto dto, CancellationToken ct = default);
}
