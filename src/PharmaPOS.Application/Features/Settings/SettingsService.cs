using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Identity;
using PharmaPOS.Domain.Entities.System;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Settings;

/// <summary>Manages company profile, branches, users and operational preferences.</summary>
public class SettingsService : ISettingsService
{
    private readonly IUnitOfWork _uow;
    private readonly IPasswordHasher _passwordHasher;

    public SettingsService(IUnitOfWork uow, IPasswordHasher passwordHasher)
    {
        _uow = uow;
        _passwordHasher = passwordHasher;
    }

    public async Task<CompanyProfileDto?> GetCompanyProfileAsync(CancellationToken ct = default)
    {
        var entity = await _uow.Repository<CompanyProfile>().Query().AsNoTracking()
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(ct);
        return entity is null ? null : MapCompany(entity);
    }

    public async Task<Result<int>> SaveCompanyProfileAsync(CompanyProfileDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.CompanyName))
            return Result.Failure<int>("Company name is required.");

        CompanyProfile entity;
        if (dto.Id > 0)
        {
            var existingEntity = await _uow.Repository<CompanyProfile>().GetByIdAsync(dto.Id, ct);
            if (existingEntity is null)
                return Result.Failure<int>("Company profile not found.");
            entity = existingEntity;
        }
        else
        {
            var existing = await _uow.Repository<CompanyProfile>().Query().AnyAsync(ct);
            if (existing)
                return Result.Failure<int>("A company profile already exists.");

            entity = new CompanyProfile();
            await _uow.Repository<CompanyProfile>().AddAsync(entity, ct);
        }

        ApplyCompany(entity, dto);
        if (dto.Id > 0)
            _uow.Repository<CompanyProfile>().Update(entity);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(entity.Id);
    }

    public async Task<AppPreferencesDto> GetPreferencesAsync(CancellationToken ct = default)
    {
        var prefs = await _uow.Repository<CompanyProfile>().Query().AsNoTracking()
            .OrderBy(c => c.Id)
            .Select(c => new AppPreferencesDto
            {
                NearExpiryDays = c.NearExpiryDays,
                DefaultLowStockThreshold = c.DefaultLowStockThreshold,
                SalesInvoicePrefix = c.SalesInvoicePrefix,
                PurchaseInvoicePrefix = c.PurchaseInvoicePrefix
            })
            .FirstOrDefaultAsync(ct);

        return prefs ?? new AppPreferencesDto
        {
            NearExpiryDays = AppConstants.Config.NearExpiryDays,
            DefaultLowStockThreshold = AppConstants.Config.DefaultLowStockThreshold
        };
    }

    public async Task<Result> SavePreferencesAsync(AppPreferencesDto dto, CancellationToken ct = default)
    {
        if (dto.NearExpiryDays < 1 || dto.NearExpiryDays > 365)
            return Result.Failure("Near-expiry days must be between 1 and 365.");

        if (dto.DefaultLowStockThreshold < 0)
            return Result.Failure("Low stock threshold cannot be negative.");

        if (string.IsNullOrWhiteSpace(dto.SalesInvoicePrefix))
            return Result.Failure("Sales invoice prefix is required.");

        if (string.IsNullOrWhiteSpace(dto.PurchaseInvoicePrefix))
            return Result.Failure("Purchase invoice prefix is required.");

        var entity = await _uow.Repository<CompanyProfile>().Query()
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(ct);

        if (entity is null)
            return Result.Failure("Company profile not found. Save company details first.");

        entity.NearExpiryDays = dto.NearExpiryDays;
        entity.DefaultLowStockThreshold = dto.DefaultLowStockThreshold;
        entity.SalesInvoicePrefix = dto.SalesInvoicePrefix.Trim().ToUpperInvariant();
        entity.PurchaseInvoicePrefix = dto.PurchaseInvoicePrefix.Trim().ToUpperInvariant();
        _uow.Repository<CompanyProfile>().Update(entity);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<List<BranchListDto>> ListBranchesAsync(CancellationToken ct = default)
        => await _uow.Repository<Branch>().Query().AsNoTracking()
            .OrderBy(b => b.IsHeadOffice ? 0 : 1).ThenBy(b => b.Name)
            .Select(b => new BranchListDto(b.Id, b.Code, b.Name, b.City, b.IsHeadOffice, b.Status))
            .ToListAsync(ct);

    public async Task<BranchDetailDto?> GetBranchAsync(int id, CancellationToken ct = default)
    {
        var b = await _uow.Repository<Branch>().Query().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return b is null ? null : MapBranch(b);
    }

    public async Task<Result<int>> SaveBranchAsync(BranchDetailDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Code))
            return Result.Failure<int>("Branch code is required.");

        if (string.IsNullOrWhiteSpace(dto.Name))
            return Result.Failure<int>("Branch name is required.");

        var code = dto.Code.Trim().ToUpperInvariant();
        var codeTaken = await _uow.Repository<Branch>().Query()
            .AnyAsync(b => b.Code == code && b.Id != dto.Id, ct);
        if (codeTaken)
            return Result.Failure<int>($"Branch code '{code}' is already in use.");

        Branch entity;
        if (dto.Id > 0)
        {
            var existingEntity = await _uow.Repository<Branch>().GetByIdAsync(dto.Id, ct);
            if (existingEntity is null)
                return Result.Failure<int>("Branch not found.");
            entity = existingEntity;
        }
        else
        {
            entity = new Branch();
            await _uow.Repository<Branch>().AddAsync(entity, ct);
        }

        if (dto.IsHeadOffice)
        {
            var others = await _uow.Repository<Branch>().Query()
                .Where(b => b.IsHeadOffice && b.Id != dto.Id)
                .ToListAsync(ct);
            foreach (var other in others)
                other.IsHeadOffice = false;
        }

        ApplyBranch(entity, dto, code);
        if (dto.Id > 0)
            _uow.Repository<Branch>().Update(entity);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(entity.Id);
    }

    public async Task<List<UserListDto>> ListUsersAsync(string term, CancellationToken ct = default)
    {
        IQueryable<User> q = _uow.Repository<User>().Query().AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.Branch);

        term = (term ?? string.Empty).Trim();
        if (term.Length >= 1)
            q = q.Where(u => u.Username.Contains(term) || u.FullName.Contains(term));

        return await q.OrderBy(u => u.FullName)
            .Select(u => new UserListDto(
                u.Id, u.Username, u.FullName,
                u.Role != null ? u.Role.Name : string.Empty,
                u.Branch != null ? u.Branch.Name : null,
                u.Status))
            .ToListAsync(ct);
    }

    public async Task<UserDetailDto?> GetUserAsync(int id, CancellationToken ct = default)
    {
        var u = await _uow.Repository<User>().Query().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        return u is null ? null : MapUser(u);
    }

    public async Task<Result<int>> SaveUserAsync(UserDetailDto dto, string? newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Username))
            return Result.Failure<int>("Username is required.");

        if (string.IsNullOrWhiteSpace(dto.FullName))
            return Result.Failure<int>("Full name is required.");

        if (dto.RoleId <= 0)
            return Result.Failure<int>("Role is required.");

        var username = dto.Username.Trim();
        var usernameTaken = await _uow.Repository<User>().Query()
            .AnyAsync(u => u.Username == username && u.Id != dto.Id, ct);
        if (usernameTaken)
            return Result.Failure<int>($"Username '{username}' is already in use.");

        User entity;
        if (dto.Id > 0)
        {
            var existingEntity = await _uow.Repository<User>().GetByIdAsync(dto.Id, ct);
            if (existingEntity is null)
                return Result.Failure<int>("User not found.");
            entity = existingEntity;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(newPassword))
                return Result.Failure<int>("Password is required for new users.");

            entity = new User();
            await _uow.Repository<User>().AddAsync(entity, ct);
        }

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            if (newPassword.Length < 6)
                return Result.Failure<int>("Password must be at least 6 characters.");

            entity.PasswordHash = _passwordHasher.Hash(newPassword);
            entity.MustChangePassword = dto.MustChangePassword;
            entity.FailedLoginAttempts = 0;
            entity.IsLockedOut = false;
            entity.LockoutEndUtc = null;
        }

        entity.Username = username;
        entity.FullName = dto.FullName.Trim();
        entity.Email = dto.Email;
        entity.Phone = dto.Phone;
        entity.RoleId = dto.RoleId;
        entity.BranchId = dto.BranchId;
        entity.Status = dto.Status;

        if (dto.Id > 0)
            _uow.Repository<User>().Update(entity);
        await _uow.SaveChangesAsync(ct);
        return Result.Success(entity.Id);
    }

    public async Task<Result> ResetUserPasswordAsync(int userId, string newPassword, CancellationToken ct = default)
    {
        if (newPassword.Length < 6)
            return Result.Failure("Password must be at least 6 characters.");

        var user = await _uow.Repository<User>().GetByIdAsync(userId, ct);
        if (user is null)
            return Result.Failure("User not found.");

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        user.MustChangePassword = true;
        user.FailedLoginAttempts = 0;
        user.IsLockedOut = false;
        user.LockoutEndUtc = null;
        _uow.Repository<User>().Update(user);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<List<RoleListDto>> ListRolesAsync(CancellationToken ct = default)
        => await _uow.Repository<Role>().Query().AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleListDto(r.Id, r.Name, r.IsSystemRole))
            .ToListAsync(ct);

    public async Task<List<PermissionDto>> ListPermissionsAsync(CancellationToken ct = default)
        => await _uow.Repository<Permission>().Query().AsNoTracking()
            .OrderBy(p => p.Module).ThenBy(p => p.Name)
            .Select(p => new PermissionDto(p.Id, p.Key, p.Name, p.Module))
            .ToListAsync(ct);

    public async Task<RolePermissionsDto?> GetRolePermissionsAsync(int roleId, CancellationToken ct = default)
    {
        var role = await _uow.Repository<Role>().Query().AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null) return null;

        var all = await ListPermissionsAsync(ct);
        var granted = await _uow.Repository<RolePermission>().Query().AsNoTracking()
            .Where(rp => rp.RoleId == roleId)
            .Include(rp => rp.Permission)
            .Select(rp => rp.Permission!.Key)
            .ToListAsync(ct);

        return new RolePermissionsDto
        {
            RoleId = role.Id,
            RoleName = role.Name,
            IsSystemRole = role.IsSystemRole,
            AllPermissions = all,
            GrantedPermissionKeys = granted.ToHashSet(StringComparer.Ordinal)
        };
    }

    public async Task<Result> SaveRolePermissionsAsync(int roleId, IEnumerable<string> permissionKeys, CancellationToken ct = default)
    {
        var role = await _uow.Repository<Role>().GetByIdAsync(roleId, ct);
        if (role is null)
            return Result.Failure("Role not found.");

        var keys = permissionKeys.Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(k => k.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (keys.Count == 0)
            return Result.Failure("Select at least one permission for this role.");

        var permissions = await _uow.Repository<Permission>().Query()
            .Where(p => keys.Contains(p.Key))
            .ToListAsync(ct);

        if (permissions.Count != keys.Count)
            return Result.Failure("One or more permission keys are invalid.");

        var desiredPermissionIds = permissions.Select(p => p.Id).ToHashSet();

        var existing = await _uow.Repository<RolePermission>().QueryIncludingDeleted()
            .Where(rp => rp.RoleId == roleId)
            .ToListAsync(ct);

        var existingByPermissionId = existing.ToDictionary(rp => rp.PermissionId);

        foreach (var permission in permissions)
        {
            if (existingByPermissionId.TryGetValue(permission.Id, out var row))
            {
                if (row.IsDeleted)
                {
                    row.IsDeleted = false;
                    row.DeletedAtUtc = null;
                    row.DeletedBy = null;
                    _uow.Repository<RolePermission>().Update(row);
                }
                continue;
            }

            await _uow.Repository<RolePermission>().AddAsync(new RolePermission
            {
                RoleId = roleId,
                PermissionId = permission.Id
            }, ct);
        }

        foreach (var row in existing.Where(rp => !rp.IsDeleted && !desiredPermissionIds.Contains(rp.PermissionId)))
            _uow.Repository<RolePermission>().Remove(row);

        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static CompanyProfileDto MapCompany(CompanyProfile c) => new()
    {
        Id = c.Id,
        CompanyName = c.CompanyName,
        LegalName = c.LegalName,
        Address = c.Address,
        City = c.City,
        State = c.State,
        Pincode = c.Pincode,
        Phone = c.Phone,
        Email = c.Email,
        Website = c.Website,
        GstNumber = c.GstNumber,
        DrugLicenseNumber = c.DrugLicenseNumber,
        Pan = c.Pan,
        LogoPath = c.LogoPath,
        InvoiceFooter = c.InvoiceFooter,
        Currency = c.Currency,
        CurrencySymbol = c.CurrencySymbol
    };

    private static void ApplyCompany(CompanyProfile c, CompanyProfileDto dto)
    {
        c.CompanyName = dto.CompanyName.Trim();
        c.LegalName = dto.LegalName;
        c.Address = dto.Address;
        c.City = dto.City;
        c.State = dto.State;
        c.Pincode = dto.Pincode;
        c.Phone = dto.Phone;
        c.Email = dto.Email;
        c.Website = dto.Website;
        c.GstNumber = dto.GstNumber;
        c.DrugLicenseNumber = dto.DrugLicenseNumber;
        c.Pan = dto.Pan;
        c.LogoPath = dto.LogoPath;
        c.InvoiceFooter = dto.InvoiceFooter;
        c.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "INR" : dto.Currency.Trim();
        c.CurrencySymbol = dto.CurrencySymbol;
    }

    private static BranchDetailDto MapBranch(Branch b) => new()
    {
        Id = b.Id,
        Code = b.Code,
        Name = b.Name,
        Address = b.Address,
        City = b.City,
        State = b.State,
        Pincode = b.Pincode,
        Phone = b.Phone,
        Email = b.Email,
        GstNumber = b.GstNumber,
        DrugLicenseNumber = b.DrugLicenseNumber,
        IsHeadOffice = b.IsHeadOffice,
        Status = b.Status
    };

    private static void ApplyBranch(Branch b, BranchDetailDto dto, string code)
    {
        b.Code = code;
        b.Name = dto.Name.Trim();
        b.Address = dto.Address;
        b.City = dto.City;
        b.State = dto.State;
        b.Pincode = dto.Pincode;
        b.Phone = dto.Phone;
        b.Email = dto.Email;
        b.GstNumber = dto.GstNumber;
        b.DrugLicenseNumber = dto.DrugLicenseNumber;
        b.IsHeadOffice = dto.IsHeadOffice;
        b.Status = dto.Status;
    }

    private static UserDetailDto MapUser(User u) => new()
    {
        Id = u.Id,
        Username = u.Username,
        FullName = u.FullName,
        Email = u.Email,
        Phone = u.Phone,
        RoleId = u.RoleId,
        BranchId = u.BranchId,
        Status = u.Status,
        MustChangePassword = u.MustChangePassword
    };
}
