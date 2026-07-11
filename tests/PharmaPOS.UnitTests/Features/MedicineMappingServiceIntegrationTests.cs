using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application;
using PharmaPOS.Application.Features.Masters;
using PharmaPOS.Infrastructure;
using PharmaPOS.Persistence;
using PharmaPOS.Persistence.Context;

namespace PharmaPOS.UnitTests.Features;

public class MedicineMappingServiceIntegrationTests
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
    public async Task SearchOneMgByBrandPrefix_ATEN_returns_narrow_set_not_all_A()
    {
        if (!CanConnectToLocalDb()) return;

        var services = new ServiceCollection();
        services.AddApplication();
        services.AddInfrastructure();
        services.AddPersistence(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var mapping = scope.ServiceProvider.GetRequiredService<IMedicineMappingService>();

        var atenResults = await mapping.SearchOneMgByBrandPrefixAsync("ATEN", null, includeMatched: false);
        var aResults = await mapping.SearchOneMgByBrandPrefixAsync("A", null, includeMatched: false);

        Assert.NotEmpty(atenResults);
        Assert.True(atenResults.Count < 200, $"Expected a narrow ATEN set, got {atenResults.Count}");
        Assert.All(atenResults, r => Assert.StartsWith("Aten", r.Name, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(aResults);
    }
}
