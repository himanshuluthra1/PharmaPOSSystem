using PharmaPOS.Domain.Common;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Domain.Entities.Sales;

/// <summary>A tender against a sale. Multiple rows support split payments.</summary>
public class SalePayment : BaseEntity
{
    public int SaleId { get; set; }
    public Sale? Sale { get; set; }

    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? ReferenceNumber { get; set; }
    public DateTime PaymentDateUtc { get; set; } = DateTime.UtcNow;
}
