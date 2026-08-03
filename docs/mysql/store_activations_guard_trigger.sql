USE pharmapos_reporting;

DROP TRIGGER IF EXISTS trg_store_activations_guard;

DELIMITER //
CREATE TRIGGER trg_store_activations_guard
BEFORE UPDATE ON store_activations
FOR EACH ROW
BEGIN
  -- App user must not approve itself, change store_id, or move an approved license.
  IF CURRENT_USER() LIKE 'pharmapos@%' THEN
    IF NEW.is_approved <> OLD.is_approved THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'Only the software vendor may change is_approved';
    END IF;
    IF NEW.store_id <> OLD.store_id THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'Only the software vendor may change store_id';
    END IF;
    IF OLD.is_approved = 1 AND NEW.machine_id <> OLD.machine_id THEN
      SIGNAL SQLSTATE '45000'
        SET MESSAGE_TEXT = 'Only the software vendor may transfer an approved store';
    END IF;
  END IF;
END//
DELIMITER ;
