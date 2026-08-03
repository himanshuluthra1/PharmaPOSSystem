-- PharmaPOS reporting database for VPS MySQL
-- Identity: unique (store_id, local_id) per table — supports separate LocalDB per store.
-- Run once after creating the empty database.

CREATE DATABASE IF NOT EXISTS pharmapos_reporting
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE pharmapos_reporting;

-- ---------------------------------------------------------------------------
-- Branches (store_id = auto-generated store tenant key; code = local branch code)
-- ---------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS branches (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  code VARCHAR(40) NOT NULL,
  name VARCHAR(120) NOT NULL,
  address VARCHAR(500) NULL,
  city VARCHAR(80) NULL,
  state VARCHAR(80) NULL,
  pincode VARCHAR(20) NULL,
  phone VARCHAR(40) NULL,
  email VARCHAR(120) NULL,
  gst_number VARCHAR(40) NULL,
  drug_license_number VARCHAR(80) NULL,
  is_head_office TINYINT(1) NOT NULL DEFAULT 0,
  status INT NOT NULL DEFAULT 0,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_branches_code (code)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS medicines (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  name VARCHAR(200) NOT NULL,
  generic_name VARCHAR(200) NULL,
  brand VARCHAR(120) NULL,
  composition VARCHAR(500) NULL,
  strength VARCHAR(80) NULL,
  dosage_form INT NOT NULL DEFAULT 0,
  hsn_code VARCHAR(40) NULL,
  gst_percent DECIMAL(9,4) NOT NULL DEFAULT 0,
  barcode VARCHAR(80) NULL,
  mrp DECIMAL(18,4) NOT NULL DEFAULT 0,
  purchase_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  selling_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  units_per_pack INT NOT NULL DEFAULT 1,
  reorder_level INT NOT NULL DEFAULT 0,
  status INT NOT NULL DEFAULT 0,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_medicines_name (name),
  KEY ix_medicines_barcode (barcode)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS medicine_batches (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  medicine_local_id INT NOT NULL,
  branch_local_id INT NULL,
  batch_number VARCHAR(60) NOT NULL,
  manufacturing_date DATE NULL,
  expiry_date DATE NULL,
  quantity_available DECIMAL(18,3) NOT NULL DEFAULT 0,
  purchase_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  mrp DECIMAL(18,4) NOT NULL DEFAULT 0,
  selling_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  gst_percent DECIMAL(9,4) NOT NULL DEFAULT 0,
  rack_number VARCHAR(40) NULL,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_batches_medicine (store_id, medicine_local_id),
  KEY ix_batches_branch (store_id, branch_local_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS customers (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  branch_local_id INT NULL,
  name VARCHAR(200) NOT NULL,
  type INT NOT NULL DEFAULT 0,
  phone VARCHAR(40) NULL,
  email VARCHAR(120) NULL,
  gst_number VARCHAR(40) NULL,
  address VARCHAR(500) NULL,
  city VARCHAR(80) NULL,
  credit_limit DECIMAL(18,4) NOT NULL DEFAULT 0,
  outstanding_balance DECIMAL(18,4) NOT NULL DEFAULT 0,
  reward_points INT NOT NULL DEFAULT 0,
  status INT NOT NULL DEFAULT 0,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_customers_phone (phone),
  KEY ix_customers_name (name)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS sales (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  branch_local_id INT NULL,
  invoice_number VARCHAR(40) NOT NULL,
  invoice_date DATETIME(6) NOT NULL,
  customer_local_id INT NULL,
  billing_customer_name VARCHAR(200) NULL,
  billing_customer_phone VARCHAR(40) NULL,
  billing_customer_address VARCHAR(500) NULL,
  billing_doctor_name VARCHAR(200) NULL,
  sub_total DECIMAL(18,4) NOT NULL DEFAULT 0,
  discount_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  taxable_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  cgst_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  sgst_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  igst_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  round_off DECIMAL(18,4) NOT NULL DEFAULT 0,
  grand_total DECIMAL(18,4) NOT NULL DEFAULT 0,
  paid_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  change_returned DECIMAL(18,4) NOT NULL DEFAULT 0,
  status INT NOT NULL DEFAULT 0,
  payment_status INT NOT NULL DEFAULT 0,
  remarks VARCHAR(500) NULL,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_sales_date (invoice_date),
  KEY ix_sales_invoice (store_id, invoice_number)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS sale_items (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  sale_local_id INT NOT NULL,
  medicine_local_id INT NOT NULL,
  medicine_batch_local_id INT NULL,
  batch_number VARCHAR(60) NULL,
  expiry_date DATE NULL,
  quantity DECIMAL(18,3) NOT NULL DEFAULT 0,
  mrp DECIMAL(18,4) NOT NULL DEFAULT 0,
  unit_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  discount_percent DECIMAL(9,4) NOT NULL DEFAULT 0,
  discount_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  gst_percent DECIMAL(9,4) NOT NULL DEFAULT 0,
  taxable_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  tax_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  line_total DECIMAL(18,4) NOT NULL DEFAULT 0,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_sale_items_sale (store_id, sale_local_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS sale_payments (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  sale_local_id INT NOT NULL,
  method INT NOT NULL DEFAULT 0,
  amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  reference_number VARCHAR(80) NULL,
  payment_date_utc DATETIME(6) NULL,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_sale_payments_sale (store_id, sale_local_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS sale_returns (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  branch_local_id INT NULL,
  return_number VARCHAR(40) NOT NULL,
  return_date DATETIME(6) NOT NULL,
  sale_local_id INT NULL,
  customer_local_id INT NULL,
  grand_total DECIMAL(18,4) NOT NULL DEFAULT 0,
  refund_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  status INT NOT NULL DEFAULT 0,
  remarks VARCHAR(500) NULL,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_sale_returns_date (return_date),
  KEY ix_sale_returns_number (store_id, return_number)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS sale_return_items (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  sale_return_local_id INT NOT NULL,
  medicine_local_id INT NOT NULL,
  medicine_batch_local_id INT NULL,
  batch_number VARCHAR(60) NULL,
  quantity DECIMAL(18,3) NOT NULL DEFAULT 0,
  unit_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  line_total DECIMAL(18,4) NOT NULL DEFAULT 0,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_sale_return_items_parent (store_id, sale_return_local_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS purchases (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  branch_local_id INT NULL,
  invoice_number VARCHAR(40) NOT NULL,
  supplier_invoice_number VARCHAR(80) NULL,
  invoice_date DATETIME(6) NOT NULL,
  supplier_local_id INT NOT NULL,
  sub_total DECIMAL(18,4) NOT NULL DEFAULT 0,
  discount_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  taxable_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  cgst_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  sgst_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  igst_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  round_off DECIMAL(18,4) NOT NULL DEFAULT 0,
  grand_total DECIMAL(18,4) NOT NULL DEFAULT 0,
  paid_amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  status INT NOT NULL DEFAULT 0,
  payment_status INT NOT NULL DEFAULT 0,
  remarks VARCHAR(500) NULL,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_purchases_date (invoice_date),
  KEY ix_purchases_invoice (store_id, invoice_number)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS purchase_items (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  purchase_local_id INT NOT NULL,
  medicine_local_id INT NOT NULL,
  medicine_batch_local_id INT NULL,
  batch_number VARCHAR(60) NULL,
  expiry_date DATE NULL,
  quantity DECIMAL(18,3) NOT NULL DEFAULT 0,
  free_quantity DECIMAL(18,3) NOT NULL DEFAULT 0,
  purchase_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  mrp DECIMAL(18,4) NOT NULL DEFAULT 0,
  gst_percent DECIMAL(9,4) NOT NULL DEFAULT 0,
  line_total DECIMAL(18,4) NOT NULL DEFAULT 0,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_purchase_items_parent (store_id, purchase_local_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS purchase_returns (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  branch_local_id INT NULL,
  return_number VARCHAR(40) NOT NULL,
  return_date DATETIME(6) NOT NULL,
  purchase_local_id INT NULL,
  supplier_local_id INT NULL,
  grand_total DECIMAL(18,4) NOT NULL DEFAULT 0,
  status INT NOT NULL DEFAULT 0,
  remarks VARCHAR(500) NULL,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_purchase_returns_date (return_date)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS purchase_return_items (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  purchase_return_local_id INT NOT NULL,
  medicine_local_id INT NOT NULL,
  medicine_batch_local_id INT NULL,
  batch_number VARCHAR(60) NULL,
  quantity DECIMAL(18,3) NOT NULL DEFAULT 0,
  purchase_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  line_total DECIMAL(18,4) NOT NULL DEFAULT 0,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_purchase_return_items_parent (store_id, purchase_return_local_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS stock_movements (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  branch_local_id INT NULL,
  medicine_local_id INT NOT NULL,
  medicine_batch_local_id INT NULL,
  movement_type INT NOT NULL,
  quantity DECIMAL(18,3) NOT NULL DEFAULT 0,
  balance_after DECIMAL(18,3) NOT NULL DEFAULT 0,
  unit_cost DECIMAL(18,4) NOT NULL DEFAULT 0,
  reference_type VARCHAR(80) NULL,
  reference_id INT NULL,
  reference_number VARCHAR(80) NULL,
  remarks VARCHAR(500) NULL,
  movement_date_utc DATETIME(6) NOT NULL,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_stock_movements_date (movement_date_utc),
  KEY ix_stock_movements_medicine (store_id, medicine_local_id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS stock_transfers (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  branch_local_id INT NULL,
  transfer_number VARCHAR(40) NOT NULL,
  transfer_date DATETIME(6) NOT NULL,
  kind INT NOT NULL DEFAULT 1,
  status INT NOT NULL DEFAULT 0,
  to_branch_local_id INT NULL,
  from_branch_code VARCHAR(40) NULL,
  from_branch_name VARCHAR(120) NULL,
  to_branch_code VARCHAR(40) NULL,
  to_branch_name VARCHAR(120) NULL,
  package_key VARCHAR(64) NULL,
  remarks VARCHAR(500) NULL,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_stock_transfers_date (transfer_date),
  KEY ix_stock_transfers_number (store_id, transfer_number)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS stock_transfer_items (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  stock_transfer_local_id INT NOT NULL,
  medicine_local_id INT NOT NULL,
  medicine_name VARCHAR(200) NULL,
  medicine_barcode VARCHAR(80) NULL,
  batch_number VARCHAR(60) NULL,
  expiry_date DATE NULL,
  quantity DECIMAL(18,3) NOT NULL DEFAULT 0,
  purchase_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  mrp DECIMAL(18,4) NOT NULL DEFAULT 0,
  selling_price DECIMAL(18,4) NOT NULL DEFAULT 0,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_stock_transfer_items_parent (store_id, stock_transfer_local_id)
) ENGINE=InnoDB;

-- Store licensing: one approved machine per store_id
CREATE TABLE IF NOT EXISTS store_activations (
  store_id VARCHAR(40) NOT NULL,
  machine_id VARCHAR(128) NOT NULL,
  machine_name VARCHAR(200) NULL,
  is_approved TINYINT(1) NOT NULL DEFAULT 0,
  requested_at_utc DATETIME(6) NOT NULL,
  approved_at_utc DATETIME(6) NULL,
  notes VARCHAR(500) NULL,
  PRIMARY KEY (store_id),
  UNIQUE KEY uk_store_activations_machine (machine_id)
) ENGINE=InnoDB;
