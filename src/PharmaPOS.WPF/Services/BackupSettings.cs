namespace PharmaPOS.WPF.Services;

public sealed class BackupSettings
{
    public bool AutoEnabled { get; set; }
    public int IntervalMinutes { get; set; } = 1440;
    public string GoogleClientId { get; set; } = string.Empty;
    public string GoogleClientSecret { get; set; } = string.Empty;
    public string? GoogleAccountEmail { get; set; }
    public DateTime? LastAutoBackupUtc { get; set; }
    public string? LastStatus { get; set; }
}
