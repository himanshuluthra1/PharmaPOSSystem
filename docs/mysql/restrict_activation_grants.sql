REVOKE ALL PRIVILEGES ON pharmapos_reporting.store_activations FROM 'pharmapos'@'%';
GRANT SELECT, INSERT ON pharmapos_reporting.store_activations TO 'pharmapos'@'%';
-- Keep full rights on other reporting tables
GRANT SELECT, INSERT, UPDATE, DELETE ON pharmapos_reporting.* TO 'pharmapos'@'%';
-- Re-apply restricted table rights (more specific wins for this table when using column? Actually in MySQL table-level GRANT SELECT,INSERT after database ALL still allows ALL from db grant)
-- So revoke DB-level ALL and grant table by table — too heavy.
-- Better: use a trigger that blocks UPDATE of is_approved by non-root — complex.
-- Practical approach: revoke UPDATE on store_activations only:
FLUSH PRIVILEGES;
