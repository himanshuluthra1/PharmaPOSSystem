using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Persistence.Context;
using PharmaPOS.Persistence.Repositories;
using PharmaPOS.Persistence.Seed;
using PharmaPOS.Shared.Constants;

namespace PharmaPOS.Persistence;

/// <summary>Registers the DbContext, repositories and unit of work.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(AppConstants.Config.ConnectionStringName)
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=PharmaPosDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

        // Transient DbContext so each unit of work / view model gets a fresh,
        // short-lived context - the natural grain for a single-user desktop app.
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.EnableRetryOnFailure()), ServiceLifetime.Transient);

        services.AddTransient<IUnitOfWork, UnitOfWork>();
        services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
        services.AddTransient<DbSeeder>();

        return services;
    }
}
