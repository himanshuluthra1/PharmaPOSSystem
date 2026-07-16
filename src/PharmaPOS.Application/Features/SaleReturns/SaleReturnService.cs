using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Entities.System;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.SaleReturns;

public class SaleReturnService : ISaleReturnService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;

    public SaleReturnService(IUnitOfWork uow, IDateTimeProvider clock)
    {
        _uow = uow;
        _clock = clock;
    }

    public async Task<SaleReturnPolicyDto> GetPolicyAsync(CancellationToken ct = default)
    {
        var company = await _uow.Repository<CompanyProfile>().Query().AsNoTracking().FirstOrDefaultAsync(ct);
        return new SaleReturnPolicyDto
        {
            AllowedDays = company?.SaleReturnAllowedDays ?? 30,
            HighValueThreshold = company?.SaleReturnHighValueThreshold ?? 5000m,
            BlockExpired = company?.SaleReturnBlockExpired ?? true,
            BlockScheduleDrugs = company?.SaleReturnBlockScheduleDrugs ?? false,
            BlockRefrigerated = company?.SaleReturnBlockRefrigerated ?? false,
            RefundOriginalPaymentMode = company?.SaleReturnRefundOriginalPaymentMode ?? true,
            CreditNoteValidityDays = company?.CreditNoteValidityDays ?? 90
        };
    }

    public Task<List<ReturnReasonDto>> ListReturnReasonsAsync(CancellationToken ct = default)
        => _uow.Repository<ReturnReason>().Query().AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new ReturnReasonDto(r.Id, r.Code, r.Name, r.RequiresRemarks))
            .ToListAsync(ct);

    public Task<List<SaleReturnSearchResultDto>> SearchSalesAsync(
        SaleReturnSearchType type, string term, int? branchId, CancellationToken ct = default)
    {
        term = term.Trim();
        var q = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => s.Status == SaleStatus.Completed
                        || s.Status == SaleStatus.PartiallyReturned
                        || s.Status == SaleStatus.Returned);

        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        q = type switch
        {
            SaleReturnSearchType.InvoiceNumber => q.Where(s => s.InvoiceNumber.Contains(term)),
            SaleReturnSearchType.CustomerMobile => q.Where(s =>
                (s.BillingCustomerPhone != null && s.BillingCustomerPhone.Contains(term))
                || (s.Customer != null && s.Customer.Phone != null && s.Customer.Phone.Contains(term))),
            SaleReturnSearchType.CustomerName => q.Where(s =>
                (s.BillingCustomerName != null && s.BillingCustomerName.Contains(term))
                || (s.Customer != null && s.Customer.Name.Contains(term))),
            SaleReturnSearchType.Barcode => q.Where(s =>
                s.Items.Any(i => i.Medicine != null && i.Medicine.Barcode != null && i.Medicine.Barcode.Contains(term))),
            SaleReturnSearchType.QrCode => q.Where(s => s.InvoiceNumber == term || s.InvoiceNumber.Contains(term)),
            _ => q
        };

        if (term.Length < 1 && type != SaleReturnSearchType.QrCode)
            return Task.FromResult(new List<SaleReturnSearchResultDto>());

        return q.OrderByDescending(s => s.InvoiceDate).Take(100)
            .Select(s => new SaleReturnSearchResultDto(
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                s.Customer != null ? s.Customer.Name
                    : s.BillingCustomerName ?? "Walk-in",
                s.Customer != null ? s.Customer.Phone : s.BillingCustomerPhone,
                s.GrandTotal,
                s.CreatedBy ?? "—",
                s.Status))
            .ToListAsync(ct);
    }

    public async Task<Result<SaleForReturnDto>> GetSaleForReturnAsync(int saleId, int? branchId, CancellationToken ct = default)
    {
        var sale = await _uow.Repository<Sale>().Query().AsNoTracking()
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct);

        if (sale is null)
            return Result.Failure<SaleForReturnDto>("Invoice not found.");
        if (branchId.HasValue && sale.BranchId != branchId)
            return Result.Failure<SaleForReturnDto>("Invoice belongs to another branch.");
        if (sale.Status is SaleStatus.Cancelled or SaleStatus.Draft or SaleStatus.Hold)
            return Result.Failure<SaleForReturnDto>("Only completed or partially returned invoices can be returned.");
        if (sale.Status == SaleStatus.Returned)
            return Result.Failure<SaleForReturnDto>("This invoice has already been fully returned.");

        var returnedQty = await LoadReturnedQuantitiesAsync(saleId, ct);
        var medIds = sale.Items.Select(i => i.MedicineId).Distinct().ToList();
        var medicines = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        var dto = new SaleForReturnDto
        {
            SaleId = sale.Id,
            InvoiceNumber = sale.InvoiceNumber,
            InvoiceDate = sale.InvoiceDate,
            CustomerId = sale.CustomerId,
            CustomerName = sale.BillingCustomerName ?? "Walk-in",
            CustomerPhone = sale.BillingCustomerPhone,
            GrandTotal = sale.GrandTotal,
            PaidAmount = sale.PaidAmount,
            Status = sale.Status,
            RewardPointsEarned = sale.RewardPointsEarned,
            RewardPointsRedeemed = sale.RewardPointsRedeemed,
            OriginalPayments = sale.Payments.Select(p =>
                new SalePaymentSnapshotDto(p.Method, p.Amount, p.ReferenceNumber)).ToList()
        };

        if (sale.CustomerId is int customerId)
        {
            var customer = await _uow.Repository<Customer>().Query().AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == customerId, ct);
            if (customer is not null)
            {
                dto.CustomerName = customer.Name;
                dto.CustomerPhone = customer.Phone;
            }
        }
        else
        {
            dto.CustomerName = sale.BillingCustomerName ?? "Walk-in";
            dto.CustomerPhone = sale.BillingCustomerPhone;
        }

        foreach (var item in sale.Items)
        {
            medicines.TryGetValue(item.MedicineId, out var med);
            returnedQty.TryGetValue(item.Id, out var alreadyReturned);
            var amounts = SaleReturnPricing.ComputeLineAmounts(
                item.Quantity, item.Quantity, item.Mrp, item.UnitPrice,
                item.DiscountAmount, item.GstPercent, item.LineTotal,
                item.TaxableAmount, item.TaxAmount);

            dto.Lines.Add(new SaleReturnLineDto
            {
                SaleItemId = item.Id,
                MedicineId = item.MedicineId,
                MedicineName = med?.Name ?? $"Medicine #{item.MedicineId}",
                GenericName = med?.GenericName,
                Barcode = med?.Barcode,
                MedicineBatchId = item.MedicineBatchId,
                BatchNumber = item.BatchNumber,
                ExpiryDate = item.ExpiryDate,
                Mrp = item.Mrp,
                UnitPrice = item.UnitPrice,
                DiscountPercent = item.DiscountPercent,
                DiscountAmount = item.DiscountAmount,
                GstPercent = item.GstPercent,
                SoldQuantity = item.Quantity,
                AlreadyReturnedQuantity = alreadyReturned,
                LineTotal = item.LineTotal,
                ProportionalLineTotal = amounts.LineTotal,
                ScheduleType = med?.ScheduleType ?? ScheduleDrugType.None,
                PrescriptionRequired = med?.PrescriptionRequired ?? false,
                IsRefrigerated = IsRefrigerated(med),
                ImagePath = med?.ImagePath
            });
        }

        return Result.Success(dto);
    }

    public async Task<Result<SaleReturnReceiptDto>> CreateReturnAsync(
        CreateSaleReturnRequest request, int? branchId, string? userName, CancellationToken ct = default)
    {
        if (request.Lines.Count == 0 && !request.ReturnEntireInvoice)
            return Result.Failure<SaleReturnReceiptDto>("Select at least one item to return.");

        try
        {
            var receipt = await _uow.ExecuteInTransactionAsync(
                token => PersistReturnAsync(request, branchId, userName, token), ct);
            return Result.Success(receipt);
        }
        catch (SaleReturnException ex)
        {
            return Result.Failure<SaleReturnReceiptDto>(ex.Message);
        }
    }

    public async Task<Result<SaleReturnReceiptDto>> GetReturnReceiptAsync(int saleReturnId, CancellationToken ct = default)
    {
        var receipt = await BuildReceiptAsync(saleReturnId, ct);
        return receipt is null
            ? Result.Failure<SaleReturnReceiptDto>("Return not found.")
            : Result.Success(receipt);
    }

    public Task<List<SaleReturnSummaryRowDto>> ListReturnsAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<SaleReturn>().Query().AsNoTracking()
            .Where(r => r.Status == SaleReturnStatus.Completed
                        && r.ReturnDate >= from && r.ReturnDate < to.AddDays(1));
        if (branchId.HasValue) q = q.Where(r => r.BranchId == branchId);

        return q.OrderByDescending(r => r.ReturnDate)
            .Select(r => new SaleReturnSummaryRowDto(
                r.ReturnNumber,
                r.ReturnDate,
                r.Sale!.InvoiceNumber,
                r.Customer != null ? r.Customer.Name
                    : r.Sale.BillingCustomerName ?? "Walk-in",
                r.RefundAmount,
                r.RefundMode,
                r.CreatedBy ?? "—",
                r.IsFullReturn))
            .ToListAsync(ct);
    }

    public Task<List<MedicineReturnReportRowDto>> GetMedicineReturnReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default)
    {
        var q = _uow.Repository<SaleReturnItem>().Query().AsNoTracking()
            .Where(i => i.SaleReturn!.Status == SaleReturnStatus.Completed
                        && i.SaleReturn.ReturnDate >= from
                        && i.SaleReturn.ReturnDate < to.AddDays(1));
        if (branchId.HasValue) q = q.Where(i => i.SaleReturn!.BranchId == branchId);

        return q.GroupBy(i => new { i.MedicineId, i.Medicine!.Name, i.BatchNumber })
            .Select(g => new MedicineReturnReportRowDto(
                g.Key.Name,
                g.Key.BatchNumber ?? "—",
                g.Sum(x => x.ReturnedQuantity),
                g.Sum(x => x.LineTotal),
                g.Count()))
            .OrderByDescending(x => x.RefundAmount)
            .ToListAsync(ct);
    }

    public async Task<DailySaleReturnReportDto> GetDailyReturnSummaryAsync(
        DateTime date, int? branchId, CancellationToken ct = default)
    {
        var from = date.Date;
        var to = from.AddDays(1);
        var q = _uow.Repository<SaleReturn>().Query().AsNoTracking()
            .Where(r => r.Status == SaleReturnStatus.Completed
                        && r.ReturnDate >= from && r.ReturnDate < to);
        if (branchId.HasValue) q = q.Where(r => r.BranchId == branchId);

        var rows = await q.Select(r => new { r.IsFullReturn, r.RefundAmount, r.CgstAmount, r.SgstAmount }).ToListAsync(ct);
        return new DailySaleReturnReportDto(
            date,
            rows.Count,
            rows.Sum(r => r.RefundAmount),
            rows.Sum(r => r.CgstAmount + r.SgstAmount),
            rows.Count(r => r.IsFullReturn),
            rows.Count(r => !r.IsFullReturn));
    }

    private async Task<SaleReturnReceiptDto> PersistReturnAsync(
        CreateSaleReturnRequest request, int? branchId, string? userName, CancellationToken ct)
    {
        var policy = await GetPolicyAsync(ct);
        var reasons = await _uow.Repository<ReturnReason>().Query().AsNoTracking()
            .Where(r => r.IsActive).ToDictionaryAsync(r => r.Id, ct);

        var sale = await _uow.Repository<Sale>().Query()
            .Include(s => s.Items)
            .Include(s => s.Payments)
            .FirstOrDefaultAsync(s => s.Id == request.SaleId, ct)
            ?? throw new SaleReturnException("Invoice not found.");

        if (sale.Status is SaleStatus.Returned or SaleStatus.Cancelled)
            throw new SaleReturnException("This invoice cannot be returned.");
        if (branchId.HasValue && sale.BranchId != branchId)
            throw new SaleReturnException("Invoice belongs to another branch.");

        // Medicines may be soft-deleted (MedWin imports) — never ThenInclude them or
        // EF query filters will drop the sale lines.
        var medIds = sale.Items.Select(i => i.MedicineId).Distinct().ToList();
        var medicineNames = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var returnedQty = await LoadReturnedQuantitiesAsync(sale.Id, ct);
        var linesToReturn = ResolveReturnLines(request, sale, returnedQty, medicineNames);

        ValidateReturnWindow(sale, policy, request.ManagerOverrideUsed, _clock.Now);
        await ValidateLinesAsync(linesToReturn, sale, returnedQty, policy, reasons, request, ct);

        var totals = ComputeReturnTotals(linesToReturn);
        if (totals.GrandTotal <= 0)
            throw new SaleReturnException("Return amount must be greater than zero.");

        if (!request.ManagerOverrideUsed && totals.GrandTotal > policy.HighValueThreshold)
            throw new SaleReturnException(
                $"Return amount exceeds threshold ({policy.HighValueThreshold:N2}). Manager approval required.");

        var saleReturn = new SaleReturn
        {
            ReturnNumber = await GenerateReturnNumberAsync(branchId, ct),
            SaleId = sale.Id,
            CustomerId = sale.CustomerId,
            ReturnDate = _clock.Now,
            BranchId = branchId,
            RefundMode = request.RefundMode,
            RefundAmount = totals.GrandTotal,
            SubTotal = totals.SubTotalMrp,
            DiscountAmount = totals.Discount,
            TaxableAmount = totals.Taxable,
            CgstAmount = totals.Cgst,
            SgstAmount = totals.Sgst,
            GrandTotal = totals.GrandTotal,
            Status = SaleReturnStatus.Completed,
            Remarks = request.Remarks,
            ManagerOverrideUsed = request.ManagerOverrideUsed,
            ManagerOverrideReason = request.ManagerOverrideReason,
            ExchangeSaleId = request.ExchangeSaleId,
            ExchangeAmount = request.ExchangeAmount,
            IsFullReturn = IsFullInvoiceReturn(sale, returnedQty, linesToReturn),
            CreatedBy = userName
        };

        saleReturn.RewardPointsReversed = SaleReturnPricing.ProportionalPoints(
            sale.RewardPointsEarned, sale.Items.Sum(i => i.Quantity), linesToReturn.Sum(l => l.ReturnQty));
        saleReturn.RewardPointsRestored = SaleReturnPricing.ProportionalPoints(
            sale.RewardPointsRedeemed, sale.Items.Sum(i => i.Quantity), linesToReturn.Sum(l => l.ReturnQty));

        await _uow.Repository<SaleReturn>().AddAsync(saleReturn, ct);
        await _uow.SaveChangesAsync(ct);

        var createdItems = new List<SaleReturnItem>();
        foreach (var line in linesToReturn)
        {
            var returnItem = MapReturnItem(line, saleReturn.Id);
            await _uow.Repository<SaleReturnItem>().AddAsync(returnItem, ct);
            createdItems.Add(returnItem);
        }

        await _uow.SaveChangesAsync(ct);

        for (var i = 0; i < linesToReturn.Count; i++)
            await ApplyInventoryForReturnAsync(createdItems[i], linesToReturn[i], saleReturn, branchId, ct);

        await ApplyRefundsAsync(saleReturn, request, totals.GrandTotal, policy, ct);
        await ApplyCustomerAdjustmentsAsync(sale, saleReturn, totals.GrandTotal, ct);
        await UpdateSaleStatusAsync(sale, returnedQty, linesToReturn, ct);

        await _uow.Repository<AuditLogEntry>().AddAsync(new AuditLogEntry
        {
            EntityType = nameof(SaleReturn),
            EntityId = saleReturn.Id,
            Action = "Create",
            UserName = userName,
            MachineName = Environment.MachineName,
            NewValuesJson = $"Sale={sale.InvoiceNumber};Refund={totals.GrandTotal:N2}",
            ManagerApproval = request.ManagerOverrideUsed,
            ApprovalReason = request.ManagerOverrideReason,
            OccurredAtUtc = _clock.UtcNow,
            CreatedBy = userName
        }, ct);

        await _uow.SaveChangesAsync(ct);
        return (await BuildReceiptAsync(saleReturn.Id, ct))!;
    }

    private async Task ApplyInventoryForReturnAsync(
        SaleReturnItem returnItem, ResolvedReturnLine line, SaleReturn saleReturn, int? branchId, CancellationToken ct)
    {
        if (!line.Request.IsSaleable)
        {
            await _uow.Repository<NonSaleableStock>().AddAsync(new NonSaleableStock
            {
                BranchId = branchId,
                MedicineId = returnItem.MedicineId,
                MedicineBatchId = returnItem.MedicineBatchId,
                BatchNumber = returnItem.BatchNumber,
                ExpiryDate = returnItem.ExpiryDate,
                Quantity = returnItem.ReturnedQuantity,
                SaleReturnItemId = returnItem.Id,
                Remarks = "Returned — non-saleable",
                ReceivedDateUtc = _clock.UtcNow
            }, ct);

            await _uow.Repository<StockMovement>().AddAsync(new StockMovement
            {
                BranchId = branchId,
                MedicineId = returnItem.MedicineId,
                MedicineBatchId = returnItem.MedicineBatchId,
                MovementType = StockMovementType.NonSaleableIn,
                Quantity = returnItem.ReturnedQuantity,
                BalanceAfter = 0,
                UnitCost = line.SaleItem.UnitPrice,
                ReferenceType = nameof(SaleReturn),
                ReferenceId = saleReturn.Id,
                ReferenceNumber = saleReturn.ReturnNumber,
                MovementDateUtc = _clock.UtcNow,
                Remarks = "Non-saleable return quarantine"
            }, ct);
            return;
        }

        // MedWin / legacy sales often store BatchNumber without MedicineBatchId.
        // Resolve the original batch by id or number; create one if missing so stock
        // always returns to the same batch identity.
        var batch = await ResolveReturnBatchAsync(returnItem, line, branchId, ct);
        returnItem.MedicineBatchId = batch.Id;
        _uow.Repository<SaleReturnItem>().Update(returnItem);

        batch.QuantityAvailable += returnItem.ReturnedQuantity;
        _uow.Repository<MedicineBatch>().Update(batch);

        await _uow.Repository<StockMovement>().AddAsync(new StockMovement
        {
            BranchId = branchId,
            MedicineId = returnItem.MedicineId,
            MedicineBatchId = batch.Id,
            MovementType = StockMovementType.SaleReturn,
            Quantity = returnItem.ReturnedQuantity,
            BalanceAfter = batch.QuantityAvailable,
            UnitCost = batch.PurchasePrice,
            ReferenceType = nameof(SaleReturn),
            ReferenceId = saleReturn.Id,
            ReferenceNumber = saleReturn.ReturnNumber,
            MovementDateUtc = _clock.UtcNow,
            Remarks = $"Return against {line.SaleItem.Sale?.InvoiceNumber}"
        }, ct);
    }

    private async Task<MedicineBatch> ResolveReturnBatchAsync(
        SaleReturnItem returnItem, ResolvedReturnLine line, int? branchId, CancellationToken ct)
    {
        if (returnItem.MedicineBatchId is int existingId)
        {
            var existing = await _uow.Repository<MedicineBatch>().GetByIdAsync(existingId, ct);
            if (existing is not null) return existing;
        }

        var batchNo = (returnItem.BatchNumber ?? string.Empty).Trim();
        if (batchNo.Length > 0)
        {
            var q = _uow.Repository<MedicineBatch>().Query()
                .Where(b => b.MedicineId == returnItem.MedicineId && b.BatchNumber == batchNo);
            if (branchId.HasValue) q = q.Where(b => b.BranchId == branchId);

            var match = await q.OrderByDescending(b => b.QuantityAvailable).FirstOrDefaultAsync(ct);
            if (match is not null) return match;
        }

        var created = new MedicineBatch
        {
            MedicineId = returnItem.MedicineId,
            BatchNumber = string.IsNullOrWhiteSpace(batchNo) ? "RETURN" : batchNo,
            ExpiryDate = returnItem.ExpiryDate,
            QuantityAvailable = 0,
            Mrp = returnItem.Mrp,
            SellingPrice = returnItem.UnitPrice,
            PurchasePrice = line.SaleItem.UnitPrice,
            GstPercent = returnItem.GstPercent,
            BranchId = branchId
        };
        await _uow.Repository<MedicineBatch>().AddAsync(created, ct);
        await _uow.SaveChangesAsync(ct);
        return created;
    }

    private async Task ApplyRefundsAsync(
        SaleReturn saleReturn, CreateSaleReturnRequest request, decimal totalRefund,
        SaleReturnPolicyDto policy, CancellationToken ct)
    {
        var allocations = request.RefundAllocations.Count > 0
            ? request.RefundAllocations
            : [new ReturnRefundAllocationDto(request.RefundMode, totalRefund, null)];

        var allocated = allocations.Sum(a => a.Amount);
        if (Math.Abs(allocated - totalRefund) > 0.05m)
            throw new SaleReturnException("Refund allocations must equal the return total.");

        CreditNote? creditNote = null;
        foreach (var alloc in allocations)
        {
            await _uow.Repository<ReturnRefund>().AddAsync(new ReturnRefund
            {
                SaleReturnId = saleReturn.Id,
                RefundMode = alloc.Mode,
                Amount = alloc.Amount,
                TransactionReference = alloc.TransactionReference,
                Status = PaymentStatus.Refunded,
                RefundDateUtc = _clock.UtcNow
            }, ct);

            if (alloc.Mode == RefundMode.CreditNote && creditNote is null)
            {
                var company = await _uow.Repository<CompanyProfile>().Query().FirstOrDefaultAsync(ct);
                creditNote = new CreditNote
                {
                    CreditNoteNumber = await GenerateCreditNoteNumberAsync(ct),
                    SaleReturnId = saleReturn.Id,
                    CustomerId = saleReturn.CustomerId,
                    Amount = alloc.Amount,
                    IssueDate = _clock.Now,
                    ExpiryDate = _clock.Now.Date.AddDays(company?.CreditNoteValidityDays ?? policy.CreditNoteValidityDays),
                    Status = CreditNoteStatus.Active,
                    Remarks = request.Remarks
                };
                await _uow.Repository<CreditNote>().AddAsync(creditNote, ct);
            }
        }

        saleReturn.RefundAmount = totalRefund;
        saleReturn.RefundMode = request.RefundMode;
    }

    private async Task ApplyCustomerAdjustmentsAsync(
        Sale sale, SaleReturn saleReturn, decimal refundAmount, CancellationToken ct)
    {
        if (sale.CustomerId is not int customerId) return;
        var customer = await _uow.Repository<Customer>().GetByIdAsync(customerId, ct);
        if (customer is null) return;

        var creditDue = Math.Max(0, sale.GrandTotal - sale.PaidAmount);
        if (creditDue > 0)
            customer.OutstandingBalance = Math.Max(0, customer.OutstandingBalance - Math.Min(refundAmount, creditDue));

        customer.RewardPoints -= saleReturn.RewardPointsReversed;
        customer.RewardPoints += saleReturn.RewardPointsRestored;
        if (customer.RewardPoints < 0) customer.RewardPoints = 0;

        _uow.Repository<Customer>().Update(customer);
    }

    private Task UpdateSaleStatusAsync(
        Sale sale, Dictionary<int, decimal> priorReturned, List<ResolvedReturnLine> newLines, CancellationToken ct)
    {
        var newReturned = newLines.ToDictionary(l => l.SaleItem.Id, l => l.ReturnQty);
        var allFull = sale.Items.All(item =>
        {
            priorReturned.TryGetValue(item.Id, out var prev);
            newReturned.TryGetValue(item.Id, out var added);
            return prev + added >= item.Quantity;
        });

        sale.Status = allFull ? SaleStatus.Returned : SaleStatus.PartiallyReturned;
        _uow.Repository<Sale>().Update(sale);
        return Task.CompletedTask;
    }

    private static bool IsFullInvoiceReturn(
        Sale sale, Dictionary<int, decimal> priorReturned, List<ResolvedReturnLine> newLines)
    {
        var newReturned = newLines.ToDictionary(l => l.SaleItem.Id, l => l.ReturnQty);
        return sale.Items.All(item =>
        {
            priorReturned.TryGetValue(item.Id, out var prev);
            newReturned.TryGetValue(item.Id, out var added);
            return prev + added >= item.Quantity;
        });
    }

    private async Task<Dictionary<int, decimal>> LoadReturnedQuantitiesAsync(int saleId, CancellationToken ct)
        => await _uow.Repository<SaleReturnItem>().Query().AsNoTracking()
            .Where(i => i.SaleReturn!.SaleId == saleId && i.SaleReturn.Status == SaleReturnStatus.Completed)
            .GroupBy(i => i.SaleItemId)
            .Select(g => new { SaleItemId = g.Key, Qty = g.Sum(x => x.ReturnedQuantity) })
            .ToDictionaryAsync(x => x.SaleItemId, x => x.Qty, ct);

    private static List<ResolvedReturnLine> ResolveReturnLines(
        CreateSaleReturnRequest request, Sale sale, Dictionary<int, decimal> returnedQty,
        IReadOnlyDictionary<int, string> medicineNames)
    {
        string NameOf(SaleItem item) =>
            medicineNames.TryGetValue(item.MedicineId, out var n) ? n : $"Medicine #{item.MedicineId}";

        if (request.ReturnEntireInvoice)
        {
            return sale.Items
                .Select(item =>
                {
                    returnedQty.TryGetValue(item.Id, out var prev);
                    var available = item.Quantity - prev;
                    if (available <= 0) return null;
                    return new ResolvedReturnLine(
                        item,
                        NameOf(item),
                        available,
                        new CreateSaleReturnLineRequest
                        {
                            SaleItemId = item.Id,
                            ReturnQuantity = available,
                            ReturnReasonId = request.Lines.FirstOrDefault()?.ReturnReasonId ?? 1,
                            IsSaleable = true,
                            ExpiryValid = true,
                            SealIntact = true
                        },
                        SaleReturnPricing.ComputeLineAmounts(
                            item.Quantity, available, item.Mrp, item.UnitPrice,
                            item.DiscountAmount, item.GstPercent, item.LineTotal,
                            item.TaxableAmount, item.TaxAmount));
                })
                .Where(l => l is not null)
                .Cast<ResolvedReturnLine>()
                .ToList();
        }

        var saleItems = sale.Items.ToDictionary(i => i.Id);
        var result = new List<ResolvedReturnLine>();
        foreach (var req in request.Lines.Where(l => l.ReturnQuantity > 0))
        {
            if (!saleItems.TryGetValue(req.SaleItemId, out var item))
                throw new SaleReturnException($"Sale line #{req.SaleItemId} not found.");
            returnedQty.TryGetValue(item.Id, out var prev);
            var available = item.Quantity - prev;
            if (req.ReturnQuantity > available)
                throw new SaleReturnException(
                    $"Return quantity for line exceeds available quantity (max {available:N2}).");

            result.Add(new ResolvedReturnLine(
                item,
                NameOf(item),
                req.ReturnQuantity,
                req,
                SaleReturnPricing.ComputeLineAmounts(
                    item.Quantity, req.ReturnQuantity, item.Mrp, item.UnitPrice,
                    item.DiscountAmount, item.GstPercent, item.LineTotal,
                    item.TaxableAmount, item.TaxAmount)));
        }

        return result;
    }

    private static void ValidateReturnWindow(Sale sale, SaleReturnPolicyDto policy, bool managerOverride, DateTime now)
    {
        if (managerOverride) return;
        var days = (now.Date - sale.InvoiceDate.Date).TotalDays;
        if (days > policy.AllowedDays)
            throw new SaleReturnException(
                $"Return window of {policy.AllowedDays} days has expired. Manager approval required.");
    }

    private async Task ValidateLinesAsync(
        List<ResolvedReturnLine> lines, Sale sale, Dictionary<int, decimal> returnedQty,
        SaleReturnPolicyDto policy, Dictionary<int, ReturnReason> reasons,
        CreateSaleReturnRequest request, CancellationToken ct)
    {
        var medIds = lines.Select(l => l.SaleItem.MedicineId).Distinct().ToList();
        var medicines = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, ct);

        foreach (var line in lines)
        {
            if (!reasons.TryGetValue(line.Request.ReturnReasonId, out var reason))
                throw new SaleReturnException("Select a valid return reason.");
            if (reason.RequiresRemarks && string.IsNullOrWhiteSpace(line.Request.ReasonRemarks))
                throw new SaleReturnException($"Remarks are required for reason: {reason.Name}.");

            returnedQty.TryGetValue(line.SaleItem.Id, out var prev);
            if (line.ReturnQty + prev > line.SaleItem.Quantity)
                throw new SaleReturnException(
                    $"Cannot return more than sold quantity for {line.MedicineName}.");

            medicines.TryGetValue(line.SaleItem.MedicineId, out var med);
            if (!request.ManagerOverrideUsed)
            {
                if (policy.BlockExpired && line.SaleItem.ExpiryDate is DateTime exp && exp.Date < _clock.Now.Date)
                    throw new SaleReturnException($"{line.MedicineName} has expired. Manager approval required.");
                if (policy.BlockScheduleDrugs && med?.ScheduleType != ScheduleDrugType.None)
                    throw new SaleReturnException($"{line.MedicineName} is a scheduled drug. Manager approval required.");
                if (policy.BlockRefrigerated && IsRefrigerated(med))
                    throw new SaleReturnException($"{line.MedicineName} is refrigerated. Manager approval required.");
            }

            if (!string.IsNullOrWhiteSpace(line.Request.ScannedBatchNumber)
                && !string.Equals(line.Request.ScannedBatchNumber, line.SaleItem.BatchNumber, StringComparison.OrdinalIgnoreCase)
                && !line.Request.BatchMismatchApproved
                && !request.ManagerOverrideUsed)
            {
                throw new SaleReturnException(
                    $"Scanned batch differs from sold batch for {line.MedicineName}. Manager approval required.");
            }
        }
    }

    private static ReturnTotals ComputeReturnTotals(List<ResolvedReturnLine> lines)
    {
        decimal subMrp = 0, discount = 0, taxable = 0, tax = 0, grand = 0;
        foreach (var line in lines)
        {
            subMrp += SaleLinePricing.GrossAtMrp(line.Amounts.Mrp, line.ReturnQty);
            discount += line.Amounts.DiscountAmount;
            taxable += line.Amounts.TaxableAmount;
            tax += line.Amounts.TaxAmount;
            grand += line.Amounts.LineTotal;
        }

        var (cgst, sgst) = SaleReturnPricing.SplitTax(tax);
        return new ReturnTotals(subMrp, discount, taxable, cgst, sgst, grand);
    }

    private static SaleReturnItem MapReturnItem(ResolvedReturnLine line, int saleReturnId) => new()
    {
        SaleReturnId = saleReturnId,
        SaleItemId = line.SaleItem.Id,
        MedicineId = line.SaleItem.MedicineId,
        MedicineBatchId = line.SaleItem.MedicineBatchId,
        BatchNumber = line.SaleItem.BatchNumber,
        ExpiryDate = line.SaleItem.ExpiryDate,
        ReturnedQuantity = line.ReturnQty,
        Mrp = line.Amounts.Mrp,
        UnitPrice = line.Amounts.UnitPrice,
        DiscountPercent = line.SaleItem.DiscountPercent,
        DiscountAmount = line.Amounts.DiscountAmount,
        GstPercent = line.Amounts.GstPercent,
        TaxableAmount = line.Amounts.TaxableAmount,
        TaxAmount = line.Amounts.TaxAmount,
        LineTotal = line.Amounts.LineTotal,
        ReturnReasonId = line.Request.ReturnReasonId,
        ReasonRemarks = line.Request.ReasonRemarks,
        SealIntact = line.Request.SealIntact,
        PackagingDamaged = line.Request.PackagingDamaged,
        ExpiryValid = line.Request.ExpiryValid,
        IsSaleable = line.Request.IsSaleable,
        ScannedBatchNumber = line.Request.ScannedBatchNumber,
        BatchMismatchApproved = line.Request.BatchMismatchApproved
    };

    private async Task<SaleReturnReceiptDto?> BuildReceiptAsync(int saleReturnId, CancellationToken ct)
    {
        var ret = await _uow.Repository<SaleReturn>().Query().AsNoTracking()
            .Include(r => r.Sale)
            .Include(r => r.Items).ThenInclude(i => i.ReturnReason)
            .Include(r => r.Refunds)
            .Include(r => r.CreditNote)
            .FirstOrDefaultAsync(r => r.Id == saleReturnId, ct);
        if (ret is null) return null;

        var company = await _uow.Repository<CompanyProfile>().Query().AsNoTracking().FirstOrDefaultAsync(ct);
        var medIds = ret.Items.Select(i => i.MedicineId).Distinct().ToList();
        var medNames = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var receipt = new SaleReturnReceiptDto
        {
            SaleReturnId = ret.Id,
            ReturnNumber = ret.ReturnNumber,
            OriginalInvoiceNumber = ret.Sale?.InvoiceNumber ?? "—",
            ReturnDate = ret.ReturnDate,
            CompanyName = company?.CompanyName ?? "PharmaPOS",
            CompanyAddress = company?.Address,
            CompanyPhone = company?.Phone,
            CompanyGst = company?.GstNumber,
            CustomerName = ret.Sale?.BillingCustomerName ?? "Walk-in",
            CustomerPhone = ret.Sale?.BillingCustomerPhone,
            RefundMode = ret.RefundMode,
            RefundAmount = ret.RefundAmount,
            SubTotal = ret.SubTotal,
            DiscountAmount = ret.DiscountAmount,
            TaxableAmount = ret.TaxableAmount,
            CgstAmount = ret.CgstAmount,
            SgstAmount = ret.SgstAmount,
            GrandTotal = ret.GrandTotal,
            CreditNoteNumber = ret.CreditNote?.CreditNoteNumber,
            CreditNoteExpiry = ret.CreditNote?.ExpiryDate,
            CashierName = ret.CreatedBy ?? "—",
            Remarks = ret.Remarks,
            Refunds = ret.Refunds.Select(r => new ReturnRefundAllocationDto(r.RefundMode, r.Amount, r.TransactionReference)).ToList()
        };

        int sr = 1;
        foreach (var item in ret.Items)
        {
            receipt.Lines.Add(new SaleReturnReceiptLineDto(
                sr++,
                medNames.TryGetValue(item.MedicineId, out var n) ? n : $"#{item.MedicineId}",
                item.BatchNumber ?? "—",
                item.ExpiryDate,
                item.ReturnedQuantity,
                item.Mrp,
                item.UnitPrice,
                item.DiscountAmount,
                item.GstPercent,
                item.LineTotal,
                item.ReturnReason?.Name ?? "—",
                item.IsSaleable));
        }

        return receipt;
    }

    private async Task<string> GenerateReturnNumberAsync(int? branchId, CancellationToken ct)
    {
        var company = await _uow.Repository<CompanyProfile>().Query().AsNoTracking().FirstOrDefaultAsync(ct);
        var prefix = company?.SaleReturnPrefix ?? "SR";
        var date = _clock.Now.ToString("yyyyMMdd");
        var pattern = $"{prefix}-{date}-";
        var last = await _uow.Repository<SaleReturn>().Query().AsNoTracking()
            .Where(r => r.ReturnNumber.StartsWith(pattern))
            .OrderByDescending(r => r.ReturnNumber)
            .Select(r => r.ReturnNumber)
            .FirstOrDefaultAsync(ct);
        var seq = 1;
        if (last is not null && int.TryParse(last[(pattern.Length)..], out var n))
            seq = n + 1;
        return $"{pattern}{seq:D4}";
    }

    private async Task<string> GenerateCreditNoteNumberAsync(CancellationToken ct)
    {
        var company = await _uow.Repository<CompanyProfile>().Query().AsNoTracking().FirstOrDefaultAsync(ct);
        var prefix = company?.CreditNotePrefix ?? "CN";
        var date = _clock.Now.ToString("yyyyMMdd");
        var pattern = $"{prefix}-{date}-";
        var last = await _uow.Repository<CreditNote>().Query().AsNoTracking()
            .Where(c => c.CreditNoteNumber.StartsWith(pattern))
            .OrderByDescending(c => c.CreditNoteNumber)
            .Select(c => c.CreditNoteNumber)
            .FirstOrDefaultAsync(ct);
        var seq = 1;
        if (last is not null && int.TryParse(last[(pattern.Length)..], out var n))
            seq = n + 1;
        return $"{pattern}{seq:D4}";
    }

    private static bool IsRefrigerated(Medicine? med) =>
        med?.StorageCondition?.Contains("cold", StringComparison.OrdinalIgnoreCase) == true
        || med?.StorageCondition?.Contains("refriger", StringComparison.OrdinalIgnoreCase) == true
        || med?.StorageCondition?.Contains("2-8", StringComparison.OrdinalIgnoreCase) == true;

    private sealed record ResolvedReturnLine(
        SaleItem SaleItem,
        string MedicineName,
        decimal ReturnQty,
        CreateSaleReturnLineRequest Request,
        SaleReturnLineAmounts Amounts);

    private sealed record ReturnTotals(
        decimal SubTotalMrp, decimal Discount, decimal Taxable, decimal Cgst, decimal Sgst, decimal GrandTotal);
}

internal sealed class SaleReturnException(string message) : Exception(message);
