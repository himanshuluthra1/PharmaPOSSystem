using PharmaPOS.Domain.Common;

namespace PharmaPOS.Domain.Entities.Identity;

/// <summary>A granular, module-level capability that can be granted to roles.</summary>
public class Permission : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Module { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

/// <summary>Join entity mapping <see cref="Role"/> to <see cref="Permission"/>.</summary>
public class RolePermission : BaseEntity
{
    public int RoleId { get; set; }
    public Role? Role { get; set; }

    public int PermissionId { get; set; }
    public Permission? Permission { get; set; }
}
