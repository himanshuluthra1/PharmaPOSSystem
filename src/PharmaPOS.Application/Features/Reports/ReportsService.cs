using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared.Constants;

namespace PharmaPOS.Application.Features.Reports;

public class ReportsService : IReportsService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ISettingsService _settings;

    public ReportsService(IUnitOfWork uow, IDateTimeProvider clock, ISettingsService settings)
    {
        _uow = uow;
        _clock = clock;
        _settings = settings;
    }

    public async Task<(ReportSummaryDto Summary, List<SalesReportRowDto> Rows)> GetSalesReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default)
    {
        var (start, end) = NormalizeRange(from, to);
        var q = SalesQuery(branchId)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end);

        var rows = await q
            .OrderByDescending(s => s.InvoiceDate)
            .Select(s => new SalesReportRowDto(
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                s.Customer != null ? s.Customer.Name :
                    s.BillingCustomerName ?? "Walk-in",
                s.Items.Count,
                s.SubTotal,
                s.DiscountAmount,
                s.CgstAmount,
                s.SgstAmount,
                s.IgstAmount,
                s.GrandTotal,
                s.PaidAmount,
                s.GrandTotal > s.PaidAmount ? s.GrandTotal - s.PaidAmount : 0m))
            .ToListAsync(ct);

        return (BuildSummary(rows.Count, rows.Sum(r => r.GrandTotal),
            rows.Sum(r => r.TaxAmount), rows.Sum(r => r.DiscountAmount)), rows);
    }

    public async Task<(ReportSummaryDto Summary, List<PurchaseReportRowDto> Rows)> GetPurchaseReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default)
    {
        var (start, end) = NormalizeRange(from, to);
        var q = PurchasesQuery(branchId)
            .Where(p => p.InvoiceDate >= start && p.InvoiceDate < end);

        var rows = await q
            .OrderByDescending(p => p.InvoiceDate)
            .Select(p => new PurchaseReportRowDto(
                p.Id,
                p.InvoiceNumber,
                p.InvoiceDate,
                p.Supplier != null ? p.Supplier.Name : $"Supplier #{p.SupplierId}",
                p.Items.Count,
                p.SubTotal,
                p.DiscountAmount,
                p.CgstAmount,
                p.SgstAmount,
                p.IgstAmount,
                p.GrandTotal,
                p.PaidAmount,
                p.GrandTotal > p.PaidAmount ? p.GrandTotal - p.PaidAmount : 0m))
            .ToListAsync(ct);

        return (BuildSummary(rows.Count, rows.Sum(r => r.GrandTotal),
            rows.Sum(r => r.TaxAmount), rows.Sum(r => r.DiscountAmount)), rows);
    }

    public async Task<(GstSummaryDto Summary, List<GstDetailRowDto> Rows)> GetGstReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default)
    {
        var (start, end) = NormalizeRange(from, to);

        var sales = await SalesQuery(branchId)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end)
            .Select(s => new GstDetailRowDto(
                "Sale",
                s.InvoiceNumber,
                s.InvoiceDate,
                s.Customer != null ? s.Customer.Name : s.BillingCustomerName ?? "Walk-in",
                s.TaxableAmount,
                s.CgstAmount,
                s.SgstAmount,
                s.IgstAmount,
                s.GrandTotal))
            .ToListAsync(ct);

        var purchases = await PurchasesQuery(branchId)
            .Where(p => p.InvoiceDate >= start && p.InvoiceDate < end)
            .Select(p => new GstDetailRowDto(
                "Purchase",
                p.InvoiceNumber,
                p.InvoiceDate,
                p.Supplier != null ? p.Supplier.Name : $"Supplier #{p.SupplierId}",
                p.TaxableAmount,
                p.CgstAmount,
                p.SgstAmount,
                p.IgstAmount,
                p.GrandTotal))
            .ToListAsync(ct);

        var summary = new GstSummaryDto
        {
            SalesTaxable = sales.Sum(s => s.TaxableAmount),
            SalesCgst = sales.Sum(s => s.CgstAmount),
            SalesSgst = sales.Sum(s => s.SgstAmount),
            SalesIgst = sales.Sum(s => s.IgstAmount),
            SalesTotalTax = sales.Sum(s => s.TotalTax),
            SalesGrandTotal = sales.Sum(s => s.GrandTotal),
            PurchaseTaxable = purchases.Sum(p => p.TaxableAmount),
            PurchaseCgst = purchases.Sum(p => p.CgstAmount),
            PurchaseSgst = purchases.Sum(p => p.SgstAmount),
            PurchaseIgst = purchases.Sum(p => p.IgstAmount),
            PurchaseTotalTax = purchases.Sum(p => p.TotalTax),
            PurchaseGrandTotal = purchases.Sum(p => p.GrandTotal)
        };

        var rows = sales.Concat(purchases)
            .OrderByDescending(r => r.InvoiceDate)
            .ToList();

        return (summary, rows);
    }

    public async Task<(ReportSummaryDto Summary, List<ProfitReportRowDto> Rows)> GetProfitReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default)
    {
        var (start, end) = NormalizeRange(from, to);
        var sales = await SalesQuery(branchId)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end)
            .Include(s => s.Items)
            .ThenInclude(i => i.MedicineBatch)
            .OrderByDescending(s => s.InvoiceDate)
            .ToListAsync(ct);

        var rows = sales.Select(s =>
        {
            var revenue = s.GrandTotal;
            var cost = s.Items.Sum(i =>
                i.Quantity * (i.MedicineBatch?.PurchasePrice ?? 0m));
            return new ProfitReportRowDto(
                s.InvoiceNumber,
                s.InvoiceDate,
                s.Customer?.Name ?? s.BillingCustomerName ?? "Walk-in",
                revenue,
                cost,
                revenue - cost);
        }).ToList();

        var totalProfit = rows.Sum(r => r.GrossProfit);
        return (new ReportSummaryDto
        {
            RecordCount = rows.Count,
            TotalAmount = rows.Sum(r => r.Revenue),
            TotalTax = rows.Sum(r => r.Cost),
            TotalDiscount = totalProfit,
            FooterNote = $"Gross profit: {totalProfit:N2}"
        }, rows);
    }

    public async Task<(ReportSummaryDto Summary, List<MedicineSalesRowDto> Rows)> GetSalesByMedicineReportAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct = default)
    {
        var (start, end) = NormalizeRange(from, to);
        var completedSales = SalesQuery(branchId)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end);

        // Keep SQL simple: EF cannot reliably translate left-join + null ternary inside Sum.
        var lines = await (
            from item in _uow.Repository<SaleItem>().Query().AsNoTracking()
            join sale in completedSales on item.SaleId equals sale.Id
            select new
            {
                item.MedicineId,
                item.MedicineBatchId,
                item.Quantity,
                item.LineTotal
            }).ToListAsync(ct);

        if (lines.Count == 0)
        {
            return (new ReportSummaryDto
            {
                RecordCount = 0,
                FooterNote = "Top seller: —"
            }, []);
        }

        var medIds = lines.Select(l => l.MedicineId).Distinct().ToList();
        var medicines = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.GenericName })
            .ToDictionaryAsync(m => m.Id, ct);

        var batchIds = lines
            .Where(l => l.MedicineBatchId.HasValue)
            .Select(l => l.MedicineBatchId!.Value)
            .Distinct()
            .ToList();
        var batchCosts = batchIds.Count == 0
            ? new Dictionary<int, decimal>()
            : await _uow.Repository<MedicineBatch>().Query().AsNoTracking()
                .Where(b => batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.PurchasePrice, ct);

        var rows = lines
            .GroupBy(l => l.MedicineId)
            .Select(g =>
            {
                medicines.TryGetValue(g.Key, out var med);
                var name = med?.Name ?? $"Medicine #{g.Key}";
                var generic = med?.GenericName;
                var qty = g.Sum(x => x.Quantity);
                var revenue = g.Sum(x => x.LineTotal);
                var cost = g.Sum(x =>
                {
                    if (x.MedicineBatchId is int bid && batchCosts.TryGetValue(bid, out var price))
                        return x.Quantity * price;
                    return 0m;
                });
                return new MedicineSalesRowDto(name, generic, qty, revenue, cost, revenue - cost);
            })
            .OrderByDescending(r => r.Revenue)
            .ToList();

        return (new ReportSummaryDto
        {
            RecordCount = rows.Count,
            TotalAmount = rows.Sum(r => r.Revenue),
            TotalDiscount = rows.Sum(r => r.GrossProfit),
            FooterNote = $"Top seller: {rows.FirstOrDefault()?.MedicineName ?? "—"}"
        }, rows);
    }

    public async Task<(ReportSummaryDto Summary, List<StockValuationReportRowDto> Rows)> GetStockValuationReportAsync(
        int? branchId, CancellationToken ct = default)
    {
        var q = BatchQuery(branchId).Where(b => b.QuantityAvailable > 0);

        var rows = await q
            .OrderBy(b => b.Medicine!.Name)
            .ThenBy(b => b.ExpiryDate)
            .Select(b => new StockValuationReportRowDto(
                b.Medicine!.Name,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityAvailable,
                b.PurchasePrice,
                b.Mrp,
                b.PurchasePrice * b.QuantityAvailable,
                b.Mrp * b.QuantityAvailable))
            .ToListAsync(ct);

        return (new ReportSummaryDto
        {
            RecordCount = rows.Count,
            TotalAmount = rows.Sum(r => r.StockAmount),
            TotalTax = rows.Sum(r => r.StockValue),
            FooterNote = $"Stock at MRP: {rows.Sum(r => r.StockAmount):N2} · Cost: {rows.Sum(r => r.StockValue):N2}"
        }, rows);
    }

    public async Task<(ReportSummaryDto Summary, List<ExpiryReportRowDto> Rows)> GetExpiryReportAsync(
        int? branchId, CancellationToken ct = default)
    {
        var today = _clock.Today;
        var prefs = await _settings.GetPreferencesAsync(ct);
        var nearExpiry = today.AddDays(prefs.NearExpiryDays);

        var batches = await BatchQuery(branchId)
            .Where(b => b.QuantityAvailable > 0 && b.ExpiryDate != null)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(ct);

        var rows = batches.Select(b =>
        {
            var expiry = b.ExpiryDate!.Value.Date;
            var status = expiry < today ? "Expired"
                : expiry <= nearExpiry ? "Near expiry"
                : "OK";
            return new ExpiryReportRowDto(
                b.Medicine!.Name,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityAvailable,
                b.PurchasePrice * b.QuantityAvailable,
                status);
        })
        .Where(r => r.ExpiryStatus != "OK")
        .ToList();

        return (new ReportSummaryDto
        {
            RecordCount = rows.Count,
            TotalAmount = rows.Sum(r => r.StockValue),
            FooterNote = $"{rows.Count(r => r.ExpiryStatus == "Expired")} expired, " +
                         $"{rows.Count(r => r.ExpiryStatus == "Near expiry")} near expiry"
        }, rows);
    }

    public async Task<(ReportSummaryDto Summary, List<LowStockReportRowDto> Rows)> GetLowStockReportAsync(
        int? branchId, CancellationToken ct = default)
    {
        var reorderMeds = await _uow.Repository<Medicine>().Query()
            .Where(m => m.Status == EntityStatus.Active && m.ReorderLevel > 0)
            .Select(m => new { m.Id, m.Name, m.GenericName, m.ReorderLevel, m.ReorderQuantity })
            .ToListAsync(ct);

        if (reorderMeds.Count == 0)
            return (new ReportSummaryDto { FooterNote = "No reorder levels configured on medicines." }, []);

        var ids = reorderMeds.Select(m => m.Id).ToList();
        var stock = await BatchQuery(branchId)
            .Where(b => ids.Contains(b.MedicineId))
            .GroupBy(b => b.MedicineId)
            .Select(g => new { MedicineId = g.Key, Qty = g.Sum(x => x.QuantityAvailable) })
            .ToDictionaryAsync(x => x.MedicineId, x => x.Qty, ct);

        var rows = reorderMeds
            .Select(m =>
            {
                var qty = stock.TryGetValue(m.Id, out var q) ? q : 0m;
                return new { m, qty };
            })
            .Where(x => x.qty <= x.m.ReorderLevel)
            .OrderBy(x => x.qty)
            .Select(x => new LowStockReportRowDto(
                x.m.Name,
                x.m.GenericName,
                x.qty,
                x.m.ReorderLevel,
                x.m.ReorderQuantity,
                Math.Max(0, x.m.ReorderLevel - x.qty)))
            .ToList();

        return (new ReportSummaryDto
        {
            RecordCount = rows.Count,
            FooterNote = $"{rows.Count(r => r.IsCritical)} out of stock"
        }, rows);
    }

    private IQueryable<Sale> SalesQuery(int? branchId)
    {
        var q = _uow.Repository<Sale>().Query()
            .Where(s => s.Status == SaleStatus.Completed);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);
        return q;
    }

    private IQueryable<Purchase> PurchasesQuery(int? branchId)
    {
        var q = _uow.Repository<Purchase>().Query()
            .Where(p => p.Status == PurchaseStatus.Received);
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);
        return q;
    }

    private IQueryable<MedicineBatch> BatchQuery(int? branchId)
    {
        var q = _uow.Repository<MedicineBatch>().Query()
            .Include(b => b.Medicine)
            .Where(b => b.Medicine != null && b.Medicine.Status == EntityStatus.Active);
        if (branchId.HasValue) q = q.Where(b => b.BranchId == branchId);
        return q;
    }

    private static (DateTime Start, DateTime EndExclusive) NormalizeRange(DateTime from, DateTime to)
    {
        var start = from.Date;
        var end = to.Date.AddDays(1);
        if (end < start) end = start.AddDays(1);
        return (start, end);
    }

    private static ReportSummaryDto BuildSummary(int count, decimal total, decimal tax, decimal discount)
        => new()
        {
            RecordCount = count,
            TotalAmount = total,
            TotalTax = tax,
            TotalDiscount = discount
        };
}
