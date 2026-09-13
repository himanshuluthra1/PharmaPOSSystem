using System.Data;
using System.Data.OleDb;
using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

public sealed class MedWinImportContext
{
    public required string MedWinPath { get; init; }
    public required string MedWinPassword { get; init; }
    public required string TargetConnectionString { get; init; }
    public bool Force { get; init; }
    public bool ForceMedicines { get; set; }
    public string? ReportCsvPath { get; init; }

    /// <summary>Optional sink for UI progress (CLI still writes to Console).</summary>
    public Action<string>? LogSink { get; set; }

    public CancellationToken CancellationToken { get; init; }

    public int BranchId { get; set; }
    public int CashierRoleId { get; set; }
    public DateTime NowUtc { get; } = DateTime.UtcNow;

    public Dictionary<int, int> MedicineMap { get; } = new();
    public Dictionary<int, int> SupplierMap { get; } = new();
    public Dictionary<int, int> CustomerMap { get; } = new();
    public Dictionary<string, int> ManufacturerMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> CategoryMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> BatchMap { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<int, int> SaleMap { get; } = new();

    private static readonly string[] AceProviders =
    [
        "Microsoft.ACE.OLEDB.16.0",
        "Microsoft.ACE.OLEDB.15.0",
        "Microsoft.ACE.OLEDB.12.0"
    ];

    private string? _resolvedProvider;

    public string MedWinConnectionString => BuildConnectionString(ResolveAceProvider());

    public OleDbConnection OpenMedWin()
        => new(BuildConnectionString(ResolveAceProvider()));

    private string BuildConnectionString(string provider) =>
        $"Provider={provider};Data Source={MedWinPath};Jet OLEDB:Database Password={MedWinPassword};";

    private string ResolveAceProvider()
    {
        if (_resolvedProvider is not null)
            return _resolvedProvider;

        Exception? last = null;
        foreach (var provider in AceProviders)
        {
            try
            {
                using var probe = new OleDbConnection(BuildConnectionString(provider));
                probe.Open();
                _resolvedProvider = provider;
                Log($"  Using OLEDB provider: {provider}");
                return provider;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            "Microsoft Access Database Engine (ACE OLEDB) is not installed on this PC.\n\n" +
            "PharmaPOS is 64-bit and needs the 64-bit ACE redistributable to read MedWin data.mdb.\n\n" +
            "Install:\n" +
            "  Microsoft Access Database Engine 2016 Redistributable (64-bit)\n" +
            "  https://www.microsoft.com/en-us/download/details.aspx?id=54920\n" +
            "  Choose AccessDatabaseEngine_X64.exe\n\n" +
            "If 32-bit Office is already installed, run from an elevated Command Prompt:\n" +
            "  AccessDatabaseEngine_X64.exe /quiet\n\n" +
            "Then restart PharmaPOS and run MedWin Import again.\n\n" +
            $"Technical detail: {last?.Message}",
            last);
    }

    public async Task<SqlConnection> OpenTargetAsync()
    {
        var conn = new SqlConnection(TargetConnectionString);
        await conn.OpenAsync(CancellationToken);
        return conn;
    }

    public void Log(string message)
    {
        try { LogSink?.Invoke(message); } catch { /* ignore UI sink errors */ }
        try { Console.WriteLine(message); } catch { /* no console (WPF) */ }
    }

    public void ThrowIfCancellationRequested() => CancellationToken.ThrowIfCancellationRequested();
}
