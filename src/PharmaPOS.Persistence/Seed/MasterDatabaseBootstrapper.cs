using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PharmaPOS.Shared.Constants;
using System.Text.Json;

namespace PharmaPOS.Persistence.Seed;

/// <summary>
/// On first launch of a customer install, restores the master-data backup shipped
/// with the installer into LocalDB when the target database does not yet exist.
/// </summary>
public static class MasterDatabaseBootstrapper
{
    public static async Task EnsureRestoredAsync(IConfiguration configuration, CancellationToken ct = default)
    {
        var enabled = configuration.GetValue("App:RestoreMasterBackupOnFirstRun", true);
        if (!enabled) return;

        var connectionString = configuration.GetConnectionString(AppConstants.Config.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var bakFileName = configuration["App:MasterBackupFileName"] ?? "PharmaPosDb_Master.bak";
        var bakPath = ResolveExistingPath(
            configuration["App:MasterBackupPath"],
            Path.Combine(AppContext.BaseDirectory, "Data", bakFileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaPOS", "Data", bakFileName));

        if (bakPath is null) return;

        var builder = new SqlConnectionStringBuilder(connectionString);
        var dbName = string.IsNullOrWhiteSpace(builder.InitialCatalog) ? "PharmaPosDb" : builder.InitialCatalog;
        builder.InitialCatalog = "master";

        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);

        if (await DatabaseExistsAsync(conn, dbName, ct))
            return;

        var meta = await ReadMetaAsync(bakPath, ct);
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS", "Data");
        Directory.CreateDirectory(dataDir);

        var mdf = Path.Combine(dataDir, $"{dbName}.mdf");
        var ldf = Path.Combine(dataDir, $"{dbName}_log.ldf");
        var logicalData = meta?.LogicalDataFile ?? $"{dbName}_Dist";
        var logicalLog = meta?.LogicalLogFile ?? $"{dbName}_Dist_log";

        // Prefer meta names; fall back to FILELISTONLY from the backup itself.
        var (dataLogical, logLogical) = await ResolveLogicalNamesAsync(conn, bakPath, logicalData, logicalLog, ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = $"""
            RESTORE DATABASE [{dbName}]
            FROM DISK = N'{Escape(bakPath)}'
            WITH MOVE N'{Escape(dataLogical)}' TO N'{Escape(mdf)}',
                 MOVE N'{Escape(logLogical)}' TO N'{Escape(ldf)}',
                 REPLACE, RECOVERY;
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<bool> DatabaseExistsAsync(SqlConnection conn, string dbName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END";
        cmd.Parameters.AddWithValue("@name", dbName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt32(result) == 1;
    }

    private static string? ResolveExistingPath(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (!string.IsNullOrWhiteSpace(c) && File.Exists(c))
                return Path.GetFullPath(c);
        }
        return null;
    }

    private static async Task<MasterBackupMeta?> ReadMetaAsync(string bakPath, CancellationToken ct)
    {
        var metaPath = Path.ChangeExtension(bakPath, ".meta.json");
        if (!File.Exists(metaPath))
            metaPath = Path.Combine(Path.GetDirectoryName(bakPath)!, "PharmaPosDb_Master.meta.json");
        if (!File.Exists(metaPath)) return null;

        await using var stream = File.OpenRead(metaPath);
        return await JsonSerializer.DeserializeAsync<MasterBackupMeta>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }, ct);
    }

    private static async Task<(string Data, string Log)> ResolveLogicalNamesAsync(
        SqlConnection conn, string bakPath, string preferredData, string preferredLog, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = $"RESTORE FILELISTONLY FROM DISK = N'{Escape(bakPath)}'";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        string? data = null, log = null;
        while (await reader.ReadAsync(ct))
        {
            var logical = reader.GetString(0);
            var type = reader.GetString(2); // FileType: D / L
            if (type.StartsWith("D", StringComparison.OrdinalIgnoreCase)) data = logical;
            if (type.StartsWith("L", StringComparison.OrdinalIgnoreCase)) log = logical;
        }

        return (data ?? preferredData, log ?? preferredLog);
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private sealed class MasterBackupMeta
    {
        public string? LogicalDataFile { get; set; }
        public string? LogicalLogFile { get; set; }
    }
}
