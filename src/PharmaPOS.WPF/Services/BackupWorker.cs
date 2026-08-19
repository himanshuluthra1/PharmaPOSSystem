using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PharmaPOS.Application.Common.Abstractions;

namespace PharmaPOS.WPF.Services;

/// <summary>
/// After shop data changes, takes a LocalDB backup on the configured interval
/// and uploads it to Google Drive when an account is connected.
/// </summary>
public sealed class BackupWorker : BackgroundService
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMinutes(1);
    private const int KeepAutoFiles = 7;

    private readonly IBackupSettingsService _settings;
    private readonly IDatabaseBackupService _backup;
    private readonly IGoogleDriveBackupService _drive;
    private readonly IDataChangeSignal _changes;
    private readonly ILogger<BackupWorker> _logger;

    public BackupWorker(
        IBackupSettingsService settings,
        IDatabaseBackupService backup,
        IGoogleDriveBackupService drive,
        IDataChangeSignal changes,
        ILogger<BackupWorker> logger)
    {
        _settings = settings;
        _backup = backup;
        _drive = drive;
        _changes = changes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TryAutoBackupAsync(stoppingToken);
                await Task.Delay(PollDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Automatic backup cycle failed.");
                try { await Task.Delay(PollDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task TryAutoBackupAsync(CancellationToken ct)
    {
        var settings = _settings.Load();
        if (!settings.AutoEnabled)
            return;
        if (!_changes.HasPendingChanges)
            return;

        var interval = TimeSpan.FromMinutes(Math.Max(1, settings.IntervalMinutes));
        if (settings.LastAutoBackupUtc is DateTime last
            && DateTime.UtcNow - last < interval)
            return;

        var path = Path.Combine(_backup.AutoBackupFolder, _backup.SuggestFileName());
        await _backup.BackupToFileAsync(path, ct);
        _changes.ClearPending();

        settings.LastAutoBackupUtc = DateTime.UtcNow;
        settings.LastStatus = $"Local auto backup saved {DateTime.Now:dd-MMM HH:mm}.";

        if (_drive.HasSavedToken
            && !string.IsNullOrWhiteSpace(settings.GoogleClientId)
            && !string.IsNullOrWhiteSpace(settings.GoogleClientSecret))
        {
            try
            {
                await _drive.UploadFileAsync(path, settings.GoogleClientId, settings.GoogleClientSecret, ct);
                settings.LastStatus =
                    $"Auto backup uploaded to Google Drive {DateTime.Now:dd-MMM HH:mm}.";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Google Drive auto-backup upload failed.");
                settings.LastStatus =
                    $"Local backup saved, but Google Drive upload failed: {ex.Message}";
            }
        }

        _settings.Save(settings);
        PruneOldAutoBackups();
    }

    private void PruneOldAutoBackups()
    {
        try
        {
            var files = Directory.GetFiles(_backup.AutoBackupFolder, "*.bak")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(KeepAutoFiles);
            foreach (var file in files)
            {
                try { file.Delete(); } catch { /* ignore */ }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not prune old auto backups.");
        }
    }
}
