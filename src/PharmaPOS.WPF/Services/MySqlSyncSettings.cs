namespace PharmaPOS.WPF.Services;

public sealed class MySqlSyncSettings
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 3306;
    public string Database { get; set; } = "pharmapos_reporting";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseSsl { get; set; }
    public string? StoreCodeOverride { get; set; }
    public DateTime? LastSuccessAtUtc { get; set; }
    public string? LastError { get; set; }
}
