-- Store activation / licensing (run on VPS MySQL)
-- store_id  = auto-generated unique key (used for all reporting sync)
-- store_code = owner-chosen display name (not the tenant key)
USE pharmapos_reporting;

CREATE TABLE IF NOT EXISTS store_activations (
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

-- ---------------------------------------------------------------------------
-- Creator helpers (run manually when a store asks for activation)
-- ---------------------------------------------------------------------------
-- 1) See pending requests:
--    SELECT store_id, store_code, machine_id, machine_name, is_approved, requested_at_utc
--    FROM store_activations ORDER BY requested_at_utc DESC;
--
-- 2) Approve a pending store for its requested machine:
--    UPDATE store_activations
--    SET is_approved = 1, approved_at_utc = UTC_TIMESTAMP(6), notes = 'Approved'
--    WHERE store_id = 'S...' AND machine_id = 'PASTE_MACHINE_ID_HERE';
--
-- 3) Transfer an existing store to a NEW machine (reinstall / new PC):
--    UPDATE store_activations
--    SET machine_id = 'NEW_MACHINE_ID',
--        machine_name = NULL,
--        is_approved = 1,
--        approved_at_utc = UTC_TIMESTAMP(6),
--        notes = 'Transferred'
--    WHERE store_id = 'S...';
--
-- 4) Revoke a store:
--    UPDATE store_activations SET is_approved = 0, approved_at_utc = NULL WHERE store_id = 'S...';
