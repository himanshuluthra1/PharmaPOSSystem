using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using PharmaPOS.Application.Features.ReportingSync;

namespace PharmaPOS.WPF.Services;

public sealed class StoreIdentitySettings
{
    /// <summary>Auto-generated unique id used for all VPS mapping.</summary>
    public string StoreId { get; set; } = string.Empty;

    /// <summary>Owner-chosen display code (not the VPS tenant key).</summary>
    public string StoreCode { get; set; } = string.Empty;

    public string MachineId { get; set; } = string.Empty;
    public DateTime? ConfiguredAtUtc { get; set; }
}

public sealed class StoreIdentityService : IStoreIdentityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly string _legacyPlainPath;
    private readonly string _logPath;
    private readonly IConfiguration _config;
    private readonly IMySqlSyncSettingsService _mysqlSettings;
    private readonly string _machineId;
    private StoreIdentitySettings _current = new();

    public StoreIdentityService(IConfiguration config, IMySqlSyncSettingsService mysqlSettings)
    {
        _config = config;
        _mysqlSettings = mysqlSettings;
        _machineId = MachineFingerprint.GetMachineId();
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "store-identity.dat");
        _legacyPlainPath = Path.Combine(dir, "store-identity.json");
        _logPath = Path.Combine(dir, "activation.log");
        Load();
        InvalidateIfMachineMismatch();
    }

    public string MachineId => _machineId;

    public bool IsConfigured
    {
        get
        {
            lock (_gate)
                return IsConfiguredUnlocked();
        }
    }

    public string? StoreId
    {
        get
        {
            lock (_gate)
            {
                if (!IsConfiguredUnlocked()) return null;
                return _current.StoreId;
            }
        }
    }

    public string? StoreCode
    {
        get
        {
            lock (_gate)
            {
                if (!IsConfiguredUnlocked()) return null;
                return _current.StoreCode;
            }
        }
    }

    public void Load()
    {
        lock (_gate)
        {
            if (ProtectedStoreIdentityFile.TryRead(_filePath, out var protectedSettings)
                && protectedSettings is not null)
            {
                _current = Normalize(protectedSettings);
                Log($"Load(encrypted): id={_current.StoreId} code={_current.StoreCode} machine={_current.MachineId} ok={IsConfiguredUnlocked()}");
                return;
            }

            if (TryLoadLegacyPlain(out var legacy) && legacy is not null)
            {
                _current = Normalize(legacy);
                if (IsConfiguredUnlocked())
                {
                    try
                    {
                        ProtectedStoreIdentityFile.Write(_filePath, _current);
                        File.Delete(_legacyPlainPath);
                        Log("Migrated plaintext identity to encrypted store-identity.dat");
                    }
                    catch (Exception ex)
                    {
                        Log($"Migrate encrypt failed: {ex.Message}");
                    }
                }
                else
                {
                    // Legacy file may only have StoreCode (pre-StoreId). Keep for restore-by-machine.
                    Log($"Legacy identity incomplete (needs StoreId). code={_current.StoreCode} id={_current.StoreId}");
                }

                return;
            }

            _current = new StoreIdentitySettings();
            Log("Load: no identity file.");
        }
    }

    private static StoreIdentitySettings Normalize(StoreIdentitySettings s) => new()
    {
        StoreId = string.IsNullOrWhiteSpace(s.StoreId) ? string.Empty : s.StoreId.Trim().ToUpperInvariant(),
        StoreCode = string.IsNullOrWhiteSpace(s.StoreCode) ? string.Empty : s.StoreCode.Trim(),
        MachineId = s.MachineId?.Trim() ?? string.Empty,
        ConfiguredAtUtc = s.ConfiguredAtUtc
    };

    private bool TryLoadLegacyPlain(out StoreIdentitySettings? settings)
    {
        settings = null;
        if (!File.Exists(_legacyPlainPath))
            return false;
        try
        {
            var json = File.ReadAllText(_legacyPlainPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var id = root.TryGetProperty("storeId", out var i) ? i.GetString() : null;
            var code = root.TryGetProperty("storeCode", out var c) ? c.GetString() : null;
            var machine = root.TryGetProperty("machineId", out var m) ? m.GetString() : null;
            DateTime? configured = null;
            if (root.TryGetProperty("configuredAtUtc", out var d) && d.ValueKind == JsonValueKind.String
                && DateTime.TryParse(d.GetString(), out var parsed))
                configured = parsed.ToUniversalTime();

            settings = new StoreIdentitySettings
            {
                StoreId = id ?? string.Empty,
                StoreCode = code ?? string.Empty,
                MachineId = machine ?? string.Empty,
                ConfiguredAtUtc = configured
            };
            return !string.IsNullOrWhiteSpace(settings.StoreCode) || !string.IsNullOrWhiteSpace(settings.StoreId);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore
        }
    }

    public void InvalidateIfMachineMismatch()
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(_current.StoreId) && string.IsNullOrWhiteSpace(_current.StoreCode))
                return;

            if (string.IsNullOrWhiteSpace(_current.MachineId) ||
                !string.Equals(_current.MachineId, _machineId, StringComparison.OrdinalIgnoreCase))
            {
                Log($"Cleared local identity (machine mismatch). saved={_current.MachineId} current={_machineId}");
                _current = new StoreIdentitySettings();
                TryDelete(_filePath);
                TryDelete(_legacyPlainPath);
            }
        }
    }

    public async Task<bool> TryRestoreFromServerAsync(CancellationToken ct = default)
    {
        if (IsConfigured)
            return true;

        try
        {
            await using var conn = CreateConnection();
            Log($"Restore: connecting for machine={_machineId}");
            await conn.OpenAsync(ct);
            await EnsureActivationsTableAsync(conn, ct);

            await using var cmd = new MySqlCommand(
                """
                SELECT store_id, store_code
                FROM store_activations
                WHERE machine_id = @machine AND is_approved = 1
                LIMIT 1
                """, conn);
            cmd.Parameters.AddWithValue("@machine", _machineId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                Log("Restore: no approved license for this machine.");
                return false;
            }

            var storeId = reader.GetString(reader.GetOrdinal("store_id"));
            var storeCode = reader.GetString(reader.GetOrdinal("store_code"));
            PersistLocal(storeId, storeCode);
            Log($"Restore: saved local identity id={storeId} code={storeCode}");
            return IsConfigured;
        }
        catch (Exception ex)
        {
            Log($"Restore failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ValidateAgainstServerAsync(CancellationToken ct = default)
    {
        string? localId;
        lock (_gate)
        {
            if (!IsConfiguredUnlocked())
                return false;
            localId = _current.StoreId;
        }

        try
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            await using var cmd = new MySqlCommand(
                """
                SELECT COUNT(*)
                FROM store_activations
                WHERE store_id = @id
                  AND machine_id = @machine
                  AND is_approved = 1
                """, conn);
            cmd.Parameters.AddWithValue("@id", localId);
            cmd.Parameters.AddWithValue("@machine", _machineId);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
            if (count > 0)
            {
                Log($"Validate OK: {localId} + {_machineId}");
                return true;
            }

            Log($"Validate FAILED: local identity not approved on VPS for {localId} / {_machineId}. Clearing.");
            ClearLocal();
            return false;
        }
        catch (Exception ex)
        {
            Log($"Validate offline/error, keeping local if present: {ex.Message}");
            return IsConfigured;
        }
    }

    public void ClearLocal()
    {
        lock (_gate)
        {
            _current = new StoreIdentitySettings();
            TryDelete(_filePath);
            TryDelete(_legacyPlainPath);
        }
    }

    public async Task<StoreActivationResult> ActivateAsync(string storeCode, CancellationToken ct = default)
    {
        var displayCode = (storeCode ?? string.Empty).Trim();
        Log($"Activate start code={displayCode} machine={_machineId}");

        if (displayCode.Length < 2)
            return StoreActivationResult.Fail("Store code must be at least 2 characters.");
        if (displayCode.Length > 40)
            return StoreActivationResult.Fail("Store code must be at most 40 characters.");
        if (displayCode.Any(char.IsControl))
            return StoreActivationResult.Fail("Store code cannot contain control characters.");

        try
        {
            await using var conn = CreateConnection();
            Log($"Connecting to {conn.DataSource} / {conn.Database} ...");
            await conn.OpenAsync(ct);
            Log("Connected.");

            await EnsureActivationsTableAsync(conn, ct);

            string? existingId = null;
            string? existingCode = null;
            string? boundMachine = null;
            var approved = false;
            var found = false;

            // One license per machine — look up by machine_id (StoreCode is display-only).
            await using (var cmd = new MySqlCommand(
                             """
                             SELECT store_id, store_code, machine_id, is_approved
                             FROM store_activations
                             WHERE machine_id = @machine
                             LIMIT 1
                             """, conn))
            {
                cmd.Parameters.AddWithValue("@machine", _machineId);
                await using var reader = await cmd.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    found = true;
                    existingId = reader.GetString(reader.GetOrdinal("store_id"));
                    existingCode = reader.GetString(reader.GetOrdinal("store_code"));
                    boundMachine = reader.GetString(reader.GetOrdinal("machine_id"));
                    approved = Convert.ToInt32(reader.GetValue(reader.GetOrdinal("is_approved"))) != 0;
                }
            }

            if (found)
            {
                Log($"Existing row id={existingId} code={existingCode} machine={boundMachine} approved={approved}");

                if (approved)
                {
                    PersistLocal(existingId!, existingCode ?? displayCode);
                    Log("Activated locally (already approved).");
                    return StoreActivationResult.Ok(
                        $"Store activated.\n\nStore code: {existingCode}\nStore ID: {existingId}");
                }

                // Pending: allow updating display store_code only (not store_id / approval).
                if (!string.Equals(existingCode, displayCode, StringComparison.Ordinal))
                {
                    await using var upd = new MySqlCommand(
                        """
                        UPDATE store_activations
                        SET store_code = @code, machine_name = @name
                        WHERE store_id = @id AND is_approved = 0
                        """, conn);
                    upd.Parameters.AddWithValue("@code", displayCode);
                    upd.Parameters.AddWithValue("@name", Environment.MachineName);
                    upd.Parameters.AddWithValue("@id", existingId);
                    await upd.ExecuteNonQueryAsync(ct);
                    Log($"Updated pending store_code to {displayCode}");
                }

                return PendingMessage(displayCode, existingId!);
            }

            var newId = NewStoreId();
            await using (var insert = new MySqlCommand(
                             """
                             INSERT INTO store_activations
                               (store_id, store_code, machine_id, machine_name, is_approved, requested_at_utc)
                             VALUES
                               (@id, @code, @machine, @name, 0, UTC_TIMESTAMP(6))
                             """, conn))
            {
                insert.Parameters.AddWithValue("@id", newId);
                insert.Parameters.AddWithValue("@code", displayCode);
                insert.Parameters.AddWithValue("@machine", _machineId);
                insert.Parameters.AddWithValue("@name", Environment.MachineName);
                var rows = await insert.ExecuteNonQueryAsync(ct);
                Log($"Inserted pending row id={newId}, affected={rows}");
            }

            return PendingMessage(displayCode, newId);
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            Log("Duplicate key on insert — treating as existing request.");
            return StoreActivationResult.Fail(
                "This PC already has an activation request on the server.\n\n" +
                "Wait for approval, then click Activate again.");
        }
        catch (MySqlException ex)
        {
            Log($"MySQL error: {ex.Number} {ex.Message}");
            return StoreActivationResult.Fail(
                "Could not reach the activation server.\n\n" +
                "Check internet access and try again.\n\n" +
                ex.Message);
        }
        catch (Exception ex)
        {
            Log($"Error: {ex}");
            return StoreActivationResult.Fail(ex.Message);
        }
    }

    private StoreActivationResult PendingMessage(string storeCode, string storeId) =>
        StoreActivationResult.Fail(
            $"Activation requested.\n\n" +
            $"Store code: {storeCode}\n" +
            $"Store ID: {storeId}\n\n" +
            "Not approved yet. Send this Machine ID to the software provider:\n\n" +
            $"{_machineId}\n\n" +
            "After they approve, restart the app or click Activate again.");

    private static string NewStoreId() =>
        "S" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    private void PersistLocal(string storeId, string storeCode)
    {
        lock (_gate)
        {
            _current = new StoreIdentitySettings
            {
                StoreId = storeId.Trim().ToUpperInvariant(),
                StoreCode = storeCode.Trim(),
                MachineId = _machineId,
                ConfiguredAtUtc = DateTime.UtcNow
            };
            ProtectedStoreIdentityFile.Write(_filePath, _current);
            TryDelete(_legacyPlainPath);
            Log($"PersistLocal wrote encrypted {_filePath} for id={storeId} code={storeCode} / {_machineId}");
        }
    }

    private bool IsConfiguredUnlocked() =>
        !string.IsNullOrWhiteSpace(_current.StoreId)
        && !string.IsNullOrWhiteSpace(_current.MachineId)
        && string.Equals(_current.MachineId, _machineId, StringComparison.OrdinalIgnoreCase);

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

        Log($"Using MySQL host={host} db={database} user={user}");

        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = (uint)port,
            Database = database,
            UserID = user,
            Password = password,
            SslMode = MySqlSslMode.Preferred,
            ConnectionTimeout = 20,
            AllowUserVariables = true
        };
        return new MySqlConnection(builder.ConnectionString);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        return null;
    }

    private static async Task EnsureActivationsTableAsync(MySqlConnection conn, CancellationToken ct)
    {
        // Prefer new schema. If an older store_code-PK table exists, migration SQL must be run on VPS.
        await using var cmd = new MySqlCommand(
            """
            CREATE TABLE IF NOT EXISTS store_activations (
              store_id VARCHAR(40) NOT NULL,
              store_code VARCHAR(80) NOT NULL,
              machine_id VARCHAR(128) NOT NULL,
              machine_name VARCHAR(200) NULL,
              is_approved TINYINT(1) NOT NULL DEFAULT 0,
              requested_at_utc DATETIME(6) NOT NULL,
              approved_at_utc DATETIME(6) NULL,
              notes VARCHAR(500) NULL,
              PRIMARY KEY (store_id),
              UNIQUE KEY uk_store_activations_machine (machine_id),
              KEY ix_store_activations_code (store_code)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4
            """, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore logging failures
        }
    }
}
