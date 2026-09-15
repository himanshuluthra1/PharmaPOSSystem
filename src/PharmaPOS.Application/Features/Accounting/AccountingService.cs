using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Domain.Entities.Accounting;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Entities.System;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Results;

namespace PharmaPOS.Application.Features.Accounting;

public class AccountingService : IAccountingService
{
    private const string CashAccountCode = "1000";
    private const string BankAccountCode = "1010";
    private const string ReceivableAccountCode = "1200";
    private const string PayableAccountCode = "2000";

    /// <summary>Synthetic bill id for <see cref="Supplier.OpeningBalance"/> in party bill lists / allocations.</summary>
    public const int SupplierOpeningBalanceBillId = 0;

    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly IFinancialYearContext _financialYear;

    public AccountingService(IUnitOfWork uow, IDateTimeProvider clock, IFinancialYearContext financialYear)
    {
        _uow = uow;
        _clock = clock;
        _financialYear = financialYear;
    }

    public async Task<AccountingSummaryDto> GetSummaryAsync(int? branchId, CancellationToken ct = default)
    {
        var supplierDues = await ComputeAllSupplierOpenDuesAsync(branchId, ct);
        var customerDues = await ComputeAllCustomerOpenDuesAsync(branchId, ct);

        var cash = await GetAccountByCodeAsync(CashAccountCode, ct);
        var bank = await GetAccountByCodeAsync(BankAccountCode, ct);

        return new AccountingSummaryDto
        {
            TotalPayables = supplierDues.Values.Sum(),
            TotalReceivables = customerDues.Values.Sum(),
            PayableParties = supplierDues.Count(kv => kv.Value > 0.009m),
            ReceivableParties = customerDues.Count(kv => kv.Value > 0.009m),
            CashInHand = cash?.CurrentBalance ?? 0m,
            BankBalance = bank?.CurrentBalance ?? 0m
        };
    }

    public async Task<List<PartyLedgerRowDto>> ListPartyLedgersAsync(
        PartyLedgerKind kind,
        string term,
        int? branchId,
        bool owedOnly = false,
        CancellationToken ct = default)
    {
        term = term.Trim();

        if (kind == PartyLedgerKind.Supplier)
        {
            // Same net formula as Parties Due column (bill Adjusted + pending return credit).
            var dueBySupplier = await ComputeAllSupplierOpenDuesAsync(
                branchId, ct, netOfReturnCredits: true);
            var unusedCredits = await GetUnusedPurchaseReturnCreditsBySupplierAsync(
                branchId, supplierIds: null, ct);

            var q = _uow.Repository<Supplier>().Query().AsNoTracking()
                .Where(s => s.Status == EntityStatus.Active);
            if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);
            if (!string.IsNullOrWhiteSpace(term))
            {
                var normalized = SearchQueryExtensions.NormalizeTerm(term);
                q = q.WhereSupplierMatches(normalized);
            }
            else if (owedOnly)
            {
                var owedIds = dueBySupplier
                    .Where(kv => kv.Value > 0.009m)
                    .Select(kv => kv.Key)
                    .Union(unusedCredits.Where(kv => kv.Value > 0.009m).Select(kv => kv.Key))
                    .Distinct()
                    .ToList();
                if (owedIds.Count == 0) return [];
                q = FilterSupplierByIdChunks(q, owedIds);
            }

            var suppliers = await q
                .Select(s => new { s.Id, s.Name, s.Phone })
                .ToListAsync(ct);

            var activityIds = owedOnly
                ? dueBySupplier.Where(kv => kv.Value > 0.009m).Select(kv => kv.Key)
                    .Union(unusedCredits.Where(kv => kv.Value > 0.009m).Select(kv => kv.Key))
                    .ToHashSet()
                : null;

            return suppliers
                .Select(s => new PartyLedgerRowDto(
                    s.Id,
                    s.Name,
                    s.Phone,
                    dueBySupplier.GetValueOrDefault(s.Id)))
                .Where(s => !owedOnly || (activityIds?.Contains(s.PartyId) ?? false))
                .OrderByDescending(s => s.OutstandingBalance)
                .ThenBy(s => s.Name)
                .Take(500)
                .ToList();
        }

        var dueByCustomer = await ComputeAllCustomerOpenDuesAsync(branchId, ct);

        var cq = _uow.Repository<Customer>().Query().AsNoTracking()
            .Where(c => c.Status == EntityStatus.Active);
        if (branchId.HasValue) cq = cq.Where(c => c.BranchId == branchId);
        if (!string.IsNullOrWhiteSpace(term))
        {
            cq = cq.Where(c => c.Name.Contains(term) || (c.Phone != null && c.Phone.Contains(term)));
        }
        else if (owedOnly)
        {
            var owedIds = dueByCustomer
                .Where(kv => kv.Value > 0.009m)
                .Select(kv => kv.Key)
                .ToList();
            if (owedIds.Count == 0) return [];
            cq = FilterByIdChunks(cq, owedIds);
        }

        var customers = await cq
            .Select(c => new { c.Id, c.Name, c.Phone })
            .ToListAsync(ct);

        return customers
            .Select(c => new PartyLedgerRowDto(
                c.Id,
                c.Name,
                c.Phone,
                dueByCustomer.GetValueOrDefault(c.Id)))
            .Where(c => !owedOnly || c.OutstandingBalance > 0.009m)
            .OrderByDescending(c => c.OutstandingBalance)
            .ThenBy(c => c.Name)
            .Take(500)
            .ToList();
    }

    /// <summary>SQL Server has ~2100 parameter limit; chunk large id filters.</summary>
    private static IQueryable<Customer> FilterByIdChunks(IQueryable<Customer> query, List<int> ids)
    {
        if (ids.Count == 0) return query.Where(_ => false);
        if (ids.Count <= 2000) return query.Where(c => ids.Contains(c.Id));

        // Build OR of chunked Contains — evaluated client-side via Union after materializing chunks.
        // Caller will ToListAsync; for large sets we Union queries.
        IQueryable<Customer>? combined = null;
        foreach (var chunk in ids.Chunk(1500))
        {
            var local = chunk.ToList();
            var part = query.Where(c => local.Contains(c.Id));
            combined = combined is null ? part : combined.Union(part);
        }

        return combined ?? query.Where(_ => false);
    }

    private static IQueryable<Supplier> FilterSupplierByIdChunks(IQueryable<Supplier> query, List<int> ids)
    {
        if (ids.Count == 0) return query.Where(_ => false);
        if (ids.Count <= 2000) return query.Where(s => ids.Contains(s.Id));

        IQueryable<Supplier>? combined = null;
        foreach (var chunk in ids.Chunk(1500))
        {
            var local = chunk.ToList();
            var part = query.Where(s => local.Contains(s.Id));
            combined = combined is null ? part : combined.Union(part);
        }

        return combined ?? query.Where(_ => false);
    }

    /// <summary>
    /// Open purchase dues for all suppliers, plus remaining <see cref="Supplier.OpeningBalance"/>.
    /// Outstanding payables are not FY-scoped (prior-year unpaid bills remain due).
    /// When <paramref name="netOfReturnCredits"/> is true, Return Records settlements and
    /// unused return credit are applied so the total matches the Parties Due column sum.
    /// </summary>
    private async Task<Dictionary<int, decimal>> ComputeAllSupplierOpenDuesAsync(
        int? branchId,
        CancellationToken ct,
        bool netOfReturnCredits = true)
    {
        var q = _uow.Repository<Purchase>().Query().AsNoTracking()
            .Where(p => p.Status != PurchaseStatus.Cancelled
                        && p.Status != PurchaseStatus.Draft
                        && p.GrandTotal > p.PaidAmount);
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

        var purchases = await q
            .Select(p => new { p.Id, p.SupplierId, Due = p.GrandTotal - p.PaidAmount })
            .ToListAsync(ct);

        var dueBySupplier = new Dictionary<int, decimal>();
        Dictionary<int, decimal>? adjustedByPurchase = null;
        if (netOfReturnCredits)
            adjustedByPurchase = await GetReturnAdjustedByPurchaseIdAsync(branchId, supplierIds: null, ct);

        foreach (var p in purchases)
        {
            var adj = adjustedByPurchase?.GetValueOrDefault(p.Id) ?? 0m;
            var due = p.Due - adj;
            if (due <= 0.009m) continue;
            dueBySupplier[p.SupplierId] = dueBySupplier.GetValueOrDefault(p.SupplierId) + due;
        }

        var suppliersQ = _uow.Repository<Supplier>().Query().AsNoTracking()
            .Where(s => s.Status == EntityStatus.Active && s.OpeningBalance > 0.009m);
        if (branchId.HasValue) suppliersQ = suppliersQ.Where(s => s.BranchId == branchId);

        var openings = await suppliersQ
            .Select(s => new { s.Id, s.OpeningBalance })
            .ToListAsync(ct);

        foreach (var row in openings)
            dueBySupplier[row.Id] = dueBySupplier.GetValueOrDefault(row.Id) + row.OpeningBalance;

        if (netOfReturnCredits)
            await SubtractUnusedPurchaseReturnCreditsAsync(dueBySupplier, branchId, supplierIds: null, ct);
        return dueBySupplier;
    }

    /// <summary>
    /// Open purchase dues by supplier — same formula as <see cref="ListPartyBillsAsync"/> for suppliers.
    /// </summary>
    private async Task<Dictionary<int, decimal>> ComputeSupplierOpenDuesAsync(
        IReadOnlyList<int> supplierIds,
        int? branchId,
        CancellationToken ct)
    {
        if (supplierIds.Count == 0) return new Dictionary<int, decimal>();

        // Prefer targeted query for small sets (e.g. single-party voucher validation).
        if (supplierIds.Count <= 50)
        {
            var ids = supplierIds.ToList();
            var q = _uow.Repository<Purchase>().Query().AsNoTracking()
                .Where(p => ids.Contains(p.SupplierId)
                            && p.Status != PurchaseStatus.Cancelled
                            && p.Status != PurchaseStatus.Draft
                            && p.GrandTotal > p.PaidAmount);
            if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

            var purchases = await q
                .Select(p => new { p.Id, p.SupplierId, Due = p.GrandTotal - p.PaidAmount })
                .ToListAsync(ct);

            var adjustedByPurchase = await GetReturnAdjustedByPurchaseIdAsync(branchId, ids, ct);
            var dueByPurchase = new Dictionary<int, decimal>();
            foreach (var p in purchases)
            {
                var due = p.Due - adjustedByPurchase.GetValueOrDefault(p.Id);
                if (due <= 0.009m) continue;
                dueByPurchase[p.SupplierId] = dueByPurchase.GetValueOrDefault(p.SupplierId) + due;
            }

            var openings = await _uow.Repository<Supplier>().Query().AsNoTracking()
                .Where(s => ids.Contains(s.Id) && s.OpeningBalance > 0.009m)
                .Select(s => new { s.Id, s.OpeningBalance })
                .ToListAsync(ct);
            foreach (var row in openings)
                dueByPurchase[row.Id] = dueByPurchase.GetValueOrDefault(row.Id) + row.OpeningBalance;

            await SubtractUnusedPurchaseReturnCreditsAsync(dueByPurchase, branchId, ids, ct);
            return dueByPurchase;
        }

        var all = await ComputeAllSupplierOpenDuesAsync(branchId, ct);
        return supplierIds
            .Where(all.ContainsKey)
            .ToDictionary(id => id, id => all[id]);
    }

    /// <summary>
    /// Credit amounts saved in Return Records against a purchase bill number.
    /// </summary>
    private async Task<Dictionary<int, decimal>> GetReturnAdjustedByPurchaseIdAsync(
        int? branchId,
        IReadOnlyList<int>? supplierIds,
        CancellationToken ct)
    {
        // SettledAgainstPurchaseId is the source of truth (Return Records → purchase bill).
        // Do not require receipt-string heuristics — MedWin refs like "MW-P-355 / 113" are valid.
        var q = _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
            .Where(r => r.Status == PurchaseReturnStatus.Completed
                        && r.SettledAgainstPurchaseId != null);
        if (branchId.HasValue) q = q.Where(r => r.BranchId == branchId);
        if (supplierIds is { Count: > 0 })
            q = q.Where(r => supplierIds.Contains(r.SupplierId));

        return await q
            .GroupBy(r => r.SettledAgainstPurchaseId!.Value)
            .Select(g => new { PurchaseId = g.Key, Credit = g.Sum(x => x.CreditAmount) })
            .ToDictionaryAsync(x => x.PurchaseId, x => x.Credit, ct);
    }

    /// <summary>
    /// Unused purchase-return credit by supplier (pending Return Records settlement).
    /// Returns already settled via receipt # or purchase bill are excluded.
    /// </summary>
    private async Task<Dictionary<int, decimal>> GetUnusedPurchaseReturnCreditsBySupplierAsync(
        int? branchId,
        IReadOnlyList<int>? supplierIds,
        CancellationToken ct)
    {
        var q = _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
            .Where(r => r.Status == PurchaseReturnStatus.Completed
                        && r.CreditAmount > r.CreditAppliedAmount
                        && (r.SupplierReturnReceiptNumber == null || r.SupplierReturnReceiptNumber == ""));
        if (branchId.HasValue) q = q.Where(r => r.BranchId == branchId);
        if (supplierIds is { Count: > 0 })
            q = q.Where(r => supplierIds.Contains(r.SupplierId));

        return await q
            .GroupBy(r => r.SupplierId)
            .Select(g => new
            {
                SupplierId = g.Key,
                Credit = g.Sum(r => r.CreditAmount - r.CreditAppliedAmount)
            })
            .ToDictionaryAsync(x => x.SupplierId, x => x.Credit, ct);
    }

    private static void ApplyUnusedPurchaseReturnCredits(
        Dictionary<int, decimal> dueBySupplier,
        IReadOnlyDictionary<int, decimal> unusedCredits)
    {
        foreach (var (supplierId, credit) in unusedCredits)
        {
            if (credit <= 0.009m) continue;
            var due = dueBySupplier.GetValueOrDefault(supplierId) - credit;
            if (due > 0.009m)
                dueBySupplier[supplierId] = due;
            else
                dueBySupplier.Remove(supplierId);
        }
    }

    /// <summary>
    /// Unused purchase-return credit reduces supplier outstanding (MedWin return bills).
    /// </summary>
    private async Task SubtractUnusedPurchaseReturnCreditsAsync(
        Dictionary<int, decimal> dueBySupplier,
        int? branchId,
        IReadOnlyList<int>? supplierIds,
        CancellationToken ct)
    {
        var credits = await GetUnusedPurchaseReturnCreditsBySupplierAsync(branchId, supplierIds, ct);
        ApplyUnusedPurchaseReturnCredits(dueBySupplier, credits);
    }

    /// <summary>
    /// Open sale dues for all customers — same formula as <see cref="ListPartyBillsAsync"/>
    /// (CustomerId match, plus walk-in name/phone match). Aggregates from sales first
    /// so parties with dues are never dropped by a premature Take(500).
    /// </summary>
    private async Task<Dictionary<int, decimal>> ComputeAllCustomerOpenDuesAsync(
        int? branchId,
        CancellationToken ct)
    {
        var openStatuses = new[] { SaleStatus.Completed, SaleStatus.PartiallyReturned };
        var sq = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => openStatuses.Contains(s.Status) && s.GrandTotal > s.PaidAmount);
        if (branchId.HasValue) sq = sq.Where(s => s.BranchId == branchId);

        var sales = await sq
            .Select(s => new
            {
                s.Id,
                s.CustomerId,
                s.BillingCustomerName,
                s.BillingCustomerPhone,
                s.GrandTotal,
                s.PaidAmount
            })
            .ToListAsync(ct);

        var result = new Dictionary<int, decimal>();
        if (sales.Count == 0) return result;

        var saleIds = sales.Select(s => s.Id).ToList();
        var returnedBySale = new Dictionary<int, decimal>();
        foreach (var chunk in saleIds.Chunk(1500))
        {
            var local = chunk.ToList();
            var part = await _uow.Repository<SaleReturn>().Query().AsNoTracking()
                .Where(r => local.Contains(r.SaleId))
                .GroupBy(r => r.SaleId)
                .Select(g => new { SaleId = g.Key, Returned = g.Sum(x => x.GrandTotal) })
                .ToListAsync(ct);
            foreach (var row in part)
                returnedBySale[row.SaleId] = row.Returned;
        }

        foreach (var sale in sales)
        {
            if (sale.CustomerId is not int cid) continue;
            returnedBySale.TryGetValue(sale.Id, out var returned);
            var due = sale.GrandTotal - sale.PaidAmount - returned;
            if (due <= 0) continue;
            result[cid] = result.GetValueOrDefault(cid) + due;
        }

        var walkIns = sales
            .Where(s => s.CustomerId is null && !string.IsNullOrWhiteSpace(s.BillingCustomerName))
            .ToList();
        if (walkIns.Count == 0) return result;

        var names = walkIns.Select(s => s.BillingCustomerName!).Distinct().ToList();
        var customers = new List<(int Id, string Name, string? Phone)>();
        var cq = _uow.Repository<Customer>().Query().AsNoTracking()
            .Where(c => c.Status == EntityStatus.Active);
        if (branchId.HasValue) cq = cq.Where(c => c.BranchId == branchId);

        foreach (var chunk in names.Chunk(500))
        {
            var local = chunk.ToList();
            var rows = await cq
                .Where(c => local.Contains(c.Name))
                .Select(c => new { c.Id, c.Name, c.Phone })
                .ToListAsync(ct);
            customers.AddRange(rows.Select(c => (c.Id, c.Name, c.Phone)));
        }

        if (customers.Count == 0) return result;

        foreach (var sale in walkIns)
        {
            returnedBySale.TryGetValue(sale.Id, out var returned);
            var due = sale.GrandTotal - sale.PaidAmount - returned;
            if (due <= 0) continue;

            foreach (var c in customers)
            {
                if (!string.Equals(c.Name, sale.BillingCustomerName, StringComparison.Ordinal))
                    continue;
                if (string.IsNullOrWhiteSpace(c.Phone)
                    || string.Equals(c.Phone, sale.BillingCustomerPhone, StringComparison.Ordinal))
                {
                    result[c.Id] = result.GetValueOrDefault(c.Id) + due;
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Open sale dues by customer — same formula as <see cref="ListPartyBillsAsync"/> for customers.
    /// </summary>
    private async Task<Dictionary<int, decimal>> ComputeCustomerOpenDuesAsync(
        IReadOnlyList<(int Id, string Name, string? Phone)> customers,
        int? branchId,
        CancellationToken ct)
    {
        var result = customers.ToDictionary(c => c.Id, _ => 0m);
        if (customers.Count == 0) return result;

        var all = await ComputeAllCustomerOpenDuesAsync(branchId, ct);
        foreach (var c in customers)
        {
            if (all.TryGetValue(c.Id, out var due))
                result[c.Id] = due;
        }

        return result;
    }

    private async Task<decimal> ComputePartyOutstandingAsync(
        PartyLedgerKind kind,
        int partyId,
        int? branchId,
        CancellationToken ct)
    {
        // Party-scoped only — never load all sales/purchases inside a voucher transaction
        // (avoids long locks and concurrent DbContext use with UI refresh).
        if (kind == PartyLedgerKind.Supplier)
        {
            var map = await ComputeSupplierOpenDuesAsync([partyId], branchId, ct);
            return map.GetValueOrDefault(partyId);
        }

        var bills = await ListPartyBillsAsync(PartyLedgerKind.Customer, partyId, branchId, openOnly: true, ct);
        return bills.Sum(b => b.BalanceDue);
    }

    public async Task<List<PartyBillRowDto>> ListPartyBillsAsync(
        PartyLedgerKind kind,
        int partyId,
        int? branchId,
        bool openOnly = true,
        CancellationToken ct = default)
    {
        if (kind == PartyLedgerKind.Supplier)
        {
            // Open payables are not FY-scoped — unpaid prior-year bills (and opening balances) stay due.
            var q = _uow.Repository<Purchase>().Query().AsNoTracking()
                .Where(p => p.SupplierId == partyId
                            && p.Status != PurchaseStatus.Cancelled
                            && p.Status != PurchaseStatus.Draft);
            if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

            var purchaseRows = await q
                .OrderByDescending(p => p.InvoiceDate)
                .Select(p => new
                {
                    p.Id,
                    p.InvoiceNumber,
                    p.InvoiceDate,
                    p.GrandTotal,
                    p.PaidAmount,
                    p.SupplierInvoiceNumber
                })
                .ToListAsync(ct);

            var returnQuery = _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
                .Where(r => r.SupplierId == partyId
                            && r.Status == PurchaseReturnStatus.Completed);
            if (branchId.HasValue) returnQuery = returnQuery.Where(r => r.BranchId == branchId);

            var returnRows = await returnQuery
                .OrderByDescending(r => r.ReturnDate)
                .ThenByDescending(r => r.Id)
                .Select(r => new
                {
                    r.Id,
                    r.ReturnNumber,
                    r.ReturnDate,
                    r.CreditAmount,
                    r.CreditAppliedAmount,
                    r.SupplierReturnReceiptNumber,
                    r.ReceiptSettlementKind,
                    r.SettledAgainstPurchaseId,
                    r.PurchaseId
                })
                .ToListAsync(ct);

            var adjustedByPurchase = returnRows
                .Where(r => r.SettledAgainstPurchaseId is int)
                .GroupBy(r => r.SettledAgainstPurchaseId!.Value)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.CreditAmount));

            var bills = purchaseRows
                .Select(p =>
                {
                    adjustedByPurchase.TryGetValue(p.Id, out var adjusted);
                    var rawDue = p.GrandTotal - p.PaidAmount - adjusted;
                    var due = rawDue > 0.009m ? rawDue : 0m;
                    return new PartyBillRowDto(
                        p.Id,
                        p.InvoiceNumber,
                        p.InvoiceDate,
                        p.GrandTotal,
                        p.PaidAmount,
                        due,
                        p.SupplierInvoiceNumber,
                        false,
                        adjusted);
                })
                .ToList();

            var supplier = await _uow.Repository<Supplier>().Query().AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == partyId, ct);
            if (supplier is not null && supplier.OpeningBalance > 0.009m)
            {
                bills.Insert(0, new PartyBillRowDto(
                    SupplierOpeningBalanceBillId,
                    "OPENING",
                    supplier.CreatedAtUtc == default ? _clock.Today : supplier.CreatedAtUtc,
                    supplier.OpeningBalance,
                    0m,
                    supplier.OpeningBalance,
                    null,
                    false,
                    0m));
            }

            Dictionary<int, string>? purchaseInvoiceById = null;
            var purchaseIds = returnRows
                .Where(r => r.PurchaseId is int)
                .Select(r => r.PurchaseId!.Value)
                .Distinct()
                .ToList();
            if (purchaseIds.Count > 0)
            {
                purchaseInvoiceById = await _uow.Repository<Purchase>().Query().AsNoTracking()
                    .Where(p => purchaseIds.Contains(p.Id))
                    .Select(p => new { p.Id, p.InvoiceNumber })
                    .ToDictionaryAsync(p => p.Id, p => p.InvoiceNumber, ct);
            }

            var returnCredits = returnRows
                .Select(r =>
                {
                    var hasReceipt = PurchaseReturn.IsRealSupplierReceiptNumber(r.SupplierReturnReceiptNumber);
                    var settledOnBill = r.SettledAgainstPurchaseId is int;
                    var settledOnReceipt = !settledOnBill && hasReceipt;

                    // Receipt #: Adjusted on the return only (no bill).
                    // Bill settlement: Adjusted on the bill row AND shown on the return so
                    // rows like "MW-P-355 / 113" are visibly marked as adjusted.
                    var adjusted = (settledOnReceipt || settledOnBill) ? r.CreditAmount : 0m;
                    decimal due;
                    if (settledOnBill || settledOnReceipt)
                        due = 0m;
                    else
                    {
                        var remaining = r.CreditAmount - r.CreditAppliedAmount;
                        due = remaining > 0.009m ? -remaining : 0m;
                    }

                    string? linkedInvoice = null;
                    if (r.PurchaseId is int pid)
                        purchaseInvoiceById?.TryGetValue(pid, out linkedInvoice);

                    return new PartyBillRowDto(
                        -r.Id,
                        r.ReturnNumber,
                        r.ReturnDate,
                        r.CreditAmount,
                        r.CreditAppliedAmount,
                        due,
                        r.SupplierReturnReceiptNumber ?? linkedInvoice,
                        true,
                        adjusted);
                })
                .ToList();
            if (returnCredits.Count > 0)
                bills.AddRange(returnCredits);

            if (openOnly)
            {
                // Keep purchase-return rows visible even when Due/Adjusted settle to zero,
                // so Enter/double-click can open the return document.
                bills = bills.Where(b => b.IsPurchaseReturn || Math.Abs(b.BalanceDue) > 0.009m).ToList();
            }

            return bills;
        }

        // Receivables: include completed and partially-returned invoices that still have due.
        // Fully returned / cancelled / draft bills are excluded. Not FY-scoped.
        var openStatuses = new[] { SaleStatus.Completed, SaleStatus.PartiallyReturned };

        var customer = await _uow.Repository<Customer>().Query().AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == partyId, ct);

        var sq = _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => openStatuses.Contains(s.Status)
                        && (s.CustomerId == partyId
                            || (s.CustomerId == null
                                && customer != null
                                && s.BillingCustomerName == customer.Name
                                && (customer.Phone == null
                                    || customer.Phone == ""
                                    || s.BillingCustomerPhone == customer.Phone))));
        if (branchId.HasValue) sq = sq.Where(s => s.BranchId == branchId);

        var sales = await sq
            .OrderByDescending(s => s.InvoiceDate)
            .ThenByDescending(s => s.Id)
            .ToListAsync(ct);

        if (sales.Count == 0) return [];

        var saleIds = sales.Select(s => s.Id).ToList();
        var returnedBySale = await _uow.Repository<SaleReturn>().Query().AsNoTracking()
            .Where(r => saleIds.Contains(r.SaleId))
            .GroupBy(r => r.SaleId)
            .Select(g => new { SaleId = g.Key, Returned = g.Sum(x => x.GrandTotal) })
            .ToDictionaryAsync(x => x.SaleId, x => x.Returned, ct);

        return sales
            .Select(s =>
            {
                returnedBySale.TryGetValue(s.Id, out var returned);
                var due = s.GrandTotal - s.PaidAmount - returned;
                return new PartyBillRowDto(
                    s.Id,
                    s.InvoiceNumber,
                    s.InvoiceDate,
                    s.GrandTotal,
                    s.PaidAmount,
                    due > 0 ? due : 0m,
                    null,
                    false);
            })
            .Where(b => !openOnly || b.BalanceDue > 0)
            .ToList();
    }

    public async Task<PartyBillsSummaryDto> GetPartyBillsSummaryAsync(
        PartyLedgerKind kind,
        int partyId,
        int? branchId,
        CancellationToken ct = default)
    {
        var bills = await ListPartyBillsAsync(kind, partyId, branchId, openOnly: false, ct);
        return SummarizeBills(bills);
    }

    public async Task<PartyBillsSummaryDto> GetPartiesBillsSummaryAsync(
        PartyLedgerKind kind,
        IReadOnlyList<int> partyIds,
        int? branchId,
        CancellationToken ct = default)
    {
        if (partyIds.Count == 0)
            return PartyBillsSummaryDto.Empty;

        var all = new List<PartyBillRowDto>();
        foreach (var id in partyIds)
            all.AddRange(await ListPartyBillsAsync(kind, id, branchId, openOnly: false, ct));

        return SummarizeBills(all);
    }

    private static PartyBillsSummaryDto SummarizeBills(IReadOnlyList<PartyBillRowDto> bills)
    {
        if (bills.Count == 0)
            return PartyBillsSummaryDto.Empty;

        var pending = bills.Where(b => b.BalanceDue > 0.009m).ToList();
        return new PartyBillsSummaryDto(
            bills.Count,
            pending.Count,
            bills.Sum(b => b.GrandTotal),
            // Net outstanding = sum of Due (purchase dues minus return credits).
            bills.Sum(b => b.BalanceDue),
            pending.Count > 0 ? pending.Min(b => b.InvoiceDate) : null,
            pending.Count > 0 ? pending.Max(b => b.InvoiceDate) : null);
    }

    public async Task<List<AccountLookupDto>> ListAccountsAsync(
        AccountType? type = null,
        CancellationToken ct = default)
    {
        var q = _uow.Repository<Account>().Query()
            .Where(a => a.Status == EntityStatus.Active);
        if (type.HasValue) q = q.Where(a => a.Type == type.Value);

        return await q
            .OrderBy(a => a.Code)
            .Select(a => new AccountLookupDto(a.Id, a.Code, a.Name, a.Type))
            .ToListAsync(ct);
    }

    public Task<List<AccountLookupDto>> ListCashAndBankAccountsAsync(CancellationToken ct = default)
    {
        var codes = new[] { CashAccountCode, BankAccountCode };
        return _uow.Repository<Account>().Query()
            .Where(a => a.Status == EntityStatus.Active && codes.Contains(a.Code))
            .OrderBy(a => a.Code)
            .Select(a => new AccountLookupDto(a.Id, a.Code, a.Name, a.Type))
            .ToListAsync(ct);
    }

    public async Task<List<CashBookRowDto>> GetCashBookAsync(int? branchId, CancellationToken ct = default)
    {
        var cash = await GetAccountByCodeAsync(CashAccountCode, ct);
        if (cash is null) return [];

        var q = _uow.Repository<JournalLine>().Query().AsNoTracking()
            .Include(l => l.JournalEntry)
            .Where(l => l.AccountId == cash.Id && l.JournalEntry != null)
            .WhereInFinancialYear(_financialYear.Active, l => l.JournalEntry!.EntryDate);

        if (branchId.HasValue)
            q = q.Where(l => l.JournalEntry!.BranchId == branchId);

        var lines = await q
            .OrderBy(l => l.JournalEntry!.EntryDate)
            .ThenBy(l => l.JournalEntryId)
            .ThenBy(l => l.Id)
            .Select(l => new
            {
                l.JournalEntry!.EntryDate,
                l.JournalEntry.VoucherNumber,
                l.JournalEntry.Narration,
                l.EntryType,
                l.Amount
            })
            .ToListAsync(ct);

        var balance = cash.OpeningBalance;
        var rows = new List<CashBookRowDto>(lines.Count);
        foreach (var line in lines)
        {
            var debit = line.EntryType == LedgerEntryType.Debit ? line.Amount : 0m;
            var credit = line.EntryType == LedgerEntryType.Credit ? line.Amount : 0m;
            balance += debit - credit;
            rows.Add(new CashBookRowDto(
                line.EntryDate,
                line.VoucherNumber,
                line.Narration,
                debit,
                credit,
                balance));
        }

        return rows;
    }

    public async Task<List<JournalEntryListDto>> ListJournalEntriesAsync(
        string? term,
        int? branchId,
        int take = 300,
        CancellationToken ct = default)
    {
        term = term?.Trim() ?? string.Empty;
        var q = _uow.Repository<JournalEntry>().Query().AsNoTracking()
            .WhereInFinancialYear(_financialYear.Active, e => e.EntryDate);
        if (branchId.HasValue) q = q.Where(e => e.BranchId == branchId);

        if (!string.IsNullOrWhiteSpace(term))
        {
            q = q.Where(e =>
                e.VoucherNumber.Contains(term) ||
                (e.Narration != null && e.Narration.Contains(term)) ||
                (e.ReferenceType != null && e.ReferenceType.Contains(term)));
        }

        return await q
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.Id)
            .Take(take)
            .Select(e => new JournalEntryListDto(
                e.Id,
                e.VoucherNumber,
                e.EntryDate,
                e.Narration,
                e.ReferenceType,
                e.Lines.Sum(l => l.EntryType == LedgerEntryType.Debit ? l.Amount : 0m)))
            .ToListAsync(ct);
    }

    public Task<List<JournalLineDto>> GetJournalLinesAsync(int journalEntryId, CancellationToken ct = default)
        => _uow.Repository<JournalLine>().Query().AsNoTracking()
            .Where(l => l.JournalEntryId == journalEntryId)
            .OrderBy(l => l.EntryType)
            .ThenBy(l => l.Id)
            .Select(l => new JournalLineDto(
                l.Account != null ? l.Account.Code : "",
                l.Account != null ? l.Account.Name : "",
                l.EntryType,
                l.Amount,
                l.Remarks))
            .ToListAsync(ct);

    public async Task<List<TrialBalanceRowDto>> GetTrialBalanceAsync(CancellationToken ct = default)
    {
        var accounts = await _uow.Repository<Account>().Query()
            .Where(a => a.Status == EntityStatus.Active)
            .OrderBy(a => a.Code)
            .ToListAsync(ct);

        return accounts.Select(a =>
        {
            var balance = a.OpeningBalance + a.CurrentBalance;
            var (debit, credit) = ToTrialBalance(balance, a.Type);
            return new TrialBalanceRowDto(a.Code, a.Name, a.Type, debit, credit);
        }).ToList();
    }

    public Task<string> PreviewNextVoucherNumberAsync(VoucherKind kind, int? branchId, CancellationToken ct = default)
        => GenerateVoucherNumberAsync(kind, branchId, ct);

    public Task<Result<VoucherReceiptDto>> CreatePaymentAsync(
        CreatePaymentRequest request,
        int? branchId,
        CancellationToken ct = default)
        => PostPartyVoucherAsync(
            VoucherKind.Payment,
            request.SupplierId,
            PartyLedgerKind.Supplier,
            request.Amount,
            request.CashOrBankAccountId,
            request.EntryDate,
            request.Narration,
            branchId,
            ct,
            request.AllocationMode,
            request.BillAllocations);

    public Task<Result<VoucherReceiptDto>> CreateReceiptAsync(
        CreateReceiptRequest request,
        int? branchId,
        CancellationToken ct = default)
        => PostPartyVoucherAsync(
            VoucherKind.Receipt,
            request.CustomerId,
            PartyLedgerKind.Customer,
            request.Amount,
            request.CashOrBankAccountId,
            request.EntryDate,
            request.Narration,
            branchId,
            ct,
            request.AllocationMode,
            billAllocations: null,
            receiptAllocations: request.BillAllocations);

    public async Task<Result<VoucherReceiptDto>> CreateExpenseAsync(
        CreateExpenseRequest request,
        int? branchId,
        CancellationToken ct = default)
    {
        if (!_financialYear.CanEditTransactions)
            return FinancialYearGuard.FailIfReadOnly<VoucherReceiptDto>(_financialYear);
        if (request.Amount <= 0)
            return Result.Failure<VoucherReceiptDto>("Enter a valid expense amount.");

        try
        {
            var receipt = await _uow.ExecuteInTransactionAsync(async token =>
            {
                var expenseAccount = await _uow.Repository<Account>().GetByIdAsync(request.ExpenseAccountId, token);
                var cashAccount = await _uow.Repository<Account>().GetByIdAsync(request.CashOrBankAccountId, token);
                if (expenseAccount is null || cashAccount is null)
                    throw new AccountingException("Select valid expense and cash/bank accounts.");
                if (expenseAccount.Type != AccountType.Expense)
                    throw new AccountingException("The debit account must be an expense account.");

                var voucherNumber = await GenerateVoucherNumberAsync(VoucherKind.Expense, branchId, token);
                var entry = new JournalEntry
                {
                    BranchId = branchId,
                    VoucherNumber = voucherNumber,
                    EntryDate = request.EntryDate,
                    Narration = request.Narration,
                    ReferenceType = nameof(VoucherKind.Expense)
                };
                await _uow.Repository<JournalEntry>().AddAsync(entry, token);
                await _uow.SaveChangesAsync(token);

                await AddLineAsync(entry.Id, expenseAccount, LedgerEntryType.Debit, request.Amount,
                    request.Narration, token);
                await AddLineAsync(entry.Id, cashAccount, LedgerEntryType.Credit, request.Amount,
                    request.Narration, token);
                await _uow.SaveChangesAsync(token);

                return new VoucherReceiptDto
                {
                    JournalEntryId = entry.Id,
                    VoucherNumber = voucherNumber,
                    EntryDate = request.EntryDate,
                    Amount = request.Amount
                };
            }, ct);

            return Result.Success(receipt);
        }
        catch (AccountingException ex)
        {
            return Result.Failure<VoucherReceiptDto>(ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<VoucherReceiptDto>($"Could not save expense: {ex.Message}");
        }
    }

    private async Task<Result<VoucherReceiptDto>> PostPartyVoucherAsync(
        VoucherKind kind,
        int partyId,
        PartyLedgerKind partyKind,
        decimal amount,
        int cashOrBankAccountId,
        DateTime entryDate,
        string? narration,
        int? branchId,
        CancellationToken ct,
        PaymentAllocationMode allocationMode = PaymentAllocationMode.Fifo,
        IReadOnlyList<BillPaymentAllocationDto>? billAllocations = null,
        IReadOnlyList<BillReceiptAllocationDto>? receiptAllocations = null)
    {
        if (!_financialYear.CanEditTransactions)
            return FinancialYearGuard.FailIfReadOnly<VoucherReceiptDto>(_financialYear);
        if (amount <= 0)
            return Result.Failure<VoucherReceiptDto>("Enter a valid amount.");

        try
        {
            var receipt = await _uow.ExecuteInTransactionAsync(async token =>
            {
                Account partyAccount;
                string partyName;

                if (partyKind == PartyLedgerKind.Supplier)
                {
                    var supplier = await _uow.Repository<Supplier>().GetByIdAsync(partyId, token);
                    if (supplier is null) throw new AccountingException("Supplier not found.");

                    var outstanding = await ComputePartyOutstandingAsync(
                        PartyLedgerKind.Supplier, partyId, branchId, token);
                    if (amount > outstanding)
                        throw new AccountingException(
                            $"Payment amount exceeds outstanding balance ({outstanding:N2}).");

                    partyAccount = await RequireAccountByCodeAsync(PayableAccountCode, token);
                    partyName = supplier.Name;

                    if (allocationMode == PaymentAllocationMode.BillWise)
                        await AllocateSupplierPaymentToBillsAsync(partyId, amount, billAllocations, branchId, token);
                    else
                        await AllocateSupplierPaymentAsync(partyId, amount, branchId, token);

                    // Keep master balance aligned with open bills after allocation.
                    supplier.OutstandingBalance = await ComputePartyOutstandingAsync(
                        PartyLedgerKind.Supplier, partyId, branchId, token);
                    _uow.Repository<Supplier>().Update(supplier);
                }
                else
                {
                    var customer = await _uow.Repository<Customer>().GetByIdAsync(partyId, token);
                    if (customer is null) throw new AccountingException("Customer not found.");

                    var outstanding = await ComputePartyOutstandingAsync(
                        PartyLedgerKind.Customer, partyId, branchId, token);
                    if (amount > outstanding)
                        throw new AccountingException(
                            $"Receipt amount exceeds outstanding balance ({outstanding:N2}).");

                    partyAccount = await RequireAccountByCodeAsync(ReceivableAccountCode, token);
                    partyName = customer.Name;

                    var cashAccountForReceipt = await _uow.Repository<Account>().GetByIdAsync(cashOrBankAccountId, token);
                    if (cashAccountForReceipt is null || cashAccountForReceipt.Type != AccountType.Asset)
                        throw new AccountingException("Select a valid cash or bank account.");
                    var receiptMethod = ResolveReceiptPaymentMethod(cashAccountForReceipt);
                    var receiptVoucherPreview = await GenerateVoucherNumberAsync(kind, branchId, token);

                    if (allocationMode == PaymentAllocationMode.BillWise)
                        await AllocateCustomerReceiptToBillsAsync(
                            partyId, amount, receiptAllocations, branchId, receiptMethod, receiptVoucherPreview, token);
                    else
                        await AllocateCustomerReceiptAsync(
                            partyId, amount, branchId, receiptMethod, receiptVoucherPreview, token);

                    customer.OutstandingBalance = await ComputePartyOutstandingAsync(
                        PartyLedgerKind.Customer, partyId, branchId, token);
                    _uow.Repository<Customer>().Update(customer);
                }

                var cashAccount = await _uow.Repository<Account>().GetByIdAsync(cashOrBankAccountId, token);
                if (cashAccount is null || cashAccount.Type != AccountType.Asset)
                    throw new AccountingException("Select a valid cash or bank account.");

                var voucherNumber = await GenerateVoucherNumberAsync(kind, branchId, token);
                var entry = new JournalEntry
                {
                    BranchId = branchId,
                    VoucherNumber = voucherNumber,
                    EntryDate = entryDate,
                    Narration = narration ?? $"{kind} — {partyName}",
                    ReferenceType = kind.ToString(),
                    ReferenceId = partyId
                };
                await _uow.Repository<JournalEntry>().AddAsync(entry, token);
                await _uow.SaveChangesAsync(token);

                if (kind == VoucherKind.Payment)
                {
                    await AddLineAsync(entry.Id, partyAccount, LedgerEntryType.Debit, amount, narration, token);
                    await AddLineAsync(entry.Id, cashAccount, LedgerEntryType.Credit, amount, narration, token);
                }
                else
                {
                    await AddLineAsync(entry.Id, cashAccount, LedgerEntryType.Debit, amount, narration, token);
                    await AddLineAsync(entry.Id, partyAccount, LedgerEntryType.Credit, amount, narration, token);
                }

                await _uow.SaveChangesAsync(token);

                var company = await _uow.Repository<CompanyProfile>().Query().AsNoTracking()
                    .FirstOrDefaultAsync(token);
                decimal outstandingAfter = 0m;
                string? partyPhone = null;
                if (partyKind == PartyLedgerKind.Customer)
                {
                    var refreshed = await _uow.Repository<Customer>().GetByIdAsync(partyId, token);
                    outstandingAfter = refreshed?.OutstandingBalance ?? 0m;
                    partyPhone = refreshed?.Phone;
                }

                return new VoucherReceiptDto
                {
                    JournalEntryId = entry.Id,
                    VoucherNumber = voucherNumber,
                    EntryDate = entryDate,
                    Amount = amount,
                    PartyName = partyName,
                    PartyPhone = partyPhone,
                    OutstandingAfter = outstandingAfter,
                    CompanyName = company?.CompanyName,
                    CashOrBankAccountName = cashAccount.Name,
                    Narration = entry.Narration
                };
            }, ct);

            return Result.Success(receipt);
        }
        catch (AccountingException ex)
        {
            return Result.Failure<VoucherReceiptDto>(ex.Message);
        }
        catch (Exception ex)
        {
            return Result.Failure<VoucherReceiptDto>($"Could not save voucher: {ex.Message}");
        }
    }

    private async Task AllocateSupplierPaymentAsync(
        int supplierId, decimal amount, int? branchId, CancellationToken ct)
    {
        var remaining = amount;

        // Opening balance is oldest payable — apply FIFO here first.
        var supplier = await _uow.Repository<Supplier>().GetByIdAsync(supplierId, ct);
        if (supplier is not null && supplier.OpeningBalance > 0.009m && remaining > 0)
        {
            var appliedOpening = Math.Min(remaining, supplier.OpeningBalance);
            supplier.OpeningBalance = Math.Round(supplier.OpeningBalance - appliedOpening, 2);
            _uow.Repository<Supplier>().Update(supplier);
            remaining -= appliedOpening;
        }

        if (remaining <= 0) return;

        var openStatuses = new[] { PurchaseStatus.Received, PurchaseStatus.PartiallyReturned };
        var q = _uow.Repository<Purchase>().Query()
            .Where(p => p.SupplierId == supplierId
                        && openStatuses.Contains(p.Status)
                        && p.GrandTotal > p.PaidAmount);
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

        var bills = await q
            .OrderBy(p => p.InvoiceDate)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);

        foreach (var bill in bills)
        {
            if (remaining <= 0) break;

            var due = bill.GrandTotal - bill.PaidAmount;
            if (due <= 0) continue;

            var applied = Math.Min(remaining, due);
            bill.PaidAmount += applied;
            bill.PaymentStatus = bill.PaidAmount >= bill.GrandTotal
                ? PaymentStatus.Paid
                : PaymentStatus.PartiallyPaid;
            _uow.Repository<Purchase>().Update(bill);
            remaining -= applied;
        }
    }

    private async Task AllocateSupplierPaymentToBillsAsync(
        int supplierId,
        decimal amount,
        IReadOnlyList<BillPaymentAllocationDto>? allocations,
        int? branchId,
        CancellationToken ct)
    {
        if (allocations is null || allocations.Count == 0)
            throw new AccountingException("Select at least one purchase bill for bill-wise payment.");

        var positive = allocations.Where(a => a.Amount > 0).ToList();
        if (positive.Count == 0)
            throw new AccountingException("Enter amounts to apply against the selected bills.");

        var sum = positive.Sum(a => a.Amount);
        if (Math.Abs(sum - amount) > 0.01m)
            throw new AccountingException(
                $"Bill allocations (₹{sum:N2}) must equal the payment amount (₹{amount:N2}).");

        var ids = positive.Select(a => a.PurchaseId).Distinct().ToList();
        if (ids.Count != positive.Count)
            throw new AccountingException("Duplicate purchase bills in the allocation list.");

        var openingAlloc = positive.FirstOrDefault(a => a.PurchaseId == SupplierOpeningBalanceBillId);
        var purchaseIds = positive
            .Where(a => a.PurchaseId != SupplierOpeningBalanceBillId)
            .Select(a => a.PurchaseId)
            .ToList();

        if (openingAlloc is not null)
        {
            var supplier = await _uow.Repository<Supplier>().GetByIdAsync(supplierId, ct)
                ?? throw new AccountingException("Supplier not found.");
            if (openingAlloc.Amount > supplier.OpeningBalance + 0.01m)
                throw new AccountingException(
                    $"Amount for OPENING exceeds balance due (₹{supplier.OpeningBalance:N2}).");
            supplier.OpeningBalance = Math.Round(supplier.OpeningBalance - openingAlloc.Amount, 2);
            if (supplier.OpeningBalance < 0) supplier.OpeningBalance = 0;
            _uow.Repository<Supplier>().Update(supplier);
        }

        if (purchaseIds.Count == 0) return;

        var openStatuses = new[] { PurchaseStatus.Received, PurchaseStatus.PartiallyReturned };
        var q = _uow.Repository<Purchase>().Query()
            .Where(p => purchaseIds.Contains(p.Id) && p.SupplierId == supplierId && openStatuses.Contains(p.Status));
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

        var bills = await q.ToListAsync(ct);
        if (bills.Count != purchaseIds.Count)
            throw new AccountingException("One or more selected purchase bills are invalid or closed.");

        var byId = bills.ToDictionary(b => b.Id);
        foreach (var alloc in positive.Where(a => a.PurchaseId != SupplierOpeningBalanceBillId))
        {
            var bill = byId[alloc.PurchaseId];
            var due = bill.GrandTotal - bill.PaidAmount;
            if (alloc.Amount > due + 0.01m)
                throw new AccountingException(
                    $"Amount for {bill.InvoiceNumber} exceeds balance due (₹{due:N2}).");

            bill.PaidAmount += alloc.Amount;
            bill.PaymentStatus = bill.PaidAmount >= bill.GrandTotal
                ? PaymentStatus.Paid
                : PaymentStatus.PartiallyPaid;
            _uow.Repository<Purchase>().Update(bill);
        }
    }

    private async Task AllocateCustomerReceiptAsync(
        int customerId,
        decimal amount,
        int? branchId,
        PaymentMethod receiptMethod,
        string? voucherReference,
        CancellationToken ct)
    {
        var remaining = amount;
        var openStatuses = new[] { SaleStatus.Completed, SaleStatus.PartiallyReturned };
        var q = _uow.Repository<Sale>().Query()
            .Include(s => s.Payments)
            .Where(s => s.CustomerId == customerId
                        && openStatuses.Contains(s.Status));
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        var bills = await q
            .OrderBy(s => s.InvoiceDate)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);

        if (bills.Count == 0) return;

        var saleIds = bills.Select(b => b.Id).ToList();
        var returnedBySale = await _uow.Repository<SaleReturn>().Query().AsNoTracking()
            .Where(r => saleIds.Contains(r.SaleId))
            .GroupBy(r => r.SaleId)
            .Select(g => new { SaleId = g.Key, Returned = g.Sum(x => x.GrandTotal) })
            .ToDictionaryAsync(x => x.SaleId, x => x.Returned, ct);

        foreach (var bill in bills)
        {
            if (remaining <= 0) break;

            returnedBySale.TryGetValue(bill.Id, out var returned);
            var due = bill.GrandTotal - bill.PaidAmount - returned;
            if (due <= 0) continue;

            var applied = Math.Min(remaining, due);
            await ApplyCustomerReceiptToSaleAsync(bill, applied, returned, receiptMethod, voucherReference, ct);
            remaining -= applied;
        }
    }

    private async Task AllocateCustomerReceiptToBillsAsync(
        int customerId,
        decimal amount,
        IReadOnlyList<BillReceiptAllocationDto>? allocations,
        int? branchId,
        PaymentMethod receiptMethod,
        string? voucherReference,
        CancellationToken ct)
    {
        if (allocations is null || allocations.Count == 0)
            throw new AccountingException("Select at least one sale bill for bill-wise receipt.");

        var positive = allocations.Where(a => a.Amount > 0).ToList();
        if (positive.Count == 0)
            throw new AccountingException("Enter amounts to apply against the selected bills.");

        var sum = positive.Sum(a => a.Amount);
        if (Math.Abs(sum - amount) > 0.01m)
            throw new AccountingException(
                $"Bill allocations (₹{sum:N2}) must equal the receipt amount (₹{amount:N2}).");

        var ids = positive.Select(a => a.SaleId).Distinct().ToList();
        if (ids.Count != positive.Count)
            throw new AccountingException("Duplicate sale bills in the allocation list.");

        var openStatuses = new[] { SaleStatus.Completed, SaleStatus.PartiallyReturned };
        var q = _uow.Repository<Sale>().Query()
            .Include(s => s.Payments)
            .Where(s => ids.Contains(s.Id) && openStatuses.Contains(s.Status)
                        && (s.CustomerId == null || s.CustomerId == customerId));
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        var bills = await q.ToListAsync(ct);
        if (bills.Count != ids.Count)
            throw new AccountingException("One or more selected sale bills are invalid or closed.");

        var returnedBySale = await _uow.Repository<SaleReturn>().Query().AsNoTracking()
            .Where(r => ids.Contains(r.SaleId))
            .GroupBy(r => r.SaleId)
            .Select(g => new { SaleId = g.Key, Returned = g.Sum(x => x.GrandTotal) })
            .ToDictionaryAsync(x => x.SaleId, x => x.Returned, ct);

        var byId = bills.ToDictionary(b => b.Id);
        foreach (var alloc in positive)
        {
            var bill = byId[alloc.SaleId];
            returnedBySale.TryGetValue(bill.Id, out var returned);
            var due = bill.GrandTotal - bill.PaidAmount - returned;
            if (alloc.Amount > due + 0.01m)
                throw new AccountingException(
                    $"Amount for {bill.InvoiceNumber} exceeds balance due (₹{due:N2}).");

            if (bill.CustomerId is null)
                bill.CustomerId = customerId;

            await ApplyCustomerReceiptToSaleAsync(bill, alloc.Amount, returned, receiptMethod, voucherReference, ct);
        }
    }

    /// <summary>
    /// Updates <see cref="Sale.PaidAmount"/> and keeps <see cref="SalePayment"/> tenders aligned
    /// so Sales UI Balance Due matches Customer Dues after collection.
    /// </summary>
    private async Task ApplyCustomerReceiptToSaleAsync(
        Sale bill,
        decimal applied,
        decimal returned,
        PaymentMethod receiptMethod,
        string? voucherReference,
        CancellationToken ct)
    {
        if (applied <= 0) return;

        bill.PaidAmount = Math.Round(bill.PaidAmount + applied, 2);
        bill.PaymentStatus = bill.PaidAmount + returned >= bill.GrandTotal - 0.009m
            ? PaymentStatus.Paid
            : PaymentStatus.PartiallyPaid;

        var method = receiptMethod == PaymentMethod.Credit ? PaymentMethod.Cash : receiptMethod;
        var payment = new SalePayment
        {
            SaleId = bill.Id,
            Method = method,
            Amount = Math.Round(applied, 2),
            ReferenceNumber = voucherReference,
            PaymentDateUtc = _clock.UtcNow
        };
        bill.Payments.Add(payment);
        await _uow.Repository<SalePayment>().AddAsync(payment, ct);

        // Shrink original Credit tender so non-credit + credit still ≈ grand total.
        var creditLeft = Math.Round(applied, 2);
        foreach (var credit in bill.Payments
                     .Where(p => p.Method == PaymentMethod.Credit && p.Amount > 0 && !ReferenceEquals(p, payment))
                     .OrderByDescending(p => p.Id)
                     .ToList())
        {
            if (creditLeft <= 0) break;
            var reduce = Math.Min(credit.Amount, creditLeft);
            credit.Amount = Math.Round(credit.Amount - reduce, 2);
            creditLeft = Math.Round(creditLeft - reduce, 2);
            if (credit.Amount <= 0.009m)
            {
                bill.Payments.Remove(credit);
                _uow.Repository<SalePayment>().Remove(credit);
            }
        }

        _uow.Repository<Sale>().Update(bill);
    }

    private static PaymentMethod ResolveReceiptPaymentMethod(Account cashOrBank)
    {
        if (string.Equals(cashOrBank.Code, BankAccountCode, StringComparison.OrdinalIgnoreCase))
            return PaymentMethod.BankTransfer;

        if (!string.IsNullOrWhiteSpace(cashOrBank.Name)
            && cashOrBank.Name.Contains("bank", StringComparison.OrdinalIgnoreCase))
            return PaymentMethod.BankTransfer;

        return PaymentMethod.Cash;
    }

    private async Task AddLineAsync(
        int journalEntryId,
        Account account,
        LedgerEntryType entryType,
        decimal amount,
        string? remarks,
        CancellationToken ct)
    {
        await _uow.Repository<JournalLine>().AddAsync(new JournalLine
        {
            JournalEntryId = journalEntryId,
            AccountId = account.Id,
            EntryType = entryType,
            Amount = amount,
            Remarks = remarks
        }, ct);

        ApplyBalanceChange(account, entryType, amount);
        _uow.Repository<Account>().Update(account);
    }

    private static void ApplyBalanceChange(Account account, LedgerEntryType entryType, decimal amount)
    {
        var isDebit = entryType == LedgerEntryType.Debit;
        var naturalDebit = account.Type is AccountType.Asset or AccountType.Expense;

        account.CurrentBalance += naturalDebit
            ? (isDebit ? amount : -amount)
            : (isDebit ? -amount : amount);
    }

    private static (decimal Debit, decimal Credit) ToTrialBalance(decimal balance, AccountType type)
    {
        var naturalDebit = type is AccountType.Asset or AccountType.Expense;
        if (balance == 0) return (0m, 0m);

        if (naturalDebit)
            return balance >= 0 ? (balance, 0m) : (0m, -balance);

        return balance >= 0 ? (0m, balance) : (-balance, 0m);
    }

    private async Task<Account?> GetAccountByCodeAsync(string code, CancellationToken ct)
        => await _uow.Repository<Account>().Query()
            .FirstOrDefaultAsync(a => a.Code == code, ct);

    private async Task<Account> RequireAccountByCodeAsync(string code, CancellationToken ct)
    {
        var account = await GetAccountByCodeAsync(code, ct);
        if (account is null)
            throw new AccountingException($"System account {code} is not configured.");
        return account;
    }

    private async Task<string> GenerateVoucherNumberAsync(VoucherKind kind, int? branchId, CancellationToken ct)
    {
        var prefix = kind switch
        {
            VoucherKind.Payment => "PAY",
            VoucherKind.Receipt => "RCT",
            VoucherKind.Expense => "EXP",
            _ => "JV"
        };

        var today = _clock.Today;
        var tomorrow = today.AddDays(1);
        var q = _uow.Repository<JournalEntry>().Query()
            .Where(e => e.EntryDate >= today && e.EntryDate < tomorrow && e.VoucherNumber.StartsWith(prefix));
        if (branchId.HasValue) q = q.Where(e => e.BranchId == branchId);

        var count = await q.CountAsync(ct);
        return $"{prefix}-{today:yyyyMMdd}-{count + 1:D4}";
    }

    private sealed class AccountingException : Exception
    {
        public AccountingException(string message) : base(message) { }
    }
}
