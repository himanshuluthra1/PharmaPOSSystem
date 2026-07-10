using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Dashboard;

/// <summary>
/// Computes dashboard KPIs directly against the database using set-based queries
/// so it stays fast even with large transaction volumes.
/// </summary>
public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _uow;
    private readonly IDateTimeProvider _clock;
    private readonly ISettingsService _settings;

    public DashboardService(IUnitOfWork uow, IDateTimeProvider clock, ISettingsService settings)
    {
        _uow = uow;
        _clock = clock;
        _settings = settings;
    }

    public async Task<DashboardDto> GetDashboardAsync(int? branchId = null, CancellationToken ct = default)
    {
        var today = _clock.Today;
        var tomorrow = today.AddDays(1);
        var prefs = await _settings.GetPreferencesAsync(ct);
        var nearExpiryDate = today.AddDays(prefs.NearExpiryDays);

        var sales = _uow.Repository<Sale>().Query()
            .Where(s => s.Status == SaleStatus.Completed);
        if (branchId.HasValue) sales = sales.Where(s => s.BranchId == branchId);

        var purchases = _uow.Repository<Purchase>().Query()
            .Where(p => p.Status == PurchaseStatus.Received);
        if (branchId.HasValue) purchases = purchases.Where(p => p.BranchId == branchId);

        var todaySales = sales.Where(s => s.InvoiceDate >= today && s.InvoiceDate < tomorrow);
        var todayPurchases = purchases.Where(p => p.InvoiceDate >= today && p.InvoiceDate < tomorrow);

        var dto = new DashboardDto
        {
            TodaySales = await todaySales.SumAsync(s => (decimal?)s.GrandTotal, ct) ?? 0m,
            TodayPurchase = await todayPurchases.SumAsync(p => (decimal?)p.GrandTotal, ct) ?? 0m,
            TodayInvoices = await todaySales.CountAsync(ct),
            TodayCustomers = await todaySales.Where(s => s.CustomerId != null)
                .Select(s => s.CustomerId).Distinct().CountAsync(ct),
            PendingReceivables = await _uow.Repository<Customer>().Query()
                .SumAsync(c => (decimal?)c.OutstandingBalance, ct) ?? 0m,
            PendingPayables = await _uow.Repository<Supplier>().Query()
                .SumAsync(s => (decimal?)s.OutstandingBalance, ct) ?? 0m
        };

        var batches = _uow.Repository<MedicineBatch>().Query();
        if (branchId.HasValue) batches = batches.Where(b => b.BranchId == branchId);

        dto.ExpiredCount = await batches.CountAsync(
            b => b.QuantityAvailable > 0 && b.ExpiryDate != null && b.ExpiryDate < today, ct);
        dto.NearExpiryCount = await batches.CountAsync(
            b => b.QuantityAvailable > 0 && b.ExpiryDate != null && b.ExpiryDate >= today && b.ExpiryDate <= nearExpiryDate, ct);

        // Low stock: only medicines with a reorder level configured whose total
        // on-hand quantity is at or below it. The reorder-level filter keeps the
        // candidate set tiny, so we resolve stock with two simple translatable
        // queries and compare in memory.
        var reorderMeds = await _uow.Repository<Medicine>().Query()
            .Where(m => m.Status == EntityStatus.Active && m.ReorderLevel > 0)
            .Select(m => new { m.Id, m.ReorderLevel })
            .ToListAsync(ct);

        if (reorderMeds.Count > 0)
        {
            var reorderIds = reorderMeds.Select(m => m.Id).ToList();
            var stockByMedicine = await batches
                .Where(b => reorderIds.Contains(b.MedicineId))
                .GroupBy(b => b.MedicineId)
                .Select(g => new { MedicineId = g.Key, Qty = g.Sum(x => x.QuantityAvailable) })
                .ToListAsync(ct);
            var stockLookup = stockByMedicine.ToDictionary(x => x.MedicineId, x => x.Qty);
            dto.LowStockCount = reorderMeds.Count(m =>
                (stockLookup.TryGetValue(m.Id, out var q) ? q : 0m) <= m.ReorderLevel);
        }

        var thirtyDaysAgo = today.AddDays(-30);
        var completedSales = _uow.Repository<Sale>().Query()
            .Where(s => s.Status == SaleStatus.Completed && s.InvoiceDate >= thirtyDaysAgo);
        if (branchId.HasValue) completedSales = completedSales.Where(s => s.BranchId == branchId);

        var topRows = await (
            from item in _uow.Repository<SaleItem>().Query()
            join sale in completedSales on item.SaleId equals sale.Id
            join medicine in _uow.Repository<Medicine>().Query() on item.MedicineId equals medicine.Id
            group item by medicine.Name into g
            orderby g.Sum(x => x.LineTotal) descending
            select new
            {
                Name = g.Key,
                QuantitySold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal)
            })
            .Take(5)
            .ToListAsync(ct);

        dto.TopSellingMedicines = topRows
            .Select(r => new TopMedicineDto(r.Name, r.QuantitySold, r.Revenue))
            .ToList();

        var windowStart = FirstDayOfMonth(_clock.Today.AddMonths(-5));

        var saleRows = await sales
            .Where(s => s.InvoiceDate >= windowStart)
            .Select(s => new MonthlyRaw(s.InvoiceDate, s.GrandTotal))
            .ToListAsync(ct);
        var purchaseRows = await purchases
            .Where(p => p.InvoiceDate >= windowStart)
            .Select(p => new MonthlyRaw(p.InvoiceDate, p.GrandTotal))
            .ToListAsync(ct);

        dto.MonthlySales = BuildMonthly(saleRows, windowStart);
        dto.MonthlyPurchases = BuildMonthly(purchaseRows, windowStart);

        return dto;
    }

    private static DateTime FirstDayOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    private static List<MonthlySalesDto> BuildMonthly(IReadOnlyList<MonthlyRaw> rows, DateTime windowStart)
    {
        var result = new List<MonthlySalesDto>(6);
        for (int i = 0; i < 6; i++)
        {
            var month = windowStart.AddMonths(i);
            var total = rows
                .Where(r => r.Date.Year == month.Year && r.Date.Month == month.Month)
                .Sum(r => r.Amount);
            result.Add(new MonthlySalesDto(month.ToString("MMM yyyy"), total));
        }
        return result;
    }

    private readonly record struct MonthlyRaw(DateTime Date, decimal Amount);
}
