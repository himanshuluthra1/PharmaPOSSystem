using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application;
using PharmaPOS.Application.Features.SaleReturns;
using PharmaPOS.Domain.Enums;
using PharmaPOS.Infrastructure;
using PharmaPOS.Persistence;
using PharmaPOS.Persistence.Context;

namespace PharmaPOS.UnitTests.Features;

public class SaleReturnServiceIntegrationTests
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
    public async Task CreateReturn_succeeds_when_sale_item_has_batch_number_but_no_batch_id()
    {
        if (!CanConnectToLocalDb()) return;

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure();
        services.AddPersistence(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var returns = scope.ServiceProvider.GetRequiredService<ISaleReturnService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Prefer a recent app-created sale with a missing batch FK (MedWin pattern).
        var line = await db.SaleItems.AsNoTracking()
            .Where(i => i.MedicineBatchId == null
                        && i.BatchNumber != null
                        && i.BatchNumber != ""
                        && i.Quantity > 0
                        && i.Sale!.Status == SaleStatus.Completed
                        && !i.IsDeleted
                        && !i.Sale.IsDeleted)
            .OrderByDescending(i => i.SaleId)
            .Select(i => new { i.Id, i.SaleId, i.Quantity, i.MedicineBatchId })
            .FirstOrDefaultAsync();

        Assert.NotNull(line);

        // Sanity: service can load this sale for return.
        var loaded = await returns.GetSaleForReturnAsync(line.SaleId, 1);
        Assert.True(loaded.IsSuccess, loaded.Error);
        Assert.Contains(loaded.Value!.Lines, l => l.SaleItemId == line.Id);

        var reasons = await returns.ListReturnReasonsAsync();
        Assert.NotEmpty(reasons);

        var already = await db.SaleReturnItems.AsNoTracking()
            .Where(r => r.SaleItemId == line.Id && r.SaleReturn!.Status == SaleReturnStatus.Completed)
            .SumAsync(r => (decimal?)r.ReturnedQuantity) ?? 0m;
        var available = line.Quantity - already;
        if (available <= 0) return;

        var qty = Math.Min(1m, available);
        var result = await returns.CreateReturnAsync(new CreateSaleReturnRequest
        {
            SaleId = line.SaleId,
            RefundMode = RefundMode.Cash,
            ManagerOverrideUsed = true,
            ManagerOverrideReason = "Integration test",
            Lines =
            [
                new CreateSaleReturnLineRequest
                {
                    SaleItemId = line.Id,
                    ReturnQuantity = qty,
                    ReturnReasonId = reasons[0].Id,
                    IsSaleable = true,
                    SealIntact = true,
                    ExpiryValid = true
                }
            ]
        }, branchId: 1, userName: "test");

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Value!.ReturnNumber));
        Assert.True(result.Value.RefundAmount > 0);
    }
}
