using PharmaPOS.MedWinImport;

const string DefaultMdb = @"D:\Medwin\datafolder\data.mdb";
const string DefaultPassword = "z111111111111111111a";
const string DefaultTarget =
    "Server=(localdb)\\MSSQLLocalDB;Database=PharmaPosDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    PrintHelp();
    return 0;
}

var mdb = DefaultMdb;
var password = DefaultPassword;
var target = DefaultTarget;
var force = false;
var clearTransactions = false;
var phases = new List<string>();
string? reportCsv = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i].ToLowerInvariant())
    {
        case "--mdb":
            mdb = args[++i];
            break;
        case "--password":
            password = args[++i];
            break;
        case "--target":
            target = args[++i];
            break;
        case "--phase":
            phases.Add(args[++i]);
            break;
        case "--force":
            force = true;
            break;
        case "--clear-transactions":
            clearTransactions = true;
            break;
        case "--rematch-medicines":
            force = true;
            break;
        case "--report-csv":
            reportCsv = args[++i];
            break;
    }
}

if (!File.Exists(mdb))
{
    Console.Error.WriteLine($"MedWin database not found: {mdb}");
    return 1;
}

Console.WriteLine("PharmaPOS MedWin importer");
Console.WriteLine("=========================");
Console.WriteLine($"Source : {mdb}");
Console.WriteLine($"Target : {target}");
Console.WriteLine($"Phases : {(phases.Count == 0 ? "all" : string.Join(", ", phases))}");

if (!string.IsNullOrWhiteSpace(reportCsv) && phases.Count == 0)
    phases.Add("medicines");

try
{
    await MedWinMigrationRunner.RunAsync(new MedWinMigrationOptions
    {
        MedWinPath = mdb,
        MedWinPassword = password,
        TargetConnectionString = target,
        Force = force,
        ClearExistingTransactionalData = clearTransactions,
        Phases = phases,
        ReportCsvPath = reportCsv
    });
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("\nImport failed:");
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
        PharmaPOS.MedWinImport — migrate data from MedWin Access database.

        Usage:
          dotnet run --project tools/PharmaPOS.MedWinImport -- [options]

        Options:
          --mdb <path>        Path to data.mdb (default: D:\Medwin\datafolder\data.mdb)
          --password <pwd>    Database password (default: z111111111111111111a — lowercase)
          --target <conn>     PharmaPOS SQL connection string
          --phase <name>      Run one phase (repeatable). Default: all
          --force             Re-import sales/purchases; with --phase medicines, rematch OneMG catalogue;
                              with --phase dedupe-onemg, apply duplicate removal (default is dry-run)
          --clear-transactions Permanently delete existing POS sales/purchases/stock/movements first
                              (keeps medicine & other masters), then run selected phases
          --report-csv <path> Preview medicine matching to CSV (no DB writes)
          -h, --help          Show help

        Phases:
          company, gst, medicines, suppliers, customers, stock,
          purchases, purchase-returns, sales, payments, users,
          backfill-expiry, backfill-purchase-payments, backfill-purchase-tax, backfill-salts, dedupe-onemg,
          clear-transactions, all

        Notes:
          - Medicines imported only if in stock or sold at least once in MedWin.
          - Salt/composition comes from itemgrp.itemgrds (not mgamma group codes).
          - Selling price comes from stock invoice rate (stkinvrate), then wrate/sale history.
          - Quantities stay in MedWin units (do not divide sales by dpsize); pack size only for per-unit MRP.
          - Purchase returns and stock movements are written during full import.
          - Active medicines are matched to existing OneMG catalogue by normalized name; matches are reused.
          - Fresh start: --clear-transactions then full import (or Settings → MedWin Import checkbox).
          - In-app: Settings → MedWin Import (same runner as this CLI).
        """);
}
