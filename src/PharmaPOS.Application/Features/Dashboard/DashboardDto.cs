namespace PharmaPOS.Application.Features.Dashboard;

/// <summary>Aggregated KPIs shown on the main dashboard.</summary>
public class DashboardDto
{
    public decimal TodaySales { get; set; }
    public decimal TodayPurchase { get; set; }
    public decimal TodayProfit { get; set; }
    public decimal PendingReceivables { get; set; }
    public decimal PendingPayables { get; set; }

    public int TodayCustomers { get; set; }
    public int TodayInvoices { get; set; }

    public int LowStockCount { get; set; }
    public int NearExpiryCount { get; set; }
    public int ExpiredCount { get; set; }

    public decimal CashInHand { get; set; }
    public decimal BankBalance { get; set; }

    public List<TopMedicineDto> TopSellingMedicines { get; set; } = new();
    public List<MonthlySalesDto> MonthlySales { get; set; } = new();
    public List<MonthlySalesDto> MonthlyPurchases { get; set; } = new();
}

public record TopMedicineDto(string Name, decimal QuantitySold, decimal Revenue);

public record MonthlySalesDto(string Month, decimal Amount);
