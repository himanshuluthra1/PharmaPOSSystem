using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;

namespace PharmaPOS.Persistence.Seed;

/// <summary>
/// Ensures SQL Server LocalDB is running before EF / restore try to connect.
/// LocalDB instances are often Stopped after reboot; error 52 / 26 then appears.
/// </summary>
[SupportedOSPlatform("windows")]
public static class LocalDbBootstrapper
{
    private static readonly Regex LocalDbInstanceRegex = new(
        @"\(localdb\)\\(?<name>[^;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task EnsureStartedAsync(string? connectionString, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var match = LocalDbInstanceRegex.Match(connectionString);
        if (!match.Success) return;

        var instance = match.Groups["name"].Value.Trim();
        if (string.IsNullOrWhiteSpace(instance))
            instance = "MSSQLLocalDB";

        // Best-effort start via SqlLocalDB.exe — do NOT treat missing exe as "not installed".
        // Some LocalDB installs auto-start on first SqlConnection without the CLI tool on PATH.
        var exe = FindSqlLocalDbExe();
        if (exe is not null)
            await RunAsync(exe, $"start \"{instance}\"", ct);

        try
        {
            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync(ct);
            return;
        }
        catch (Exception ex) when (IsLocalDbMissing(ex))
        {
            throw new InvalidOperationException(
                "SQL Server LocalDB runtime is not available to this Windows user.\n\n" +
                "Even if LocalDB was installed, check on the customer PC:\n" +
                "  1. Open PowerShell as the SAME Windows user who runs PharmaPOS\n" +
                "  2. Run:  sqllocaldb info\n" +
                "  3. Run:  sqllocaldb start MSSQLLocalDB\n" +
                "  4. Reboot once after installing LocalDB\n\n" +
                "If 'sqllocaldb' is not recognized, install/repair:\n" +
                "SQL Server Express LocalDB — https://go.microsoft.com/fwlink/?LinkID=866658\n\n" +
                "Technical detail: " + RootMessage(ex), ex);
        }
        catch
        {
            // Other connection errors (restore/migrate will report them).
        }
    }

    private static bool IsLocalDbMissing(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
        {
            var msg = e.Message ?? string.Empty;
            if (msg.Contains("error: 52", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("Unable to locate a Local Database Runtime", StringComparison.OrdinalIgnoreCase)) return true;
            if (msg.Contains("Local Database Runtime installation", StringComparison.OrdinalIgnoreCase)) return true;
            if (e is SqlException sql && sql.Number == 52) return true;
        }
        return false;
    }

    private static string RootMessage(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return ex.Message;
    }

    private static string? FindSqlLocalDbExe()
    {
        // Registry is the most reliable signal of a real LocalDB install.
        foreach (var fromReg in FindFromRegistry())
        {
            if (File.Exists(fromReg)) return fromReg;
        }

        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)))
        {
            var sqlRoot = Path.Combine(root, "Microsoft SQL Server");
            if (!Directory.Exists(sqlRoot)) continue;

            try
            {
                var found = Directory.EnumerateFiles(sqlRoot, "SqlLocalDB.exe", SearchOption.AllDirectories)
                    .OrderByDescending(p => p) // prefer newer version folders when sorted
                    .FirstOrDefault();
                if (found is not null) return found;
            }
            catch
            {
                // Ignore access issues while scanning.
            }
        }

        var fromPath = Environment.GetEnvironmentVariable("PATH")?
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Path.Combine(p.Trim('"'), "SqlLocalDB.exe"))
            .FirstOrDefault(File.Exists);
        return fromPath;
    }

    private static IEnumerable<string> FindFromRegistry()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Microsoft SQL Server Local DB\Installed Versions";
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var versions = baseKey.OpenSubKey(keyPath);
            if (versions is null) continue;

            foreach (var name in versions.GetSubKeyNames().OrderByDescending(n => n))
            {
                using var ver = versions.OpenSubKey(name);
                var api = ver?.GetValue("InstanceAPIPath") as string;
                if (string.IsNullOrWhiteSpace(api)) continue;

                // InstanceAPIPath points at sqluserinstance.dll folder; CLI is usually nearby under Tools\Binn.
                var dir = Path.GetDirectoryName(api);
                if (dir is null) continue;

                var sibling = Path.Combine(dir, "SqlLocalDB.exe");
                if (File.Exists(sibling)) yield return sibling;

                // Walk up to version root and search Tools\Binn.
                var versionRoot = Directory.GetParent(dir)?.Parent?.FullName;
                if (versionRoot is null) continue;
                var tools = Path.Combine(versionRoot, "Tools", "Binn", "SqlLocalDB.exe");
                if (File.Exists(tools)) yield return tools;
            }
        }
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, stdout + stderr);
    }
}
