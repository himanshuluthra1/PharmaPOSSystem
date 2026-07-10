using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Settings;

/// <summary>Company profile, branches, users and application preferences.</summary>
public interface ISettingsService
{
    Task<CompanyProfileDto?> GetCompanyProfileAsync(CancellationToken ct = default);
    Task<Result<int>> SaveCompanyProfileAsync(CompanyProfileDto dto, CancellationToken ct = default);

    Task<AppPreferencesDto> GetPreferencesAsync(CancellationToken ct = default);
    Task<Result> SavePreferencesAsync(AppPreferencesDto dto, CancellationToken ct = default);

    Task<List<BranchListDto>> ListBranchesAsync(CancellationToken ct = default);
    Task<BranchDetailDto?> GetBranchAsync(int id, CancellationToken ct = default);
    Task<Result<int>> SaveBranchAsync(BranchDetailDto dto, CancellationToken ct = default);

    Task<List<UserListDto>> ListUsersAsync(string term, CancellationToken ct = default);
    Task<UserDetailDto?> GetUserAsync(int id, CancellationToken ct = default);
    Task<Result<int>> SaveUserAsync(UserDetailDto dto, string? newPassword, CancellationToken ct = default);
    Task<Result> ResetUserPasswordAsync(int userId, string newPassword, CancellationToken ct = default);
    Task<List<RoleListDto>> ListRolesAsync(CancellationToken ct = default);

    Task<List<PermissionDto>> ListPermissionsAsync(CancellationToken ct = default);
    Task<RolePermissionsDto?> GetRolePermissionsAsync(int roleId, CancellationToken ct = default);
    Task<Result> SaveRolePermissionsAsync(int roleId, IEnumerable<string> permissionKeys, CancellationToken ct = default);
}
