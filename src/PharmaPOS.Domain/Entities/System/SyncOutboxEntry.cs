using PharmaPOS.Domain.Common;

namespace PharmaPOS.Domain.Entities.System;

public enum SyncOutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

public enum SyncOutboxOperation
{
    Upsert = 0,
    Delete = 1
}

/// <summary>Local queue of reporting payloads waiting to upload to VPS MySQL.</summary>
public class SyncOutboxEntry : BaseEntity
{
    public string EntityType { get; set; } = string.Empty;
    public string StoreCode { get; set; } = string.Empty;
    public int LocalId { get; set; }
    public SyncOutboxOperation Operation { get; set; } = SyncOutboxOperation.Upsert;
    public string PayloadJson { get; set; } = "{}";
    public SyncOutboxStatus Status { get; set; } = SyncOutboxStatus.Pending;
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
}
