-- Remote POS updates (run on VPS MySQL)
USE pharmapos_reporting;

CREATE TABLE IF NOT EXISTS pos_releases (
  version VARCHAR(20) NOT NULL,
  file_name VARCHAR(200) NOT NULL,
  package_url VARCHAR(500) NOT NULL,
  sha256 CHAR(64) NULL,
  file_size_bytes BIGINT NULL,
  notes VARCHAR(500) NULL,
  created_at_utc DATETIME(6) NOT NULL,
  PRIMARY KEY (version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS pos_update_assignments (
  id INT NOT NULL AUTO_INCREMENT,
  store_id VARCHAR(40) NOT NULL,
  version VARCHAR(20) NOT NULL,
  status VARCHAR(20) NOT NULL DEFAULT 'pending',
  assigned_at_utc DATETIME(6) NOT NULL,
  started_at_utc DATETIME(6) NULL,
  completed_at_utc DATETIME(6) NULL,
  error_message VARCHAR(1000) NULL,
  PRIMARY KEY (id),
  KEY ix_pos_update_store_status (store_id, status),
  KEY ix_pos_update_store_version (store_id, version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Heartbeat + vendor console (ignore errors if columns already exist)
ALTER TABLE store_activations ADD COLUMN is_vendor TINYINT(1) NOT NULL DEFAULT 0;
ALTER TABLE store_activations ADD COLUMN app_version VARCHAR(20) NULL;
ALTER TABLE store_activations ADD COLUMN last_seen_utc DATETIME(6) NULL;

-- Your development PC is the vendor console
UPDATE store_activations SET is_vendor = 1 WHERE store_code = 'STORE-001';

-- ---------------------------------------------------------------------------
-- nginx: serve installers from /var/www/html/updates
--   location /updates/ {
--     alias /var/www/html/updates/;
--     autoindex off;
--   }
-- mkdir -p /var/www/html/updates && chown www-data:www-data /var/www/html/updates
-- ---------------------------------------------------------------------------
