using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Masters;

/// <summary>Default master-data service with prefix-based search for large catalogues.</summary>
public class MastersService : IMastersService
{
    private const int DefaultTake = 50;

    private readonly IUnitOfWork _uow;

    public MastersService(IUnitOfWork uow) => _uow = uow;

    #region Suppliers

    public async Task<List<SupplierListDto>> SearchSuppliersAsync(string term, int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<Supplier>().Query().AsNoTracking();
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId || s.BranchId == null);
        term = (term ?? string.Empty).Trim();
        var normalized = SearchQueryExtensions.NormalizeTerm(term);
        if (normalized.Length >= 1)
            q = q.WhereSupplierMatches(normalized);
        return await q.OrderBy(s => s.Name).Take(DefaultTake)
            .Select(s => new SupplierListDto(s.Id, s.Name, s.Phone, s.GstNumber, s.Status))
            .ToListAsync(ct);
    }

    public async Task<SupplierDetailDto?> GetSupplierAsync(int id, CancellationToken ct = default)
    {
        var s = await _uow.Repository<Supplier>().Query().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return s is null ? null : MapSupplier(s);
    }

    public async Task<Result<int>> SaveSupplierAsync(SupplierDetailDto dto, int? branchId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure<int>("Supplier name is required.");

        if (dto.Id > 0)
        {
            var entity = await _uow.Repository<Supplier>().GetByIdAsync(dto.Id, ct);
            if (entity is null) return Result.Failure<int>("Supplier not found.");
            ApplySupplier(entity, dto);
            _uow.Repository<Supplier>().Update(entity);
            await _uow.SaveChangesAsync(ct);
            return Result.Success(entity.Id);
        }

        var created = new Supplier { BranchId = branchId };
        ApplySupplier(created, dto);
        await _uow.Repository<Supplier>().AddAsync(created, ct);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(created.Id);
    }

    private static SupplierDetailDto MapSupplier(Supplier s) => new()
    {
        Id = s.Id, Name = s.Name, GstNumber = s.GstNumber, DrugLicenseNumber = s.DrugLicenseNumber,
        ContactPerson = s.ContactPerson, Phone = s.Phone, Email = s.Email,
        Address = s.Address, City = s.City, State = s.State, Pincode = s.Pincode,
        PaymentTermsDays = s.PaymentTermsDays, Status = s.Status
    };

    private static void ApplySupplier(Supplier s, SupplierDetailDto dto)
    {
        s.Name = dto.Name.Trim();
        s.GstNumber = dto.GstNumber;
        s.DrugLicenseNumber = dto.DrugLicenseNumber;
        s.ContactPerson = dto.ContactPerson;
        s.Phone = dto.Phone;
        s.Email = dto.Email;
        s.Address = dto.Address;
        s.City = dto.City;
        s.State = dto.State;
        s.Pincode = dto.Pincode;
        s.PaymentTermsDays = dto.PaymentTermsDays;
        s.Status = dto.Status;
    }

    #endregion

    #region Customers

    public async Task<List<CustomerListDto>> SearchCustomersAsync(string term, int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<Customer>().Query().AsNoTracking();
        if (branchId.HasValue) q = q.Where(c => c.BranchId == branchId || c.BranchId == null);
        term = (term ?? string.Empty).Trim();
        if (term.Length >= 1)
            q = q.Where(c => c.Name.Contains(term) || (c.Phone != null && c.Phone.Contains(term)));
        return await q.OrderBy(c => c.Name).Take(DefaultTake)
            .Select(c => new CustomerListDto(c.Id, c.Name, c.Phone, c.Type, c.Status))
            .ToListAsync(ct);
    }

    public async Task<CustomerDetailDto?> GetCustomerAsync(int id, CancellationToken ct = default)
    {
        var c = await _uow.Repository<Customer>().Query().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is null ? null : MapCustomer(c);
    }

    public async Task<Result<int>> SaveCustomerAsync(CustomerDetailDto dto, int? branchId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure<int>("Customer name is required.");

        if (dto.Id > 0)
        {
            var entity = await _uow.Repository<Customer>().GetByIdAsync(dto.Id, ct);
            if (entity is null) return Result.Failure<int>("Customer not found.");
            ApplyCustomer(entity, dto);
            _uow.Repository<Customer>().Update(entity);
            await _uow.SaveChangesAsync(ct);
            return Result.Success(entity.Id);
        }

        var created = new Customer { BranchId = branchId };
        ApplyCustomer(created, dto);
        await _uow.Repository<Customer>().AddAsync(created, ct);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(created.Id);
    }

    private static CustomerDetailDto MapCustomer(Customer c) => new()
    {
        Id = c.Id, Name = c.Name, Type = c.Type, Phone = c.Phone, Email = c.Email,
        GstNumber = c.GstNumber, Address = c.Address, City = c.City,
        CreditLimit = c.CreditLimit, Status = c.Status
    };

    private static void ApplyCustomer(Customer c, CustomerDetailDto dto)
    {
        c.Name = dto.Name.Trim();
        c.Type = dto.Type;
        c.Phone = dto.Phone;
        c.Email = dto.Email;
        c.GstNumber = dto.GstNumber;
        c.Address = dto.Address;
        c.City = dto.City;
        c.CreditLimit = dto.CreditLimit;
        c.Status = dto.Status;
    }

    #endregion

    #region Doctors

    public async Task<List<DoctorListDto>> SearchDoctorsAsync(string term, CancellationToken ct = default)
    {
        var q = _uow.Repository<Doctor>().Query().AsNoTracking();
        term = (term ?? string.Empty).Trim();
        if (term.Length >= 1) q = q.Where(d => d.Name.Contains(term));
        return await q.OrderBy(d => d.Name).Take(DefaultTake)
            .Select(d => new DoctorListDto(d.Id, d.Name, d.Specialization, d.Phone, d.Status))
            .ToListAsync(ct);
    }

    public async Task<DoctorDetailDto?> GetDoctorAsync(int id, CancellationToken ct = default)
    {
        var d = await _uow.Repository<Doctor>().Query().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return d is null ? null : MapDoctor(d);
    }

    public async Task<Result<int>> SaveDoctorAsync(DoctorDetailDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure<int>("Doctor name is required.");

        if (dto.Id > 0)
        {
            var entity = await _uow.Repository<Doctor>().GetByIdAsync(dto.Id, ct);
            if (entity is null) return Result.Failure<int>("Doctor not found.");
            ApplyDoctor(entity, dto);
            _uow.Repository<Doctor>().Update(entity);
            await _uow.SaveChangesAsync(ct);
            return Result.Success(entity.Id);
        }

        var created = new Doctor();
        ApplyDoctor(created, dto);
        await _uow.Repository<Doctor>().AddAsync(created, ct);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(created.Id);
    }

    private static DoctorDetailDto MapDoctor(Doctor d) => new()
    {
        Id = d.Id, Name = d.Name, Qualification = d.Qualification,
        Specialization = d.Specialization, RegistrationNumber = d.RegistrationNumber,
        Hospital = d.Hospital, Phone = d.Phone, Email = d.Email, Status = d.Status
    };

    private static void ApplyDoctor(Doctor d, DoctorDetailDto dto)
    {
        d.Name = dto.Name.Trim();
        d.Qualification = dto.Qualification;
        d.Specialization = dto.Specialization;
        d.RegistrationNumber = dto.RegistrationNumber;
        d.Hospital = dto.Hospital;
        d.Phone = dto.Phone;
        d.Email = dto.Email;
        d.Status = dto.Status;
    }

    #endregion

    #region Manufacturers

    public async Task<List<ManufacturerListDto>> SearchManufacturersAsync(string term, CancellationToken ct = default)
    {
        var q = _uow.Repository<Manufacturer>().Query().AsNoTracking();
        term = (term ?? string.Empty).Trim();
        if (term.Length >= 1) q = q.Where(m => m.Name.Contains(term));
        return await q.OrderBy(m => m.Name).Take(DefaultTake)
            .Select(m => new ManufacturerListDto(m.Id, m.Name, m.City, m.Phone, m.Status))
            .ToListAsync(ct);
    }

    public async Task<ManufacturerDetailDto?> GetManufacturerAsync(int id, CancellationToken ct = default)
    {
        var m = await _uow.Repository<Manufacturer>().Query().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return m is null ? null : MapManufacturer(m);
    }

    public async Task<Result<int>> SaveManufacturerAsync(ManufacturerDetailDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure<int>("Manufacturer name is required.");

        if (dto.Id > 0)
        {
            var entity = await _uow.Repository<Manufacturer>().GetByIdAsync(dto.Id, ct);
            if (entity is null) return Result.Failure<int>("Manufacturer not found.");
            ApplyManufacturer(entity, dto);
            _uow.Repository<Manufacturer>().Update(entity);
            await _uow.SaveChangesAsync(ct);
            return Result.Success(entity.Id);
        }

        var created = new Manufacturer();
        ApplyManufacturer(created, dto);
        await _uow.Repository<Manufacturer>().AddAsync(created, ct);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(created.Id);
    }

    private static ManufacturerDetailDto MapManufacturer(Manufacturer m) => new()
    {
        Id = m.Id, Name = m.Name, GstNumber = m.GstNumber, LicenseNumber = m.LicenseNumber,
        ContactPerson = m.ContactPerson, Phone = m.Phone, Email = m.Email,
        Address = m.Address, City = m.City, State = m.State, Pincode = m.Pincode, Status = m.Status
    };

    private static void ApplyManufacturer(Manufacturer m, ManufacturerDetailDto dto)
    {
        m.Name = dto.Name.Trim();
        m.GstNumber = dto.GstNumber;
        m.LicenseNumber = dto.LicenseNumber;
        m.ContactPerson = dto.ContactPerson;
        m.Phone = dto.Phone;
        m.Email = dto.Email;
        m.Address = dto.Address;
        m.City = dto.City;
        m.State = dto.State;
        m.Pincode = dto.Pincode;
        m.Status = dto.Status;
    }

    #endregion

    #region Employees

    public async Task<List<EmployeeListDto>> SearchEmployeesAsync(string term, int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<Employee>().Query().AsNoTracking();
        if (branchId.HasValue) q = q.Where(e => e.BranchId == branchId || e.BranchId == null);
        term = (term ?? string.Empty).Trim();
        if (term.Length >= 1)
            q = q.Where(e => e.Name.Contains(term) || e.Code.Contains(term));
        return await q.OrderBy(e => e.Name).Take(DefaultTake)
            .Select(e => new EmployeeListDto(e.Id, e.Code, e.Name, e.Designation, e.Status))
            .ToListAsync(ct);
    }

    public async Task<EmployeeDetailDto?> GetEmployeeAsync(int id, CancellationToken ct = default)
    {
        var e = await _uow.Repository<Employee>().Query().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return e is null ? null : MapEmployee(e);
    }

    public async Task<Result<int>> SaveEmployeeAsync(EmployeeDetailDto dto, int? branchId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure<int>("Employee name is required.");
        if (string.IsNullOrWhiteSpace(dto.Code))
            return Result.Failure<int>("Employee code is required.");

        if (dto.Id > 0)
        {
            var entity = await _uow.Repository<Employee>().GetByIdAsync(dto.Id, ct);
            if (entity is null) return Result.Failure<int>("Employee not found.");
            ApplyEmployee(entity, dto);
            _uow.Repository<Employee>().Update(entity);
            await _uow.SaveChangesAsync(ct);
            return Result.Success(entity.Id);
        }

        var created = new Employee { BranchId = branchId };
        ApplyEmployee(created, dto);
        await _uow.Repository<Employee>().AddAsync(created, ct);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(created.Id);
    }

    private static EmployeeDetailDto MapEmployee(Employee e) => new()
    {
        Id = e.Id, Code = e.Code, Name = e.Name, Designation = e.Designation,
        Phone = e.Phone, Email = e.Email, Address = e.Address,
        DateOfJoining = e.DateOfJoining, Salary = e.Salary,
        CommissionPercent = e.CommissionPercent, Shift = e.Shift, Status = e.Status
    };

    private static void ApplyEmployee(Employee e, EmployeeDetailDto dto)
    {
        e.Code = dto.Code.Trim();
        e.Name = dto.Name.Trim();
        e.Designation = dto.Designation;
        e.Phone = dto.Phone;
        e.Email = dto.Email;
        e.Address = dto.Address;
        e.DateOfJoining = dto.DateOfJoining;
        e.Salary = dto.Salary;
        e.CommissionPercent = dto.CommissionPercent;
        e.Shift = dto.Shift;
        e.Status = dto.Status;
    }

    #endregion

    #region Medicines

    public async Task<List<MedicineListDto>> SearchMedicinesAsync(string term, CancellationToken ct = default)
    {
        var normalized = SearchQueryExtensions.NormalizeTerm(term);
        if (normalized.Length < 2) return new();

        var baseQuery = _uow.Repository<Medicine>().Query().AsNoTracking()
            .Where(m => m.Status == EntityStatus.Active);

        var results = await baseQuery
            .WhereMedicineMatches(normalized, prefixOnly: true)
            .OrderBy(m => m.Name).Take(DefaultTake)
            .Select(m => new MedicineListDto(m.Id, m.Name, m.GenericName, m.Mrp, m.PurchasePrice, m.Status))
            .ToListAsync(ct);

        if (results.Count == 0)
        {
            results = await baseQuery
                .WhereMedicineMatches(normalized, prefixOnly: false)
                .OrderBy(m => m.Name).Take(DefaultTake)
                .Select(m => new MedicineListDto(m.Id, m.Name, m.GenericName, m.Mrp, m.PurchasePrice, m.Status))
                .ToListAsync(ct);
        }

        return results;
    }

    public async Task<MedicineDetailDto?> GetMedicineAsync(int id, CancellationToken ct = default)
    {
        var m = await _uow.Repository<Medicine>().Query().AsNoTracking()
            .Include(x => x.Manufacturer)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return null;

        return new MedicineDetailDto
        {
            Id = m.Id, Name = m.Name, GenericName = m.GenericName, Brand = m.Brand,
            Barcode = m.Barcode, HsnCode = m.HsnCode, GstPercent = m.GstPercent,
            Mrp = m.Mrp, PurchasePrice = m.PurchasePrice, SellingPrice = m.SellingPrice,
            DefaultDiscountPercent = m.DefaultDiscountPercent,
            ReorderLevel = m.ReorderLevel, ReorderQuantity = m.ReorderQuantity,
            PrescriptionRequired = m.PrescriptionRequired, Status = m.Status,
            ManufacturerName = m.Manufacturer?.Name
        };
    }

    public async Task<Result<int>> SaveMedicineAsync(MedicineDetailDto dto, CancellationToken ct = default)
    {
        if (dto.Id <= 0)
            return Result.Failure<int>("Select a medicine to update.");

        var entity = await _uow.Repository<Medicine>().GetByIdAsync(dto.Id, ct);
        if (entity is null) return Result.Failure<int>("Medicine not found.");

        entity.Barcode = dto.Barcode;
        entity.HsnCode = dto.HsnCode;
        entity.GstPercent = dto.GstPercent;
        entity.Mrp = dto.Mrp;
        entity.PurchasePrice = dto.PurchasePrice;
        entity.SellingPrice = dto.SellingPrice;
        entity.DefaultDiscountPercent = dto.DefaultDiscountPercent;
        entity.ReorderLevel = dto.ReorderLevel;
        entity.ReorderQuantity = dto.ReorderQuantity;
        entity.PrescriptionRequired = dto.PrescriptionRequired;
        entity.Status = dto.Status;

        _uow.Repository<Medicine>().Update(entity);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(entity.Id);
    }

    #endregion
}
