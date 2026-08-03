using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

/// <summary>
/// Permanently removes POS transactional / stock data so MedWin sales, purchases, and stock can be imported cleanly.
/// Keeps masters: medicines, suppliers, customers, categories, manufacturers, users, roles, company, branches, chart of accounts, return reasons.
/// </summary>
public static class MedWinTransactionalDataCleaner
{
    private static readonly string[] DeleteTablesInOrder =
    [
        "CreditNotes",
        "ReturnRefunds",
        "SaleReturnItems",
        "SaleReturns",
        "SalePayments",
        "SaleItems",
        "Sales",
        "PurchaseReturnItems",
        "PurchaseReturns",
        "PurchaseItems",
        "Purchases",
        "PurchaseOrderItems",
        "PurchaseOrders",
        "StockAdjustmentItems",
        "StockAdjustments",
        "StockTransferItems",
        "StockTransfers",
        "StockMovements",
        "NonSaleableStocks",
        "MedicineBatches",
        "JournalLines",
        "JournalEntries",
        "SyncOutboxEntries"
    ];

    public static async Task RunAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[clear-transactions] Removing existing sales, purchases, stock, and related movements...");
        ctx.ThrowIfCancellationRequested();

        await using var tx = (SqlTransaction)await target.BeginTransactionAsync(ctx.CancellationToken);
        try
        {
            foreach (var table in DeleteTablesInOrder)
            {
                ctx.ThrowIfCancellationRequested();
                var deleted = await DeleteAllAsync(target, tx, table, ctx.CancellationToken);
                if (deleted > 0)
                    ctx.Log($"  Deleted {deleted:N0} from {table}");
            }

            await using (var cust = new SqlCommand(
                             "UPDATE Customers SET OutstandingBalance = 0 WHERE OutstandingBalance <> 0",
                             target, tx))
            {
                var n = await cust.ExecuteNonQueryAsync(ctx.CancellationToken);
                if (n > 0) ctx.Log($"  Reset OutstandingBalance on {n:N0} customer(s)");
            }

            await using (var sup = new SqlCommand(
                             "UPDATE Suppliers SET OutstandingBalance = 0 WHERE OutstandingBalance <> 0",
                             target, tx))
            {
                var n = await sup.ExecuteNonQueryAsync(ctx.CancellationToken);
                if (n > 0) ctx.Log($"  Reset OutstandingBalance on {n:N0} supplier(s)");
            }

            await tx.CommitAsync(ctx.CancellationToken);
            ctx.Log("  Transactional data cleared. Masters (medicines, parties, users, company) kept.");
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<int> DeleteAllAsync(
        SqlConnection conn, SqlTransaction tx, string table, CancellationToken ct)
    {
        // Table may not exist on older DBs — skip quietly.
        await using (var exists = new SqlCommand(
                         """
                         SELECT CASE WHEN EXISTS (
                           SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                           WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @t
                         ) THEN 1 ELSE 0 END
                         """, conn, tx))
        {
            exists.Parameters.AddWithValue("@t", table);
            var ok = Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) == 1;
            if (!ok) return 0;
        }

        await using var cmd = new SqlCommand($"DELETE FROM [{table}]", conn, tx);
        cmd.CommandTimeout = 0;
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
