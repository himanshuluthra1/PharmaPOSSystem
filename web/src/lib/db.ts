import mysql, { Pool, RowDataPacket, ResultSetHeader } from "mysql2/promise";

const GLOBAL_KEY = "__pharmapos_mysql_pool__";

type SqlParam = string | number | boolean | Date | null | Buffer;

function createPool(): Pool {
  return mysql.createPool({
    host: process.env.DATABASE_HOST || "127.0.0.1",
    port: Number(process.env.DATABASE_PORT || 3306),
    user: process.env.DATABASE_USER || "pharmapos",
    password: process.env.DATABASE_PASSWORD || "",
    database: process.env.DATABASE_NAME || "pharmapos_reporting",
    waitForConnections: true,
    connectionLimit: 10,
    maxIdle: 10,
    idleTimeout: 30_000,
    queueLimit: 30,
    connectTimeout: 10_000,
    enableKeepAlive: true,
    keepAliveInitialDelay: 10_000,
    timezone: "Z",
    dateStrings: false,
  });
}

export function getPool(): Pool {
  const g = globalThis as typeof globalThis & { [GLOBAL_KEY]?: Pool };
  if (!g[GLOBAL_KEY]) {
    g[GLOBAL_KEY] = createPool();
  }
  return g[GLOBAL_KEY];
}

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
