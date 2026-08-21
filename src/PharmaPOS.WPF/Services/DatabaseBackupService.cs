using System.IO;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PharmaPOS.Shared.Constants;

namespace PharmaPOS.WPF.Services;

public interface IDatabaseBackupService
{
    string SuggestFileName();
    string AutoBackupFolder { get; }
    Task<string> BackupToFileAsync(string destinationPath, CancellationToken ct = default);
    Task RestoreFromFileAsync(string backupPath, CancellationToken ct = default);
}

public sealed class DatabaseBackupService : IDatabaseBackupService
{
    private readonly IConfiguration _configuration;

    public DatabaseBackupService(IConfiguration configuration)
    {
        _configuration = configuration;
        AutoBackupFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS", "Backups", "Auto");
        Directory.CreateDirectory(AutoBackupFolder);
    }

    public string AutoBackupFolder { get; }

    public string SuggestFileName()
        => $"PharmaPOS-{DateTime.Now:yyyyMMdd-HHmmss}.bak";

    public async Task<string> BackupToFileAsync(string destinationPath, CancellationToken ct = default)
    {
        var cs = _configuration.GetConnectionString(AppConstants.Config.ConnectionStringName)
                 ?? throw new InvalidOperationException("Database connection is not configured.");
        var builder = new SqlConnectionStringBuilder(cs);
        var dbName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(dbName))
            throw new InvalidOperationException("Database name is missing from the connection string.");

        var fullPath = Path.GetFullPath(destinationPath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        if (File.Exists(fullPath))
            File.Delete(fullPath);

        var escaped = fullPath.Replace("'", "''");
        var sqlDb = dbName.Replace("]", "]]");

        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = $"""
            BACKUP DATABASE [{sqlDb}]
            TO DISK = N'{escaped}'
            WITH COPY_ONLY, INIT, NAME = N'PharmaPOS shop backup';
            """;
        await cmd.ExecuteNonQueryAsync(ct);

        if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
            throw new InvalidOperationException("Backup file was not created.");

        return fullPath;
    }

    public async Task RestoreFromFileAsync(string backupPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
            throw new FileNotFoundException("Backup file was not found.", backupPath);

        var cs = _configuration.GetConnectionString(AppConstants.Config.ConnectionStringName)
                 ?? throw new InvalidOperationException("Database connection is not configured.");
        var builder = new SqlConnectionStringBuilder(cs);
        var dbName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(dbName))
            throw new InvalidOperationException("Database name is missing from the connection string.");

        var fullPath = Path.GetFullPath(backupPath);
        builder.InitialCatalog = "master";
        builder.Pooling = false;

        SqlConnection.ClearAllPools();

        await using var conn = new SqlConnection(builder.ConnectionString);
        await conn.OpenAsync(ct);

        var (dataLogical, logLogical) = await ReadLogicalNamesAsync(conn, fullPath, ct);
        var (dataPhysical, logPhysical) = await ReadPhysicalPathsAsync(conn, dbName, ct);

        var escapedBak = Escape(fullPath);
        var sqlDb = dbName.Replace("]", "]]");
        var exists = await DatabaseExistsAsync(conn, dbName, ct);

        try
        {
            if (exists)
            {
                await ExecAsync(conn, $"""
                    ALTER DATABASE [{sqlDb}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    """, ct);
            }

            await ExecAsync(conn, $"""
                RESTORE DATABASE [{sqlDb}]
                FROM DISK = N'{escapedBak}'
                WITH MOVE N'{Escape(dataLogical)}' TO N'{Escape(dataPhysical)}',
                     MOVE N'{Escape(logLogical)}' TO N'{Escape(logPhysical)}',
                     REPLACE, RECOVERY;
                """, ct);
        }
        catch
        {
            try
            {
                await ExecAsync(conn, $"IF DB_ID(N'{Escape(dbName)}') IS NOT NULL ALTER DATABASE [{sqlDb}] SET MULTI_USER;", ct);
            }
            catch
            {
                // Restore failed; leave SQL error as the outer exception.
            }

            throw;
        }

        await ExecAsync(conn, $"ALTER DATABASE [{sqlDb}] SET MULTI_USER;", ct);
        SqlConnection.ClearAllPools();
    }

    private static async Task ExecAsync(SqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = sql;
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

    private static async Task<(string Data, string Log)> ReadLogicalNamesAsync(
        SqlConnection conn, string bakPath, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 0;
        cmd.CommandText = $"RESTORE FILELISTONLY FROM DISK = N'{Escape(bakPath)}'";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        string? data = null, log = null;
        while (await reader.ReadAsync(ct))
        {
            var logical = reader.GetString(0);
            var type = reader.GetString(2);
            if (type.StartsWith("D", StringComparison.OrdinalIgnoreCase) && data is null)
                data = logical;
            if (type.StartsWith("L", StringComparison.OrdinalIgnoreCase) && log is null)
                log = logical;
        }

        if (string.IsNullOrWhiteSpace(data) || string.IsNullOrWhiteSpace(log))
            throw new InvalidOperationException("This file is not a valid SQL Server backup.");

        return (data, log);
    }

    private static async Task<(string Data, string Log)> ReadPhysicalPathsAsync(
        SqlConnection conn, string dbName, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT mf.type_desc, mf.physical_name
            FROM sys.master_files mf
            INNER JOIN sys.databases d ON d.database_id = mf.database_id
            WHERE d.name = @name
            """;
        cmd.Parameters.AddWithValue("@name", dbName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        string? data = null, log = null;
        while (await reader.ReadAsync(ct))
        {
            var type = reader.GetString(0);
            var path = reader.GetString(1);
            if (type.Contains("ROWS", StringComparison.OrdinalIgnoreCase) && data is null)
                data = path;
            if (type.Contains("LOG", StringComparison.OrdinalIgnoreCase) && log is null)
                log = path;
        }

        if (!string.IsNullOrWhiteSpace(data) && !string.IsNullOrWhiteSpace(log))
            return (data, log);

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS", "Data");
        Directory.CreateDirectory(dataDir);
        return (
            Path.Combine(dataDir, $"{dbName}.mdf"),
            Path.Combine(dataDir, $"{dbName}_log.ldf"));
    }

    private static string Escape(string value) => value.Replace("'", "''");
}
