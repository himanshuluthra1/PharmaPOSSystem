using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Masters;

// ── List rows (grid) ──────────────────────────────────────────────────────────

public record SupplierListDto(int Id, string Name, string? Phone, string? GstNumber, EntityStatus Status);
public record CustomerListDto(int Id, string Name, string? Phone, CustomerType Type, EntityStatus Status);
public record DoctorListDto(int Id, string Name, string? Specialization, string? Phone, EntityStatus Status);
public record ManufacturerListDto(int Id, string Name, string? City, string? Phone, EntityStatus Status);
public record EmployeeListDto(int Id, string Code, string Name, string? Designation, EntityStatus Status);
public record MedicineListDto(int Id, string Name, string? GenericName, decimal Mrp, decimal PurchasePrice, EntityStatus Status);

// ── Editor forms (detail) ───────────────────────────────────────────────────

public class SupplierDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? DrugLicenseNumber { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public int PaymentTermsDays { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}

public class CustomerDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public CustomerType Type { get; set; } = CustomerType.Retail;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GstNumber { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public decimal CreditLimit { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}

public class DoctorDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Qualification { get; set; }
    public string? Specialization { get; set; }
    public string? RegistrationNumber { get; set; }
    public string? Hospital { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}

public class ManufacturerDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GstNumber { get; set; }
    public string? LicenseNumber { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}

public class EmployeeDetailDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public DateTime? DateOfJoining { get; set; }
    public decimal Salary { get; set; }
    public decimal CommissionPercent { get; set; }
    public string? Shift { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}

public class MedicineDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Brand { get; set; }
    public string? Barcode { get; set; }
    public string? HsnCode { get; set; }
    public decimal GstPercent { get; set; }
    public decimal Mrp { get; set; }
    public decimal PurchasePrice { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal DefaultDiscountPercent { get; set; }
    public int ReorderLevel { get; set; }
    public int ReorderQuantity { get; set; }
    public bool PrescriptionRequired { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public string? ManufacturerName { get; set; }
}
