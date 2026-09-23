-- Add denormalized COGS on sales for dashboard aggregations.
-- Safe to re-run: skips if column already exists.

USE pharmapos_reporting;

SET @col_exists := (
  SELECT COUNT(*) FROM information_schema.COLUMNS
  WHERE TABLE_SCHEMA = DATABASE()
    AND TABLE_NAME = 'sales'
    AND COLUMN_NAME = 'cogs'
);

SET @sql := IF(
  @col_exists = 0,
  'ALTER TABLE sales ADD COLUMN cogs DECIMAL(18,4) NOT NULL DEFAULT 0 AFTER paid_amount',
  'SELECT ''sales.cogs already exists'' AS info'
);
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

UPDATE sales s
SET cogs = (
  SELECT COALESCE(SUM(
    si.quantity * COALESCE(
      NULLIF(b.purchase_price, 0),
      NULLIF(m.purchase_price, 0),
      0)
  ), 0)
  FROM sale_items si
  LEFT JOIN medicine_batches b
    ON b.store_id = si.store_id AND b.local_id = si.medicine_batch_local_id
  LEFT JOIN medicines m
    ON m.store_id = si.store_id AND m.local_id = si.medicine_local_id
  WHERE si.store_id = s.store_id
    AND si.sale_local_id = s.local_id
    AND si.is_deleted = 0
);
