using Microsoft.EntityFrameworkCore;
using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.Domain.Entities.Accounting;
using PharmaPOS.Domain.Entities.Inventory;
using PharmaPOS.Domain.Entities.Masters;
using PharmaPOS.Domain.Entities.Purchases;
using PharmaPOS.Domain.Entities.Sales;
using PharmaPOS.Domain.Enums;

namespace PharmaPOS.Application.Features.Reports;

public partial class ReportsService
{
    public async Task<ReportTableDto> GetReportTableAsync(
        ReportKind kind,
        DateTime from,
        DateTime to,
        int? branchId,
        ScheduleRegisterFilter scheduleFilter = ScheduleRegisterFilter.HAndH1,
        CancellationToken ct = default)
    {
        switch (kind)
        {
            case ReportKind.Sales:
            {
                var (s, rows) = await GetSalesReportAsync(from, to, branchId, ct);
                return ReportTableMapper.FromSales(s, rows);
            }
            case ReportKind.Purchases:
            {
                var (s, rows) = await GetPurchaseReportAsync(from, to, branchId, ct);
                return ReportTableMapper.FromPurchases(s, rows);
            }
            case ReportKind.GstSummary:
            {
                var (gst, rows) = await GetGstReportAsync(from, to, branchId, ct);
                return ReportTableMapper.FromGst(gst, rows);
            }
            case ReportKind.Gstr1:
                return ReportTableMapper.FromGstReturn(await GetGstr1ExportAsync(from, to, branchId, ct));
            case ReportKind.Gstr2B:
                return ReportTableMapper.FromGstReturn(await GetGstr2BExportAsync(from, to, branchId, ct));
            case ReportKind.Profit:
            {
                var (s, rows) = await GetProfitReportAsync(from, to, branchId, ct);
                return ReportTableMapper.FromProfit(s, rows);
            }
            case ReportKind.SalesByMedicine:
            {
                var (s, rows) = await GetSalesByMedicineReportAsync(from, to, branchId, ct);
                return ReportTableMapper.FromMedicineSales(s, rows);
            }
            case ReportKind.MedicinesSoldByDate:
                return await BuildMedicinesSoldByDateAsync(from, to, branchId, ct);
            case ReportKind.StockValuation:
            {
                var (s, rows) = await GetStockValuationReportAsync(branchId, ct);
                return ReportTableMapper.FromStock(s, rows);
            }
            case ReportKind.Expiry:
            {
                var (s, rows) = await GetExpiryReportAsync(branchId, ct);
                return ReportTableMapper.FromExpiry(s, rows);
            }
            case ReportKind.LowStock:
            {
                var (s, rows) = await GetLowStockReportAsync(branchId, ct);
                return ReportTableMapper.FromLowStock(s, rows);
            }
            case ReportKind.ScheduleRegister:
            {
                var (s, report) = await GetScheduleRegisterAsync(from, to, branchId, scheduleFilter, ct);
                return ReportTableMapper.FromSchedule(s, report);
            }
            case ReportKind.SaleReturns:
            {
                var rows = await _saleReturns.ListReturnsAsync(from, to, branchId, ct);
                var summary = BuildSummary(rows.Count, rows.Sum(r => r.RefundAmount), 0, 0);
                return ReportTableMapper.FromSaleReturns(summary, rows);
            }
            case ReportKind.MedicineReturns:
            {
                var rows = await _saleReturns.GetMedicineReturnReportAsync(from, to, branchId, ct);
                var summary = BuildSummary(rows.Count, rows.Sum(r => r.RefundAmount), 0, 0);
                return ReportTableMapper.FromMedicineReturns(summary, rows);
            }
            case ReportKind.SalesByCustomer:
                return await BuildSalesByCustomerAsync(from, to, branchId, ct);
            case ReportKind.SalesByPaymentMode:
                return await BuildSalesByPaymentModeAsync(from, to, branchId, ct);
            case ReportKind.SalesDayWise:
                return await BuildSalesDayWiseAsync(from, to, branchId, ct);
            case ReportKind.SalesCreditDue:
                return await BuildSalesCreditDueAsync(from, to, branchId, ct);
            case ReportKind.PurchasesBySupplier:
                return await BuildPurchasesBySupplierAsync(from, to, branchId, ct);
            case ReportKind.SupplierOutstanding:
                return await BuildSupplierOutstandingAsync(branchId, ct);
            case ReportKind.SupplierPayments:
                return await BuildPurchasePaymentsAsync(from, to, branchId, ct);
            case ReportKind.PaymentVouchers:
                return await BuildVoucherRegisterAsync("Payment", from, to, branchId, ct);
            case ReportKind.PurchaseReturns:
                return await BuildPurchaseReturnsAsync(from, to, branchId, ct);
            case ReportKind.ExpiryToCompanyClaims:
                return await BuildExpiryToCompanyClaimsAsync(from, to, branchId, ct);
            case ReportKind.CustomerOutstanding:
                return await BuildCustomerOutstandingAsync(branchId, ct);
            case ReportKind.CustomerReceipts:
            case ReportKind.ReceiptVouchers:
                return await BuildVoucherRegisterAsync("Receipt", from, to, branchId, ct);
            case ReportKind.ExpenseRegister:
                return await BuildExpenseRegisterAsync(from, to, branchId, ct);
            case ReportKind.ExpenseByAccount:
                return await BuildExpenseByAccountAsync(from, to, branchId, ct);
            case ReportKind.CashBookSummary:
                return await BuildCashBookSummaryAsync(from, to, branchId, ct);
            case ReportKind.BatchStock:
                return await BuildBatchStockAsync(branchId, ct);
            case ReportKind.SlowMovingStock:
                return await BuildSlowMovingStockAsync(branchId, ct);
            case ReportKind.StockAdjustments:
                return await BuildStockAdjustmentsAsync(from, to, branchId, ct);
            default:
                return Empty($"Report '{kind}' is not implemented.");
        }
    }

    private static ReportTableDto Empty(string note) => new()
    {
        Summary = new ReportSummaryDto { FooterNote = note }
    };

    private async Task<ReportTableDto> BuildSalesByCustomerAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var rows = await SalesQuery(branchId)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end)
            .GroupBy(s => s.Customer != null ? s.Customer.Name : (s.BillingCustomerName ?? "Walk-in"))
            .Select(g => new
            {
                Customer = g.Key,
                Bills = g.Count(),
                Total = g.Sum(x => x.GrandTotal),
                Paid = g.Sum(x => x.PaidAmount),
                Due = g.Sum(x => x.GrandTotal > x.PaidAmount ? x.GrandTotal - x.PaidAmount : 0m)
            })
            .OrderByDescending(x => x.Total)
            .ToListAsync(ct);

        return ReportTableMapper.Table(
            BuildSummary(rows.Count, rows.Sum(r => r.Total), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("Customer", "Customer"),
                ReportTableMapper.C("Bills", "Bills"),
                ReportTableMapper.C("Total", "Total", "N2"),
                ReportTableMapper.C("Paid", "Paid", "N2"),
                ReportTableMapper.C("Due", "Due", "N2")),
            rows.Select(r => ReportTableMapper.Dict(
                ("Customer", (object?)r.Customer),
                ("Bills", r.Bills),
                ("Total", r.Total),
                ("Paid", r.Paid),
                ("Due", r.Due))));
    }

    public async Task<IReadOnlyList<CustomerSaleBillRowDto>> ListCustomerSalesAsync(
        DateTime from, DateTime to, string customerKey, int? branchId, CancellationToken ct = default)
    {
        var bills = await ListUnderlyingBillsAsync(new ReportBillDrillDownQuery
        {
            Kind = ReportKind.SalesByCustomer,
            From = from,
            To = to,
            BranchId = branchId,
            CustomerKey = customerKey,
            Title = customerKey
        }, ct);

        return bills
            .Where(b => b.DocumentKind == ReportDocumentKind.Sale)
            .Select(b => new CustomerSaleBillRowDto(
                b.DocumentId,
                b.InvoiceNumber,
                b.InvoiceDate,
                b.GrandTotal,
                b.PaidAmount,
                b.BalanceDue,
                b.Status))
            .ToList();
    }

    public async Task<IReadOnlyList<ReportBillListRowDto>> ListUnderlyingBillsAsync(
        ReportBillDrillDownQuery query, CancellationToken ct = default)
    {
        return query.Kind switch
        {
            ReportKind.SalesByCustomer => await ListSalesByCustomerKeyAsync(query, ct),
            ReportKind.SalesDayWise => await ListSalesByDayAsync(query, ct),
            ReportKind.SalesByPaymentMode => await ListSalesByPaymentMethodAsync(query, ct),
            ReportKind.SalesByMedicine => await ListSalesByMedicineAsync(query, ct),
            ReportKind.MedicineReturns => await ListSalesByMedicineReturnAsync(query, ct),
            ReportKind.PurchasesBySupplier => await ListPurchasesBySupplierAsync(query, openOnly: false, ct),
            ReportKind.SupplierOutstanding => await ListPurchasesBySupplierAsync(query, openOnly: true, ct),
            ReportKind.CustomerOutstanding => await ListOpenSalesByCustomerIdAsync(query, ct),
            _ => []
        };
    }

    private async Task<IReadOnlyList<ReportBillListRowDto>> ListSalesByCustomerKeyAsync(
        ReportBillDrillDownQuery query, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(query.From, query.To);
        var key = string.IsNullOrWhiteSpace(query.CustomerKey) ? "Walk-in" : query.CustomerKey.Trim();

        var rows = await SalesQuery(query.BranchId)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end)
            .Where(s => (s.Customer != null ? s.Customer.Name : (s.BillingCustomerName ?? "Walk-in")) == key)
            .OrderByDescending(s => s.InvoiceDate)
            .Select(s => new
            {
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                Party = s.Customer != null ? s.Customer.Name : (s.BillingCustomerName ?? "Walk-in"),
                s.GrandTotal,
                s.PaidAmount,
                s.Status
            })
            .ToListAsync(ct);

        return rows.Select(s => ToSaleBillRow(
            s.Id, s.InvoiceNumber, s.InvoiceDate, s.Party, s.GrandTotal, s.PaidAmount, s.Status)).ToList();
    }

    private async Task<IReadOnlyList<ReportBillListRowDto>> ListSalesByDayAsync(
        ReportBillDrillDownQuery query, CancellationToken ct)
    {
        if (query.Day is not DateTime day) return [];
        var start = day.Date;
        var end = start.AddDays(1);

        var rows = await SalesQuery(query.BranchId)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end)
            .OrderByDescending(s => s.InvoiceDate)
            .Select(s => new
            {
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                Party = s.Customer != null ? s.Customer.Name : (s.BillingCustomerName ?? "Walk-in"),
                s.GrandTotal,
                s.PaidAmount,
                s.Status
            })
            .ToListAsync(ct);

        return rows.Select(s => ToSaleBillRow(
            s.Id, s.InvoiceNumber, s.InvoiceDate, s.Party, s.GrandTotal, s.PaidAmount, s.Status)).ToList();
    }

    private async Task<IReadOnlyList<ReportBillListRowDto>> ListSalesByPaymentMethodAsync(
        ReportBillDrillDownQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.PaymentMethod)
            || !Enum.TryParse<PaymentMethod>(query.PaymentMethod, ignoreCase: true, out var method))
            return [];

        var (start, end) = NormalizeRange(query.From, query.To);
        var saleIds = await (
            from pay in _uow.Repository<SalePayment>().Query().AsNoTracking()
            join sale in SalesQuery(query.BranchId) on pay.SaleId equals sale.Id
            where pay.Method == method
                  && sale.InvoiceDate >= start && sale.InvoiceDate < end
            select sale.Id).Distinct().ToListAsync(ct);

        if (saleIds.Count == 0) return [];

        var rows = await SalesQuery(query.BranchId)
            .Where(s => saleIds.Contains(s.Id))
            .OrderByDescending(s => s.InvoiceDate)
            .Select(s => new
            {
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                Party = s.Customer != null ? s.Customer.Name : (s.BillingCustomerName ?? "Walk-in"),
                s.GrandTotal,
                s.PaidAmount,
                s.Status
            })
            .ToListAsync(ct);

        return rows.Select(s => ToSaleBillRow(
            s.Id, s.InvoiceNumber, s.InvoiceDate, s.Party, s.GrandTotal, s.PaidAmount, s.Status)).ToList();
    }

    private async Task<IReadOnlyList<ReportBillListRowDto>> ListSalesByMedicineAsync(
        ReportBillDrillDownQuery query, CancellationToken ct)
    {
        if (query.MedicineId is not int medicineId || medicineId <= 0) return [];

        var (start, end) = NormalizeRange(query.From, query.To);
        var saleIds = await (
            from item in _uow.Repository<SaleItem>().Query().AsNoTracking()
            join sale in SalesQuery(query.BranchId) on item.SaleId equals sale.Id
            where item.MedicineId == medicineId
                  && sale.InvoiceDate >= start && sale.InvoiceDate < end
            select sale.Id).Distinct().ToListAsync(ct);

        if (saleIds.Count == 0) return [];

        var rows = await SalesQuery(query.BranchId)
            .Where(s => saleIds.Contains(s.Id))
            .OrderByDescending(s => s.InvoiceDate)
            .Select(s => new
            {
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                Party = s.Customer != null ? s.Customer.Name : (s.BillingCustomerName ?? "Walk-in"),
                s.GrandTotal,
                s.PaidAmount,
                s.Status
            })
            .ToListAsync(ct);

        return rows.Select(s => ToSaleBillRow(
            s.Id, s.InvoiceNumber, s.InvoiceDate, s.Party, s.GrandTotal, s.PaidAmount, s.Status)).ToList();
    }

    private async Task<IReadOnlyList<ReportBillListRowDto>> ListSalesByMedicineReturnAsync(
        ReportBillDrillDownQuery query, CancellationToken ct)
    {
        if (query.MedicineId is not int medicineId || medicineId <= 0) return [];

        var (start, end) = NormalizeRange(query.From, query.To);
        var batch = string.IsNullOrWhiteSpace(query.BatchNumber) || query.BatchNumber == "—"
            ? null
            : query.BatchNumber.Trim();

        var itemQ = _uow.Repository<SaleReturnItem>().Query().AsNoTracking()
            .Where(i => i.SaleReturn!.Status == SaleReturnStatus.Completed
                        && i.MedicineId == medicineId
                        && i.SaleReturn.ReturnDate >= start
                        && i.SaleReturn.ReturnDate < end);
        if (query.BranchId.HasValue)
            itemQ = itemQ.Where(i => i.SaleReturn!.BranchId == query.BranchId);
        if (batch is null)
            itemQ = itemQ.Where(i => i.BatchNumber == null || i.BatchNumber == "");
        else
            itemQ = itemQ.Where(i => i.BatchNumber == batch);

        var saleIds = await itemQ
            .Select(i => i.SaleReturn!.SaleId)
            .Distinct()
            .ToListAsync(ct);

        if (saleIds.Count == 0) return [];

        var rows = await _uow.Repository<Sale>().Query().AsNoTracking()
            .Where(s => saleIds.Contains(s.Id))
            .OrderByDescending(s => s.InvoiceDate)
            .Select(s => new
            {
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                Party = s.Customer != null ? s.Customer.Name : (s.BillingCustomerName ?? "Walk-in"),
                s.GrandTotal,
                s.PaidAmount,
                s.Status
            })
            .ToListAsync(ct);

        return rows.Select(s => ToSaleBillRow(
            s.Id, s.InvoiceNumber, s.InvoiceDate, s.Party, s.GrandTotal, s.PaidAmount, s.Status)).ToList();
    }

    private async Task<IReadOnlyList<ReportBillListRowDto>> ListPurchasesBySupplierAsync(
        ReportBillDrillDownQuery query, bool openOnly, CancellationToken ct)
    {
        if (query.SupplierId is not int supplierId || supplierId <= 0) return [];

        var q = PurchasesQuery(query.BranchId).Where(p => p.SupplierId == supplierId);
        if (openOnly)
        {
            q = q.Where(p => p.GrandTotal > p.PaidAmount + 0.009m);
        }
        else
        {
            var (start, end) = NormalizeRange(query.From, query.To);
            q = q.Where(p => p.InvoiceDate >= start && p.InvoiceDate < end);
        }

        var rows = await q
            .OrderByDescending(p => p.InvoiceDate)
            .Select(p => new
            {
                p.Id,
                p.InvoiceNumber,
                p.InvoiceDate,
                Party = p.Supplier != null ? p.Supplier.Name : $"Supplier #{p.SupplierId}",
                p.GrandTotal,
                p.PaidAmount,
                p.Status
            })
            .ToListAsync(ct);

        return rows.Select(p => ToPurchaseBillRow(
            p.Id, p.InvoiceNumber, p.InvoiceDate, p.Party, p.GrandTotal, p.PaidAmount, p.Status)).ToList();
    }

    private async Task<IReadOnlyList<ReportBillListRowDto>> ListOpenSalesByCustomerIdAsync(
        ReportBillDrillDownQuery query, CancellationToken ct)
    {
        if (query.CustomerId is not int customerId || customerId <= 0) return [];

        var rows = await SalesQuery(query.BranchId)
            .Where(s => s.CustomerId == customerId && s.GrandTotal > s.PaidAmount + 0.009m)
            .OrderByDescending(s => s.InvoiceDate)
            .Select(s => new
            {
                s.Id,
                s.InvoiceNumber,
                s.InvoiceDate,
                Party = s.Customer != null ? s.Customer.Name : (s.BillingCustomerName ?? "Walk-in"),
                s.GrandTotal,
                s.PaidAmount,
                s.Status
            })
            .ToListAsync(ct);

        return rows.Select(s => ToSaleBillRow(
            s.Id, s.InvoiceNumber, s.InvoiceDate, s.Party, s.GrandTotal, s.PaidAmount, s.Status)).ToList();
    }

    private static ReportBillListRowDto ToSaleBillRow(
        int id, string invoice, DateTime date, string party, decimal total, decimal paid, SaleStatus status)
        => new(
            ReportDocumentKind.Sale,
            id,
            invoice,
            date,
            party,
            total,
            paid,
            total > paid ? total - paid : 0m,
            status == SaleStatus.PartiallyReturned ? "Partial return"
                : status == SaleStatus.Returned ? "Returned"
                : status == SaleStatus.Cancelled ? "Cancelled"
                : "Completed");

    private static ReportBillListRowDto ToPurchaseBillRow(
        int id, string invoice, DateTime date, string party, decimal total, decimal paid, PurchaseStatus status)
        => new(
            ReportDocumentKind.Purchase,
            id,
            invoice,
            date,
            party,
            total,
            paid,
            total > paid ? total - paid : 0m,
            status == PurchaseStatus.Cancelled ? "Cancelled"
                : status == PurchaseStatus.Draft ? "Draft"
                : total <= paid + 0.009m ? "Paid"
                : paid <= 0.009m ? "Pending"
                : "Partial");

    private async Task<ReportTableDto> BuildSalesByPaymentModeAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var saleIds = await SalesQuery(branchId)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end)
            .Select(s => s.Id)
            .ToListAsync(ct);

        if (saleIds.Count == 0)
            return Empty("No sales in this period.");

        var payments = await _uow.Repository<SalePayment>().Query().AsNoTracking()
            .Where(p => saleIds.Contains(p.SaleId))
            .GroupBy(p => p.Method)
            .Select(g => new { Method = g.Key, Amount = g.Sum(x => x.Amount), Count = g.Count() })
            .OrderByDescending(x => x.Amount)
            .ToListAsync(ct);

        return ReportTableMapper.Table(
            BuildSummary(payments.Count, payments.Sum(r => r.Amount), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("Method", "PaymentMode"),
                ReportTableMapper.C("Count", "Lines"),
                ReportTableMapper.C("Amount", "Amount", "N2")),
            payments.Select(r => ReportTableMapper.Dict(
                ("Method", (object?)r.Method.ToString()),
                ("Count", r.Count),
                ("Amount", r.Amount))));
    }

    private async Task<ReportTableDto> BuildSalesDayWiseAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var raw = await SalesQuery(branchId)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end)
            .Select(s => new { s.InvoiceDate, s.GrandTotal, s.CgstAmount, s.SgstAmount, s.IgstAmount, s.DiscountAmount })
            .ToListAsync(ct);

        var rows = raw
            .GroupBy(s => s.InvoiceDate.Date)
            .Select(g => new
            {
                Day = g.Key,
                Bills = g.Count(),
                Total = g.Sum(x => x.GrandTotal),
                Tax = g.Sum(x => x.CgstAmount + x.SgstAmount + x.IgstAmount),
                Discount = g.Sum(x => x.DiscountAmount)
            })
            .OrderBy(x => x.Day)
            .ToList();

        return ReportTableMapper.Table(
            BuildSummary(rows.Count, rows.Sum(r => r.Total), rows.Sum(r => r.Tax), rows.Sum(r => r.Discount)),
            ReportTableMapper.Cols(
                ReportTableMapper.C("Date", "Date"),
                ReportTableMapper.C("Bills", "Bills"),
                ReportTableMapper.C("Total", "Total", "N2"),
                ReportTableMapper.C("Tax", "Tax", "N2"),
                ReportTableMapper.C("Discount", "Discount", "N2")),
            rows.Select(r => ReportTableMapper.Dict(
                ("Date", (object?)r.Day.ToString("dd/MM/yyyy")),
                ("DayKey", r.Day.ToString("yyyy-MM-dd")),
                ("Bills", r.Bills),
                ("Total", r.Total),
                ("Tax", r.Tax),
                ("Discount", r.Discount))));
    }

    private async Task<ReportTableDto> BuildSalesCreditDueAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var (summary, all) = await GetSalesReportAsync(from, to, branchId, ct);
        var due = all.Where(r => r.BalanceDue > 0.009m).ToList();
        summary.RecordCount = due.Count;
        summary.TotalAmount = due.Sum(r => r.BalanceDue);
        summary.FooterNote = $"{due.Count} credit bill(s) · due ₹{summary.TotalAmount:N2}";
        return ReportTableMapper.FromSales(summary, due);
    }

    private async Task<ReportTableDto> BuildPurchasesBySupplierAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        // Group by id + name (no string.Format) so EF can translate; format label in-memory.
        var rows = await PurchasesQuery(branchId)
            .Where(p => p.InvoiceDate >= start && p.InvoiceDate < end)
            .GroupBy(p => new { p.SupplierId, Name = p.Supplier != null ? p.Supplier.Name : null })
            .Select(g => new
            {
                g.Key.SupplierId,
                g.Key.Name,
                Bills = g.Count(),
                Total = g.Sum(x => x.GrandTotal),
                Paid = g.Sum(x => x.PaidAmount),
                Due = g.Sum(x => x.GrandTotal > x.PaidAmount ? x.GrandTotal - x.PaidAmount : 0m)
            })
            .OrderByDescending(x => x.Total)
            .ToListAsync(ct);

        return ReportTableMapper.Table(
            BuildSummary(rows.Count, rows.Sum(r => r.Total), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("Supplier", "Supplier"),
                ReportTableMapper.C("Bills", "Bills"),
                ReportTableMapper.C("Total", "Total", "N2"),
                ReportTableMapper.C("Paid", "Paid", "N2"),
                ReportTableMapper.C("Due", "Due", "N2")),
            rows.Select(r => ReportTableMapper.Dict(
                ("SupplierId", (object?)r.SupplierId),
                ("Supplier", r.Name ?? $"Supplier #{r.SupplierId}"),
                ("Bills", r.Bills),
                ("Total", r.Total),
                ("Paid", r.Paid),
                ("Due", r.Due))));
    }

    private async Task<ReportTableDto> BuildSupplierOutstandingAsync(int? branchId, CancellationToken ct)
    {
        var q = _uow.Repository<Supplier>().Query().AsNoTracking()
            .Where(s => s.Status == EntityStatus.Active);
        if (branchId.HasValue) q = q.Where(s => s.BranchId == branchId);

        var rows = await q
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Phone,
                Due = s.OutstandingBalance > 0.009m ? s.OutstandingBalance : s.OpeningBalance
            })
            .Where(s => s.Due > 0.009m)
            .OrderByDescending(s => s.Due)
            .ToListAsync(ct);

        return ReportTableMapper.Table(
            BuildSummary(rows.Count, rows.Sum(r => r.Due), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("Name", "Supplier"),
                ReportTableMapper.C("Phone", "Phone"),
                ReportTableMapper.C("OutstandingBalance", "Outstanding", "N2")),
            rows.Select(r => ReportTableMapper.Dict(
                ("SupplierId", (object?)r.Id),
                ("Name", r.Name),
                ("Phone", r.Phone),
                ("OutstandingBalance", r.Due))));
    }

    private async Task<ReportTableDto> BuildCustomerOutstandingAsync(int? branchId, CancellationToken ct)
    {
        var q = _uow.Repository<Customer>().Query().AsNoTracking()
            .Where(c => c.Status == EntityStatus.Active && c.OutstandingBalance > 0);
        if (branchId.HasValue) q = q.Where(c => c.BranchId == branchId);

        var rows = await q
            .OrderByDescending(c => c.OutstandingBalance)
            .Select(c => new { c.Id, c.Name, c.Phone, c.OutstandingBalance })
            .ToListAsync(ct);

        return ReportTableMapper.Table(
            BuildSummary(rows.Count, rows.Sum(r => r.OutstandingBalance), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("Name", "Customer"),
                ReportTableMapper.C("Phone", "Phone"),
                ReportTableMapper.C("OutstandingBalance", "Outstanding", "N2")),
            rows.Select(r => ReportTableMapper.Dict(
                ("CustomerId", (object?)r.Id),
                ("Name", r.Name),
                ("Phone", r.Phone),
                ("OutstandingBalance", r.OutstandingBalance))));
    }

    private async Task<ReportTableDto> BuildPurchaseReturnsAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var q = _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
            .Where(r => r.Status == PurchaseReturnStatus.Completed
                        && r.ReturnDate >= start && r.ReturnDate < end);
        if (branchId.HasValue) q = q.Where(r => r.BranchId == branchId);

        var rows = await q
            .OrderByDescending(r => r.ReturnDate)
            .Select(r => new
            {
                r.ReturnNumber,
                r.ReturnDate,
                r.SupplierId,
                SupplierName = r.Supplier != null ? r.Supplier.Name : null,
                Invoice = r.Purchase != null ? r.Purchase.InvoiceNumber : null,
                Kind = r.ReturnKind == PurchaseReturnKind.ExpiryToCompany ? "Expiry to company" : "Standard",
                Direct = r.PurchaseId == null,
                r.PurchaseId,
                SettledPurchaseId = r.SettledAgainstPurchaseId,
                r.GrandTotal,
                r.CreditAmount,
                Remaining = r.CreditAmount - r.CreditAppliedAmount,
                r.SupplierReturnReceiptNumber,
                r.ReceiptSettlementKind,
                ReceiptDate = r.SupplierReturnReceiptDate
            })
            .ToListAsync(ct);

        var mapped = rows.Select(r =>
        {
            var hasReceipt = PurchaseReturn.IsRealSupplierReceiptNumber(r.SupplierReturnReceiptNumber);
            return new
            {
                r.ReturnNumber,
                r.ReturnDate,
                r.SupplierId,
                r.SupplierName,
                r.Invoice,
                r.Kind,
                r.Direct,
                r.PurchaseId,
                r.SettledPurchaseId,
                r.GrandTotal,
                r.CreditAmount,
                r.Remaining,
                Settlement = !hasReceipt
                    ? "Pending"
                    : r.ReceiptSettlementKind == PurchaseReturnReceiptSettlementKind.PurchaseBill
                        ? "Purchase bill"
                        : "Receipt",
                Reference = hasReceipt ? r.SupplierReturnReceiptNumber : null,
                r.ReceiptDate
            };
        }).ToList();

        return ReportTableMapper.Table(
            BuildSummary(mapped.Count, mapped.Sum(r => r.GrandTotal), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("ReturnNumber", "Return#"),
                ReportTableMapper.C("Date", "Date"),
                ReportTableMapper.C("Kind", "Kind"),
                ReportTableMapper.C("Supplier", "Supplier"),
                ReportTableMapper.C("Invoice", "Source bill"),
                ReportTableMapper.C("Direct", "Direct"),
                ReportTableMapper.C("GrandTotal", "Amount", "N2"),
                ReportTableMapper.C("CreditAmount", "Credit", "N2"),
                ReportTableMapper.C("Remaining", "Remaining", "N2"),
                ReportTableMapper.C("Settlement", "Settled via"),
                ReportTableMapper.C("Reference", "Reference"),
                ReportTableMapper.C("ReceiptDate", "Settled date")),
            mapped.Select(r => ReportTableMapper.Dict(
                ("ReturnNumber", (object?)r.ReturnNumber),
                ("Date", r.ReturnDate.ToString("dd/MM/yyyy")),
                ("Kind", r.Kind),
                ("Supplier", r.SupplierName ?? $"Supplier #{r.SupplierId}"),
                ("Invoice", r.Invoice ?? (r.Direct ? "Direct" : "—")),
                ("Direct", r.Direct ? "Yes" : "No"),
                ("PurchaseId", r.PurchaseId ?? r.SettledPurchaseId),
                ("GrandTotal", r.GrandTotal),
                ("CreditAmount", r.CreditAmount),
                ("Remaining", r.Remaining),
                ("Settlement", r.Settlement),
                ("Reference", r.Reference ?? "—"),
                ("ReceiptDate", r.ReceiptDate?.ToString("dd/MM/yyyy") ?? "—"))));
    }

    private async Task<ReportTableDto> BuildExpiryToCompanyClaimsAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var q = _uow.Repository<ExpirySupplierClaim>().Query().AsNoTracking()
            .Where(c => c.ClaimDate >= start && c.ClaimDate < end
                        && c.Status != ExpiryClaimStatus.Cancelled);
        if (branchId.HasValue) q = q.Where(c => c.BranchId == branchId);

        var claims = await q
            .OrderByDescending(c => c.ClaimDate)
            .Select(c => new
            {
                c.ClaimNumber,
                c.ClaimDate,
                SupplierName = c.Supplier != null ? c.Supplier.Name : "—",
                c.ExpectedCreditAmount,
                c.Status,
                c.CreditSettlementKind,
                c.CreditNoteNumber,
                ReturnNumber = c.PurchaseReturn != null ? c.PurchaseReturn.ReturnNumber : "",
                Items = c.Items.Select(i => new
                {
                    i.MedicineId,
                    i.BatchNumber,
                    i.ExpiryDate,
                    i.ClaimQuantity,
                    i.LineTotal,
                    i.PurchaseId
                }).ToList()
            })
            .ToListAsync(ct);

        var medIds = claims.SelectMany(c => c.Items.Select(i => i.MedicineId)).Distinct().ToList();
        var names = medIds.Count == 0
            ? new Dictionary<int, string>()
            : await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
                .Where(m => medIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var purchaseIds = claims.SelectMany(c => c.Items)
            .Where(i => i.PurchaseId.HasValue)
            .Select(i => i.PurchaseId!.Value)
            .Distinct()
            .ToList();
        var invoices = purchaseIds.Count == 0
            ? new Dictionary<int, string>()
            : await _uow.Repository<Purchase>().Query().AsNoTracking()
                .Where(p => purchaseIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.InvoiceNumber, ct);

        var rows = claims.SelectMany(c =>
        {
            var status = c.Status == ExpiryClaimStatus.CreditReceived
                ? "Credit received"
                : "Awaiting credit note";
            var settlement = c.Status != ExpiryClaimStatus.CreditReceived
                ? "—"
                : c.CreditSettlementKind == ExpiryCreditSettlementKind.PurchaseBill
                    ? "Purchase bill"
                    : "Credit note";
            return c.Items.Select(i => new
            {
                c.ClaimNumber,
                c.ClaimDate,
                c.SupplierName,
                Medicine = names.TryGetValue(i.MedicineId, out var n) ? n : $"Medicine #{i.MedicineId}",
                i.BatchNumber,
                i.ExpiryDate,
                i.ClaimQuantity,
                i.LineTotal,
                Status = status,
                Settlement = settlement,
                c.CreditNoteNumber,
                c.ReturnNumber,
                Invoice = i.PurchaseId is int pid && invoices.TryGetValue(pid, out var inv) ? inv : null
            });
        }).ToList();

        return ReportTableMapper.Table(
            BuildSummary(rows.Count, rows.Sum(r => r.LineTotal), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("ClaimNumber", "Claim#"),
                ReportTableMapper.C("Date", "Date"),
                ReportTableMapper.C("Supplier", "Supplier"),
                ReportTableMapper.C("Medicine", "Medicine"),
                ReportTableMapper.C("Batch", "Batch"),
                ReportTableMapper.C("Expiry", "Expiry"),
                ReportTableMapper.C("Qty", "Qty", "0.##"),
                ReportTableMapper.C("LineTotal", "Amount", "N2"),
                ReportTableMapper.C("Status", "Status"),
                ReportTableMapper.C("Settlement", "Settled via"),
                ReportTableMapper.C("CreditNote", "Reference"),
                ReportTableMapper.C("ReturnNumber", "Return#"),
                ReportTableMapper.C("Invoice", "PurchaseInvoice")),
            rows.Select(r => ReportTableMapper.Dict(
                ("ClaimNumber", (object?)r.ClaimNumber),
                ("Date", r.ClaimDate.ToString("dd/MM/yyyy")),
                ("Supplier", r.SupplierName),
                ("Medicine", r.Medicine),
                ("Batch", r.BatchNumber),
                ("Expiry", r.ExpiryDate?.ToString("dd/MM/yyyy") ?? "—"),
                ("Qty", r.ClaimQuantity),
                ("LineTotal", r.LineTotal),
                ("Status", r.Status),
                ("Settlement", r.Settlement),
                ("CreditNote", r.CreditNoteNumber ?? "—"),
                ("ReturnNumber", string.IsNullOrWhiteSpace(r.ReturnNumber) ? "—" : r.ReturnNumber),
                ("Invoice", r.Invoice ?? "—"))));
    }

    private async Task<ReportTableDto> BuildPurchasePaymentsAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var q = _uow.Repository<Purchase>().Query().AsNoTracking()
            .Where(p => p.Status != PurchaseStatus.Cancelled
                        && p.Status != PurchaseStatus.Draft
                        // Period bills, plus any still-pending bills (so Pending filter is useful)
                        && ((p.InvoiceDate >= start && p.InvoiceDate < end)
                            || p.GrandTotal > p.PaidAmount));
        if (branchId.HasValue) q = q.Where(p => p.BranchId == branchId);

        var rows = await q
            .OrderByDescending(p => p.InvoiceDate)
            .Select(p => new
            {
                p.Id,
                p.InvoiceNumber,
                SupplierBillNumber = p.SupplierInvoiceNumber,
                p.InvoiceDate,
                Supplier = p.Supplier != null ? p.Supplier.Name : "—",
                p.GrandTotal,
                p.PaidAmount
            })
            .ToListAsync(ct);

        // Same source as Accounting → Parties RHS "Adjusted":
        // any completed return with SettledAgainstPurchaseId (Return Records bill settlement).
        var returnAdj = await _uow.Repository<PurchaseReturn>().Query().AsNoTracking()
            .Where(r => r.Status == PurchaseReturnStatus.Completed
                        && r.SettledAgainstPurchaseId != null)
            .Select(r => new
            {
                PurchaseId = r.SettledAgainstPurchaseId!.Value,
                r.CreditAmount
            })
            .ToListAsync(ct);

        var adjustedByPurchase = returnAdj
            .GroupBy(r => r.PurchaseId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.CreditAmount));

        var projected = rows.Select(r =>
        {
            adjustedByPurchase.TryGetValue(r.Id, out var adjusted);
            var rawDue = r.GrandTotal - r.PaidAmount - adjusted;
            var due = rawDue > 0.009m ? rawDue : 0m;
            var status = due <= 0.009m
                ? "Paid"
                : r.PaidAmount <= 0.009m && adjusted <= 0.009m
                    ? "Pending"
                    : "Partial";
            return new
            {
                PurchaseId = r.Id,
                r.InvoiceNumber,
                r.SupplierBillNumber,
                r.InvoiceDate,
                r.Supplier,
                r.GrandTotal,
                r.PaidAmount,
                AdjustedAmount = adjusted,
                BalanceDue = due,
                Status = status
            };
        }).ToList();

        // Keep period bills even if settled; for "open" extras keep only those still due after adjustments.
        projected = projected
            .Where(r => (r.InvoiceDate >= start && r.InvoiceDate < end) || r.BalanceDue > 0.009m)
            .ToList();

        return ReportTableMapper.Table(
            new ReportSummaryDto
            {
                RecordCount = projected.Count,
                TotalAmount = projected.Sum(r => r.GrandTotal),
                FooterNote =
                    $"Paid ₹{projected.Sum(r => r.PaidAmount):N2} · Adjusted ₹{projected.Sum(r => r.AdjustedAmount):N2} · Pending ₹{projected.Sum(r => r.BalanceDue):N2}"
            },
            ReportTableMapper.Cols(
                ReportTableMapper.C("InvoiceNumber", "Invoice"),
                ReportTableMapper.C("SupplierBillNumber", "SupplierBill#"),
                ReportTableMapper.C("InvoiceDateLabel", "Date"),
                ReportTableMapper.C("Supplier", "Supplier"),
                ReportTableMapper.C("GrandTotal", "BillAmt", "N2"),
                ReportTableMapper.C("PaidAmount", "Paid", "N2"),
                ReportTableMapper.C("AdjustedAmount", "Adjusted", "N2"),
                ReportTableMapper.C("BalanceDue", "Due", "N2"),
                ReportTableMapper.C("Status", "Status")),
            projected.Select(r => ReportTableMapper.Dict(
                ("PurchaseId", (object?)r.PurchaseId),
                ("InvoiceNumber", r.InvoiceNumber),
                ("SupplierBillNumber", r.SupplierBillNumber ?? "—"),
                ("InvoiceDateLabel", r.InvoiceDate.ToString("dd/MM/yyyy")),
                ("Supplier", r.Supplier),
                ("GrandTotal", r.GrandTotal),
                ("PaidAmount", r.PaidAmount),
                ("AdjustedAmount", r.AdjustedAmount),
                ("BalanceDue", r.BalanceDue),
                ("Status", r.Status))));
    }

    private async Task<ReportTableDto> BuildVoucherRegisterAsync(
        string referenceType, DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var q = _uow.Repository<JournalEntry>().Query().AsNoTracking()
            .Where(e => e.ReferenceType == referenceType
                        && e.EntryDate >= start && e.EntryDate < end);
        if (branchId.HasValue) q = q.Where(e => e.BranchId == branchId);

        var entries = await q
            .OrderByDescending(e => e.EntryDate)
            .Select(e => new
            {
                e.VoucherNumber,
                e.EntryDate,
                e.Narration,
                e.ReferenceId,
                Amount = e.Lines.Where(l => l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount)
            })
            .ToListAsync(ct);

        var partyIds = entries
            .Where(e => e.ReferenceId.HasValue)
            .Select(e => e.ReferenceId!.Value)
            .Distinct()
            .ToList();

        Dictionary<int, string> partyNames;
        if (string.Equals(referenceType, "Payment", StringComparison.OrdinalIgnoreCase))
        {
            partyNames = await _uow.Repository<Supplier>().Query().AsNoTracking()
                .Where(s => partyIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        }
        else
        {
            partyNames = await _uow.Repository<Customer>().Query().AsNoTracking()
                .Where(c => partyIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        }

        return ReportTableMapper.Table(
            BuildSummary(entries.Count, entries.Sum(r => r.Amount), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("VoucherNumber", "Voucher"),
                ReportTableMapper.C("Date", "Date"),
                ReportTableMapper.C("Party", "Party"),
                ReportTableMapper.C("Narration", "Narration"),
                ReportTableMapper.C("Amount", "Amount", "N2")),
            entries.Select(r =>
            {
                string party = "—";
                if (r.ReferenceId.HasValue && partyNames.TryGetValue(r.ReferenceId.Value, out var name))
                    party = name;
                return ReportTableMapper.Dict(
                    ("VoucherNumber", (object?)r.VoucherNumber),
                    ("Date", r.EntryDate.ToString("dd/MM/yyyy")),
                    ("Party", party),
                    ("Narration", r.Narration),
                    ("Amount", r.Amount));
            }));
    }

    private async Task<ReportTableDto> BuildExpenseRegisterAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var q = _uow.Repository<JournalEntry>().Query().AsNoTracking()
            .Where(e => e.ReferenceType == "Expense"
                        && e.EntryDate >= start && e.EntryDate < end);
        if (branchId.HasValue) q = q.Where(e => e.BranchId == branchId);

        var entries = await q
            .Include(e => e.Lines).ThenInclude(l => l.Account)
            .OrderByDescending(e => e.EntryDate)
            .ToListAsync(ct);

        var rows = entries.Select(e =>
        {
            var expenseLine = e.Lines.FirstOrDefault(l =>
                l.EntryType == LedgerEntryType.Debit && l.Account?.Type == AccountType.Expense);
            return new
            {
                e.VoucherNumber,
                e.EntryDate,
                Account = expenseLine?.Account?.Name ?? "Expense",
                e.Narration,
                Amount = expenseLine?.Amount ?? e.Lines.Where(l => l.EntryType == LedgerEntryType.Debit).Sum(l => l.Amount)
            };
        }).ToList();

        return ReportTableMapper.Table(
            BuildSummary(rows.Count, rows.Sum(r => r.Amount), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("VoucherNumber", "Voucher"),
                ReportTableMapper.C("Date", "Date"),
                ReportTableMapper.C("Account", "ExpenseAccount"),
                ReportTableMapper.C("Narration", "Narration"),
                ReportTableMapper.C("Amount", "Amount", "N2")),
            rows.Select(r => ReportTableMapper.Dict(
                ("VoucherNumber", (object?)r.VoucherNumber),
                ("Date", r.EntryDate.ToString("dd/MM/yyyy")),
                ("Account", r.Account),
                ("Narration", r.Narration),
                ("Amount", r.Amount))));
    }

    private async Task<ReportTableDto> BuildExpenseByAccountAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var table = await BuildExpenseRegisterAsync(from, to, branchId, ct);
        var grouped = table.Rows
            .GroupBy(r => r.TryGetValue("Account", out var a) ? a?.ToString() ?? "Expense" : "Expense")
            .Select(g => new
            {
                Account = g.Key,
                Count = g.Count(),
                Amount = g.Sum(x => x.TryGetValue("Amount", out var amt) && amt is decimal d ? d : 0m)
            })
            .OrderByDescending(x => x.Amount)
            .ToList();

        return ReportTableMapper.Table(
            BuildSummary(grouped.Count, grouped.Sum(r => r.Amount), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("Account", "ExpenseAccount"),
                ReportTableMapper.C("Count", "Vouchers"),
                ReportTableMapper.C("Amount", "Amount", "N2")),
            grouped.Select(r => ReportTableMapper.Dict(
                ("Account", (object?)r.Account),
                ("Count", r.Count),
                ("Amount", r.Amount))));
    }

    private async Task<ReportTableDto> BuildCashBookSummaryAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var cash = await _uow.Repository<Account>().Query().AsNoTracking()
            .FirstOrDefaultAsync(a => a.Code == "1000", ct);
        if (cash is null)
            return Empty("Cash account (1000) not found.");

        var (start, end) = NormalizeRange(from, to);
        var q = _uow.Repository<JournalLine>().Query().AsNoTracking()
            .Where(l => l.AccountId == cash.Id
                        && l.JournalEntry != null
                        && l.JournalEntry.EntryDate >= start
                        && l.JournalEntry.EntryDate < end);
        if (branchId.HasValue)
            q = q.Where(l => l.JournalEntry!.BranchId == branchId);

        var lines = await q
            .Select(l => new
            {
                l.JournalEntry!.EntryDate,
                l.EntryType,
                l.Amount
            })
            .ToListAsync(ct);

        var rows = lines
            .GroupBy(l => l.EntryDate.Date)
            .Select(g =>
            {
                var inflow = g.Where(x => x.EntryType == LedgerEntryType.Debit).Sum(x => x.Amount);
                var outflow = g.Where(x => x.EntryType == LedgerEntryType.Credit).Sum(x => x.Amount);
                return new
                {
                    Day = g.Key,
                    Inflow = inflow,
                    Outflow = outflow,
                    Net = inflow - outflow
                };
            })
            .OrderBy(x => x.Day)
            .ToList();

        return ReportTableMapper.Table(
            BuildSummary(rows.Count, rows.Sum(r => r.Net), 0, 0),
            ReportTableMapper.Cols(
                ReportTableMapper.C("Date", "Date"),
                ReportTableMapper.C("Inflow", "CashIn", "N2"),
                ReportTableMapper.C("Outflow", "CashOut", "N2"),
                ReportTableMapper.C("Net", "Net", "N2")),
            rows.Select(r => ReportTableMapper.Dict(
                ("Date", (object?)r.Day.ToString("dd/MM/yyyy")),
                ("Inflow", r.Inflow),
                ("Outflow", r.Outflow),
                ("Net", r.Net))));
    }

    private async Task<ReportTableDto> BuildBatchStockAsync(int? branchId, CancellationToken ct)
    {
        var rows = await BatchQuery(branchId)
            .Where(b => b.QuantityAvailable != 0)
            .OrderBy(b => b.Medicine!.Name).ThenBy(b => b.ExpiryDate)
            .Select(b => new
            {
                Medicine = b.Medicine!.Name,
                b.BatchNumber,
                b.ExpiryDate,
                b.QuantityAvailable,
                b.PurchasePrice,
                b.Mrp
            })
            .ToListAsync(ct);

        return ReportTableMapper.Table(
            new ReportSummaryDto
            {
                RecordCount = rows.Count,
                TotalAmount = rows.Sum(r => r.QuantityAvailable * r.Mrp),
                FooterNote = $"Cost value ₹{rows.Sum(r => r.QuantityAvailable * r.PurchasePrice):N2}"
            },
            ReportTableMapper.Cols(
                ReportTableMapper.C("Medicine", "Medicine"),
                ReportTableMapper.C("Batch", "Batch"),
                ReportTableMapper.C("Expiry", "Expiry"),
                ReportTableMapper.C("Qty", "Qty", "0.##"),
                ReportTableMapper.C("Cost", "Cost", "N2"),
                ReportTableMapper.C("Mrp", "MRP", "N2"),
                ReportTableMapper.C("Value", "MRPValue", "N2")),
            rows.Select(r => ReportTableMapper.Dict(
                ("Medicine", (object?)r.Medicine),
                ("Batch", r.BatchNumber),
                ("Expiry", r.ExpiryDate.HasValue ? r.ExpiryDate.Value.ToString("dd/MM/yyyy") : "—"),
                ("Qty", r.QuantityAvailable),
                ("Cost", r.PurchasePrice),
                ("Mrp", r.Mrp),
                ("Value", Math.Round(r.QuantityAvailable * r.Mrp, 2)))));
    }

    private async Task<ReportTableDto> BuildSlowMovingStockAsync(int? branchId, CancellationToken ct)
    {
        var cutoff = _clock.Today.AddDays(-90);
        var stock = await BatchQuery(branchId)
            .Where(b => b.QuantityAvailable != 0)
            .Select(b => new
            {
                b.MedicineId,
                Medicine = b.Medicine!.Name,
                b.BatchNumber,
                b.QuantityAvailable,
                b.PurchasePrice,
                b.Mrp
            })
            .ToListAsync(ct);

        if (stock.Count == 0)
            return Empty("No stock on hand.");

        var medIds = stock.Select(s => s.MedicineId).Distinct().ToList();
        var lastSales = await (
            from item in _uow.Repository<SaleItem>().Query().AsNoTracking()
            join sale in SalesQuery(branchId) on item.SaleId equals sale.Id
            where medIds.Contains(item.MedicineId)
            group sale by item.MedicineId
            into g
            select new { MedicineId = g.Key, LastSale = g.Max(x => x.InvoiceDate) }
        ).ToDictionaryAsync(x => x.MedicineId, x => x.LastSale, ct);

        var rows = stock
            .Select(s =>
            {
                lastSales.TryGetValue(s.MedicineId, out var last);
                var days = last == default
                    ? (int?)null
                    : (int)(_clock.Today.Date - last.Date).TotalDays;
                return new
                {
                    s.Medicine,
                    s.BatchNumber,
                    s.QuantityAvailable,
                    Value = Math.Round(s.QuantityAvailable * s.PurchasePrice, 2),
                    LastSale = last == default ? null : last.ToString("dd/MM/yyyy"),
                    DaysSince = days,
                    IsSlow = last == default || last < cutoff
                };
            })
            .Where(r => r.IsSlow)
            .OrderByDescending(r => r.DaysSince ?? 9999)
            .ToList();

        return ReportTableMapper.Table(
            new ReportSummaryDto
            {
                RecordCount = rows.Count,
                TotalAmount = rows.Sum(r => r.Value),
                FooterNote = "No sale in last 90 days (or never sold)."
            },
            ReportTableMapper.Cols(
                ReportTableMapper.C("Medicine", "Medicine"),
                ReportTableMapper.C("Batch", "Batch"),
                ReportTableMapper.C("Qty", "Qty", "0.##"),
                ReportTableMapper.C("Value", "CostValue", "N2"),
                ReportTableMapper.C("LastSale", "LastSale"),
                ReportTableMapper.C("DaysSince", "DaysIdle")),
            rows.Select(r => ReportTableMapper.Dict(
                ("Medicine", (object?)r.Medicine),
                ("Batch", r.BatchNumber),
                ("Qty", r.QuantityAvailable),
                ("Value", r.Value),
                ("LastSale", r.LastSale ?? "Never"),
                ("DaysSince", r.DaysSince ?? 9999))));
    }

    private async Task<ReportTableDto> BuildMedicinesSoldByDateAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var completedSales = SalesQuery(branchId)
            .Where(s => s.InvoiceDate >= start && s.InvoiceDate < end);

        var lines = await (
            from item in _uow.Repository<SaleItem>().Query().AsNoTracking()
            join sale in completedSales on item.SaleId equals sale.Id
            select new
            {
                sale.Id,
                sale.InvoiceDate,
                sale.InvoiceNumber,
                item.MedicineId,
                item.MedicineBatchId,
                item.BatchNumber,
                item.Quantity,
                item.LineTotal
            }).ToListAsync(ct);

        if (lines.Count == 0)
        {
            return ReportTableMapper.FromMedicinesSoldByDate(
                new ReportSummaryDto { RecordCount = 0, FooterNote = "No sales in range." },
                []);
        }

        var medIds = lines.Select(l => l.MedicineId).Distinct().ToList();
        var medicines = await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
            .Where(m => medIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.GenericName, m.PurchasePrice })
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
            .Select(l =>
            {
                medicines.TryGetValue(l.MedicineId, out var med);
                var batchPp = l.MedicineBatchId is int bid && batchCosts.TryGetValue(bid, out var price)
                    ? price
                    : (decimal?)null;
                var unitCost = ResolveUnitCost(batchPp, med?.PurchasePrice ?? 0m);
                var cost = l.Quantity * unitCost;
                return new MedicinesSoldByDateRowDto(
                    l.InvoiceDate.Date,
                    l.InvoiceNumber,
                    l.Id,
                    l.MedicineId,
                    med?.Name ?? $"Medicine #{l.MedicineId}",
                    med?.GenericName,
                    l.BatchNumber ?? "—",
                    l.Quantity,
                    l.LineTotal,
                    cost,
                    l.LineTotal - cost);
            })
            .OrderByDescending(r => r.SaleDate)
            .ThenBy(r => r.InvoiceNumber)
            .ThenBy(r => r.MedicineName)
            .ToList();

        return ReportTableMapper.FromMedicinesSoldByDate(
            new ReportSummaryDto
            {
                RecordCount = rows.Count,
                TotalAmount = rows.Sum(r => r.Revenue),
                FooterNote = $"{rows.Count} line(s) · qty {rows.Sum(r => r.Quantity):0.##}"
            },
            rows);
    }

    private async Task<ReportTableDto> BuildStockAdjustmentsAsync(
        DateTime from, DateTime to, int? branchId, CancellationToken ct)
    {
        var (start, end) = NormalizeRange(from, to);
        var q = _uow.Repository<StockAdjustmentItem>().Query().AsNoTracking()
            .Where(i => i.StockAdjustment != null
                        && !i.StockAdjustment.IsDeleted
                        && i.StockAdjustment.AdjustmentDate >= start
                        && i.StockAdjustment.AdjustmentDate < end);
        if (branchId.HasValue)
            q = q.Where(i => i.StockAdjustment!.BranchId == branchId);

        var raw = await q
            .OrderByDescending(i => i.StockAdjustment!.AdjustmentDate)
            .ThenByDescending(i => i.StockAdjustment!.Id)
            .Select(i => new
            {
                i.StockAdjustment!.AdjustmentDate,
                i.StockAdjustment.AdjustmentNumber,
                i.MedicineId,
                MedicineName = i.Medicine != null ? i.Medicine.Name : null,
                BatchNumber = i.MedicineBatch != null ? i.MedicineBatch.BatchNumber : null,
                i.SystemQuantity,
                i.PhysicalQuantity,
                i.StockAdjustment.Reason,
                i.Remarks
            })
            .ToListAsync(ct);

        var missingMedIds = raw.Where(r => string.IsNullOrWhiteSpace(r.MedicineName))
            .Select(r => r.MedicineId)
            .Distinct()
            .ToList();
        var medNames = missingMedIds.Count == 0
            ? new Dictionary<int, string>()
            : await _uow.Repository<Medicine>().QueryIncludingDeleted().AsNoTracking()
                .Where(m => missingMedIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var rows = raw.Select(r =>
        {
            var name = !string.IsNullOrWhiteSpace(r.MedicineName)
                ? r.MedicineName!
                : medNames.TryGetValue(r.MedicineId, out var n) ? n : $"Medicine #{r.MedicineId}";
            return new StockAdjustmentReportRowDto(
                r.AdjustmentDate,
                r.AdjustmentNumber,
                name,
                string.IsNullOrWhiteSpace(r.BatchNumber) ? "—" : r.BatchNumber!,
                r.SystemQuantity,
                r.PhysicalQuantity,
                r.PhysicalQuantity - r.SystemQuantity,
                r.Reason,
                r.Remarks);
        }).ToList();

        return ReportTableMapper.FromStockAdjustments(
            new ReportSummaryDto
            {
                RecordCount = rows.Count,
                TotalAmount = rows.Sum(r => Math.Abs(r.Difference)),
                FooterNote =
                    $"{rows.Count(r => r.Difference > 0)} increase · {rows.Count(r => r.Difference < 0)} decrease"
            },
            rows);
    }
}
