using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PharmaPOS.Application.Common.Abstractions;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Domain.Entities.System;

namespace PharmaPOS.WPF.Services;

/// <summary>Polls the local sync outbox and publishes pending rows to VPS MySQL.</summary>
public sealed class ReportingSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMySqlSyncSettingsService _settings;
    private readonly IReportingSyncGate _gate;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DisabledDelay = TimeSpan.FromSeconds(10);

    public ReportingSyncWorker(
        IServiceScopeFactory scopeFactory,
        IMySqlSyncSettingsService settings,
        IReportingSyncGate gate)
    {
        _scopeFactory = scopeFactory;
        _settings = settings;
        _gate = gate;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_gate.IsEnabled)
                {
                    await Task.Delay(DisabledDelay, stoppingToken);
                    continue;
                }

                var processed = await ProcessBatchAsync(stoppingToken);
                await Task.Delay(processed > 0 ? TimeSpan.FromMilliseconds(500) : IdleDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var publisher = scope.ServiceProvider.GetRequiredService<IMySqlReportingPublisher>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();

        var now = clock.UtcNow;
        var pending = await uow.Repository<SyncOutboxEntry>().Query()
            .Where(e => e.Status == SyncOutboxStatus.Pending || e.Status == SyncOutboxStatus.Failed)
            .Where(e => e.NextAttemptAtUtc == null || e.NextAttemptAtUtc <= now)
            .OrderBy(e => e.CreatedAtUtc)
            .Take(20)
            .ToListAsync(ct);

        if (pending.Count == 0)
            return 0;

        var sent = 0;
        string? lastError = null;

        foreach (var entry in pending)
        {
            try
            {
                await publisher.PublishAsync(entry, ct);
                entry.Status = SyncOutboxStatus.Sent;
                entry.SentAtUtc = clock.UtcNow;
                entry.LastError = null;
                entry.NextAttemptAtUtc = null;
                uow.Repository<SyncOutboxEntry>().Update(entry);
                sent++;
            }
            catch (Exception ex)
            {
                entry.AttemptCount++;
                entry.Status = SyncOutboxStatus.Failed;
                entry.LastError = Truncate(ex.Message, 1900);
                entry.NextAttemptAtUtc = clock.UtcNow.Add(Backoff(entry.AttemptCount));
                uow.Repository<SyncOutboxEntry>().Update(entry);
                lastError = entry.LastError;
            }
        }

        await uow.SaveChangesAsync(ct);

        if (sent > 0)
            _settings.UpdateStatus(DateTime.UtcNow, null);
        else if (!string.IsNullOrWhiteSpace(lastError))
            _settings.UpdateStatus(null, lastError);

        return sent;
    }

    private static TimeSpan Backoff(int attemptCount)
    {
        var seconds = Math.Min(300, Math.Pow(2, Math.Min(attemptCount, 8)));
        return TimeSpan.FromSeconds(seconds);
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
