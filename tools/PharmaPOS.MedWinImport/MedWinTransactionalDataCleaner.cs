using Microsoft.Data.SqlClient;

namespace PharmaPOS.MedWinImport;

/// <summary>
/// Permanently removes POS transactional / stock / party data so MedWin can be imported cleanly.
/// Keeps: medicines, categories, manufacturers, users, roles, company, branches, chart of accounts, return reasons.
/// Deletes: sales, purchases, stock, journals, sync outbox, customers, suppliers.
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
        "ExpirySupplierClaimItems",
        "ExpirySupplierClaims",
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
        "SyncOutboxEntries",
        // Parties last — FKs from sales/purchases/claims cleared above.
        "Customers",
        "Suppliers"
    ];

    public static async Task RunAsync(MedWinImportContext ctx, SqlConnection target)
    {
        ctx.Log("\n[clear-transactions] Removing existing sales, purchases, stock, customers, and suppliers...");
        ctx.ThrowIfCancellationRequested();

        await using var tx = (SqlTransaction)await target.BeginTransactionAsync(ctx.CancellationToken);
        try
        {
            foreach (var table in DeleteTablesInOrder)
            {
                ctx.ThrowIfCancellationRequested();

                // Purchases.LinkedPurchaseReturnId → PurchaseReturns (Restrict). Clear before DELETE.
                if (table == "PurchaseReturns")
                {
                    var cleared = await NullLinkedPurchaseReturnIdsAsync(target, tx, ctx.CancellationToken);
                    if (cleared > 0)
                        ctx.Log($"  Cleared LinkedPurchaseReturnId on {cleared:N0} purchase(s)");
                }

                var deleted = await DeleteAllAsync(target, tx, table, ctx.CancellationToken);
                if (deleted > 0)
                    ctx.Log($"  Deleted {deleted:N0} from {table}");
            }

            await tx.CommitAsync(ctx.CancellationToken);
            ctx.Log("  Cleared. Kept: medicines, company, users, roles, branches, chart of accounts.");
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

    private static async Task<int> NullLinkedPurchaseReturnIdsAsync(
        SqlConnection conn, SqlTransaction tx, CancellationToken ct)
    {
        await using (var exists = new SqlCommand(
                         """
                         SELECT CASE WHEN EXISTS (
                           SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                           WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Purchases'
                             AND COLUMN_NAME = 'LinkedPurchaseReturnId'
                         ) THEN 1 ELSE 0 END
                         """, conn, tx))
        {
            var ok = Convert.ToInt32(await exists.ExecuteScalarAsync(ct)) == 1;
            if (!ok) return 0;
        }

        await using var cmd = new SqlCommand(
            """
            UPDATE Purchases
            SET LinkedPurchaseReturnId = NULL,
                ReturnCreditApplied = 0
            WHERE LinkedPurchaseReturnId IS NOT NULL
               OR ReturnCreditApplied <> 0
            """, conn, tx);
        cmd.CommandTimeout = 0;
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
