using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

internal static class OneMgDuplicateCleaner
{
    private sealed record MedicineRow(int Id, string Name, int SaleCount, int StockBatchCount);

    public static async Task RunAsync(MedWinImportContext ctx, SqlConnection target, bool dryRun)
    {
        Console.WriteLine($"\n[dedupe-onemg] Removing duplicate OneMG medicines (dry-run={dryRun})...");

        var groups = await LoadDuplicateGroupsAsync(target);
        Console.WriteLine($"  Duplicate name groups: {groups.Count:N0}");

        var toDelete = new List<MedicineRow>();
        var kept = 0;

        foreach (var (name, rows) in groups)
        {
            var withSales = rows.Where(r => r.SaleCount > 0).ToList();
            if (withSales.Count > 0)
            {
                kept += withSales.Count;
                toDelete.AddRange(rows.Where(r => r.SaleCount == 0));
                continue;
            }

            var keeper = rows.OrderBy(r => r.Id).First();
            kept++;
            toDelete.AddRange(rows.Where(r => r.Id != keeper.Id));
        }

        Console.WriteLine($"  Keeping {kept:N0} rows, removing {toDelete.Count:N0} duplicate rows with no sale bills.");

        if (toDelete.Count == 0)
        {
            Console.WriteLine("  Nothing to remove.");
            return;
        }

        if (dryRun)
        {
            foreach (var sample in toDelete.Take(10))
                Console.WriteLine($"    would delete Id={sample.Id} ({sample.Name})");
            if (toDelete.Count > 10)
                Console.WriteLine($"    ... and {toDelete.Count - 10:N0} more");
            return;
        }

        const int batchSize = 500;
        for (var i = 0; i < toDelete.Count; i += batchSize)
        {
            var batch = toDelete.Skip(i).Take(batchSize).Select(r => r.Id).ToList();
            var idList = string.Join(",", batch);

            await using (var batchCmd = new SqlCommand($"""
                UPDATE MedicineBatches
                SET IsDeleted = 1, DeletedAtUtc = @Now
                WHERE IsDeleted = 0 AND MedicineId IN ({idList})
                """, target))
            {
                batchCmd.Parameters.AddWithValue("@Now", ctx.NowUtc);
                await batchCmd.ExecuteNonQueryAsync();
            }

            await using var medCmd = new SqlCommand($"""
                UPDATE Medicines
                SET IsDeleted = 1, DeletedAtUtc = @Now, Status = 0
                WHERE IsDeleted = 0 AND Id IN ({idList})
                """, target);
            medCmd.Parameters.AddWithValue("@Now", ctx.NowUtc);
            await medCmd.ExecuteNonQueryAsync();
        }

        Console.WriteLine($"  Soft-deleted {toDelete.Count:N0} duplicate OneMG medicines.");
    }

    private static async Task<List<(string Name, List<MedicineRow> Rows)>> LoadDuplicateGroupsAsync(SqlConnection target)
    {
        const string sql = """
            WITH OneMg AS (
                SELECT m.Id, m.Name
                FROM Medicines m
                WHERE m.IsDeleted = 0 AND m.Notes LIKE '%OneMG%'
            ),
            DupNames AS (
                SELECT Name
                FROM OneMg
                GROUP BY Name
                HAVING COUNT(*) > 1
            )
            SELECT o.Id, o.Name,
                   ISNULL(s.SaleCount, 0) AS SaleCount,
                   ISNULL(b.BatchCount, 0) AS StockBatchCount
            FROM OneMg o
            INNER JOIN DupNames d ON d.Name = o.Name
            OUTER APPLY (
                SELECT COUNT(*) AS SaleCount
                FROM SaleItems si
                WHERE si.MedicineId = o.Id AND si.IsDeleted = 0
            ) s
            OUTER APPLY (
                SELECT COUNT(*) AS BatchCount
                FROM MedicineBatches mb
                WHERE mb.MedicineId = o.Id AND mb.IsDeleted = 0 AND mb.QuantityAvailable > 0
            ) b
            ORDER BY o.Name, o.Id
            """;

        await using var cmd = new SqlCommand(sql, target);
        await using var reader = await cmd.ExecuteReaderAsync();

        var map = new Dictionary<string, List<MedicineRow>>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            var row = new MedicineRow(reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3));
            if (!map.TryGetValue(row.Name, out var list))
            {
                list = new List<MedicineRow>();
                map[row.Name] = list;
            }
            list.Add(row);
        }

        return map.Select(kv => (kv.Key, kv.Value)).ToList();
    }
}
