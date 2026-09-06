import mysql, { Pool, RowDataPacket, ResultSetHeader } from "mysql2/promise";

let pool: Pool | null = null;

export function getPool(): Pool {
  if (pool) return pool;
  pool = mysql.createPool({
    host: process.env.DATABASE_HOST || "127.0.0.1",
    port: Number(process.env.DATABASE_PORT || 3306),
    user: process.env.DATABASE_USER || "pharmapos",
    password: process.env.DATABASE_PASSWORD || "",
    database: process.env.DATABASE_NAME || "pharmapos_reporting",
    waitForConnections: true,
    connectionLimit: 10,
    timezone: "Z",
    dateStrings: false,
  });
  return pool;
}

type SqlParam = string | number | boolean | Date | null | Buffer;

export async function query<T extends RowDataPacket[]>(
  sql: string,
  params: SqlParam[] | unknown[] = []
): Promise<T> {
  const [rows] = await getPool().query<T>(sql, params as SqlParam[]);
  return rows;
}

export async function execute(
  sql: string,
  params: SqlParam[] | unknown[] = []
): Promise<ResultSetHeader> {
  const [result] = await getPool().execute<ResultSetHeader>(sql, params as SqlParam[]);
  return result;
}
