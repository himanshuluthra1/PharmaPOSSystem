-- Multi-tenant dashboard auth (run on VPS MySQL pharmapos_reporting)
-- Tenants own 1..N store_id values from store_activations / POS sync.

USE pharmapos_reporting;

CREATE TABLE IF NOT EXISTS tenants (
  id INT NOT NULL AUTO_INCREMENT,
  name VARCHAR(200) NOT NULL,
  status TINYINT NOT NULL DEFAULT 1 COMMENT '1=active 0=inactive',
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS tenant_stores (
  tenant_id INT NOT NULL,
  store_id VARCHAR(40) NOT NULL,
  display_name VARCHAR(120) NULL,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (tenant_id, store_id),
  KEY ix_tenant_stores_store (store_id),
  CONSTRAINT fk_tenant_stores_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS dashboard_roles (
  id INT NOT NULL AUTO_INCREMENT,
  name VARCHAR(80) NOT NULL,
  is_system TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (id),
  UNIQUE KEY uk_dashboard_roles_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS dashboard_role_permissions (
  role_id INT NOT NULL,
  permission_key VARCHAR(80) NOT NULL,
  PRIMARY KEY (role_id, permission_key),
  CONSTRAINT fk_drp_role FOREIGN KEY (role_id) REFERENCES dashboard_roles(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS dashboard_users (
  id INT NOT NULL AUTO_INCREMENT,
  tenant_id INT NOT NULL,
  role_id INT NOT NULL,
  email VARCHAR(200) NOT NULL,
  full_name VARCHAR(150) NOT NULL,
  password_hash VARCHAR(200) NOT NULL,
  status TINYINT NOT NULL DEFAULT 1 COMMENT '1=active 0=inactive',
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  last_login_at_utc DATETIME(6) NULL,
  PRIMARY KEY (id),
  UNIQUE KEY uk_dashboard_users_email (email),
  KEY ix_dashboard_users_tenant (tenant_id),
  CONSTRAINT fk_du_tenant FOREIGN KEY (tenant_id) REFERENCES tenants(id) ON DELETE CASCADE,
  CONSTRAINT fk_du_role FOREIGN KEY (role_id) REFERENCES dashboard_roles(id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sync_events (
  id BIGINT NOT NULL AUTO_INCREMENT,
  store_id VARCHAR(40) NOT NULL,
  entity_type VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
  PRIMARY KEY (id),
  KEY ix_sync_events_store_time (store_id, created_at_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Suppliers (for purchase party names on dashboard)
CREATE TABLE IF NOT EXISTS suppliers (
  store_id VARCHAR(40) NOT NULL,
  local_id INT NOT NULL,
  branch_local_id INT NULL,
  name VARCHAR(200) NOT NULL,
  phone VARCHAR(40) NULL,
  gst_number VARCHAR(40) NULL,
  outstanding_balance DECIMAL(18,4) NOT NULL DEFAULT 0,
  status INT NOT NULL DEFAULT 0,
  is_deleted TINYINT(1) NOT NULL DEFAULT 0,
  synced_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (store_id, local_id),
  KEY ix_suppliers_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Seed roles
INSERT INTO dashboard_roles (id, name, is_system) VALUES
  (1, 'Owner', 1),
  (2, 'Manager', 1),
  (3, 'Accountant', 1),
  (4, 'Viewer', 1)
ON DUPLICATE KEY UPDATE name = VALUES(name);

-- Owner: everything
INSERT IGNORE INTO dashboard_role_permissions (role_id, permission_key) VALUES
  (1, 'dashboard.view'),
  (1, 'sales.view'),
  (1, 'purchases.view'),
  (1, 'stock.view'),
  (1, 'payments.view'),
  (1, 'returns.view'),
  (1, 'stores.manage_users'),
  (1, 'stores.manage_stores');

-- Manager
INSERT IGNORE INTO dashboard_role_permissions (role_id, permission_key) VALUES
  (2, 'dashboard.view'),
  (2, 'sales.view'),
  (2, 'purchases.view'),
  (2, 'stock.view'),
  (2, 'payments.view'),
  (2, 'returns.view');

-- Accountant
INSERT IGNORE INTO dashboard_role_permissions (role_id, permission_key) VALUES
  (3, 'dashboard.view'),
  (3, 'sales.view'),
  (3, 'purchases.view'),
  (3, 'payments.view'),
  (3, 'returns.view');

-- Viewer
INSERT IGNORE INTO dashboard_role_permissions (role_id, permission_key) VALUES
  (4, 'dashboard.view'),
  (4, 'sales.view'),
  (4, 'stock.view');

-- Bootstrap default tenant (link stores manually or via admin UI)
INSERT INTO tenants (id, name, status)
SELECT 1, 'Default Tenant', 1
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM tenants WHERE id = 1);

-- Link all approved store activations into default tenant (safe to re-run)
INSERT IGNORE INTO tenant_stores (tenant_id, store_id, display_name)
SELECT 1, sa.store_id, COALESCE(NULLIF(sa.store_code, ''), sa.store_id)
FROM store_activations sa
WHERE sa.is_approved = 1;

-- Default owner login: owner@cloudpharma.site / Owner@123
-- bcrypt hash for Owner@123 (cost 10)
INSERT INTO dashboard_users (tenant_id, role_id, email, full_name, password_hash, status)
SELECT 1, 1, 'owner@cloudpharma.site', 'Owner',
  '$2b$10$hCa8p3bIrJeL/m.6C2Dspe3H9WT4QDayU.6keUByKx1HozLiV6/p6',
  1
FROM DUAL
WHERE NOT EXISTS (SELECT 1 FROM dashboard_users WHERE email = 'owner@cloudpharma.site');
