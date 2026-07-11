using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

// ---------------------------------------------------------------------------
// One-off importer: copies the OneMG medicine catalogue from the source SQL
// Server database into the PharmaPOS application database.
//
//   Source : Server=.  Database=OneMGData  Table=dbo.OneMGData_3   (287k rows)
//   Target : (localdb)\MSSQLLocalDB  Database=PharmaPosDb
//
// Because the two databases live on different SQL Server instances a plain
// cross-database INSERT..SELECT is not possible, so we stream rows with a
// SqlDataReader and write them with SqlBulkCopy in batches. Manufacturers are
// de-duplicated and created first, then referenced by the medicine rows.
// ---------------------------------------------------------------------------

const string SourceConnStr =
    "Server=.;Database=OneMGData;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";
const string TargetConnStr =
    "Server=(localdb)\\MSSQLLocalDB;Database=PharmaPosDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

const int BatchSize = 20_000;
const decimal DefaultGstPercent = 12m;   // most common medicine GST slab in India
var now = DateTime.UtcNow;

// Allow overriding via args: PharmaPOS.DataImport [source] [target]
var sourceConn = args.Length > 0 ? args[0] : SourceConnStr;
var targetConn = args.Length > 1 ? args[1] : TargetConnStr;

Console.WriteLine("PharmaPOS medicine importer");
Console.WriteLine("===========================");

await using var target = new SqlConnection(targetConn);
await target.OpenAsync();

var existingMeds = await ScalarInt(target, "SELECT COUNT(*) FROM Medicines WHERE IsDeleted = 0");
Console.WriteLine($"Target currently has {existingMeds:N0} medicines.");
if (existingMeds > 50_000 && !args.Contains("--force"))
{
    Console.WriteLine("Target already looks populated. Re-run with --force to import anyway. Aborting.");
    return;
}

// --- 1. Manufacturers -----------------------------------------------------
Console.WriteLine("\n[1/2] Importing manufacturers...");

var manufacturerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
await LoadExistingManufacturers(target, manufacturerMap);
Console.WriteLine($"  Loaded {manufacturerMap.Count:N0} existing manufacturers from target.");

var sourceManufacturers = new List<string>();
await using (var src = new SqlConnection(sourceConn))
{
    await src.OpenAsync();
    const string sql = "SELECT DISTINCT manufacturer FROM dbo.OneMGData_3 WHERE manufacturer IS NOT NULL AND manufacturer <> ''";
    await using var cmd = new SqlCommand(sql, src) { CommandTimeout = 300 };
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var name = Trunc(reader.GetString(0).Trim(), 200);
        if (name.Length > 0) sourceManufacturers.Add(name);
    }
}

var newManufacturers = sourceManufacturers
    .Where(m => !manufacturerMap.ContainsKey(m))
    .GroupBy(m => m, StringComparer.OrdinalIgnoreCase)
    .Select(g => g.Key)
    .ToList();

Console.WriteLine($"  {newManufacturers.Count:N0} new manufacturers to insert.");

if (newManufacturers.Count > 0)
{
    var mfgTable = new DataTable();
    mfgTable.Columns.Add("Name", typeof(string));
    mfgTable.Columns.Add("Status", typeof(int));
    mfgTable.Columns.Add("CreatedAtUtc", typeof(DateTime));
    mfgTable.Columns.Add("IsDeleted", typeof(bool));
    foreach (var name in newManufacturers)
        mfgTable.Rows.Add(name, 1, now, false);

    using var bulk = new SqlBulkCopy(target) { DestinationTableName = "Manufacturers", BulkCopyTimeout = 300 };
    bulk.ColumnMappings.Add("Name", "Name");
    bulk.ColumnMappings.Add("Status", "Status");
    bulk.ColumnMappings.Add("CreatedAtUtc", "CreatedAtUtc");
    bulk.ColumnMappings.Add("IsDeleted", "IsDeleted");
    await bulk.WriteToServerAsync(mfgTable);

    manufacturerMap.Clear();
    await LoadExistingManufacturers(target, manufacturerMap);
}
Console.WriteLine($"  Manufacturer map now has {manufacturerMap.Count:N0} entries.");

// --- 2. Medicines ---------------------------------------------------------
Console.WriteLine("\n[2/2] Importing medicines (batches of {0:N0})...", BatchSize);

var medTable = CreateMedicineTable();
long imported = 0, skipped = 0;
var packDigits = new Regex(@"\d+", RegexOptions.Compiled);

await using (var src = new SqlConnection(sourceConn))
{
    await src.OpenAsync();
    const string sql = @"SELECT medicine_name, manufacturer, composition, price,
                                prescription_required, pack_info, image_url, medicine_url, medicine_id
                         FROM dbo.OneMGData_3";
    await using var cmd = new SqlCommand(sql, src) { CommandTimeout = 600 };
    await using var reader = await cmd.ExecuteReaderAsync();

    while (await reader.ReadAsync())
    {
        var name = reader.IsDBNull(0) ? null : reader.GetString(0).Trim();
        if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }

        var mfgName = reader.IsDBNull(1) ? null : Trunc(reader.GetString(1).Trim(), 200);
        int? manufacturerId = mfgName is not null && manufacturerMap.TryGetValue(mfgName, out var id) ? id : null;

        var composition = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
        var priceText = reader.IsDBNull(3) ? null : reader.GetString(3);
        decimal price = ParsePrice(priceText);
        bool rx = !reader.IsDBNull(4) && reader.GetBoolean(4);
        var packInfo = reader.IsDBNull(5) ? null : reader.GetString(5).Trim();
        var imageUrl = reader.IsDBNull(6) ? null : reader.GetString(6);
        var medicineUrl = reader.IsDBNull(7) ? null : reader.GetString(7);
        var medicineId = reader.IsDBNull(8) ? null : reader.GetString(8);

        int unitsPerPack = 1;
        if (!string.IsNullOrWhiteSpace(packInfo))
        {
            var m = packDigits.Match(packInfo);
            if (m.Success && int.TryParse(m.Value, out var u) && u > 0) unitsPerPack = u;
        }

        var notes = string.Join(" | ", new[]
        {
            packInfo,
            medicineId is null ? null : $"OneMG-ID:{medicineId}",
            medicineUrl
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var row = medTable.NewRow();
        row["Name"] = Trunc(name, 200);
        row["GenericName"] = (object?)Trunc(composition, 200) ?? DBNull.Value;
        row["Composition"] = (object?)composition ?? DBNull.Value;
        row["DosageForm"] = 0;                 // Tablet (default; not provided by source)
        row["ManufacturerId"] = (object?)manufacturerId ?? DBNull.Value;
        row["GstPercent"] = DefaultGstPercent;
        row["IsBatchEnabled"] = true;
        row["IsExpiryEnabled"] = true;
        row["Mrp"] = price;
        row["PurchasePrice"] = 0m;
        row["SellingPrice"] = price;
        row["DefaultDiscountPercent"] = 0m;
        row["ScheduleType"] = 0;
        row["PrescriptionRequired"] = rx;
        row["UnitsPerPack"] = unitsPerPack;
        row["UnitOfMeasure"] = "Nos";
        row["ReorderLevel"] = 0;
        row["ReorderQuantity"] = 0;
        row["ImagePath"] = (object?)Trunc(imageUrl, 1000) ?? DBNull.Value;
        row["PackInfo"] = (object?)Trunc(packInfo, 100) ?? DBNull.Value;
        row["Notes"] = string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes;
        row["Status"] = 1;                     // Active
        row["CreatedAtUtc"] = now;
        row["IsDeleted"] = false;
        medTable.Rows.Add(row);

        if (medTable.Rows.Count >= BatchSize)
        {
            imported += await FlushMedicines(target, medTable);
            Console.WriteLine($"  ...{imported:N0} imported");
            medTable.Clear();
        }
    }
}

if (medTable.Rows.Count > 0)
    imported += await FlushMedicines(target, medTable);

Console.WriteLine($"\nDone. Imported {imported:N0} medicines ({skipped:N0} skipped for blank name).");
var finalCount = await ScalarInt(target, "SELECT COUNT(*) FROM Medicines WHERE IsDeleted = 0");
Console.WriteLine($"Target now has {finalCount:N0} medicines total.");

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------
static DataTable CreateMedicineTable()
{
    var t = new DataTable();
    t.Columns.Add("Name", typeof(string));
    t.Columns.Add("GenericName", typeof(string));
    t.Columns.Add("Composition", typeof(string));
    t.Columns.Add("DosageForm", typeof(int));
    t.Columns.Add("ManufacturerId", typeof(int));
    t.Columns.Add("GstPercent", typeof(decimal));
    t.Columns.Add("IsBatchEnabled", typeof(bool));
    t.Columns.Add("IsExpiryEnabled", typeof(bool));
    t.Columns.Add("Mrp", typeof(decimal));
    t.Columns.Add("PurchasePrice", typeof(decimal));
    t.Columns.Add("SellingPrice", typeof(decimal));
    t.Columns.Add("DefaultDiscountPercent", typeof(decimal));
    t.Columns.Add("ScheduleType", typeof(int));
    t.Columns.Add("PrescriptionRequired", typeof(bool));
    t.Columns.Add("UnitsPerPack", typeof(int));
    t.Columns.Add("UnitOfMeasure", typeof(string));
    t.Columns.Add("ReorderLevel", typeof(int));
    t.Columns.Add("ReorderQuantity", typeof(int));
    t.Columns.Add("ImagePath", typeof(string));
    t.Columns.Add("PackInfo", typeof(string));
    t.Columns.Add("Notes", typeof(string));
    t.Columns.Add("Status", typeof(int));
    t.Columns.Add("CreatedAtUtc", typeof(DateTime));
    t.Columns.Add("IsDeleted", typeof(bool));
    return t;
}

static async Task<long> FlushMedicines(SqlConnection target, DataTable table)
{
    using var bulk = new SqlBulkCopy(target)
    {
        DestinationTableName = "Medicines",
        BulkCopyTimeout = 600,
        BatchSize = 5000
    };
    foreach (DataColumn c in table.Columns)
        bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
    await bulk.WriteToServerAsync(table);
    return table.Rows.Count;
}

static async Task LoadExistingManufacturers(SqlConnection target, Dictionary<string, int> map)
{
    await using var cmd = new SqlCommand("SELECT Id, Name FROM Manufacturers WHERE IsDeleted = 0", target)
    { CommandTimeout = 120 };
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var id = reader.GetInt32(0);
        var name = reader.GetString(1);
        map[name] = id;
    }
}

static async Task<int> ScalarInt(SqlConnection conn, string sql)
{
    await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 120 };
    var result = await cmd.ExecuteScalarAsync();
    return result is int i ? i : Convert.ToInt32(result);
}

static decimal ParsePrice(string? text)
{
    if (string.IsNullOrWhiteSpace(text)) return 0m;
    // Keep digits and decimal point only (strips ₹, commas, "MRP", etc.).
    var cleaned = new string(text.Where(c => char.IsDigit(c) || c == '.').ToArray());
    return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0m;
}

static string? Trunc(string? s, int max)
    => s is null ? null : (s.Length <= max ? s : s[..max]);
