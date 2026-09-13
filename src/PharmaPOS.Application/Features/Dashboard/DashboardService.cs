using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.Settings;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Shared;

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
    private readonly IFinancialYearContext _financialYear;

    public DashboardService(
        IUnitOfWork uow,
        IDateTimeProvider clock,
        ISettingsService settings,
        IFinancialYearContext financialYear)
    {
        _uow = uow;
        _clock = clock;
        _settings = settings;
        _financialYear = financialYear;
    }

    public async Task<DashboardDto> GetDashboardAsync(int? branchId = null, CancellationToken ct = default)
    {
        var today = _clock.Today;
        var tomorrow = today.AddDays(1);
        var prefs = await _settings.GetPreferencesAsync(ct);
        var nearExpiryDate = today.AddDays(prefs.NearExpiryDays);
        var fy = _financialYear.Active;

        var sales = _uow.Repository<Sale>().Query()
            .Where(s => s.Status == SaleStatus.Completed)
            .WhereInFinancialYear(fy, s => s.InvoiceDate);
        if (branchId.HasValue) sales = sales.Where(s => s.BranchId == branchId);

        var purchases = _uow.Repository<Purchase>().Query()
            .Where(p => p.Status == PurchaseStatus.Received)
            .WhereInFinancialYear(fy, p => p.InvoiceDate);
        if (branchId.HasValue) purchases = purchases.Where(p => p.BranchId == branchId);

        var dto = new DashboardDto();

        // Today KPIs are only meaningful in the current FY.
        if (fy.IsCurrent)
        {
            var todaySales = sales.Where(s => s.InvoiceDate >= today && s.InvoiceDate < tomorrow);
            var todayPurchases = purchases.Where(p => p.InvoiceDate >= today && p.InvoiceDate < tomorrow);
            dto.TodaySales = await todaySales.SumAsync(s => (decimal?)s.GrandTotal, ct) ?? 0m;
            dto.TodayPurchase = await todayPurchases.SumAsync(p => (decimal?)p.GrandTotal, ct) ?? 0m;
            dto.TodayInvoices = await todaySales.CountAsync(ct);
            dto.TodayCustomers = await todaySales.Where(s => s.CustomerId != null)
                .Select(s => s.CustomerId).Distinct().CountAsync(ct);
        }

        dto.PendingReceivables = await _uow.Repository<Customer>().Query()
            .SumAsync(c => (decimal?)c.OutstandingBalance, ct) ?? 0m;
        dto.PendingPayables = await _uow.Repository<Supplier>().Query()
            .SumAsync(s => (decimal?)s.OutstandingBalance, ct) ?? 0m;

        var batches = _uow.Repository<MedicineBatch>().Query();
        if (branchId.HasValue) batches = batches.Where(b => b.BranchId == branchId);

        dto.ExpiredCount = await batches.CountAsync(
            b => b.QuantityAvailable > 0 && b.ExpiryDate != null && b.ExpiryDate < today, ct);
        dto.NearExpiryCount = await batches.CountAsync(
            b => b.QuantityAvailable > 0 && b.ExpiryDate != null && b.ExpiryDate >= today && b.ExpiryDate <= nearExpiryDate, ct);

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

        var topWindowStart = fy.IsCurrent
            ? (today.AddDays(-30) < fy.Start ? fy.Start : today.AddDays(-30))
            : fy.Start;
        var completedSales = _uow.Repository<Sale>().Query()
            .Where(s => s.Status == SaleStatus.Completed && s.InvoiceDate >= topWindowStart)
            .WhereInFinancialYear(fy, s => s.InvoiceDate);
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

        var windowStart = fy.IsCurrent
            ? FirstDayOfMonth(_clock.Today.AddMonths(-5))
            : FirstDayOfMonth(fy.Start);
        if (windowStart < fy.Start) windowStart = fy.Start;

        var saleRows = await sales
            .Where(s => s.InvoiceDate >= windowStart)
            .Select(s => new MonthlyRaw(s.InvoiceDate, s.GrandTotal))
            .ToListAsync(ct);
        var purchaseRows = await purchases
            .Where(p => p.InvoiceDate >= windowStart)
            .Select(p => new MonthlyRaw(p.InvoiceDate, p.GrandTotal))
            .ToListAsync(ct);

        dto.MonthlySales = BuildMonthly(saleRows, windowStart, fy);
        dto.MonthlyPurchases = BuildMonthly(purchaseRows, windowStart, fy);

        return dto;
    }

    private static DateTime FirstDayOfMonth(DateTime date) => new(date.Year, date.Month, 1);

    private static List<MonthlySalesDto> BuildMonthly(
        IReadOnlyList<MonthlyRaw> rows,
        DateTime windowStart,
        FinancialYearInfo fy)
    {
        var result = new List<MonthlySalesDto>(6);
        for (int i = 0; i < 6; i++)
        {
            var month = windowStart.AddMonths(i);
            if (month >= fy.EndExclusive) break;
            var total = rows
                .Where(r => r.Date.Year == month.Year && r.Date.Month == month.Month)
                .Sum(r => r.Amount);
            result.Add(new MonthlySalesDto(month.ToString("MMM yyyy"), total));
        }
        return result;
    }

    private readonly record struct MonthlyRaw(DateTime Date, decimal Amount);
}
