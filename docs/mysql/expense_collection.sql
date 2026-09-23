-- Expense & Collection tables for MyPOS web dashboard (cloud-side, not POS-synced)
USE pharmapos_reporting;

CREATE TABLE IF NOT EXISTS expense_heads (
  id BIGINT NOT NULL AUTO_INCREMENT,
  tenant_id INT NOT NULL,
  name VARCHAR(120) NOT NULL,
  sort_order INT NOT NULL DEFAULT 0,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  KEY ix_expense_heads_tenant (tenant_id),
  CONSTRAINT fk_expense_heads_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS expenses (
  id BIGINT NOT NULL AUTO_INCREMENT,
  tenant_id INT NOT NULL,
  store_id VARCHAR(40) NOT NULL,
  head_id BIGINT NULL,
  expense_date DATE NOT NULL,
  amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  notes VARCHAR(500) NULL,
  is_recurring TINYINT(1) NOT NULL DEFAULT 0,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  KEY ix_expenses_store_date (store_id, expense_date),
  KEY ix_expenses_tenant (tenant_id),
  CONSTRAINT fk_expenses_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
  CONSTRAINT fk_expenses_head FOREIGN KEY (head_id) REFERENCES expense_heads(id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS collection_payments (
  id BIGINT NOT NULL AUTO_INCREMENT,
  tenant_id INT NOT NULL,
  store_id VARCHAR(40) NOT NULL,
  entry_date DATE NOT NULL,
  entry_type ENUM('collection','payment') NOT NULL,
  mode VARCHAR(40) NOT NULL DEFAULT 'Cash',
  amount DECIMAL(18,4) NOT NULL DEFAULT 0,
  notes VARCHAR(500) NULL,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  KEY ix_cp_store_date (store_id, entry_date),
  KEY ix_cp_tenant (tenant_id),
  CONSTRAINT fk_cp_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
