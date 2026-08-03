using System.Text.Json;
using MySqlConnector;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Domain.Entities.System;

namespace PharmaPOS.WPF.Services;

public interface IMySqlReportingPublisher
{
    Task PublishAsync(SyncOutboxEntry entry, CancellationToken ct = default);
    Task TestConnectionAsync(CancellationToken ct = default);
}

public sealed class MySqlReportingPublisher : IMySqlReportingPublisher
{
    private readonly IMySqlSyncSettingsService _settings;

    public MySqlReportingPublisher(IMySqlSyncSettingsService settings)
    {
        _settings = settings;
    }

    public async Task TestConnectionAsync(CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = new MySqlCommand("SELECT 1", conn);
        _ = await cmd.ExecuteScalarAsync(ct);
    }

    public async Task PublishAsync(SyncOutboxEntry entry, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        using var doc = JsonDocument.Parse(entry.PayloadJson);
        var root = doc.RootElement;

        switch (entry.EntityType)
        {
            case ReportingSyncEntityTypes.Branch:
                await UpsertBranchAsync(conn, tx, root, ct);
                break;
            case ReportingSyncEntityTypes.Medicine:
                await UpsertMedicineAsync(conn, tx, root, ct);
                break;
            case ReportingSyncEntityTypes.MedicineBatch:
                await UpsertMedicineBatchAsync(conn, tx, root, ct);
                break;
            case ReportingSyncEntityTypes.Customer:
                await UpsertCustomerAsync(conn, tx, root, ct);
                break;
            case ReportingSyncEntityTypes.Sale:
                await UpsertSaleAggregateAsync(conn, tx, root, ct);
                break;
            case ReportingSyncEntityTypes.SaleReturn:
                await UpsertSaleReturnAggregateAsync(conn, tx, root, ct);
                break;
            case ReportingSyncEntityTypes.Purchase:
                await UpsertPurchaseAggregateAsync(conn, tx, root, ct);
                break;
            case ReportingSyncEntityTypes.PurchaseReturn:
                await UpsertPurchaseReturnAggregateAsync(conn, tx, root, ct);
                break;
            case ReportingSyncEntityTypes.StockMovement:
                await UpsertStockMovementAsync(conn, tx, root, ct);
                break;
            case ReportingSyncEntityTypes.StockTransfer:
                await UpsertStockTransferAggregateAsync(conn, tx, root, ct);
                break;
            default:
                throw new InvalidOperationException($"Unknown sync entity type: {entry.EntityType}");
        }

        await tx.CommitAsync(ct);
    }

    private MySqlConnection CreateConnection()
    {
        var s = _settings.Current;
        var builder = new MySqlConnectionStringBuilder
        {
            Server = s.Host,
            Port = (uint)s.Port,
            Database = s.Database,
            UserID = s.Username,
            Password = s.Password,
            SslMode = s.UseSsl ? MySqlSslMode.Required : MySqlSslMode.Preferred,
            ConnectionTimeout = 15,
            DefaultCommandTimeout = 60
        };
        return new MySqlConnection(builder.ConnectionString);
    }

    private static async Task UpsertBranchAsync(MySqlConnection conn, MySqlTransaction tx, JsonElement r, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO branches (store_id, local_id, code, name, address, city, state, pincode, phone, email,
              gst_number, drug_license_number, is_head_office, status, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @code, @name, @address, @city, @state, @pincode, @phone, @email,
              @gst_number, @drug_license_number, @is_head_office, @status, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              code=VALUES(code), name=VALUES(name), address=VALUES(address), city=VALUES(city), state=VALUES(state),
              pincode=VALUES(pincode), phone=VALUES(phone), email=VALUES(email), gst_number=VALUES(gst_number),
              drug_license_number=VALUES(drug_license_number), is_head_office=VALUES(is_head_office),
              status=VALUES(status), is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
            """;
        await using var cmd = new MySqlCommand(sql, conn, tx);
        Add(cmd, "@store_id", StoreKey(r));
        Add(cmd, "@local_id", Int(r, "local_id"));
        Add(cmd, "@code", Str(r, "code"));
        Add(cmd, "@name", Str(r, "name"));
        Add(cmd, "@address", StrOrNull(r, "address"));
        Add(cmd, "@city", StrOrNull(r, "city"));
        Add(cmd, "@state", StrOrNull(r, "state"));
        Add(cmd, "@pincode", StrOrNull(r, "pincode"));
        Add(cmd, "@phone", StrOrNull(r, "phone"));
        Add(cmd, "@email", StrOrNull(r, "email"));
        Add(cmd, "@gst_number", StrOrNull(r, "gst_number"));
        Add(cmd, "@drug_license_number", StrOrNull(r, "drug_license_number"));
        Add(cmd, "@is_head_office", Bool(r, "is_head_office"));
        Add(cmd, "@status", Int(r, "status"));
        Add(cmd, "@is_deleted", Bool(r, "is_deleted"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertMedicineAsync(MySqlConnection conn, MySqlTransaction tx, JsonElement r, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO medicines (store_id, local_id, name, generic_name, brand, composition, strength, dosage_form,
              hsn_code, gst_percent, barcode, mrp, purchase_price, selling_price, units_per_pack, reorder_level,
              status, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @name, @generic_name, @brand, @composition, @strength, @dosage_form,
              @hsn_code, @gst_percent, @barcode, @mrp, @purchase_price, @selling_price, @units_per_pack, @reorder_level,
              @status, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              name=VALUES(name), generic_name=VALUES(generic_name), brand=VALUES(brand), composition=VALUES(composition),
              strength=VALUES(strength), dosage_form=VALUES(dosage_form), hsn_code=VALUES(hsn_code),
              gst_percent=VALUES(gst_percent), barcode=VALUES(barcode), mrp=VALUES(mrp),
              purchase_price=VALUES(purchase_price), selling_price=VALUES(selling_price),
              units_per_pack=VALUES(units_per_pack), reorder_level=VALUES(reorder_level), status=VALUES(status),
              is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
            """;
        await using var cmd = new MySqlCommand(sql, conn, tx);
        Add(cmd, "@store_id", StoreKey(r));
        Add(cmd, "@local_id", Int(r, "local_id"));
        Add(cmd, "@name", Str(r, "name"));
        Add(cmd, "@generic_name", StrOrNull(r, "generic_name"));
        Add(cmd, "@brand", StrOrNull(r, "brand"));
        Add(cmd, "@composition", StrOrNull(r, "composition"));
        Add(cmd, "@strength", StrOrNull(r, "strength"));
        Add(cmd, "@dosage_form", Int(r, "dosage_form"));
        Add(cmd, "@hsn_code", StrOrNull(r, "hsn_code"));
        Add(cmd, "@gst_percent", Dec(r, "gst_percent"));
        Add(cmd, "@barcode", StrOrNull(r, "barcode"));
        Add(cmd, "@mrp", Dec(r, "mrp"));
        Add(cmd, "@purchase_price", Dec(r, "purchase_price"));
        Add(cmd, "@selling_price", Dec(r, "selling_price"));
        Add(cmd, "@units_per_pack", Int(r, "units_per_pack"));
        Add(cmd, "@reorder_level", Int(r, "reorder_level"));
        Add(cmd, "@status", Int(r, "status"));
        Add(cmd, "@is_deleted", Bool(r, "is_deleted"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertMedicineBatchAsync(MySqlConnection conn, MySqlTransaction tx, JsonElement r, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO medicine_batches (store_id, local_id, medicine_local_id, branch_local_id, batch_number,
              manufacturing_date, expiry_date, quantity_available, purchase_price, mrp, selling_price, gst_percent,
              rack_number, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @medicine_local_id, @branch_local_id, @batch_number,
              @manufacturing_date, @expiry_date, @quantity_available, @purchase_price, @mrp, @selling_price, @gst_percent,
              @rack_number, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              medicine_local_id=VALUES(medicine_local_id), branch_local_id=VALUES(branch_local_id),
              batch_number=VALUES(batch_number), manufacturing_date=VALUES(manufacturing_date),
              expiry_date=VALUES(expiry_date), quantity_available=VALUES(quantity_available),
              purchase_price=VALUES(purchase_price), mrp=VALUES(mrp), selling_price=VALUES(selling_price),
              gst_percent=VALUES(gst_percent), rack_number=VALUES(rack_number), is_deleted=VALUES(is_deleted),
              synced_at_utc=VALUES(synced_at_utc)
            """;
        await using var cmd = new MySqlCommand(sql, conn, tx);
        Add(cmd, "@store_id", StoreKey(r));
        Add(cmd, "@local_id", Int(r, "local_id"));
        Add(cmd, "@medicine_local_id", Int(r, "medicine_local_id"));
        Add(cmd, "@branch_local_id", IntOrNull(r, "branch_local_id"));
        Add(cmd, "@batch_number", Str(r, "batch_number"));
        Add(cmd, "@manufacturing_date", DateOrNull(r, "manufacturing_date"));
        Add(cmd, "@expiry_date", DateOrNull(r, "expiry_date"));
        Add(cmd, "@quantity_available", Dec(r, "quantity_available"));
        Add(cmd, "@purchase_price", Dec(r, "purchase_price"));
        Add(cmd, "@mrp", Dec(r, "mrp"));
        Add(cmd, "@selling_price", Dec(r, "selling_price"));
        Add(cmd, "@gst_percent", Dec(r, "gst_percent"));
        Add(cmd, "@rack_number", StrOrNull(r, "rack_number"));
        Add(cmd, "@is_deleted", Bool(r, "is_deleted"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertCustomerAsync(MySqlConnection conn, MySqlTransaction tx, JsonElement r, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO customers (store_id, local_id, branch_local_id, name, type, phone, email, gst_number,
              address, city, credit_limit, outstanding_balance, reward_points, status, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @branch_local_id, @name, @type, @phone, @email, @gst_number,
              @address, @city, @credit_limit, @outstanding_balance, @reward_points, @status, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              branch_local_id=VALUES(branch_local_id), name=VALUES(name), type=VALUES(type), phone=VALUES(phone),
              email=VALUES(email), gst_number=VALUES(gst_number), address=VALUES(address), city=VALUES(city),
              credit_limit=VALUES(credit_limit), outstanding_balance=VALUES(outstanding_balance),
              reward_points=VALUES(reward_points), status=VALUES(status), is_deleted=VALUES(is_deleted),
              synced_at_utc=VALUES(synced_at_utc)
            """;
        await using var cmd = new MySqlCommand(sql, conn, tx);
        Add(cmd, "@store_id", StoreKey(r));
        Add(cmd, "@local_id", Int(r, "local_id"));
        Add(cmd, "@branch_local_id", IntOrNull(r, "branch_local_id"));
        Add(cmd, "@name", Str(r, "name"));
        Add(cmd, "@type", Int(r, "type"));
        Add(cmd, "@phone", StrOrNull(r, "phone"));
        Add(cmd, "@email", StrOrNull(r, "email"));
        Add(cmd, "@gst_number", StrOrNull(r, "gst_number"));
        Add(cmd, "@address", StrOrNull(r, "address"));
        Add(cmd, "@city", StrOrNull(r, "city"));
        Add(cmd, "@credit_limit", Dec(r, "credit_limit"));
        Add(cmd, "@outstanding_balance", Dec(r, "outstanding_balance"));
        Add(cmd, "@reward_points", Int(r, "reward_points"));
        Add(cmd, "@status", Int(r, "status"));
        Add(cmd, "@is_deleted", Bool(r, "is_deleted"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertSaleAggregateAsync(MySqlConnection conn, MySqlTransaction tx, JsonElement r, CancellationToken ct)
    {
        var store = StoreKey(r);
        var saleId = Int(r, "local_id");
        const string sql = """
            INSERT INTO sales (store_id, local_id, branch_local_id, invoice_number, invoice_date, customer_local_id,
              billing_customer_name, billing_customer_phone, billing_customer_address, billing_doctor_name,
              sub_total, discount_amount, taxable_amount, cgst_amount, sgst_amount, igst_amount, round_off,
              grand_total, paid_amount, change_returned, status, payment_status, remarks, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @branch_local_id, @invoice_number, @invoice_date, @customer_local_id,
              @billing_customer_name, @billing_customer_phone, @billing_customer_address, @billing_doctor_name,
              @sub_total, @discount_amount, @taxable_amount, @cgst_amount, @sgst_amount, @igst_amount, @round_off,
              @grand_total, @paid_amount, @change_returned, @status, @payment_status, @remarks, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              branch_local_id=VALUES(branch_local_id), invoice_number=VALUES(invoice_number), invoice_date=VALUES(invoice_date),
              customer_local_id=VALUES(customer_local_id), billing_customer_name=VALUES(billing_customer_name),
              billing_customer_phone=VALUES(billing_customer_phone), billing_customer_address=VALUES(billing_customer_address),
              billing_doctor_name=VALUES(billing_doctor_name), sub_total=VALUES(sub_total), discount_amount=VALUES(discount_amount),
              taxable_amount=VALUES(taxable_amount), cgst_amount=VALUES(cgst_amount), sgst_amount=VALUES(sgst_amount),
              igst_amount=VALUES(igst_amount), round_off=VALUES(round_off), grand_total=VALUES(grand_total),
              paid_amount=VALUES(paid_amount), change_returned=VALUES(change_returned), status=VALUES(status),
              payment_status=VALUES(payment_status), remarks=VALUES(remarks), is_deleted=VALUES(is_deleted),
              synced_at_utc=VALUES(synced_at_utc)
            """;
        await using (var cmd = new MySqlCommand(sql, conn, tx))
        {
            Add(cmd, "@store_id", store);
            Add(cmd, "@local_id", saleId);
            Add(cmd, "@branch_local_id", IntOrNull(r, "branch_local_id"));
            Add(cmd, "@invoice_number", Str(r, "invoice_number"));
            Add(cmd, "@invoice_date", DateTimeVal(r, "invoice_date"));
            Add(cmd, "@customer_local_id", IntOrNull(r, "customer_local_id"));
            Add(cmd, "@billing_customer_name", StrOrNull(r, "billing_customer_name"));
            Add(cmd, "@billing_customer_phone", StrOrNull(r, "billing_customer_phone"));
            Add(cmd, "@billing_customer_address", StrOrNull(r, "billing_customer_address"));
            Add(cmd, "@billing_doctor_name", StrOrNull(r, "billing_doctor_name"));
            Add(cmd, "@sub_total", Dec(r, "sub_total"));
            Add(cmd, "@discount_amount", Dec(r, "discount_amount"));
            Add(cmd, "@taxable_amount", Dec(r, "taxable_amount"));
            Add(cmd, "@cgst_amount", Dec(r, "cgst_amount"));
            Add(cmd, "@sgst_amount", Dec(r, "sgst_amount"));
            Add(cmd, "@igst_amount", Dec(r, "igst_amount"));
            Add(cmd, "@round_off", Dec(r, "round_off"));
            Add(cmd, "@grand_total", Dec(r, "grand_total"));
            Add(cmd, "@paid_amount", Dec(r, "paid_amount"));
            Add(cmd, "@change_returned", Dec(r, "change_returned"));
            Add(cmd, "@status", Int(r, "status"));
            Add(cmd, "@payment_status", Int(r, "payment_status"));
            Add(cmd, "@remarks", StrOrNull(r, "remarks"));
            Add(cmd, "@is_deleted", Bool(r, "is_deleted"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await ReplaceChildrenAsync(conn, tx, "sale_items", "sale_local_id", store, saleId, r, "items", UpsertSaleItemAsync, ct);
        await ReplaceChildrenAsync(conn, tx, "sale_payments", "sale_local_id", store, saleId, r, "payments", UpsertSalePaymentAsync, ct);
    }

    private static async Task UpsertSaleItemAsync(MySqlConnection conn, MySqlTransaction tx, string store, JsonElement i, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO sale_items (store_id, local_id, sale_local_id, medicine_local_id, medicine_batch_local_id,
              batch_number, expiry_date, quantity, mrp, unit_price, discount_percent, discount_amount, gst_percent,
              taxable_amount, tax_amount, line_total, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @sale_local_id, @medicine_local_id, @medicine_batch_local_id,
              @batch_number, @expiry_date, @quantity, @mrp, @unit_price, @discount_percent, @discount_amount, @gst_percent,
              @taxable_amount, @tax_amount, @line_total, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              sale_local_id=VALUES(sale_local_id), medicine_local_id=VALUES(medicine_local_id),
              medicine_batch_local_id=VALUES(medicine_batch_local_id), batch_number=VALUES(batch_number),
              expiry_date=VALUES(expiry_date), quantity=VALUES(quantity), mrp=VALUES(mrp), unit_price=VALUES(unit_price),
              discount_percent=VALUES(discount_percent), discount_amount=VALUES(discount_amount),
              gst_percent=VALUES(gst_percent), taxable_amount=VALUES(taxable_amount), tax_amount=VALUES(tax_amount),
              line_total=VALUES(line_total), is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
            """;
        await using var cmd = new MySqlCommand(sql, conn, tx);
        Add(cmd, "@store_id", store);
        Add(cmd, "@local_id", Int(i, "local_id"));
        Add(cmd, "@sale_local_id", Int(i, "sale_local_id"));
        Add(cmd, "@medicine_local_id", Int(i, "medicine_local_id"));
        Add(cmd, "@medicine_batch_local_id", IntOrNull(i, "medicine_batch_local_id"));
        Add(cmd, "@batch_number", StrOrNull(i, "batch_number"));
        Add(cmd, "@expiry_date", DateOrNull(i, "expiry_date"));
        Add(cmd, "@quantity", Dec(i, "quantity"));
        Add(cmd, "@mrp", Dec(i, "mrp"));
        Add(cmd, "@unit_price", Dec(i, "unit_price"));
        Add(cmd, "@discount_percent", Dec(i, "discount_percent"));
        Add(cmd, "@discount_amount", Dec(i, "discount_amount"));
        Add(cmd, "@gst_percent", Dec(i, "gst_percent"));
        Add(cmd, "@taxable_amount", Dec(i, "taxable_amount"));
        Add(cmd, "@tax_amount", Dec(i, "tax_amount"));
        Add(cmd, "@line_total", Dec(i, "line_total"));
        Add(cmd, "@is_deleted", Bool(i, "is_deleted"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertSalePaymentAsync(MySqlConnection conn, MySqlTransaction tx, string store, JsonElement p, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO sale_payments (store_id, local_id, sale_local_id, method, amount, reference_number,
              payment_date_utc, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @sale_local_id, @method, @amount, @reference_number,
              @payment_date_utc, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              sale_local_id=VALUES(sale_local_id), method=VALUES(method), amount=VALUES(amount),
              reference_number=VALUES(reference_number), payment_date_utc=VALUES(payment_date_utc),
              is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
            """;
        await using var cmd = new MySqlCommand(sql, conn, tx);
        Add(cmd, "@store_id", store);
        Add(cmd, "@local_id", Int(p, "local_id"));
        Add(cmd, "@sale_local_id", Int(p, "sale_local_id"));
        Add(cmd, "@method", Int(p, "method"));
        Add(cmd, "@amount", Dec(p, "amount"));
        Add(cmd, "@reference_number", StrOrNull(p, "reference_number"));
        Add(cmd, "@payment_date_utc", DateTimeOrNull(p, "payment_date_utc"));
        Add(cmd, "@is_deleted", Bool(p, "is_deleted"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertSaleReturnAggregateAsync(MySqlConnection conn, MySqlTransaction tx, JsonElement r, CancellationToken ct)
    {
        var store = StoreKey(r);
        var id = Int(r, "local_id");
        const string sql = """
            INSERT INTO sale_returns (store_id, local_id, branch_local_id, return_number, return_date, sale_local_id,
              customer_local_id, grand_total, refund_amount, status, remarks, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @branch_local_id, @return_number, @return_date, @sale_local_id,
              @customer_local_id, @grand_total, @refund_amount, @status, @remarks, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              branch_local_id=VALUES(branch_local_id), return_number=VALUES(return_number), return_date=VALUES(return_date),
              sale_local_id=VALUES(sale_local_id), customer_local_id=VALUES(customer_local_id),
              grand_total=VALUES(grand_total), refund_amount=VALUES(refund_amount), status=VALUES(status),
              remarks=VALUES(remarks), is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
            """;
        await using (var cmd = new MySqlCommand(sql, conn, tx))
        {
            Add(cmd, "@store_id", store);
            Add(cmd, "@local_id", id);
            Add(cmd, "@branch_local_id", IntOrNull(r, "branch_local_id"));
            Add(cmd, "@return_number", Str(r, "return_number"));
            Add(cmd, "@return_date", DateTimeVal(r, "return_date"));
            Add(cmd, "@sale_local_id", IntOrNull(r, "sale_local_id"));
            Add(cmd, "@customer_local_id", IntOrNull(r, "customer_local_id"));
            Add(cmd, "@grand_total", Dec(r, "grand_total"));
            Add(cmd, "@refund_amount", Dec(r, "refund_amount"));
            Add(cmd, "@status", Int(r, "status"));
            Add(cmd, "@remarks", StrOrNull(r, "remarks"));
            Add(cmd, "@is_deleted", Bool(r, "is_deleted"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await ReplaceChildrenAsync(conn, tx, "sale_return_items", "sale_return_local_id", store, id, r, "items",
            async (c, t, s, i, token) =>
            {
                const string itemSql = """
                    INSERT INTO sale_return_items (store_id, local_id, sale_return_local_id, medicine_local_id,
                      medicine_batch_local_id, batch_number, quantity, unit_price, line_total, is_deleted, synced_at_utc)
                    VALUES (@store_id, @local_id, @sale_return_local_id, @medicine_local_id,
                      @medicine_batch_local_id, @batch_number, @quantity, @unit_price, @line_total, @is_deleted, UTC_TIMESTAMP(6))
                    ON DUPLICATE KEY UPDATE
                      sale_return_local_id=VALUES(sale_return_local_id), medicine_local_id=VALUES(medicine_local_id),
                      medicine_batch_local_id=VALUES(medicine_batch_local_id), batch_number=VALUES(batch_number),
                      quantity=VALUES(quantity), unit_price=VALUES(unit_price), line_total=VALUES(line_total),
                      is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
                    """;
                await using var cmd = new MySqlCommand(itemSql, c, t);
                Add(cmd, "@store_id", s);
                Add(cmd, "@local_id", Int(i, "local_id"));
                Add(cmd, "@sale_return_local_id", Int(i, "sale_return_local_id"));
                Add(cmd, "@medicine_local_id", Int(i, "medicine_local_id"));
                Add(cmd, "@medicine_batch_local_id", IntOrNull(i, "medicine_batch_local_id"));
                Add(cmd, "@batch_number", StrOrNull(i, "batch_number"));
                Add(cmd, "@quantity", Dec(i, "quantity"));
                Add(cmd, "@unit_price", Dec(i, "unit_price"));
                Add(cmd, "@line_total", Dec(i, "line_total"));
                Add(cmd, "@is_deleted", Bool(i, "is_deleted"));
                await cmd.ExecuteNonQueryAsync(token);
            }, ct);
    }

    private static async Task UpsertPurchaseAggregateAsync(MySqlConnection conn, MySqlTransaction tx, JsonElement r, CancellationToken ct)
    {
        var store = StoreKey(r);
        var id = Int(r, "local_id");
        const string sql = """
            INSERT INTO purchases (store_id, local_id, branch_local_id, invoice_number, supplier_invoice_number,
              invoice_date, supplier_local_id, sub_total, discount_amount, taxable_amount, cgst_amount, sgst_amount,
              igst_amount, round_off, grand_total, paid_amount, status, payment_status, remarks, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @branch_local_id, @invoice_number, @supplier_invoice_number,
              @invoice_date, @supplier_local_id, @sub_total, @discount_amount, @taxable_amount, @cgst_amount, @sgst_amount,
              @igst_amount, @round_off, @grand_total, @paid_amount, @status, @payment_status, @remarks, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              branch_local_id=VALUES(branch_local_id), invoice_number=VALUES(invoice_number),
              supplier_invoice_number=VALUES(supplier_invoice_number), invoice_date=VALUES(invoice_date),
              supplier_local_id=VALUES(supplier_local_id), sub_total=VALUES(sub_total), discount_amount=VALUES(discount_amount),
              taxable_amount=VALUES(taxable_amount), cgst_amount=VALUES(cgst_amount), sgst_amount=VALUES(sgst_amount),
              igst_amount=VALUES(igst_amount), round_off=VALUES(round_off), grand_total=VALUES(grand_total),
              paid_amount=VALUES(paid_amount), status=VALUES(status), payment_status=VALUES(payment_status),
              remarks=VALUES(remarks), is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
            """;
        await using (var cmd = new MySqlCommand(sql, conn, tx))
        {
            Add(cmd, "@store_id", store);
            Add(cmd, "@local_id", id);
            Add(cmd, "@branch_local_id", IntOrNull(r, "branch_local_id"));
            Add(cmd, "@invoice_number", Str(r, "invoice_number"));
            Add(cmd, "@supplier_invoice_number", StrOrNull(r, "supplier_invoice_number"));
            Add(cmd, "@invoice_date", DateTimeVal(r, "invoice_date"));
            Add(cmd, "@supplier_local_id", Int(r, "supplier_local_id"));
            Add(cmd, "@sub_total", Dec(r, "sub_total"));
            Add(cmd, "@discount_amount", Dec(r, "discount_amount"));
            Add(cmd, "@taxable_amount", Dec(r, "taxable_amount"));
            Add(cmd, "@cgst_amount", Dec(r, "cgst_amount"));
            Add(cmd, "@sgst_amount", Dec(r, "sgst_amount"));
            Add(cmd, "@igst_amount", Dec(r, "igst_amount"));
            Add(cmd, "@round_off", Dec(r, "round_off"));
            Add(cmd, "@grand_total", Dec(r, "grand_total"));
            Add(cmd, "@paid_amount", Dec(r, "paid_amount"));
            Add(cmd, "@status", Int(r, "status"));
            Add(cmd, "@payment_status", Int(r, "payment_status"));
            Add(cmd, "@remarks", StrOrNull(r, "remarks"));
            Add(cmd, "@is_deleted", Bool(r, "is_deleted"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await ReplaceChildrenAsync(conn, tx, "purchase_items", "purchase_local_id", store, id, r, "items",
            async (c, t, s, i, token) =>
            {
                const string itemSql = """
                    INSERT INTO purchase_items (store_id, local_id, purchase_local_id, medicine_local_id,
                      medicine_batch_local_id, batch_number, expiry_date, quantity, free_quantity, purchase_price,
                      mrp, gst_percent, line_total, is_deleted, synced_at_utc)
                    VALUES (@store_id, @local_id, @purchase_local_id, @medicine_local_id,
                      @medicine_batch_local_id, @batch_number, @expiry_date, @quantity, @free_quantity, @purchase_price,
                      @mrp, @gst_percent, @line_total, @is_deleted, UTC_TIMESTAMP(6))
                    ON DUPLICATE KEY UPDATE
                      purchase_local_id=VALUES(purchase_local_id), medicine_local_id=VALUES(medicine_local_id),
                      medicine_batch_local_id=VALUES(medicine_batch_local_id), batch_number=VALUES(batch_number),
                      expiry_date=VALUES(expiry_date), quantity=VALUES(quantity), free_quantity=VALUES(free_quantity),
                      purchase_price=VALUES(purchase_price), mrp=VALUES(mrp), gst_percent=VALUES(gst_percent),
                      line_total=VALUES(line_total), is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
                    """;
                await using var cmd = new MySqlCommand(itemSql, c, t);
                Add(cmd, "@store_id", s);
                Add(cmd, "@local_id", Int(i, "local_id"));
                Add(cmd, "@purchase_local_id", Int(i, "purchase_local_id"));
                Add(cmd, "@medicine_local_id", Int(i, "medicine_local_id"));
                Add(cmd, "@medicine_batch_local_id", IntOrNull(i, "medicine_batch_local_id"));
                Add(cmd, "@batch_number", StrOrNull(i, "batch_number"));
                Add(cmd, "@expiry_date", DateOrNull(i, "expiry_date"));
                Add(cmd, "@quantity", Dec(i, "quantity"));
                Add(cmd, "@free_quantity", Dec(i, "free_quantity"));
                Add(cmd, "@purchase_price", Dec(i, "purchase_price"));
                Add(cmd, "@mrp", Dec(i, "mrp"));
                Add(cmd, "@gst_percent", Dec(i, "gst_percent"));
                Add(cmd, "@line_total", Dec(i, "line_total"));
                Add(cmd, "@is_deleted", Bool(i, "is_deleted"));
                await cmd.ExecuteNonQueryAsync(token);
            }, ct);
    }

    private static async Task UpsertPurchaseReturnAggregateAsync(MySqlConnection conn, MySqlTransaction tx, JsonElement r, CancellationToken ct)
    {
        var store = StoreKey(r);
        var id = Int(r, "local_id");
        const string sql = """
            INSERT INTO purchase_returns (store_id, local_id, branch_local_id, return_number, return_date,
              purchase_local_id, supplier_local_id, grand_total, status, remarks, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @branch_local_id, @return_number, @return_date,
              @purchase_local_id, @supplier_local_id, @grand_total, @status, @remarks, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              branch_local_id=VALUES(branch_local_id), return_number=VALUES(return_number), return_date=VALUES(return_date),
              purchase_local_id=VALUES(purchase_local_id), supplier_local_id=VALUES(supplier_local_id),
              grand_total=VALUES(grand_total), status=VALUES(status), remarks=VALUES(remarks),
              is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
            """;
        await using (var cmd = new MySqlCommand(sql, conn, tx))
        {
            Add(cmd, "@store_id", store);
            Add(cmd, "@local_id", id);
            Add(cmd, "@branch_local_id", IntOrNull(r, "branch_local_id"));
            Add(cmd, "@return_number", Str(r, "return_number"));
            Add(cmd, "@return_date", DateTimeVal(r, "return_date"));
            Add(cmd, "@purchase_local_id", IntOrNull(r, "purchase_local_id"));
            Add(cmd, "@supplier_local_id", IntOrNull(r, "supplier_local_id"));
            Add(cmd, "@grand_total", Dec(r, "grand_total"));
            Add(cmd, "@status", Int(r, "status"));
            Add(cmd, "@remarks", StrOrNull(r, "remarks"));
            Add(cmd, "@is_deleted", Bool(r, "is_deleted"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await ReplaceChildrenAsync(conn, tx, "purchase_return_items", "purchase_return_local_id", store, id, r, "items",
            async (c, t, s, i, token) =>
            {
                const string itemSql = """
                    INSERT INTO purchase_return_items (store_id, local_id, purchase_return_local_id, medicine_local_id,
                      medicine_batch_local_id, batch_number, quantity, purchase_price, line_total, is_deleted, synced_at_utc)
                    VALUES (@store_id, @local_id, @purchase_return_local_id, @medicine_local_id,
                      @medicine_batch_local_id, @batch_number, @quantity, @purchase_price, @line_total, @is_deleted, UTC_TIMESTAMP(6))
                    ON DUPLICATE KEY UPDATE
                      purchase_return_local_id=VALUES(purchase_return_local_id), medicine_local_id=VALUES(medicine_local_id),
                      medicine_batch_local_id=VALUES(medicine_batch_local_id), batch_number=VALUES(batch_number),
                      quantity=VALUES(quantity), purchase_price=VALUES(purchase_price), line_total=VALUES(line_total),
                      is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
                    """;
                await using var cmd = new MySqlCommand(itemSql, c, t);
                Add(cmd, "@store_id", s);
                Add(cmd, "@local_id", Int(i, "local_id"));
                Add(cmd, "@purchase_return_local_id", Int(i, "purchase_return_local_id"));
                Add(cmd, "@medicine_local_id", Int(i, "medicine_local_id"));
                Add(cmd, "@medicine_batch_local_id", IntOrNull(i, "medicine_batch_local_id"));
                Add(cmd, "@batch_number", StrOrNull(i, "batch_number"));
                Add(cmd, "@quantity", Dec(i, "quantity"));
                Add(cmd, "@purchase_price", Dec(i, "purchase_price"));
                Add(cmd, "@line_total", Dec(i, "line_total"));
                Add(cmd, "@is_deleted", Bool(i, "is_deleted"));
                await cmd.ExecuteNonQueryAsync(token);
            }, ct);
    }

    private static async Task UpsertStockMovementAsync(MySqlConnection conn, MySqlTransaction tx, JsonElement r, CancellationToken ct)
    {
        const string sql = """
            INSERT INTO stock_movements (store_id, local_id, branch_local_id, medicine_local_id, medicine_batch_local_id,
              movement_type, quantity, balance_after, unit_cost, reference_type, reference_id, reference_number,
              remarks, movement_date_utc, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @branch_local_id, @medicine_local_id, @medicine_batch_local_id,
              @movement_type, @quantity, @balance_after, @unit_cost, @reference_type, @reference_id, @reference_number,
              @remarks, @movement_date_utc, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              branch_local_id=VALUES(branch_local_id), medicine_local_id=VALUES(medicine_local_id),
              medicine_batch_local_id=VALUES(medicine_batch_local_id), movement_type=VALUES(movement_type),
              quantity=VALUES(quantity), balance_after=VALUES(balance_after), unit_cost=VALUES(unit_cost),
              reference_type=VALUES(reference_type), reference_id=VALUES(reference_id),
              reference_number=VALUES(reference_number), remarks=VALUES(remarks),
              movement_date_utc=VALUES(movement_date_utc), is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
            """;
        await using var cmd = new MySqlCommand(sql, conn, tx);
        Add(cmd, "@store_id", StoreKey(r));
        Add(cmd, "@local_id", Int(r, "local_id"));
        Add(cmd, "@branch_local_id", IntOrNull(r, "branch_local_id"));
        Add(cmd, "@medicine_local_id", Int(r, "medicine_local_id"));
        Add(cmd, "@medicine_batch_local_id", IntOrNull(r, "medicine_batch_local_id"));
        Add(cmd, "@movement_type", Int(r, "movement_type"));
        Add(cmd, "@quantity", Dec(r, "quantity"));
        Add(cmd, "@balance_after", Dec(r, "balance_after"));
        Add(cmd, "@unit_cost", Dec(r, "unit_cost"));
        Add(cmd, "@reference_type", StrOrNull(r, "reference_type"));
        Add(cmd, "@reference_id", IntOrNull(r, "reference_id"));
        Add(cmd, "@reference_number", StrOrNull(r, "reference_number"));
        Add(cmd, "@remarks", StrOrNull(r, "remarks"));
        Add(cmd, "@movement_date_utc", DateTimeVal(r, "movement_date_utc"));
        Add(cmd, "@is_deleted", Bool(r, "is_deleted"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertStockTransferAggregateAsync(MySqlConnection conn, MySqlTransaction tx, JsonElement r, CancellationToken ct)
    {
        var store = StoreKey(r);
        var id = Int(r, "local_id");
        const string sql = """
            INSERT INTO stock_transfers (store_id, local_id, branch_local_id, transfer_number, transfer_date, kind,
              status, to_branch_local_id, from_branch_code, from_branch_name, to_branch_code, to_branch_name,
              package_key, remarks, is_deleted, synced_at_utc)
            VALUES (@store_id, @local_id, @branch_local_id, @transfer_number, @transfer_date, @kind,
              @status, @to_branch_local_id, @from_branch_code, @from_branch_name, @to_branch_code, @to_branch_name,
              @package_key, @remarks, @is_deleted, UTC_TIMESTAMP(6))
            ON DUPLICATE KEY UPDATE
              branch_local_id=VALUES(branch_local_id), transfer_number=VALUES(transfer_number),
              transfer_date=VALUES(transfer_date), kind=VALUES(kind), status=VALUES(status),
              to_branch_local_id=VALUES(to_branch_local_id), from_branch_code=VALUES(from_branch_code),
              from_branch_name=VALUES(from_branch_name), to_branch_code=VALUES(to_branch_code),
              to_branch_name=VALUES(to_branch_name), package_key=VALUES(package_key), remarks=VALUES(remarks),
              is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
            """;
        await using (var cmd = new MySqlCommand(sql, conn, tx))
        {
            Add(cmd, "@store_id", store);
            Add(cmd, "@local_id", id);
            Add(cmd, "@branch_local_id", IntOrNull(r, "branch_local_id"));
            Add(cmd, "@transfer_number", Str(r, "transfer_number"));
            Add(cmd, "@transfer_date", DateTimeVal(r, "transfer_date"));
            Add(cmd, "@kind", Int(r, "kind"));
            Add(cmd, "@status", Int(r, "status"));
            Add(cmd, "@to_branch_local_id", IntOrNull(r, "to_branch_local_id"));
            Add(cmd, "@from_branch_code", StrOrNull(r, "from_branch_code"));
            Add(cmd, "@from_branch_name", StrOrNull(r, "from_branch_name"));
            Add(cmd, "@to_branch_code", StrOrNull(r, "to_branch_code"));
            Add(cmd, "@to_branch_name", StrOrNull(r, "to_branch_name"));
            Add(cmd, "@package_key", StrOrNull(r, "package_key"));
            Add(cmd, "@remarks", StrOrNull(r, "remarks"));
            Add(cmd, "@is_deleted", Bool(r, "is_deleted"));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await ReplaceChildrenAsync(conn, tx, "stock_transfer_items", "stock_transfer_local_id", store, id, r, "items",
            async (c, t, s, i, token) =>
            {
                const string itemSql = """
                    INSERT INTO stock_transfer_items (store_id, local_id, stock_transfer_local_id, medicine_local_id,
                      medicine_name, medicine_barcode, batch_number, expiry_date, quantity, purchase_price, mrp,
                      selling_price, is_deleted, synced_at_utc)
                    VALUES (@store_id, @local_id, @stock_transfer_local_id, @medicine_local_id,
                      @medicine_name, @medicine_barcode, @batch_number, @expiry_date, @quantity, @purchase_price, @mrp,
                      @selling_price, @is_deleted, UTC_TIMESTAMP(6))
                    ON DUPLICATE KEY UPDATE
                      stock_transfer_local_id=VALUES(stock_transfer_local_id), medicine_local_id=VALUES(medicine_local_id),
                      medicine_name=VALUES(medicine_name), medicine_barcode=VALUES(medicine_barcode),
                      batch_number=VALUES(batch_number), expiry_date=VALUES(expiry_date), quantity=VALUES(quantity),
                      purchase_price=VALUES(purchase_price), mrp=VALUES(mrp), selling_price=VALUES(selling_price),
                      is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
                    """;
                await using var cmd = new MySqlCommand(itemSql, c, t);
                Add(cmd, "@store_id", s);
                Add(cmd, "@local_id", Int(i, "local_id"));
                Add(cmd, "@stock_transfer_local_id", Int(i, "stock_transfer_local_id"));
                Add(cmd, "@medicine_local_id", Int(i, "medicine_local_id"));
                Add(cmd, "@medicine_name", StrOrNull(i, "medicine_name"));
                Add(cmd, "@medicine_barcode", StrOrNull(i, "medicine_barcode"));
                Add(cmd, "@batch_number", StrOrNull(i, "batch_number"));
                Add(cmd, "@expiry_date", DateOrNull(i, "expiry_date"));
                Add(cmd, "@quantity", Dec(i, "quantity"));
                Add(cmd, "@purchase_price", Dec(i, "purchase_price"));
                Add(cmd, "@mrp", Dec(i, "mrp"));
                Add(cmd, "@selling_price", Dec(i, "selling_price"));
                Add(cmd, "@is_deleted", Bool(i, "is_deleted"));
                await cmd.ExecuteNonQueryAsync(token);
            }, ct);
    }

    private static async Task ReplaceChildrenAsync(
        MySqlConnection conn,
        MySqlTransaction tx,
        string table,
        string parentCol,
        string store,
        int parentId,
        JsonElement root,
        string arrayProp,
        Func<MySqlConnection, MySqlTransaction, string, JsonElement, CancellationToken, Task> upsert,
        CancellationToken ct)
    {
        await using (var del = new MySqlCommand(
                           $"DELETE FROM {table} WHERE store_id=@store AND {parentCol}=@parent", conn, tx))
        {
            Add(del, "@store", store);
            Add(del, "@parent", parentId);
            await del.ExecuteNonQueryAsync(ct);
        }

        if (!root.TryGetProperty(arrayProp, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return;

        foreach (var child in arr.EnumerateArray())
            await upsert(conn, tx, store, child, ct);
    }

    private static void Add(MySqlCommand cmd, string name, object? value)
        => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    /// <summary>Tenant key: prefers store_id; falls back to legacy store_code in older outbox payloads.</summary>
    private static string StoreKey(JsonElement e)
    {
        var id = Str(e, "store_id");
        if (!string.IsNullOrWhiteSpace(id)) return id;
        return Str(e, "store_code");
    }

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? string.Empty
            : e.TryGetProperty(name, out var n) ? n.ToString() : string.Empty;

    private static string? StrOrNull(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return p.ValueKind == JsonValueKind.String ? p.GetString() : p.ToString();
    }

    private static int Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.TryGetInt32(out var v) ? v : 0;

    private static int? IntOrNull(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return p.TryGetInt32(out var v) ? v : null;
    }

    private static decimal Dec(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.TryGetDecimal(out var v) ? v : 0m;

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static DateTime DateTimeVal(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.TryGetDateTime(out var v) ? v : DateTime.UtcNow;

    private static DateTime? DateTimeOrNull(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        return p.TryGetDateTime(out var v) ? v : null;
    }

    private static object? DateOrNull(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (p.TryGetDateTime(out var v)) return v.Date;
        return null;
    }
}
