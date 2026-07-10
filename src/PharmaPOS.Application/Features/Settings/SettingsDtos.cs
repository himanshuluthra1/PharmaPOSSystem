using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Settings;

public class CompanyProfileDto
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string? LegalName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Website { get; set; }
    public string? GstNumber { get; set; }
    public string? DrugLicenseNumber { get; set; }
    public string? Pan { get; set; }
    public string? LogoPath { get; set; }
    public string? InvoiceFooter { get; set; }
    public string Currency { get; set; } = "INR";
    public string? CurrencySymbol { get; set; } = "\u20B9";
}

public class AppPreferencesDto
{
    public int NearExpiryDays { get; set; } = 90;
    public int DefaultLowStockThreshold { get; set; } = 10;
    public string SalesInvoicePrefix { get; set; } = "INV";
    public string PurchaseInvoicePrefix { get; set; } = "PUR";
}

public record BranchListDto(int Id, string Code, string Name, string? City, bool IsHeadOffice, EntityStatus Status);

public class BranchDetailDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Pincode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GstNumber { get; set; }
    public string? DrugLicenseNumber { get; set; }
    public bool IsHeadOffice { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
}

public record UserListDto(int Id, string Username, string FullName, string RoleName, string? BranchName, EntityStatus Status);

public class UserDetailDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int RoleId { get; set; }
    public int? BranchId { get; set; }
    public EntityStatus Status { get; set; } = EntityStatus.Active;
    public bool MustChangePassword { get; set; }
}

public record RoleListDto(int Id, string Name, bool IsSystemRole);

public record PermissionDto(int Id, string Key, string Name, string? Module);

public class RolePermissionsDto
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public bool IsSystemRole { get; set; }
    public List<PermissionDto> AllPermissions { get; set; } = new();
    public HashSet<string> GrantedPermissionKeys { get; set; } = new(StringComparer.Ordinal);
}
