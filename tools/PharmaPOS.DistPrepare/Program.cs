using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Infrastructure.Security;
using PharmaPOS.Persistence;
using PharmaPOS.Persistence.Context;
using PharmaPOS.Persistence.Seed;
using PharmaPOS.Shared.Constants;

namespace PharmaPOS.DistPrepare;

/// <summary>
/// Builds a customer-distribution database that contains system seed data plus
/// catalogue masters (medicines/salts/manufacturers/categories/suppliers/mappings)
/// and excludes shop-specific data (customers, doctors, sales, purchases, stock).
/// </summary>
internal static class Program
{
    private const string DistDbName = "PharmaPosDb_Dist";

    private static async Task<int> Main(string[] args)
    {
        var sourceCs = args.Length > 0
            ? args[0]
            : @"Server=(localdb)\MSSQLLocalDB;Database=PharmaPosDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

        var outDir = args.Length > 1
            ? Path.GetFullPath(args[1])
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "dist"));

        Directory.CreateDirectory(outDir);
        var bakPath = Path.Combine(outDir, "PharmaPosDb_Master.bak");
        var metaPath = Path.Combine(outDir, "PharmaPosDb_Master.meta.json");

        var builder = new SqlConnectionStringBuilder(sourceCs)
        {
            InitialCatalog = "master"
        };
        var masterCs = builder.ConnectionString;
        builder.InitialCatalog = DistDbName;
        var distCs = builder.ConnectionString;

        Console.WriteLine("PharmaPOS distribution database prepare");
        Console.WriteLine($"Source : {GetDbName(sourceCs)}");
        Console.WriteLine($"Target : {DistDbName}");
        Console.WriteLine($"Output : {bakPath}");
        Console.WriteLine();

        await RecreateDatabaseAsync(masterCs, DistDbName);

        // Migrate + system seed (no demo medicines/customers/batches).
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{AppConstants.Config.ConnectionStringName}"] = distCs,
                ["App:SeedSampleData"] = "false"
            })
            .Build();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        services.AddPersistence(config);
        await using var sp = services.BuildServiceProvider();
        var seeder = sp.GetRequiredService<DbSeeder>();
        Console.WriteLine("Applying migrations and system seed...");
        await seeder.SeedAsync();

        Console.WriteLine("Copying master catalogue from source...");
        await using (var source = new SqlConnection(sourceCs))
        await using (var dest = new SqlConnection(distCs))
        {
            await source.OpenAsync();
            await dest.OpenAsync();

            var hoBranchId = await ScalarIntAsync(dest,
                "SELECT TOP 1 Id FROM Branches WHERE IsHeadOffice = 1 ORDER BY Id");

            await CopyTableAsync(source, dest, "MedicineCategories",
                "SELECT * FROM MedicineCategories WHERE IsDeleted = 0");
            await CopyTableAsync(source, dest, "Manufacturers",
                "SELECT * FROM Manufacturers WHERE IsDeleted = 0");
            await CopyTableAsync(source, dest, "Medicines",
                "SELECT * FROM Medicines WHERE IsDeleted = 0");
            await CopyTableAsync(source, dest, "MedicineMedWinMappings",
                "SELECT * FROM MedicineMedWinMappings WHERE IsDeleted = 0");

            // Suppliers as master list; wipe shop balances and map to HO branch.
            await CopyTableAsync(source, dest, "Suppliers",
                $"""
                 SELECT Id, Name, NameSearchKey, GstNumber, DrugLicenseNumber, Address, City, State, Pincode,
                        ContactPerson, Phone, PhoneSearchKey, Email,
                        PaymentTermsDays,
                        CAST(0 AS decimal(18,2)) AS OpeningBalance,
                        CAST(0 AS decimal(18,2)) AS OutstandingBalance,
                        Status, {hoBranchId} AS BranchId,
                        CreatedAtUtc, CreatedBy, ModifiedAtUtc, ModifiedBy,
                        IsDeleted, DeletedAtUtc, DeletedBy
                 FROM Suppliers
                 WHERE IsDeleted = 0
                 """);
        }

        Console.WriteLine("Verifying distribution database contents...");
        await using (var dest = new SqlConnection(distCs))
        {
            await dest.OpenAsync();
            foreach (var table in new[]
                     {
                         "Medicines", "Manufacturers", "MedicineCategories", "Suppliers",
                         "MedicineMedWinMappings", "Customers", "Doctors", "MedicineBatches", "Sales", "Purchases"
                     })
            {
                var count = await ScalarIntAsync(dest, $"SELECT COUNT(*) FROM {table}");
                Console.WriteLine($"  {table,-28} {count,8:N0}");
            }

            // Sanity: no shop-specific rows.
            var customers = await ScalarIntAsync(dest, "SELECT COUNT(*) FROM Customers");
            var doctors = await ScalarIntAsync(dest, "SELECT COUNT(*) FROM Doctors");
            var batches = await ScalarIntAsync(dest, "SELECT COUNT(*) FROM MedicineBatches");
            var sales = await ScalarIntAsync(dest, "SELECT COUNT(*) FROM Sales");
            var purchases = await ScalarIntAsync(dest, "SELECT COUNT(*) FROM Purchases");
            if (customers + doctors + batches + sales + purchases > 0)
                throw new InvalidOperationException("Distribution DB still contains shop-specific rows.");
        }

        var logical = await GetLogicalFilesAsync(distCs);
        Console.WriteLine("Creating compressed backup...");
        if (File.Exists(bakPath)) File.Delete(bakPath);
        await using (var master = new SqlConnection(masterCs))
        {
            await master.OpenAsync();
            await using var cmd = master.CreateCommand();
            cmd.CommandTimeout = 0;
            cmd.CommandText = $"""
                BACKUP DATABASE [{DistDbName}]
                TO DISK = N'{bakPath.Replace("'", "''")}'
                WITH FORMAT, INIT, NAME = N'PharmaPOS master distribution';
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        var meta = $$"""
            {
              "databaseName": "PharmaPosDb",
              "backupFile": "PharmaPosDb_Master.bak",
              "createdUtc": "{{DateTime.UtcNow:O}}",
              "logicalDataFile": "{{logical.Data}}",
              "logicalLogFile": "{{logical.Log}}",
              "includes": [
                "Medicines (catalogue / salts via GenericName)",
                "Manufacturers",
                "MedicineCategories",
                "Suppliers (outstanding reset to 0)",
                "MedicineMedWinMappings",
                "Roles, Permissions, admin user",
                "Chart of accounts, Return reasons, Company profile"
              ],
              "excludes": [
                "Customers / patients",
                "Doctors",
                "Employees",
                "MedicineBatches / stock",
                "Sales, Purchases, Returns",
                "Journal entries, stock movements"
              ],
              "defaultLogin": { "username": "admin", "password": "Admin@123" }
            }
            """;
        await File.WriteAllTextAsync(metaPath, meta);

        var sizeMb = new FileInfo(bakPath).Length / (1024.0 * 1024.0);
        Console.WriteLine();
        Console.WriteLine($"Done. Backup: {bakPath} ({sizeMb:N1} MB)");
        Console.WriteLine($"Meta   : {metaPath}");
        return 0;
    }

    private static string GetDbName(string cs)
        => new SqlConnectionStringBuilder(cs).InitialCatalog;

    private static async Task RecreateDatabaseAsync(string masterCs, string dbName)
    {
        await using var conn = new SqlConnection(masterCs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = $"""
            IF DB_ID(N'{dbName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{dbName}];
            END
            CREATE DATABASE [{dbName}];
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CopyTableAsync(
        SqlConnection source, SqlConnection dest, string table, string selectSql)
    {
        Console.WriteLine($"  Copying {table}...");
        await using var readCmd = source.CreateCommand();
        readCmd.CommandText = selectSql;
        readCmd.CommandTimeout = 0;
        await using var reader = await readCmd.ExecuteReaderAsync();

        await using (var setIdentity = dest.CreateCommand())
        {
            setIdentity.CommandText = $"SET IDENTITY_INSERT [{table}] ON;";
            try { await setIdentity.ExecuteNonQueryAsync(); }
            catch { /* table may have no identity */ }
        }

        using var bulk = new SqlBulkCopy(dest, SqlBulkCopyOptions.KeepIdentity, null)
        {
            DestinationTableName = table,
            BulkCopyTimeout = 0,
            BatchSize = 5000
        };

        var destCols = await GetColumnsAsync(dest, table);
        var sourceCols = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var col in destCols.Where(sourceCols.Contains))
            bulk.ColumnMappings.Add(col, col);

        try
        {
            await bulk.WriteToServerAsync(reader);
        }
        finally
        {
            await using var unset = dest.CreateCommand();
            unset.CommandText = $"SET IDENTITY_INSERT [{table}] OFF;";
            try { await unset.ExecuteNonQueryAsync(); } catch { /* ignore */ }
        }

        // Reseed identity to MAX(Id).
        await using var reseed = dest.CreateCommand();
        reseed.CommandText = $"""
            IF EXISTS (SELECT 1 FROM sys.identity_columns WHERE object_id = OBJECT_ID(N'{table}'))
            BEGIN
                DECLARE @max int = ISNULL((SELECT MAX(Id) FROM [{table}]), 0);
                DBCC CHECKIDENT (N'{table}', RESEED, @max);
            END
            """;
        try { await reseed.ExecuteNonQueryAsync(); } catch { /* ignore */ }
    }

    private static async Task<List<string>> GetColumnsAsync(SqlConnection conn, string table)
    {
        var cols = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT c.name
            FROM sys.columns c
            WHERE c.object_id = OBJECT_ID(@table)
              AND c.is_computed = 0
            ORDER BY c.column_id
            """;
        cmd.Parameters.AddWithValue("@table", table);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            cols.Add(reader.GetString(0));
        return cols;
    }

    private static async Task<int> ScalarIntAsync(SqlConnection conn, string sql)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private static async Task<(string Data, string Log)> GetLogicalFilesAsync(string distCs)
    {
        await using var conn = new SqlConnection(distCs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT name, type_desc
            FROM sys.database_files
            ORDER BY type
            """;
        string? data = null, log = null;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            if (type.Contains("ROWS", StringComparison.OrdinalIgnoreCase)) data = name;
            if (type.Contains("LOG", StringComparison.OrdinalIgnoreCase)) log = name;
        }
        if (data is null || log is null)
            throw new InvalidOperationException("Could not resolve logical file names for restore metadata.");
        return (data, log);
    }
}
