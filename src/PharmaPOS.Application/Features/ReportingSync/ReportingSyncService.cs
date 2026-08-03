using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Identity;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Entities.System;

namespace PharmaPOS.Application.Features.ReportingSync;

public sealed class ReportingSyncService : IReportingSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IUnitOfWork _uow;
    private readonly IReportingSyncGate _gate;
    private readonly IStoreIdentityService _storeIdentity;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public ReportingSyncService(
        IUnitOfWork uow,
        IReportingSyncGate gate,
        IStoreIdentityService storeIdentity,
        ICurrentUserService currentUser,
        IDateTimeProvider clock)
    {
        _uow = uow;
        _gate = gate;
        _storeIdentity = storeIdentity;
        _currentUser = currentUser;
        _clock = clock;
    }

    public Task EnqueueBranchAsync(int branchId, CancellationToken ct = default)
        => SafeAsync(async () =>
        {
            var e = await _uow.Repository<Branch>().Query().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == branchId, ct);
            if (e is null) return;
            var store = await ResolveStoreIdAsync(e.Id, ct);
            if (store is null) return;
            await EnqueueAsync(ReportingSyncEntityTypes.Branch, store, e.Id, new
            {
                store_id = store,
                local_id = e.Id,
                code = e.Code,
                name = e.Name,
                address = e.Address,
                city = e.City,
                state = e.State,
                pincode = e.Pincode,
                phone = e.Phone,
                email = e.Email,
                gst_number = e.GstNumber,
                drug_license_number = e.DrugLicenseNumber,
                is_head_office = e.IsHeadOffice,
                status = (int)e.Status,
                is_deleted = e.IsDeleted
            }, ct);
        });

    public Task EnqueueMedicineAsync(int medicineId, CancellationToken ct = default)
        => SafeAsync(async () =>
        {
            var e = await _uow.Repository<Medicine>().Query().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == medicineId, ct);
            if (e is null) return;
            var store = await ResolveStoreIdAsync(null, ct);
            if (store is null) return;
            await EnqueueAsync(ReportingSyncEntityTypes.Medicine, store, e.Id, new
            {
                store_id = store,
                local_id = e.Id,
                name = e.Name,
                generic_name = e.GenericName,
                brand = e.Brand,
                composition = e.Composition,
                strength = e.Strength,
                dosage_form = (int)e.DosageForm,
                hsn_code = e.HsnCode,
                gst_percent = e.GstPercent,
                barcode = e.Barcode,
                mrp = e.Mrp,
                purchase_price = e.PurchasePrice,
                selling_price = e.SellingPrice,
                units_per_pack = e.UnitsPerPack,
                reorder_level = e.ReorderLevel,
                status = (int)e.Status,
                is_deleted = e.IsDeleted
            }, ct);
        });

    public Task EnqueueMedicineBatchAsync(int batchId, CancellationToken ct = default)
        => SafeAsync(async () =>
        {
            var e = await _uow.Repository<MedicineBatch>().Query().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == batchId, ct);
            if (e is null) return;
            var store = await ResolveStoreIdAsync(e.BranchId, ct);
            if (store is null) return;
            await EnqueueAsync(ReportingSyncEntityTypes.MedicineBatch, store, e.Id, new
            {
                store_id = store,
                local_id = e.Id,
                medicine_local_id = e.MedicineId,
                branch_local_id = e.BranchId,
                batch_number = e.BatchNumber,
                manufacturing_date = e.ManufacturingDate,
                expiry_date = e.ExpiryDate,
                quantity_available = e.QuantityAvailable,
                purchase_price = e.PurchasePrice,
                mrp = e.Mrp,
                selling_price = e.SellingPrice,
                gst_percent = e.GstPercent,
                rack_number = e.RackNumber,
                is_deleted = e.IsDeleted
            }, ct);
        });

    public Task EnqueueCustomerAsync(int customerId, CancellationToken ct = default)
        => SafeAsync(async () =>
        {
            var e = await _uow.Repository<Customer>().Query().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == customerId, ct);
            if (e is null) return;
            var store = await ResolveStoreIdAsync(e.BranchId, ct);
            if (store is null) return;
            await EnqueueAsync(ReportingSyncEntityTypes.Customer, store, e.Id, new
            {
                store_id = store,
                local_id = e.Id,
                branch_local_id = e.BranchId,
                name = e.Name,
                type = (int)e.Type,
                phone = e.Phone,
                email = e.Email,
                gst_number = e.GstNumber,
                address = e.Address,
                city = e.City,
                credit_limit = e.CreditLimit,
                outstanding_balance = e.OutstandingBalance,
                reward_points = e.RewardPoints,
                status = (int)e.Status,
                is_deleted = e.IsDeleted
            }, ct);
        });

    public Task EnqueueSaleAsync(int saleId, CancellationToken ct = default)
        => SafeAsync(async () =>
        {
            var sale = await _uow.Repository<Sale>().Query().AsNoTracking()
                .Include(s => s.Items)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(x => x.Id == saleId, ct);
            if (sale is null) return;
            var store = await ResolveStoreIdAsync(sale.BranchId, ct);
            if (store is null) return;

            await EnqueueAsync(ReportingSyncEntityTypes.Sale, store, sale.Id, new
            {
                store_id = store,
                local_id = sale.Id,
                branch_local_id = sale.BranchId,
                invoice_number = sale.InvoiceNumber,
                invoice_date = sale.InvoiceDate,
                customer_local_id = sale.CustomerId,
                billing_customer_name = sale.BillingCustomerName,
                billing_customer_phone = sale.BillingCustomerPhone,
                billing_customer_address = sale.BillingCustomerAddress,
                billing_doctor_name = sale.BillingDoctorName,
                sub_total = sale.SubTotal,
                discount_amount = sale.DiscountAmount,
                taxable_amount = sale.TaxableAmount,
                cgst_amount = sale.CgstAmount,
                sgst_amount = sale.SgstAmount,
                igst_amount = sale.IgstAmount,
                round_off = sale.RoundOff,
                grand_total = sale.GrandTotal,
                paid_amount = sale.PaidAmount,
                change_returned = sale.ChangeReturned,
                status = (int)sale.Status,
                payment_status = (int)sale.PaymentStatus,
                remarks = sale.Remarks,
                is_deleted = sale.IsDeleted,
                items = sale.Items.Select(i => new
                {
                    local_id = i.Id,
                    sale_local_id = sale.Id,
                    medicine_local_id = i.MedicineId,
                    medicine_batch_local_id = i.MedicineBatchId,
                    batch_number = i.BatchNumber,
                    expiry_date = i.ExpiryDate,
                    quantity = i.Quantity,
                    mrp = i.Mrp,
                    unit_price = i.UnitPrice,
                    discount_percent = i.DiscountPercent,
                    discount_amount = i.DiscountAmount,
                    gst_percent = i.GstPercent,
                    taxable_amount = i.TaxableAmount,
                    tax_amount = i.TaxAmount,
                    line_total = i.LineTotal,
                    is_deleted = i.IsDeleted
                }),
                payments = sale.Payments.Select(p => new
                {
                    local_id = p.Id,
                    sale_local_id = sale.Id,
                    method = (int)p.Method,
                    amount = p.Amount,
                    reference_number = p.ReferenceNumber,
                    payment_date_utc = p.PaymentDateUtc,
                    is_deleted = p.IsDeleted
                })
            }, ct);

            if (sale.CustomerId is int cid)
                await EnqueueCustomerAsync(cid, ct);

            foreach (var batchId in sale.Items.Where(i => i.MedicineBatchId.HasValue)
                         .Select(i => i.MedicineBatchId!.Value).Distinct())
                await EnqueueMedicineBatchAsync(batchId, ct);
        });

    public Task EnqueueSaleReturnAsync(int saleReturnId, CancellationToken ct = default)
        => SafeAsync(async () =>
        {
            var r = await _uow.Repository<SaleReturn>().Query().AsNoTracking()
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == saleReturnId, ct);
            if (r is null) return;
            var store = await ResolveStoreIdAsync(r.BranchId, ct);
            if (store is null) return;

            await EnqueueAsync(ReportingSyncEntityTypes.SaleReturn, store, r.Id, new
            {
                store_id = store,
                local_id = r.Id,
                branch_local_id = r.BranchId,
                return_number = r.ReturnNumber,
                return_date = r.ReturnDate,
                sale_local_id = r.SaleId,
                customer_local_id = r.CustomerId,
                grand_total = r.GrandTotal,
                refund_amount = r.RefundAmount,
                status = (int)r.Status,
                remarks = r.Remarks,
                is_deleted = r.IsDeleted,
                items = r.Items.Select(i => new
                {
                    local_id = i.Id,
                    sale_return_local_id = r.Id,
                    medicine_local_id = i.MedicineId,
                    medicine_batch_local_id = i.MedicineBatchId,
                    batch_number = i.BatchNumber,
                    quantity = i.ReturnedQuantity,
                    unit_price = i.UnitPrice,
                    line_total = i.LineTotal,
                    is_deleted = i.IsDeleted
                })
            }, ct);

            if (r.SaleId > 0)
                await EnqueueSaleAsync(r.SaleId, ct);

            foreach (var batchId in r.Items.Where(i => i.MedicineBatchId.HasValue)
                         .Select(i => i.MedicineBatchId!.Value).Distinct())
                await EnqueueMedicineBatchAsync(batchId, ct);
        });

    public Task EnqueuePurchaseAsync(int purchaseId, CancellationToken ct = default)
        => SafeAsync(async () =>
        {
            var p = await _uow.Repository<Purchase>().Query().AsNoTracking()
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == purchaseId, ct);
            if (p is null) return;
            var store = await ResolveStoreIdAsync(p.BranchId, ct);
            if (store is null) return;

            await EnqueueAsync(ReportingSyncEntityTypes.Purchase, store, p.Id, new
            {
                store_id = store,
                local_id = p.Id,
                branch_local_id = p.BranchId,
                invoice_number = p.InvoiceNumber,
                supplier_invoice_number = p.SupplierInvoiceNumber,
                invoice_date = p.InvoiceDate,
                supplier_local_id = p.SupplierId,
                sub_total = p.SubTotal,
                discount_amount = p.DiscountAmount,
                taxable_amount = p.TaxableAmount,
                cgst_amount = p.CgstAmount,
                sgst_amount = p.SgstAmount,
                igst_amount = p.IgstAmount,
                round_off = p.RoundOff,
                grand_total = p.GrandTotal,
                paid_amount = p.PaidAmount,
                status = (int)p.Status,
                payment_status = (int)p.PaymentStatus,
                remarks = p.Remarks,
                is_deleted = p.IsDeleted,
                items = p.Items.Select(i => new
                {
                    local_id = i.Id,
                    purchase_local_id = p.Id,
                    medicine_local_id = i.MedicineId,
                    medicine_batch_local_id = i.MedicineBatchId,
                    batch_number = i.BatchNumber,
                    expiry_date = i.ExpiryDate,
                    quantity = i.Quantity,
                    free_quantity = i.FreeQuantity,
                    purchase_price = i.PurchasePrice,
                    mrp = i.Mrp,
                    gst_percent = i.GstPercent,
                    line_total = i.LineTotal,
                    is_deleted = i.IsDeleted
                })
            }, ct);

            foreach (var medId in p.Items.Select(i => i.MedicineId).Distinct())
                await EnqueueMedicineAsync(medId, ct);

            foreach (var batchId in p.Items.Where(i => i.MedicineBatchId.HasValue)
                         .Select(i => i.MedicineBatchId!.Value).Distinct())
                await EnqueueMedicineBatchAsync(batchId, ct);
        });

    public Task EnqueuePurchaseReturnAsync(int purchaseReturnId, CancellationToken ct = default)
        => SafeAsync(async () =>
        {
            var r = await _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == purchaseReturnId, ct);
            if (r is null) return;
            var store = await ResolveStoreIdAsync(r.BranchId, ct);
            if (store is null) return;

            await EnqueueAsync(ReportingSyncEntityTypes.PurchaseReturn, store, r.Id, new
            {
                store_id = store,
                local_id = r.Id,
                branch_local_id = r.BranchId,
                return_number = r.ReturnNumber,
                return_date = r.ReturnDate,
                purchase_local_id = r.PurchaseId,
                supplier_local_id = r.SupplierId,
                grand_total = r.GrandTotal,
                status = (int)r.Status,
                remarks = r.Remarks,
                is_deleted = r.IsDeleted,
                items = r.Items.Select(i => new
                {
                    local_id = i.Id,
                    purchase_return_local_id = r.Id,
                    medicine_local_id = i.MedicineId,
                    medicine_batch_local_id = i.MedicineBatchId,
                    batch_number = i.BatchNumber,
                    quantity = i.ReturnedQuantity,
                    purchase_price = i.PurchasePrice,
                    line_total = i.LineTotal,
                    is_deleted = i.IsDeleted
                })
            }, ct);

            if (r.PurchaseId is int pid)
                await EnqueuePurchaseAsync(pid, ct);

            foreach (var batchId in r.Items.Where(i => i.MedicineBatchId.HasValue)
                         .Select(i => i.MedicineBatchId!.Value).Distinct())
                await EnqueueMedicineBatchAsync(batchId, ct);
        });

    public Task EnqueueStockMovementAsync(int movementId, CancellationToken ct = default)
        => SafeAsync(async () =>
        {
            var e = await _uow.Repository<StockMovement>().Query().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == movementId, ct);
            if (e is null) return;
            var store = await ResolveStoreIdAsync(e.BranchId, ct);
            if (store is null) return;
            await EnqueueAsync(ReportingSyncEntityTypes.StockMovement, store, e.Id, new
            {
                store_id = store,
                local_id = e.Id,
                branch_local_id = e.BranchId,
                medicine_local_id = e.MedicineId,
                medicine_batch_local_id = e.MedicineBatchId,
                movement_type = (int)e.MovementType,
                quantity = e.Quantity,
                balance_after = e.BalanceAfter,
                unit_cost = e.UnitCost,
                reference_type = e.ReferenceType,
                reference_id = e.ReferenceId,
                reference_number = e.ReferenceNumber,
                remarks = e.Remarks,
                movement_date_utc = e.MovementDateUtc,
                is_deleted = e.IsDeleted
            }, ct);

            if (e.MedicineBatchId is int bid)
                await EnqueueMedicineBatchAsync(bid, ct);
        });

    public Task EnqueueStockTransferAsync(int transferId, CancellationToken ct = default)
        => SafeAsync(async () =>
        {
            var t = await _uow.Repository<StockTransfer>().Query().AsNoTracking()
                .Include(x => x.Items)
                .FirstOrDefaultAsync(x => x.Id == transferId, ct);
            if (t is null) return;
            var store = await ResolveStoreIdAsync(t.BranchId, ct);
            if (store is null) return;

            await EnqueueAsync(ReportingSyncEntityTypes.StockTransfer, store, t.Id, new
            {
                store_id = store,
                local_id = t.Id,
                branch_local_id = t.BranchId,
                transfer_number = t.TransferNumber,
                transfer_date = t.TransferDate,
                kind = (int)t.Kind,
                status = (int)t.Status,
                to_branch_local_id = t.ToBranchId,
                from_branch_code = t.FromBranchCode,
                from_branch_name = t.FromBranchName,
                to_branch_code = t.ToBranchCode,
                to_branch_name = t.ToBranchName,
                package_key = t.PackageKey,
                remarks = t.Remarks,
                is_deleted = t.IsDeleted,
                items = t.Items.Select(i => new
                {
                    local_id = i.Id,
                    stock_transfer_local_id = t.Id,
                    medicine_local_id = i.MedicineId,
                    medicine_name = i.MedicineName,
                    medicine_barcode = i.MedicineBarcode,
                    batch_number = i.BatchNumber,
                    expiry_date = i.ExpiryDate,
                    quantity = i.Quantity,
                    purchase_price = i.PurchasePrice,
                    mrp = i.Mrp,
                    selling_price = i.SellingPrice,
                    is_deleted = i.IsDeleted
                })
            }, ct);

            foreach (var batchId in t.Items
                         .SelectMany(i => new int?[] { i.SourceMedicineBatchId, i.DestinationMedicineBatchId })
                         .Where(id => id.HasValue)
                         .Select(id => id!.Value)
                         .Distinct())
                await EnqueueMedicineBatchAsync(batchId, ct);
        });

    private async Task EnqueueAsync(string entityType, string storeId, int localId, object payload, CancellationToken ct)
    {
        if (!_gate.IsEnabled) return;

        var entry = new SyncOutboxEntry
        {
            EntityType = entityType,
            // Column historically named StoreCode; value is the unique StoreId used on VPS.
            StoreCode = storeId,
            LocalId = localId,
            Operation = SyncOutboxOperation.Upsert,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            Status = SyncOutboxStatus.Pending,
            AttemptCount = 0,
            CreatedAtUtc = _clock.UtcNow,
            NextAttemptAtUtc = _clock.UtcNow
        };
        await _uow.Repository<SyncOutboxEntry>().AddAsync(entry, ct);
        await _uow.SaveChangesAsync(ct);
    }

    /// <summary>Resolves the unique StoreId used as the VPS tenant key for all sync rows.</summary>
    private Task<string?> ResolveStoreIdAsync(int? branchId, CancellationToken ct)
    {
        if (!_gate.IsEnabled)
            return Task.FromResult<string?>(null);

        if (!string.IsNullOrWhiteSpace(_storeIdentity.StoreId))
            return Task.FromResult<string?>(_storeIdentity.StoreId.Trim().ToUpperInvariant());

        // Legacy override / branch code only if StoreId not yet configured
        var overrideCode = _gate.StoreCodeOverride;
        if (!string.IsNullOrWhiteSpace(overrideCode))
            return Task.FromResult<string?>(overrideCode.Trim().ToUpperInvariant());

        return Task.FromResult<string?>(null);
    }

    private static async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch
        {
            // Never fail POS operations because reporting enqueue failed.
        }
    }
}
