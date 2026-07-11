using System.Data.OleDb;

const string cs = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=D:\\Medwin\\datafolder\\data.mdb;Jet OLEDB:Database Password=z111111111111111111a;";
using var conn = new OleDbConnection(cs);
conn.Open();

void Q(string title, string sql)
{
  Console.WriteLine($"\n=== {title} ===");
  using var cmd = new OleDbCommand(sql, conn);
  using var r = cmd.ExecuteReader();
  while (r.Read())
  {
    var parts = new List<string>();
    for (int i = 0; i < r.FieldCount; i++) parts.Add($"{r.GetName(i)}={r.GetValue(i)}");
    Console.WriteLine(string.Join(" | ", parts));
  }
}

Q("price fields sample", @"SELECT TOP 5 numbercd, medname, mrprate, wrate, specialrate, fpurrat, purrate FROM mednmas WHERE wrate > 0");
Q("stock selling", @"SELECT TOP 5 stkcode, stkinvrate, stkfnmrp, mrprate, stkwrate FROM stockmas WHERE stkinvrate > 0");
Q("active med count", @"SELECT COUNT(*) FROM mednmas m WHERE m.numbercd IN (SELECT stkcode FROM stockmas UNION SELECT dpmedcod FROM dsalemaster)");
Q("wrate zero sample", @"SELECT TOP 3 numbercd, medname, mrprate, wrate FROM mednmas WHERE (wrate IS NULL OR wrate = 0) AND mrprate > 0");
