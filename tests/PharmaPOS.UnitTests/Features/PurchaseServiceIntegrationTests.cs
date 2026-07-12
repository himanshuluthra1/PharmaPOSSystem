using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.Infrastructure;
using PharmaPOS.Persistence;
using PharmaPOS.Persistence.Context;

namespace PharmaPOS.UnitTests.Features;

public class PurchaseServiceIntegrationTests
{
    private static bool CanConnectToLocalDb()
    {
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(
                    "Server=(localdb)\\MSSQLLocalDB;Database=PharmaPosDb;Trusted_Connection=True;TrustServerCertificate=True")
                .Options;
            using var ctx = new ApplicationDbContext(options);
            return ctx.Database.CanConnect();
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task ListPurchasesBySupplier_includes_supplier_invoice_number()
    {
        if (!CanConnectToLocalDb()) return;

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure();
        services.AddPersistence(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseService>();

        var rows = await purchases.ListPurchasesBySupplierAsync(null, 1);
        var medWin = rows.FirstOrDefault(r => r.InvoiceNumber.StartsWith("MW-P-", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(medWin);
        Assert.False(string.IsNullOrWhiteSpace(medWin.SupplierInvoiceNumber));
    }

    [Fact]
    public async Task ListPurchasesBySupplier_returns_non_zero_payment_due_when_partially_paid()
    {
        if (!CanConnectToLocalDb()) return;

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure();
        services.AddPersistence(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseService>();

        var rows = await purchases.ListPurchasesBySupplierAsync(null, 1);
        var bill407 = rows.FirstOrDefault(r => r.InvoiceNumber == "MW-P-407");
        Assert.NotNull(bill407);
        Assert.Equal(4946m, bill407.PaymentDue);
        Assert.Equal(8315m, bill407.PaidAmount);

        Assert.Contains(rows, r => r.PaymentDue > 0);
    }

    [Fact]
    public async Task ListPurchasesBySupplier_bill_2223_shows_full_payment_due()
    {
        if (!CanConnectToLocalDb()) return;

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure();
        services.AddPersistence(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseService>();

        var rows = await purchases.ListPurchasesBySupplierAsync(null, 1);
        var bill = rows.FirstOrDefault(r => r.SupplierInvoiceNumber == "2223");
        Assert.NotNull(bill);
        Assert.Equal(12799m, bill.GrandTotal);
        Assert.Equal(0m, bill.PaidAmount);
        Assert.Equal(12799m, bill.PaymentDue);
    }

    [Fact]
    public async Task GetPurchaseForLoad_returns_medicine_names_for_soft_deleted_medwin_rows()
    {
        if (!CanConnectToLocalDb()) return;

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure();
        services.AddPersistence(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var purchases = scope.ServiceProvider.GetRequiredService<IPurchaseService>();

        var rows = await purchases.ListPurchasesBySupplierAsync(null, 1);
        var medWin = rows.First(r => r.InvoiceNumber.StartsWith("MW-P-", StringComparison.OrdinalIgnoreCase));

        var result = await purchases.GetPurchaseForLoadAsync(medWin.PurchaseId, 1);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!.Lines);
        Assert.All(result.Value.Lines, line =>
        {
            Assert.False(line.MedicineName.StartsWith("Medicine #", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(line.MedicineName));
        });
    }
}
