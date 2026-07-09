using PharmaPOS.Domain.Entities.Identity;

namespace PharmaPOS.Domain.Common;

/// <summary>
/// Base type for all persisted entities. Provides an identity key plus audit and
/// soft-delete metadata that the persistence layer populates automatically.
/// </summary>
public abstract class BaseEntity
{
    public int Id { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? ModifiedAtUtc { get; set; }
    public string? ModifiedBy { get; set; }

    /// <summary>Soft-delete flag. Deleted rows are filtered out via a global query filter.</summary>
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>
/// Base for entities that belong to a specific branch in a multi-branch deployment.
/// </summary>
public abstract class BranchEntity : BaseEntity
{
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
}
