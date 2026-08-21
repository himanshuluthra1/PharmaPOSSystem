namespace PharmaPOS.WPF.Services;

public sealed class PosShopRow
{
    public string StoreId { get; init; } = string.Empty;
    public string StoreCode { get; init; } = string.Empty;
    public string? MachineName { get; init; }
    public bool IsApproved { get; init; }
    public bool IsVendor { get; init; }
    public string? AppVersion { get; init; }
    public DateTime? LastSeenUtc { get; init; }
    public string? PendingVersion { get; init; }
    public string? AssignmentStatus { get; init; }
}

public sealed class PosReleaseRow
{
    public string Version { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string PackageUrl { get; init; } = string.Empty;
    public string? Sha256 { get; init; }
    public long? FileSizeBytes { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAtUtc { get; init; }

    /// <summary>Shown in the Published version combo (version + installer file name).</summary>
    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(FileName) ? Version : $"{Version}  ({FileName})";
}

public sealed class PosPendingUpdate
{
    public int AssignmentId { get; init; }
    public string Version { get; init; } = string.Empty;
    public string PackageUrl { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string? Sha256 { get; init; }
    public long? FileSizeBytes { get; init; }
}

public sealed class PosPublishRequest
{
    public string Version { get; init; } = string.Empty;
    public string LocalFilePath { get; init; } = string.Empty;
    public string? Notes { get; init; }
}

public sealed class PosPublishResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? PackageUrl { get; init; }

    public static PosPublishResult Ok(string message, string url) =>
        new() { Success = true, Message = message, PackageUrl = url };

    public static PosPublishResult Fail(string message) =>
        new() { Success = false, Message = message };
}
