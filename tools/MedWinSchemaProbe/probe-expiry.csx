using System.Data.OleDb;
var cs = "Provider=Microsoft.ACE.OLEDB.12.0;Data Source=D:\\Medwin\\datafolder\\data.mdb;Jet OLEDB:Database Password=z111111111111111111a;";
using var conn = new OleDbConnection(cs);
conn.Open();
void Q(string title, string sql) {
  Console.WriteLine($"\n=== {title} ===");
  using var cmd = new OleDbCommand(sql, conn);
  using var r = cmd.ExecuteReader();
  while (r.Read()) {
    var parts = new List<string>();
    for (int i = 0; i < r.FieldCount; i++) parts.Add($"{r.GetName(i)}={r.GetValue(i)}");
    Console.WriteLine(string.Join(" | ", parts));
  }
}
Q("bill 10494", "SELECT dpmedcod, dpbatch, dpexmon, dpexyear, manfdate FROM dsalemaster WHERE dpurblno=10494");
Q("nonzero expiry sample", "SELECT TOP 10 dpurblno, dpbatch, dpexmon, dpexyear, manfdate FROM dsalemaster WHERE dpexyear > 0 OR dpexmon > 0");
Q("expiry stats", "SELECT COUNT(*) total, SUM(IIF(dpexyear>0,1,0)) yr, SUM(IIF(dpexmon>0,1,0)) mn FROM dsalemaster");
Q("stock expiry sample", "SELECT TOP 5 stkcode, stkbatch, stkexyr, stkexmn FROM stockmas WHERE stkexyr>0");
