#!/bin/bash
# Run on VPS as root after uploading pharmapos_reporting_schema.sql
# Usage: MYSQL_ROOT_PASSWORD='...' bash vps_setup_reporting.sh
set -euo pipefail

: "${MYSQL_ROOT_PASSWORD:?Set MYSQL_ROOT_PASSWORD}"
: "${APP_PASSWORD:=PharmaPos@Report2026}"
: "${STORE_IP:=}"

export MYSQL_PWD="$MYSQL_ROOT_PASSWORD"
MYSQL=(mysql -uroot)

"${MYSQL[@]}" <<SQL
CREATE DATABASE IF NOT EXISTS pharmapos_reporting
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS 'pharmapos'@'%' IDENTIFIED BY '${APP_PASSWORD}';
CREATE USER IF NOT EXISTS 'pharmapos'@'localhost' IDENTIFIED BY '${APP_PASSWORD}';
ALTER USER 'pharmapos'@'%' IDENTIFIED BY '${APP_PASSWORD}';
ALTER USER 'pharmapos'@'localhost' IDENTIFIED BY '${APP_PASSWORD}';
GRANT ALL PRIVILEGES ON pharmapos_reporting.* TO 'pharmapos'@'%';
GRANT ALL PRIVILEGES ON pharmapos_reporting.* TO 'pharmapos'@'localhost';
FLUSH PRIVILEGES;
SQL

sed -i 's/\r$//' /root/pharmapos_reporting_schema.sql
"${MYSQL[@]}" < /root/pharmapos_reporting_schema.sql
"${MYSQL[@]}" -e "USE pharmapos_reporting; SHOW TABLES;"

if [[ -n "$STORE_IP" ]] && command -v ufw >/dev/null 2>&1; then
  ufw allow from "$STORE_IP" to any port 3306 proto tcp comment 'PharmaPOS store' || true
fi

echo DONE
