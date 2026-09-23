using System.Data;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using MySqlConnector;

// Bulk-copy PharmaPOS LocalDB → MySQL pharmapos_reporting for the web dashboard.
// Usage:
//   dotnet run --project tools/PharmaPOS.ReportingBackfill -- [storeId]
// Default storeId is taken from existing MySQL sales / arg.

const string SqlConn =
    "Server=(localdb)\\MSSQLLocalDB;Database=PharmaPosDb;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";
const string MySqlConn =
    "Server=127.0.0.1;Port=3306;Database=pharmapos_reporting;User ID=pharmapos;Password=PharmaPos@Report2026;SslMode=Preferred;AllowLoadLocalInfile=true;MaximumPoolSize=5;DefaultCommandTimeout=600";

var storeId = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
    ? args[0].Trim().ToUpperInvariant()
    : "S584D2A50B8B1F9BD1B361CBF93AFD404";

Console.WriteLine($"PharmaPOS → MySQL reporting backfill");
Console.WriteLine($"StoreId: {storeId}");
Console.WriteLine();

await using var sql = new SqlConnection(SqlConn);
await sql.OpenAsync();
await using var mysql = new MySqlConnection(MySqlConn);
await mysql.OpenAsync();
await using (var set = new MySqlCommand("SET NAMES utf8mb4 COLLATE utf8mb4_unicode_ci; SET FOREIGN_KEY_CHECKS=0;", mysql))
    await set.ExecuteNonQueryAsync();

var sid = Esc(storeId);

await CopyTable(
    "branches",
    """
    SELECT Id, Code, Name, Address, City, State, Pincode, Phone, Email,
           GstNumber, DrugLicenseNumber,
           CASE WHEN IsHeadOffice=1 THEN 1 ELSE 0 END, Status,
           CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END
    FROM Branches
    """,
    """
    INSERT INTO branches (store_id, local_id, code, name, address, city, state, pincode, phone, email,
      gst_number, drug_license_number, is_head_office, status, is_deleted, synced_at_utc)
    VALUES {0}
    ON DUPLICATE KEY UPDATE
      code=VALUES(code), name=VALUES(name), address=VALUES(address), city=VALUES(city),
      state=VALUES(state), pincode=VALUES(pincode), phone=VALUES(phone), email=VALUES(email),
      gst_number=VALUES(gst_number), drug_license_number=VALUES(drug_license_number),
      is_head_office=VALUES(is_head_office), status=VALUES(status), is_deleted=VALUES(is_deleted),
      synced_at_utc=VALUES(synced_at_utc)
    """,
    r => $"({sid},{I(r,0)},{S(r,1)},{S(r,2)},{S(r,3)},{S(r,4)},{S(r,5)},{S(r,6)},{S(r,7)},{S(r,8)},{S(r,9)},{S(r,10)},{I(r,11)},{I(r,12)},{I(r,13)},UTC_TIMESTAMP(6))");

// Only medicines referenced by stock / sales / purchases (full 284k catalogue is optional).
await CopyTable(
    "medicines (referenced)",
    """
    SELECT m.Id, m.Name, m.GenericName, m.Brand, m.Composition, m.Strength, m.DosageForm, m.HsnCode,
           m.GstPercent, m.Barcode, m.Mrp, m.PurchasePrice, m.SellingPrice, m.UnitsPerPack,
           m.ReorderLevel, m.Status, CASE WHEN m.IsDeleted=1 THEN 1 ELSE 0 END
    FROM Medicines m
    WHERE m.Id IN (
      SELECT DISTINCT MedicineId FROM MedicineBatches WHERE MedicineId IS NOT NULL
      UNION SELECT DISTINCT MedicineId FROM SaleItems WHERE MedicineId IS NOT NULL
      UNION SELECT DISTINCT MedicineId FROM PurchaseItems WHERE MedicineId IS NOT NULL
    )
    """,
    """
    INSERT INTO medicines (store_id, local_id, name, generic_name, brand, composition, strength, dosage_form,
      hsn_code, gst_percent, barcode, mrp, purchase_price, selling_price, units_per_pack, reorder_level,
      status, is_deleted, synced_at_utc)
    VALUES {0}
    ON DUPLICATE KEY UPDATE
      name=VALUES(name), generic_name=VALUES(generic_name), brand=VALUES(brand),
      composition=VALUES(composition), strength=VALUES(strength), dosage_form=VALUES(dosage_form),
      hsn_code=VALUES(hsn_code), gst_percent=VALUES(gst_percent), barcode=VALUES(barcode),
      mrp=VALUES(mrp), purchase_price=VALUES(purchase_price), selling_price=VALUES(selling_price),
      units_per_pack=VALUES(units_per_pack), reorder_level=VALUES(reorder_level),
      status=VALUES(status), is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
    """,
    r => $"({sid},{I(r,0)},{S(r,1)},{S(r,2)},{S(r,3)},{S(r,4)},{S(r,5)},{I(r,6)},{S(r,7)},{D(r,8)},{S(r,9)},{D(r,10)},{D(r,11)},{D(r,12)},{I(r,13)},{I(r,14)},{I(r,15)},{I(r,16)},UTC_TIMESTAMP(6))",
    batchSize: 300);

await CopyTable(
    "medicine_batches",
    """
    SELECT Id, MedicineId, BranchId, BatchNumber, ManufacturingDate, ExpiryDate, QuantityAvailable,
           PurchasePrice, Mrp, SellingPrice, GstPercent, RackNumber,
           CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END
    FROM MedicineBatches
    """,
    """
    INSERT INTO medicine_batches (store_id, local_id, medicine_local_id, branch_local_id, batch_number,
      manufacturing_date, expiry_date, quantity_available, purchase_price, mrp, selling_price, gst_percent,
      rack_number, is_deleted, synced_at_utc)
    VALUES {0}
    ON DUPLICATE KEY UPDATE
      medicine_local_id=VALUES(medicine_local_id), branch_local_id=VALUES(branch_local_id),
      batch_number=VALUES(batch_number), manufacturing_date=VALUES(manufacturing_date),
      expiry_date=VALUES(expiry_date), quantity_available=VALUES(quantity_available),
      purchase_price=VALUES(purchase_price), mrp=VALUES(mrp), selling_price=VALUES(selling_price),
      gst_percent=VALUES(gst_percent), rack_number=VALUES(rack_number), is_deleted=VALUES(is_deleted),
      synced_at_utc=VALUES(synced_at_utc)
    """,
    r => $"({sid},{I(r,0)},{I(r,1)},{Ni(r,2)},{S(r,3)},{Dt(r,4)},{Dt(r,5)},{D(r,6)},{D(r,7)},{D(r,8)},{D(r,9)},{D(r,10)},{S(r,11)},{I(r,12)},UTC_TIMESTAMP(6))");

await CopyTable(
    "customers",
    """
    SELECT Id, BranchId, Name, Type, Phone, Email, GstNumber, Address, City, CreditLimit,
           OutstandingBalance, RewardPoints, Status, CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END
    FROM Customers
    """,
    """
    INSERT INTO customers (store_id, local_id, branch_local_id, name, type, phone, email, gst_number,
      address, city, credit_limit, outstanding_balance, reward_points, status, is_deleted, synced_at_utc)
    VALUES {0}
    ON DUPLICATE KEY UPDATE
      branch_local_id=VALUES(branch_local_id), name=VALUES(name), type=VALUES(type), phone=VALUES(phone),
      email=VALUES(email), gst_number=VALUES(gst_number), address=VALUES(address), city=VALUES(city),
      credit_limit=VALUES(credit_limit), outstanding_balance=VALUES(outstanding_balance),
      reward_points=VALUES(reward_points), status=VALUES(status), is_deleted=VALUES(is_deleted),
      synced_at_utc=VALUES(synced_at_utc)
    """,
    r => $"({sid},{I(r,0)},{Ni(r,1)},{S(r,2)},{I(r,3)},{S(r,4)},{S(r,5)},{S(r,6)},{S(r,7)},{S(r,8)},{D(r,9)},{D(r,10)},{I(r,11)},{I(r,12)},{I(r,13)},UTC_TIMESTAMP(6))",
    batchSize: 400);

await CopyTable(
    "suppliers",
    """
    SELECT Id, BranchId, Name, Phone, GstNumber, OutstandingBalance, Status,
           CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END
    FROM Suppliers
    """,
    """
    INSERT INTO suppliers (store_id, local_id, branch_local_id, name, phone, gst_number,
      outstanding_balance, status, is_deleted, synced_at_utc)
    VALUES {0}
    ON DUPLICATE KEY UPDATE
      branch_local_id=VALUES(branch_local_id), name=VALUES(name), phone=VALUES(phone),
      gst_number=VALUES(gst_number), outstanding_balance=VALUES(outstanding_balance),
      status=VALUES(status), is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
    """,
    r => $"({sid},{I(r,0)},{Ni(r,1)},{S(r,2)},{S(r,3)},{S(r,4)},{D(r,5)},{I(r,6)},{I(r,7)},UTC_TIMESTAMP(6))");

await CopyTable(
    "sales",
    """
    SELECT Id, BranchId, InvoiceNumber, InvoiceDate, CustomerId, BillingCustomerName, BillingCustomerPhone,
           BillingCustomerAddress, BillingDoctorName, SubTotal, DiscountAmount, TaxableAmount,
           CgstAmount, SgstAmount, IgstAmount, RoundOff, GrandTotal, PaidAmount, ChangeReturned,
           Status, PaymentStatus, Remarks, CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END
    FROM Sales
    """,
    """
    INSERT INTO sales (store_id, local_id, branch_local_id, invoice_number, invoice_date, customer_local_id,
      billing_customer_name, billing_customer_phone, billing_customer_address, billing_doctor_name,
      sub_total, discount_amount, taxable_amount, cgst_amount, sgst_amount, igst_amount, round_off,
      grand_total, paid_amount, change_returned, status, payment_status, remarks, is_deleted, synced_at_utc)
    VALUES {0}
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
    """,
    r => $"({sid},{I(r,0)},{Ni(r,1)},{S(r,2)},{Dts(r,3)},{Ni(r,4)},{S(r,5)},{S(r,6)},{S(r,7)},{S(r,8)},{D(r,9)},{D(r,10)},{D(r,11)},{D(r,12)},{D(r,13)},{D(r,14)},{D(r,15)},{D(r,16)},{D(r,17)},{D(r,18)},{I(r,19)},{I(r,20)},{S(r,21)},{I(r,22)},UTC_TIMESTAMP(6))",
    batchSize: 200);

await CopyTable(
    "sale_items",
    """
    SELECT Id, SaleId, MedicineId, MedicineBatchId, BatchNumber, ExpiryDate, Quantity, Mrp, UnitPrice,
           DiscountPercent, DiscountAmount, GstPercent, TaxableAmount, TaxAmount, LineTotal,
           CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END
    FROM SaleItems
    """,
    """
    INSERT INTO sale_items (store_id, local_id, sale_local_id, medicine_local_id, medicine_batch_local_id,
      batch_number, expiry_date, quantity, mrp, unit_price, discount_percent, discount_amount, gst_percent,
      taxable_amount, tax_amount, line_total, is_deleted, synced_at_utc)
    VALUES {0}
    ON DUPLICATE KEY UPDATE
      sale_local_id=VALUES(sale_local_id), medicine_local_id=VALUES(medicine_local_id),
      medicine_batch_local_id=VALUES(medicine_batch_local_id), batch_number=VALUES(batch_number),
      expiry_date=VALUES(expiry_date), quantity=VALUES(quantity), mrp=VALUES(mrp), unit_price=VALUES(unit_price),
      discount_percent=VALUES(discount_percent), discount_amount=VALUES(discount_amount),
      gst_percent=VALUES(gst_percent), taxable_amount=VALUES(taxable_amount), tax_amount=VALUES(tax_amount),
      line_total=VALUES(line_total), is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
    """,
    r => $"({sid},{I(r,0)},{I(r,1)},{I(r,2)},{Ni(r,3)},{S(r,4)},{Dt(r,5)},{D(r,6)},{D(r,7)},{D(r,8)},{D(r,9)},{D(r,10)},{D(r,11)},{D(r,12)},{D(r,13)},{D(r,14)},{I(r,15)},UTC_TIMESTAMP(6))",
    batchSize: 300);

await CopyTable(
    "sale_payments",
    """
    SELECT Id, SaleId, Method, Amount, ReferenceNumber, PaymentDateUtc,
           CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END
    FROM SalePayments
    """,
    """
    INSERT INTO sale_payments (store_id, local_id, sale_local_id, method, amount, reference_number,
      payment_date_utc, is_deleted, synced_at_utc)
    VALUES {0}
    ON DUPLICATE KEY UPDATE
      sale_local_id=VALUES(sale_local_id), method=VALUES(method), amount=VALUES(amount),
      reference_number=VALUES(reference_number), payment_date_utc=VALUES(payment_date_utc),
      is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
    """,
    r => $"({sid},{I(r,0)},{I(r,1)},{I(r,2)},{D(r,3)},{S(r,4)},{Dts(r,5)},{I(r,6)},UTC_TIMESTAMP(6))",
    batchSize: 400);

await CopyTable(
    "purchases",
    """
    SELECT Id, BranchId, InvoiceNumber, SupplierInvoiceNumber, InvoiceDate, SupplierId,
           SubTotal, DiscountAmount, TaxableAmount, CgstAmount, SgstAmount, IgstAmount, RoundOff,
           GrandTotal, PaidAmount, Status, PaymentStatus, Remarks,
           CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END
    FROM Purchases
    """,
    """
    INSERT INTO purchases (store_id, local_id, branch_local_id, invoice_number, supplier_invoice_number,
      invoice_date, supplier_local_id, sub_total, discount_amount, taxable_amount, cgst_amount, sgst_amount,
      igst_amount, round_off, grand_total, paid_amount, status, payment_status, remarks, is_deleted, synced_at_utc)
    VALUES {0}
    ON DUPLICATE KEY UPDATE
      branch_local_id=VALUES(branch_local_id), invoice_number=VALUES(invoice_number),
      supplier_invoice_number=VALUES(supplier_invoice_number), invoice_date=VALUES(invoice_date),
      supplier_local_id=VALUES(supplier_local_id), sub_total=VALUES(sub_total),
      discount_amount=VALUES(discount_amount), taxable_amount=VALUES(taxable_amount),
      cgst_amount=VALUES(cgst_amount), sgst_amount=VALUES(sgst_amount), igst_amount=VALUES(igst_amount),
      round_off=VALUES(round_off), grand_total=VALUES(grand_total), paid_amount=VALUES(paid_amount),
      status=VALUES(status), payment_status=VALUES(payment_status), remarks=VALUES(remarks),
      is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
    """,
    r => $"({sid},{I(r,0)},{Ni(r,1)},{S(r,2)},{S(r,3)},{Dts(r,4)},{Ni(r,5)},{D(r,6)},{D(r,7)},{D(r,8)},{D(r,9)},{D(r,10)},{D(r,11)},{D(r,12)},{D(r,13)},{D(r,14)},{I(r,15)},{I(r,16)},{S(r,17)},{I(r,18)},UTC_TIMESTAMP(6))");

await CopyTable(
    "purchase_items",
    """
    SELECT Id, PurchaseId, MedicineId, MedicineBatchId, BatchNumber, ExpiryDate, Quantity, FreeQuantity,
           PurchasePrice, Mrp, GstPercent, LineTotal, CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END
    FROM PurchaseItems
    """,
    """
    INSERT INTO purchase_items (store_id, local_id, purchase_local_id, medicine_local_id, medicine_batch_local_id,
      batch_number, expiry_date, quantity, free_quantity, purchase_price, mrp, gst_percent, line_total,
      is_deleted, synced_at_utc)
    VALUES {0}
    ON DUPLICATE KEY UPDATE
      purchase_local_id=VALUES(purchase_local_id), medicine_local_id=VALUES(medicine_local_id),
      medicine_batch_local_id=VALUES(medicine_batch_local_id), batch_number=VALUES(batch_number),
      expiry_date=VALUES(expiry_date), quantity=VALUES(quantity), free_quantity=VALUES(free_quantity),
      purchase_price=VALUES(purchase_price), mrp=VALUES(mrp), gst_percent=VALUES(gst_percent),
      line_total=VALUES(line_total), is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
    """,
    r => $"({sid},{I(r,0)},{I(r,1)},{I(r,2)},{Ni(r,3)},{S(r,4)},{Dt(r,5)},{D(r,6)},{D(r,7)},{D(r,8)},{D(r,9)},{D(r,10)},{D(r,11)},{I(r,12)},UTC_TIMESTAMP(6))",
    batchSize: 300);

await CopyTable(
    "purchase_returns",
    """
    SELECT Id, BranchId, ReturnNumber, ReturnDate, PurchaseId, SupplierId, GrandTotal, Status, Remarks,
           CASE WHEN IsDeleted=1 THEN 1 ELSE 0 END
    FROM PurchaseReturns
    """,
    """
    INSERT INTO purchase_returns (store_id, local_id, branch_local_id, return_number, return_date,
      purchase_local_id, supplier_local_id, grand_total, status, remarks, is_deleted, synced_at_utc)
    VALUES {0}
    ON DUPLICATE KEY UPDATE
      branch_local_id=VALUES(branch_local_id), return_number=VALUES(return_number), return_date=VALUES(return_date),
      purchase_local_id=VALUES(purchase_local_id), supplier_local_id=VALUES(supplier_local_id),
      grand_total=VALUES(grand_total), status=VALUES(status), remarks=VALUES(remarks),
      is_deleted=VALUES(is_deleted), synced_at_utc=VALUES(synced_at_utc)
    """,
    r => $"({sid},{I(r,0)},{Ni(r,1)},{S(r,2)},{Dts(r,3)},{Ni(r,4)},{Ni(r,5)},{D(r,6)},{I(r,7)},{S(r,8)},{I(r,9)},UTC_TIMESTAMP(6))");

await using (var sync = new MySqlCommand(
                 $"""
                  INSERT INTO sync_events (store_id, entity_type, local_id)
                  VALUES ({sid}, 'Backfill', 0)
                  """, mysql))
{
    try { await sync.ExecuteNonQueryAsync(); } catch { /* optional */ }
}

await using (var chk = new MySqlCommand(
                 """
                 SELECT 'sales' t, COUNT(*) c FROM sales WHERE store_id=@s
                 UNION ALL SELECT 'sale_items', COUNT(*) FROM sale_items WHERE store_id=@s
                 UNION ALL SELECT 'purchases', COUNT(*) FROM purchases WHERE store_id=@s
                 UNION ALL SELECT 'batches', COUNT(*) FROM medicine_batches WHERE store_id=@s
                 UNION ALL SELECT 'customers', COUNT(*) FROM customers WHERE store_id=@s
                 """, mysql))
{
    chk.Parameters.AddWithValue("@s", storeId);
    await using var r = await chk.ExecuteReaderAsync();
    Console.WriteLine();
    Console.WriteLine("MySQL totals for store:");
    while (await r.ReadAsync())
        Console.WriteLine($"  {r.GetString(0)} = {r.GetInt64(1):N0}");
}

Console.WriteLine();
Console.WriteLine("Done. Refresh the web dashboard.");
return;

async Task CopyTable(
    string label,
    string selectSql,
    string insertTemplate,
    Func<IDataRecord, string> rowSql,
    int batchSize = 250)
{
    Console.Write($"[{label}] reading… ");
    await using var cmd = new SqlCommand(selectSql, sql) { CommandTimeout = 0 };
    await using var reader = await cmd.ExecuteReaderAsync();
    var batch = new List<string>(batchSize);
    var total = 0;
    while (await reader.ReadAsync())
    {
        try
        {
            batch.Add(rowSql(reader));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  skip row: {ex.Message}");
            continue;
        }
        if (batch.Count < batchSize) continue;
        try
        {
            total += await Flush(insertTemplate, batch);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  batch failed ({batch.Count} rows): {ex.Message}");
            // retry one-by-one
            foreach (var row in batch)
            {
                try
                {
                    total += await Flush(insertTemplate, [row]);
                }
                catch (Exception ex2)
                {
                    Console.WriteLine($"  skip: {ex2.Message}");
                }
            }
        }
        batch.Clear();
        Console.Write($"\r[{label}] {total:N0}     ");
    }
    if (batch.Count > 0)
    {
        try
        {
            total += await Flush(insertTemplate, batch);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  final batch failed: {ex.Message}");
            foreach (var row in batch)
            {
                try { total += await Flush(insertTemplate, [row]); }
                catch (Exception ex2) { Console.WriteLine($"  skip: {ex2.Message}"); }
            }
        }
    }
    Console.WriteLine($"\r[{label}] {total:N0} rows");
}

async Task<int> Flush(string insertTemplate, List<string> batch)
{
    var sqlText = string.Format(CultureInfo.InvariantCulture, insertTemplate, string.Join(",\n", batch));
    await using var cmd = new MySqlCommand(sqlText, mysql) { CommandTimeout = 600 };
    await cmd.ExecuteNonQueryAsync();
    return batch.Count;
}

static string Esc(string s)
{
    var cleaned = s
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\0", "", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal);
    return "'" + cleaned + "'";
}
static string S(IDataRecord r, int i) => r.IsDBNull(i) ? "NULL" : Esc(Convert.ToString(r.GetValue(i)) ?? "");
static string I(IDataRecord r, int i) => r.IsDBNull(i) ? "0" : Convert.ToInt32(r.GetValue(i)).ToString(CultureInfo.InvariantCulture);
static string Ni(IDataRecord r, int i) => r.IsDBNull(i) ? "NULL" : Convert.ToInt32(r.GetValue(i)).ToString(CultureInfo.InvariantCulture);
static string D(IDataRecord r, int i)
{
    if (r.IsDBNull(i)) return "0";
    return Convert.ToDecimal(r.GetValue(i)).ToString(CultureInfo.InvariantCulture);
}
static string Dt(IDataRecord r, int i)
{
    if (r.IsDBNull(i)) return "NULL";
    var d = Convert.ToDateTime(r.GetValue(i));
    return Esc(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
}
static string Dts(IDataRecord r, int i)
{
    if (r.IsDBNull(i)) return "NULL";
    var d = Convert.ToDateTime(r.GetValue(i));
    return Esc(d.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture));
}
