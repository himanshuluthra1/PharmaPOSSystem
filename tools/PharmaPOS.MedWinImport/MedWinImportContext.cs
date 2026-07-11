using System.Data;
using System.Data.OleDb;
using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

internal sealed class MedWinImportContext
{
    public required string MedWinPath { get; init; }
    public required string MedWinPassword { get; init; }
    public required string TargetConnectionString { get; init; }
    public bool Force { get; init; }
    public bool ForceMedicines { get; set; }
    public string? ReportCsvPath { get; init; }

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

    public string MedWinConnectionString =>
        $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={MedWinPath};Jet OLEDB:Database Password={MedWinPassword};";

    public OleDbConnection OpenMedWin() => new(MedWinConnectionString);

    public async Task<SqlConnection> OpenTargetAsync()
    {
        var conn = new SqlConnection(TargetConnectionString);
        await conn.OpenAsync();
        return conn;
    }
}
