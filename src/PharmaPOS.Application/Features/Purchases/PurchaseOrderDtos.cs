using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Purchases;

public record PurchaseOrderListItemDto(
    int Id,
    string OrderNumber,
    DateTime OrderDate,
    int SupplierId,
    string SupplierName,
    PurchaseStatus Status,
    decimal TotalAmount,
    int LineCount,
    decimal OrderedQty,
    decimal ReceivedQty);

public record PurchaseOrderLineDto(
    int Id,
    int MedicineId,
    string MedicineName,
    string? GenericName,
    decimal Quantity,
    decimal ReceivedQuantity,
    decimal RemainingQuantity,
    decimal EstimatedPrice,
    decimal LineTotal);

public record PurchaseOrderDetailDto(
    int Id,
    string OrderNumber,
    DateTime OrderDate,
    DateTime? ExpectedDate,
    int SupplierId,
    string SupplierName,
    PurchaseStatus Status,
    decimal TotalAmount,
    string? Remarks,
    IReadOnlyList<PurchaseOrderLineDto> Lines);

public class PurchaseOrderLineRequest
{
    public int MedicineId { get; set; }
    public decimal Quantity { get; set; }
    public decimal EstimatedPrice { get; set; }
}

public class SavePurchaseOrderRequest
{
    public int? Id { get; set; }
    public int SupplierId { get; set; }
    public DateTime OrderDate { get; set; }
    public DateTime? ExpectedDate { get; set; }
    public string? Remarks { get; set; }
    public List<PurchaseOrderLineRequest> Lines { get; set; } = new();
}

public record SuggestReorderResultDto(
    int DraftOrdersCreated,
    int MedicinesIncluded,
    int MedicinesSkippedNoSupplier,
    IReadOnlyList<PurchaseOrderListItemDto> CreatedOrders);

/// <summary>Payload used to prefill Purchase GRN from an open PO.</summary>
public record PurchaseOrderReceiveDraftDto(
    int PurchaseOrderId,
    string OrderNumber,
    int SupplierId,
    string SupplierName,
    IReadOnlyList<PurchaseOrderReceiveLineDto> Lines);

public record PurchaseOrderReceiveLineDto(
    int MedicineId,
    string MedicineName,
    string? GenericName,
    decimal RemainingQuantity,
    decimal EstimatedPrice,
    decimal GstPercent,
    decimal Mrp,
    decimal SellingPrice);
