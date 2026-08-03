-- Migrate store_activations + reporting tables from store_code tenant key → store_id.
-- Run once as MySQL root on the VPS. Safe to re-run partially (IF checks).
USE pharmapos_reporting;

-- ---------------------------------------------------------------------------
-- 1) store_activations: introduce store_id PK, keep store_code as display
-- ---------------------------------------------------------------------------
DROP TRIGGER IF EXISTS trg_store_activations_guard;

-- If legacy table has store_code PK and no store_id column:
SET @has_store_id := (
  SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'store_activations'
    AND COLUMN_NAME = 'store_id'
);

SET @sql := IF(
  @has_store_id = 0,
  'ALTER TABLE store_activations ADD COLUMN store_id VARCHAR(40) NULL AFTER store_code',
  'SELECT 1'
);
PREPARE stmt FROM @sql; EXECUTE stmt; DEALLOCATE PREPARE stmt;

UPDATE store_activations
SET store_id = CONCAT('S', UPPER(MD5(CONCAT(IFNULL(store_code,''), '|', IFNULL(machine_id,'')))))
WHERE store_id IS NULL OR store_id = '';

-- Rebuild table with correct PK if still on store_code PK
CREATE TABLE IF NOT EXISTS store_activations_new (
  store_id VARCHAR(40) NOT NULL,
  store_code VARCHAR(80) NOT NULL,
  machine_id VARCHAR(128) NOT NULL,
  machine_name VARCHAR(200) NULL,
  is_approved TINYINT(1) NOT NULL DEFAULT 0,
  requested_at_utc DATETIME(6) NOT NULL,
  approved_at_utc DATETIME(6) NULL,
  notes VARCHAR(500) NULL,
  PRIMARY KEY (store_id),
  UNIQUE KEY uk_store_activations_machine (machine_id),
  KEY ix_store_activations_code (store_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT IGNORE INTO store_activations_new
  (store_id, store_code, machine_id, machine_name, is_approved, requested_at_utc, approved_at_utc, notes)
SELECT
  store_id,
  store_code,
  machine_id,
  machine_name,
  is_approved,
  requested_at_utc,
  approved_at_utc,
  notes
FROM store_activations
WHERE store_id IS NOT NULL AND store_id <> '';

DROP TABLE store_activations;
RENAME TABLE store_activations_new TO store_activations;

-- ---------------------------------------------------------------------------
-- 2) Rename store_code → store_id on all reporting tables (tenant key column)
-- ---------------------------------------------------------------------------
-- Helper pattern: only rename when old column exists and new does not.

DROP PROCEDURE IF EXISTS migrate_rename_store_code_to_store_id;
DELIMITER //
CREATE PROCEDURE migrate_rename_store_code_to_store_id(IN tbl VARCHAR(64))
BEGIN
  DECLARE has_old INT DEFAULT 0;
  DECLARE has_new INT DEFAULT 0;
  SELECT COUNT(*) INTO has_old FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = 'store_code';
  SELECT COUNT(*) INTO has_new FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = 'store_id';
  IF has_old = 1 AND has_new = 0 THEN
    SET @q = CONCAT('ALTER TABLE `', tbl, '` CHANGE COLUMN store_code store_id VARCHAR(40) NOT NULL');
    PREPARE s FROM @q; EXECUTE s; DEALLOCATE PREPARE s;
  END IF;
END//
DELIMITER ;

CALL migrate_rename_store_code_to_store_id('branches');
CALL migrate_rename_store_code_to_store_id('medicines');
CALL migrate_rename_store_code_to_store_id('medicine_batches');
CALL migrate_rename_store_code_to_store_id('customers');
CALL migrate_rename_store_code_to_store_id('sales');
CALL migrate_rename_store_code_to_store_id('sale_items');
CALL migrate_rename_store_code_to_store_id('sale_payments');
CALL migrate_rename_store_code_to_store_id('sale_returns');
CALL migrate_rename_store_code_to_store_id('sale_return_items');
CALL migrate_rename_store_code_to_store_id('purchases');
CALL migrate_rename_store_code_to_store_id('purchase_items');
CALL migrate_rename_store_code_to_store_id('purchase_returns');
CALL migrate_rename_store_code_to_store_id('purchase_return_items');
CALL migrate_rename_store_code_to_store_id('stock_movements');
CALL migrate_rename_store_code_to_store_id('stock_transfers');
CALL migrate_rename_store_code_to_store_id('stock_transfer_items');

DROP PROCEDURE IF EXISTS migrate_rename_store_code_to_store_id;

-- ---------------------------------------------------------------------------
-- 3) Remap existing reporting rows that used display store_code (e.g. STORE-001)
--    to the generated store_id from store_activations.
-- ---------------------------------------------------------------------------
DROP PROCEDURE IF EXISTS migrate_remap_tenant_values;
DELIMITER //
CREATE PROCEDURE migrate_remap_tenant_values(IN tbl VARCHAR(64))
BEGIN
  DECLARE has_col INT DEFAULT 0;
  SELECT COUNT(*) INTO has_col FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = tbl AND COLUMN_NAME = 'store_id';
  IF has_col = 1 THEN
    SET @q = CONCAT(
      'UPDATE `', tbl, '` t ',
      'INNER JOIN store_activations a ON a.store_code = t.store_id ',
      'SET t.store_id = a.store_id ',
      'WHERE t.store_id = a.store_code AND a.store_id <> a.store_code'
    );
    PREPARE s FROM @q; EXECUTE s; DEALLOCATE PREPARE s;
  END IF;
END//
DELIMITER ;

CALL migrate_remap_tenant_values('branches');
CALL migrate_remap_tenant_values('medicines');
CALL migrate_remap_tenant_values('medicine_batches');
CALL migrate_remap_tenant_values('customers');
CALL migrate_remap_tenant_values('sales');
CALL migrate_remap_tenant_values('sale_items');
CALL migrate_remap_tenant_values('sale_payments');
CALL migrate_remap_tenant_values('sale_returns');
CALL migrate_remap_tenant_values('sale_return_items');
CALL migrate_remap_tenant_values('purchases');
CALL migrate_remap_tenant_values('purchase_items');
CALL migrate_remap_tenant_values('purchase_returns');
CALL migrate_remap_tenant_values('purchase_return_items');
CALL migrate_remap_tenant_values('stock_movements');
CALL migrate_remap_tenant_values('stock_transfers');
CALL migrate_remap_tenant_values('stock_transfer_items');

DROP PROCEDURE IF EXISTS migrate_remap_tenant_values;

-- Re-apply guard trigger after migration (see store_activations_guard_trigger.sql)
