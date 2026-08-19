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
}
