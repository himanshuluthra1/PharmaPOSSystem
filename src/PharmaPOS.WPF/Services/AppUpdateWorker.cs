using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Hosting;
using PharmaPOS.Application.Features.ReportingSync;
using PharmaPOS.Shared.Constants;

namespace PharmaPOS.WPF.Services;

/// <summary>Polls VPS for a shop-specific POS update, downloads it, and launches the installer.</summary>
public sealed class AppUpdateWorker : BackgroundService
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromMinutes(5);

    private readonly IPosUpdateService _updates;
    private readonly IStoreIdentityService _identity;
    private readonly IDialogService _dialog;
    private bool _prompting;

    public AppUpdateWorker(
        IPosUpdateService updates,
        IStoreIdentityService identity,
        IDialogService dialog)
    {
        _updates = updates;
        _identity = identity;
        _dialog = dialog;
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
                if (_identity.IsConfigured)
                {
                    await _updates.EnsureSchemaAsync(stoppingToken);
                    await _updates.HeartbeatAsync(stoppingToken);
                    await TryApplyPendingAsync(stoppingToken);
                }
                await Task.Delay(PollDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                try { await Task.Delay(ErrorDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task TryApplyPendingAsync(CancellationToken ct)
    {
        if (_prompting) return;

        var pending = await _updates.GetPendingUpdateAsync(ct);
        if (pending is null) return;

        if (!IsNewer(pending.Version, AppConstants.ApplicationVersion))
        {
            await _updates.MarkAssignmentAsync(pending.AssignmentId, "installed", "Already on this version", ct);
            return;
        }

        if (IsSnoozed(pending.Version))
            return;

        _prompting = true;
        try
        {
            var accept = await ConfirmOnUiAsync(
                $"A POS update ({pending.Version}) was sent to this shop.\n\n" +
                $"Current version: {AppConstants.ApplicationVersion}\n\n" +
                "Download and install now? PharmaPOS will close and reopen.",
                "Shop update");
            if (!accept)
            {
                Snooze(pending.Version);
                return;
            }

            await _updates.MarkAssignmentAsync(pending.AssignmentId, "downloading", null, ct);
            var setupPath = await _updates.DownloadPackageAsync(pending, null, ct);

            await _updates.MarkAssignmentAsync(pending.AssignmentId, "installing", null, ct);
            LaunchInstaller(setupPath);
            await ShutdownAppAsync();
        }
        catch (Exception ex)
        {
            await _updates.MarkAssignmentAsync(pending.AssignmentId, "failed", ex.Message, ct);
            await ShowErrorOnUiAsync("Could not install the update.\n\n" + ex.Message);
        }
        finally
        {
            _prompting = false;
        }
    }

    private static bool IsNewer(string assigned, string current)
    {
        if (!Version.TryParse(Normalize(assigned), out var a)) return !string.Equals(assigned, current, StringComparison.OrdinalIgnoreCase);
        if (!Version.TryParse(Normalize(current), out var c)) return true;
        return a > c;
    }

    private static string Normalize(string version)
    {
        var v = version.Trim();
        var parts = v.Split('.');
        return parts.Length switch
        {
            1 => v + ".0.0",
            2 => v + ".0",
            _ => v
        };
    }

    private static string SnoozePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PharmaPOS", "update-snooze.txt");

    private static bool IsSnoozed(string version)
    {
        try
        {
            if (!File.Exists(SnoozePath)) return false;
            var line = File.ReadAllText(SnoozePath);
            var parts = line.Split('|');
            if (parts.Length != 2) return false;
            if (!string.Equals(parts[0], version, StringComparison.OrdinalIgnoreCase)) return false;
            if (!DateTime.TryParse(parts[1], out var until)) return false;
            return DateTime.UtcNow < until;
        }
        catch
        {
            return false;
        }
    }

    private static void Snooze(string version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SnoozePath)!);
            File.WriteAllText(SnoozePath, $"{version}|{DateTime.UtcNow.AddHours(6):o}");
        }
        catch
        {
            // ignore
        }
    }

    private static void LaunchInstaller(string setupPath)
    {
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var args = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /DIR=\"{installDir}\"";
        var psi = new ProcessStartInfo
        {
            FileName = setupPath,
            Arguments = args,
            UseShellExecute = true,
            Verb = "runas"
        };
        Process.Start(psi);
    }

    private async Task<bool> ConfirmOnUiAsync(string message, string title)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null) return false;
        return await app.Dispatcher.InvokeAsync(() => _dialog.Confirm(message, title));
    }

    private static async Task ShutdownAppAsync()
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null) return;
        await app.Dispatcher.InvokeAsync(() => app.Shutdown());
    }

    private async Task ShowErrorOnUiAsync(string message)
    {
        var app = System.Windows.Application.Current;
        if (app?.Dispatcher is null) return;
        await app.Dispatcher.InvokeAsync(() => _dialog.ShowError(message, "Shop update"));
    }
}
