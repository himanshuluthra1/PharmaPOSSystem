using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Shared.Constants;
using Renci.SshNet;

namespace PharmaPOS.WPF.Services;

public interface IPosUpdateService
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
    Task<bool> IsVendorConsoleAsync(CancellationToken ct = default);
    Task HeartbeatAsync(CancellationToken ct = default);
    Task<List<PosShopRow>> ListShopsAsync(CancellationToken ct = default);
    Task<List<PosReleaseRow>> ListReleasesAsync(CancellationToken ct = default);
    Task<PosPublishResult> PublishReleaseAsync(PosPublishRequest request, CancellationToken ct = default);
    Task AssignUpdateAsync(IReadOnlyList<string> storeIds, string version, CancellationToken ct = default);
    Task<PosPendingUpdate?> GetPendingUpdateAsync(CancellationToken ct = default);
    Task MarkAssignmentAsync(int assignmentId, string status, string? error = null, CancellationToken ct = default);
    Task<string> DownloadPackageAsync(PosPendingUpdate update, IProgress<double>? progress, CancellationToken ct = default);
}

/// <summary>
/// Vendor assigns per-shop POS updates on VPS MySQL; shops poll and download the package.
/// </summary>
public sealed class PosUpdateService : IPosUpdateService
{
    private readonly IConfiguration _config;
    private readonly IMySqlSyncSettingsService _mysqlSettings;
    private readonly IStoreIdentityService _identity;
    private readonly IBillShareSettingsService _billShare;
    private readonly IHttpClientFactory _httpFactory;

    public PosUpdateService(
        IConfiguration config,
        IMySqlSyncSettingsService mysqlSettings,
        IStoreIdentityService identity,
        IBillShareSettingsService billShare,
        IHttpClientFactory httpFactory)
    {
        _config = config;
        _mysqlSettings = mysqlSettings;
        _identity = identity;
        _billShare = billShare;
        _httpFactory = httpFactory;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await ExecAsync(conn, """
            CREATE TABLE IF NOT EXISTS pos_releases (
              version VARCHAR(20) NOT NULL,
              file_name VARCHAR(200) NOT NULL,
              package_url VARCHAR(500) NOT NULL,
              sha256 CHAR(64) NULL,
              file_size_bytes BIGINT NULL,
              notes VARCHAR(500) NULL,
              created_at_utc DATETIME(6) NOT NULL,
              PRIMARY KEY (version)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """, ct);

        await ExecAsync(conn, """
            CREATE TABLE IF NOT EXISTS pos_update_assignments (
              id INT NOT NULL AUTO_INCREMENT,
              store_id VARCHAR(40) NOT NULL,
              version VARCHAR(20) NOT NULL,
              status VARCHAR(20) NOT NULL DEFAULT 'pending',
              assigned_at_utc DATETIME(6) NOT NULL,
              started_at_utc DATETIME(6) NULL,
              completed_at_utc DATETIME(6) NULL,
              error_message VARCHAR(1000) NULL,
              PRIMARY KEY (id),
              KEY ix_pos_update_store_status (store_id, status),
              KEY ix_pos_update_store_version (store_id, version)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """, ct);

        await TryExecAsync(conn, "ALTER TABLE store_activations ADD COLUMN is_vendor TINYINT(1) NOT NULL DEFAULT 0", ct);
        await TryExecAsync(conn, "ALTER TABLE store_activations ADD COLUMN app_version VARCHAR(20) NULL", ct);
        await TryExecAsync(conn, "ALTER TABLE store_activations ADD COLUMN last_seen_utc DATETIME(6) NULL", ct);
        await TryExecAsync(conn,
            "UPDATE store_activations SET is_vendor = 1 WHERE store_code = 'STORE-001' AND is_vendor = 0", ct);
    }

    public async Task<bool> IsVendorConsoleAsync(CancellationToken ct = default)
    {
        if (!_identity.IsConfigured || string.IsNullOrWhiteSpace(_identity.StoreId))
            return false;

        try
        {
            await EnsureSchemaAsync(ct);
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);
            await using var cmd = new MySqlCommand(
                "SELECT is_vendor FROM store_activations WHERE store_id = @id LIMIT 1", conn);
            cmd.Parameters.AddWithValue("@id", _identity.StoreId);
            var value = await cmd.ExecuteScalarAsync(ct);
            if (value is not null && value is not DBNull && Convert.ToInt32(value) != 0)
                return true;
        }
        catch
        {
            // Fall through to store-code fallback.
        }

        return string.Equals(_identity.StoreCode, "STORE-001", StringComparison.OrdinalIgnoreCase);
    }

    public async Task HeartbeatAsync(CancellationToken ct = default)
    {
        if (!_identity.IsConfigured || string.IsNullOrWhiteSpace(_identity.StoreId))
            return;

        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);
            await using var cmd = new MySqlCommand(
                """
                UPDATE store_activations
                SET app_version = @ver, last_seen_utc = UTC_TIMESTAMP(6)
                WHERE store_id = @id
                """, conn);
            cmd.Parameters.AddWithValue("@ver", AppConstants.ApplicationVersion);
            cmd.Parameters.AddWithValue("@id", _identity.StoreId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch
        {
            // Heartbeat is best-effort.
        }
    }

    public async Task<List<PosShopRow>> ListShopsAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using var cmd = new MySqlCommand(
            """
            SELECT a.store_id, a.store_code, a.machine_name, a.is_approved, a.is_vendor,
                   a.app_version, a.last_seen_utc,
                   p.version AS pending_version, p.status AS assignment_status
            FROM store_activations a
            LEFT JOIN (
                SELECT x.store_id, x.version, x.status
                FROM pos_update_assignments x
                INNER JOIN (
                    SELECT store_id, MAX(id) AS max_id
                    FROM pos_update_assignments
                    GROUP BY store_id
                ) last ON last.max_id = x.id
            ) p ON p.store_id = a.store_id
            ORDER BY a.store_code
            """, conn);

        var list = new List<PosShopRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PosShopRow
            {
                StoreId = reader.GetString("store_id"),
                StoreCode = reader.GetString("store_code"),
                MachineName = reader.IsDBNull(reader.GetOrdinal("machine_name")) ? null : reader.GetString("machine_name"),
                IsApproved = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("is_approved"))) != 0,
                IsVendor = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("is_vendor"))) != 0,
                AppVersion = reader.IsDBNull(reader.GetOrdinal("app_version")) ? null : reader.GetString("app_version"),
                LastSeenUtc = reader.IsDBNull(reader.GetOrdinal("last_seen_utc")) ? null : reader.GetDateTime("last_seen_utc"),
                PendingVersion = reader.IsDBNull(reader.GetOrdinal("pending_version")) ? null : reader.GetString("pending_version"),
                AssignmentStatus = reader.IsDBNull(reader.GetOrdinal("assignment_status")) ? null : reader.GetString("assignment_status")
            });
        }

        return list;
    }

    public async Task<List<PosReleaseRow>> ListReleasesAsync(CancellationToken ct = default)
    {
        await EnsureSchemaAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            "SELECT version, file_name, package_url, sha256, file_size_bytes, notes, created_at_utc FROM pos_releases ORDER BY created_at_utc DESC",
            conn);

        var list = new List<PosReleaseRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PosReleaseRow
            {
                Version = reader.GetString("version"),
                FileName = reader.GetString("file_name"),
                PackageUrl = reader.GetString("package_url"),
                Sha256 = reader.IsDBNull(reader.GetOrdinal("sha256")) ? null : reader.GetString("sha256"),
                FileSizeBytes = reader.IsDBNull(reader.GetOrdinal("file_size_bytes")) ? null : reader.GetInt64("file_size_bytes"),
                Notes = reader.IsDBNull(reader.GetOrdinal("notes")) ? null : reader.GetString("notes"),
                CreatedAtUtc = reader.GetDateTime("created_at_utc")
            });
        }

        return list;
    }

    public async Task<PosPublishResult> PublishReleaseAsync(PosPublishRequest request, CancellationToken ct = default)
    {
        var version = (request.Version ?? string.Empty).Trim();
        if (!Version.TryParse(version, out _))
            return PosPublishResult.Fail("Version must look like 1.2.0");
        if (string.IsNullOrWhiteSpace(request.LocalFilePath) || !File.Exists(request.LocalFilePath))
            return PosPublishResult.Fail("Select a PharmaPOS-Setup-*.exe file.");

        var fileName = Path.GetFileName(request.LocalFilePath);
        long size;
        string sha;
        try
        {
            size = new FileInfo(request.LocalFilePath).Length;
            sha = await HashFileAsync(request.LocalFilePath, ct);
        }
        catch (Exception ex)
        {
            return PosPublishResult.Fail($"Could not read the installer: {ex.Message}");
        }

        string url;
        try
        {
            url = await UploadInstallerAsync(request.LocalFilePath, fileName, ct);
        }
        catch (Exception ex)
        {
            return PosPublishResult.Fail(
                "Could not upload the installer to the VPS.\n\n" +
                "Set SFTP details under Settings → Preferences (same as bill upload),\n" +
                "and create folder /var/www/html/updates on the server.\n\n" +
                ex.Message);
        }

        await EnsureSchemaAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            """
            INSERT INTO pos_releases (version, file_name, package_url, sha256, file_size_bytes, notes, created_at_utc)
            VALUES (@ver, @file, @url, @sha, @size, @notes, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              file_name = VALUES(file_name),
              package_url = VALUES(package_url),
              sha256 = VALUES(sha256),
              file_size_bytes = VALUES(file_size_bytes),
              notes = VALUES(notes)
            """, conn);
        cmd.Parameters.AddWithValue("@ver", version);
        cmd.Parameters.AddWithValue("@file", fileName);
        cmd.Parameters.AddWithValue("@url", url);
        cmd.Parameters.AddWithValue("@sha", sha);
        cmd.Parameters.AddWithValue("@size", size);
        cmd.Parameters.AddWithValue("@notes", (object?)request.Notes ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);

        return PosPublishResult.Ok($"Published {version} ({fileName}).", url);
    }

    public async Task AssignUpdateAsync(IReadOnlyList<string> storeIds, string version, CancellationToken ct = default)
    {
        if (storeIds.Count == 0)
            throw new InvalidOperationException("Select at least one shop.");
        version = version.Trim();
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("Select a published version.");

        await EnsureSchemaAsync(ct);
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await using (var exists = new MySqlCommand("SELECT COUNT(*) FROM pos_releases WHERE version = @ver", conn))
        {
            exists.Parameters.AddWithValue("@ver", version);
            var count = Convert.ToInt32(await exists.ExecuteScalarAsync(ct));
            if (count == 0)
                throw new InvalidOperationException($"Version {version} is not published yet.");
        }

        foreach (var storeId in storeIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await using var cancel = new MySqlCommand(
                """
                UPDATE pos_update_assignments
                SET status = 'cancelled', error_message = 'Superseded', completed_at_utc = UTC_TIMESTAMP(6)
                WHERE store_id = @id AND status IN ('pending','downloading','failed')
                """, conn);
            cancel.Parameters.AddWithValue("@id", storeId);
            await cancel.ExecuteNonQueryAsync(ct);

            await using var insert = new MySqlCommand(
                """
                INSERT INTO pos_update_assignments (store_id, version, status, assigned_at_utc)
                VALUES (@id, @ver, 'pending', UTC_TIMESTAMP(6))
                """, conn);
            insert.Parameters.AddWithValue("@id", storeId);
            insert.Parameters.AddWithValue("@ver", version);
            await insert.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<PosPendingUpdate?> GetPendingUpdateAsync(CancellationToken ct = default)
    {
        if (!_identity.IsConfigured || string.IsNullOrWhiteSpace(_identity.StoreId))
            return null;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            """
            SELECT a.id, a.version, r.package_url, r.file_name, r.sha256, r.file_size_bytes
            FROM pos_update_assignments a
            INNER JOIN pos_releases r ON r.version = a.version
            WHERE a.store_id = @id
              AND a.status IN ('pending','downloading','failed')
            ORDER BY a.id DESC
            LIMIT 1
            """, conn);
        cmd.Parameters.AddWithValue("@id", _identity.StoreId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new PosPendingUpdate
        {
            AssignmentId = reader.GetInt32("id"),
            Version = reader.GetString("version"),
            PackageUrl = reader.GetString("package_url"),
            FileName = reader.GetString("file_name"),
            Sha256 = reader.IsDBNull(reader.GetOrdinal("sha256")) ? null : reader.GetString("sha256"),
            FileSizeBytes = reader.IsDBNull(reader.GetOrdinal("file_size_bytes")) ? null : reader.GetInt64("file_size_bytes")
        };
    }

    public async Task MarkAssignmentAsync(int assignmentId, string status, string? error = null, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new MySqlCommand(
            """
            UPDATE pos_update_assignments
            SET status = @status,
                error_message = @err,
                started_at_utc = CASE
                    WHEN @status IN ('downloading','installing') AND started_at_utc IS NULL THEN UTC_TIMESTAMP(6)
                    ELSE started_at_utc END,
                completed_at_utc = CASE
                    WHEN @status IN ('installed','cancelled') THEN UTC_TIMESTAMP(6)
                    ELSE completed_at_utc END
            WHERE id = @id
            """, conn);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@err", (object?)Truncate(error, 1000) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", assignmentId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task<string> UploadInstallerAsync(string localPath, string fileName, CancellationToken ct)
    {
        var cfg = _billShare.Current;
        var host = FirstNonEmpty(cfg.SftpHost, _config["ReportingSync:Host"]) ?? "50.6.251.47";
        var user = FirstNonEmpty(cfg.SftpUsername, "pharmapos") ?? "pharmapos";
        var password = cfg.SftpPassword;
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("SFTP password is not set in Settings → Preferences.");

        var port = cfg.SftpPort > 0 ? cfg.SftpPort : 22;
        var remoteDir = FirstNonEmpty(
            _config["App:UpdatesSftpDirectory"],
            DeriveUpdatesDirectory(cfg.SftpRemoteDirectory),
            "/var/www/html/updates")!;

        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var client = new SftpClient(host, port, user, password);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(45);
            client.Connect();
            try
            {
                EnsureRemoteDirectory(client, remoteDir);
                var remotePath = $"{remoteDir.TrimEnd('/')}/{fileName}".Replace('\\', '/');
                using (var fs = File.OpenRead(localPath))
                    client.UploadFile(fs, remotePath, canOverride: true);
            }
            finally
            {
                client.Disconnect();
            }

            var publicBase = FirstNonEmpty(
                _config["App:UpdatesPublicBaseUrl"],
                DeriveUpdatesPublicUrl(cfg.PublicBaseUrl),
                "http://50.6.251.47/updates/")!;
            return $"{publicBase.TrimEnd('/')}/{fileName}";
        }, ct);
    }

    private static string DeriveUpdatesDirectory(string billsDir)
    {
        if (string.IsNullOrWhiteSpace(billsDir)) return "/var/www/html/updates";
        var d = billsDir.Trim().Replace('\\', '/').TrimEnd('/');
        if (d.EndsWith("/bills", StringComparison.OrdinalIgnoreCase))
            return d[..^"/bills".Length] + "/updates";
        if (d.EndsWith("bills", StringComparison.OrdinalIgnoreCase))
            return d[..^"bills".Length] + "updates";
        return "/var/www/html/updates";
    }

    private static string DeriveUpdatesPublicUrl(string billsUrl)
    {
        if (string.IsNullOrWhiteSpace(billsUrl)) return "http://50.6.251.47/updates/";
        var u = billsUrl.Trim().TrimEnd('/');
        if (u.EndsWith("/bills", StringComparison.OrdinalIgnoreCase))
            return u[..^"/bills".Length] + "/updates/";
        return "http://50.6.251.47/updates/";
    }

    private static void EnsureRemoteDirectory(SftpClient client, string directory)
    {
        var parts = directory.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = directory.StartsWith('/') ? "/" : "";
        foreach (var part in parts)
        {
            current = current == "/" ? "/" + part : $"{current.TrimEnd('/')}/{part}";
            if (!client.Exists(current))
                client.CreateDirectory(current);
        }
    }

    public async Task<string> DownloadPackageAsync(PosPendingUpdate update, IProgress<double>? progress, CancellationToken ct)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS", "Updates");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, update.FileName);

        var client = _httpFactory.CreateClient("PosUpdates");
        using var response = await client.GetAsync(update.PackageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? update.FileSizeBytes ?? 0;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(dest);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await input.ReadAsync(buffer, ct)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0)
                progress?.Report(read / (double)total);
        }

        await output.FlushAsync(ct);
        output.Close();

        if (!string.IsNullOrWhiteSpace(update.Sha256))
        {
            var actual = await HashFileAsync(dest, ct);
            if (!string.Equals(actual, update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(dest); } catch { /* ignore */ }
                throw new InvalidOperationException("Downloaded installer failed the checksum. Try again.");
            }
        }

        return dest;
    }

    private MySqlConnection CreateConnection()
    {
        _mysqlSettings.Load();
        var local = _mysqlSettings.Current;
        var host = FirstNonEmpty(local.Host, _config["ReportingSync:Host"]) ?? "50.6.251.47";
        var port = local.Port > 0
            ? local.Port
            : int.TryParse(_config["ReportingSync:Port"], out var p) ? p : 3306;
        var database = FirstNonEmpty(local.Database, _config["ReportingSync:Database"]) ?? "pharmapos_reporting";
        var user = FirstNonEmpty(local.Username, _config["ReportingSync:Username"]) ?? "pharmapos";
        var password = FirstNonEmpty(local.Password, _config["ReportingSync:Password"])
                       ?? "PharmaPos@Report2026";

        return new MySqlConnection(new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            Database = database,
            UserID = user,
            Password = password,
            SslMode = MySqlSslMode.Preferred,
            ConnectionTimeout = 20,
            AllowUserVariables = true
        }.ConnectionString);
    }

    private static async Task ExecAsync(MySqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task TryExecAsync(MySqlConnection conn, string sql, CancellationToken ct)
    {
        try
        {
            await ExecAsync(conn, sql, ct);
        }
        catch (MySqlException)
        {
            // Duplicate column / insufficient rights — ignore.
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        return null;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}
