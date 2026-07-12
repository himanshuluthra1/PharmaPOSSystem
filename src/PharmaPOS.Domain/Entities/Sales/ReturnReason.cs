using PharmaPOS.Domain.Common;

namespace PharmaPOS.Domain.Entities.Sales;

/// <summary>Configurable return reason master.</summary>
public class ReturnReason : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool RequiresRemarks { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}
