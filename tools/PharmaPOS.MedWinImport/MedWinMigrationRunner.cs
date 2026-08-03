namespace PharmaPOS.MedWinImport;

/// <summary>Options for in-app or programmatic MedWin → PharmaPOS migration.</summary>
public sealed class MedWinMigrationOptions
{
    public const string DefaultMdbPath = @"D:\Medwin\datafolder\data.mdb";
    public const string DefaultPassword = "z111111111111111111a";

    public required string MedWinPath { get; init; }
    public string MedWinPassword { get; init; } = DefaultPassword;
    public required string TargetConnectionString { get; init; }
    public bool Force { get; init; }

    /// <summary>
    /// When true, permanently deletes existing POS sales/purchases/stock/movements before import phases run.
    /// Masters (medicines, parties, users, company) are kept.
    /// </summary>
    public bool ClearExistingTransactionalData { get; init; }

    public IReadOnlyList<string> Phases { get; init; } = Array.Empty<string>();
    public string? ReportCsvPath { get; init; }
    public Action<string>? LogSink { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>Public entry point for migrating MedWin Access data into PharmaPOS.</summary>
public static class MedWinMigrationRunner
{
    public static IReadOnlyList<(string Id, string Label, bool InFullImport)> AvailablePhases { get; } =
    [
        ("company", "Company profile", true),
        ("gst", "GST categories", true),
        ("medicines", "Medicines (active + OneMG match)", true),
        ("suppliers", "Suppliers", true),
        ("customers", "Customers", true),
        ("stock", "Stock batches", true),
        ("purchases", "Purchases", true),
        ("purchase-returns", "Purchase returns", true),
        ("sales", "Sales", true),
        ("payments", "Sale payments", true),
        ("users", "Operator users", true),
        ("backfill-expiry", "Backfill expiry dates", false),
        ("backfill-purchase-payments", "Backfill purchase payments", false),
        ("backfill-purchase-tax", "Backfill purchase tax from lines", false),
        ("backfill-salts", "Backfill salts / pack / strength", false),
        ("dedupe-onemg", "Dedupe OneMG catalogue", false)
    ];

    public static IReadOnlyList<string> DefaultFullPhases { get; } =
        AvailablePhases.Where(p => p.InFullImport).Select(p => p.Id).ToArray();

    public static async Task RunAsync(MedWinMigrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.MedWinPath))
            throw new ArgumentException("MedWin MDB path is required.", nameof(options));
        if (!File.Exists(options.MedWinPath))
            throw new FileNotFoundException("MedWin database not found.", options.MedWinPath);
        if (string.IsNullOrWhiteSpace(options.TargetConnectionString))
            throw new ArgumentException("Target SQL connection string is required.", nameof(options));

        var phases = (options.Phases.Count == 0
            ? DefaultFullPhases
            : options.Phases).ToList();

        if (options.ClearExistingTransactionalData &&
            !phases.Contains("clear-transactions", StringComparer.OrdinalIgnoreCase))
        {
            phases.Insert(0, "clear-transactions");
        }

        // After a wipe, re-import sales/purchases even if a prior partial import existed.
        // Do NOT force medicine rematch on wipe — that re-bulks the catalogue and can hang LocalDB.
        var forceTransactions = options.Force || options.ClearExistingTransactionalData;
        var forceMedicines = options.Force &&
            (phases.Contains("all", StringComparer.OrdinalIgnoreCase) ||
             phases.Contains("medicines", StringComparer.OrdinalIgnoreCase) ||
             options.Phases.Count == 0);

        var ctx = new MedWinImportContext
        {
            MedWinPath = options.MedWinPath,
            MedWinPassword = options.MedWinPassword,
            TargetConnectionString = options.TargetConnectionString,
            Force = forceTransactions,
            ForceMedicines = forceMedicines,
            ReportCsvPath = options.ReportCsvPath,
            LogSink = options.LogSink,
            CancellationToken = options.CancellationToken
        };

        ctx.Log("PharmaPOS MedWin importer");
        ctx.Log("=========================");
        ctx.Log($"Source : {options.MedWinPath}");
        ctx.Log($"Phases : {string.Join(", ", phases)}");
        if (options.ClearExistingTransactionalData)
            ctx.Log("Clear  : existing transactional data BEFORE import");
        if (forceTransactions) ctx.Log("Force  : transactional re-import enabled");
        if (forceMedicines) ctx.Log("Force  : medicine rematch enabled (slow)");

        await MedWinImporter.RunAsync(ctx, phases);
        ctx.Log("\nImport completed successfully.");
    }
}
