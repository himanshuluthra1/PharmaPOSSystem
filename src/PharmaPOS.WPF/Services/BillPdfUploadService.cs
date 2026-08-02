using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Renci.SshNet;

namespace PharmaPOS.WPF.Services;

public interface IBillPdfUploadService
{
    /// <summary>Uploads a local PDF to the configured VPS and returns the public HTTPS URL.</summary>
    Task<string> UploadAsync(string localPdfPath, CancellationToken ct = default);
}

public sealed class BillPdfUploadService : IBillPdfUploadService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private readonly IBillShareSettingsService _settings;

    public BillPdfUploadService(IBillShareSettingsService settings)
    {
        _settings = settings;
    }

    public Task<string> UploadAsync(string localPdfPath, CancellationToken ct = default)
    {
        if (!_settings.IsVpsUploadConfigured)
            throw new InvalidOperationException(
                "VPS upload is not configured. Set Public URL + SFTP details in Settings → Preferences.");

        if (!File.Exists(localPdfPath))
            throw new FileNotFoundException("Bill PDF was not found.", localPdfPath);

        var cfg = _settings.Current;
        var fileName = MakeUrlSafeFileName(Path.GetFileName(localPdfPath));

        var uploadLocalPath = localPdfPath;
        var localDir = Path.GetDirectoryName(localPdfPath)!;
        var safeLocalPath = Path.Combine(localDir, fileName);
        if (!string.Equals(Path.GetFileName(localPdfPath), fileName, StringComparison.Ordinal))
        {
            File.Copy(localPdfPath, safeLocalPath, overwrite: true);
            uploadLocalPath = safeLocalPath;
        }

        return Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();

            string remotePath;
            string usedRemoteDir;
            var attempts = new List<string>();

            using (var client = new SftpClient(cfg.SftpHost, cfg.SftpPort, cfg.SftpUsername, cfg.SftpPassword))
            {
                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
                client.Connect();

                var home = client.WorkingDirectory ?? "/";
                var candidates = BuildRemoteDirectoryCandidates(cfg.SftpRemoteDirectory, home);

                Exception? lastError = null;
                string? successDir = null;

                foreach (var dir in candidates)
                {
                    var path = $"{dir.TrimEnd('/')}/{fileName}".Replace('\\', '/');
                    attempts.Add(dir);
                    try
                    {
                        EnsureRemoteDirectory(client, dir);
                        using (var fs = File.OpenRead(uploadLocalPath))
                            client.UploadFile(fs, path, canOverride: true);
                        successDir = dir;
                        remotePath = path;
                        break;
                    }
                    catch (Exception ex) when (IsPermissionDenied(ex) || IsPathError(ex))
                    {
                        lastError = ex;
                    }
                }

                client.Disconnect();

                if (successDir is null)
                {
                    throw new InvalidOperationException(
                        BuildPermissionHelp(cfg, home, attempts, lastError),
                        lastError);
                }

                usedRemoteDir = successDir;
                remotePath = $"{usedRemoteDir.TrimEnd('/')}/{fileName}".Replace('\\', '/');
            }

            var publicUrl = BuildPublicUrl(cfg.PublicBaseUrl, usedRemoteDir, fileName);
            await EnsurePublicUrlReachableAsync(publicUrl, remotePath, ct).ConfigureAwait(false);
            return publicUrl;
        }, ct);
    }

    /// <summary>
    /// SFTP accounts are often chrooted. Absolute paths like /var/www/html/bills then fail,
    /// while /bills or public_html/bills work. Try several likely folders.
    /// </summary>
    internal static List<string> BuildRemoteDirectoryCandidates(string configured, string workingDirectory)
    {
        var list = new List<string>();
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            p = p.Trim().Replace('\\', '/').TrimEnd('/');
            if (p.Length == 0) p = "/";
            if (!list.Contains(p, StringComparer.Ordinal))
                list.Add(p);
        }

        Add(configured);

        var cfg = configured.Trim().Replace('\\', '/');
        if (cfg.Contains("/var/www/html/", StringComparison.OrdinalIgnoreCase))
        {
            // Chroot to web root → only /bills is visible
            Add("/bills");
            Add("bills");
        }

        if (cfg.Contains("public_html", StringComparison.OrdinalIgnoreCase)
            || cfg.Contains("/var/www/", StringComparison.OrdinalIgnoreCase)
            || cfg.StartsWith('/'))
        {
            Add("public_html/bills");
            Add("/public_html/bills");
            Add("bills");
            Add("/bills");
        }

        // Relative to login home
        var home = (workingDirectory ?? "/").TrimEnd('/');
        if (!string.IsNullOrEmpty(home) && home != "/")
        {
            Add(home + "/public_html/bills");
            Add(home + "/bills");
        }

        return list;
    }

    internal static string MakeUrlSafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(name)) name = "bill.pdf";
        var safe = Regex.Replace(name, @"[^a-zA-Z0-9._-]+", "_");
        if (!safe.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            safe += ".pdf";
        return safe;
    }

    internal static string BuildPublicUrl(string publicBaseUrl, string remoteDirectory, string fileName)
    {
        var baseUrl = (publicBaseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Public base URL is empty.");

        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            baseUrl = "https://" + baseUrl;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Public base URL is invalid: {publicBaseUrl}\nUse e.g. http://YOUR-IP/bills/");
        }

        var root = parsed.GetLeftPart(UriPartial.Authority);
        var path = parsed.AbsolutePath;
        if (string.IsNullOrEmpty(path) || path == "/")
            path = "/";
        else if (!path.EndsWith('/'))
            path += "/";

        // Only auto-append last folder when public URL has no path yet.
        if (path == "/")
        {
            var remoteFolder = remoteDirectory
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            if (!string.IsNullOrWhiteSpace(remoteFolder)
                && !remoteFolder.Equals("public_html", StringComparison.OrdinalIgnoreCase)
                && !remoteFolder.Equals("html", StringComparison.OrdinalIgnoreCase)
                && !remoteFolder.Equals("www", StringComparison.OrdinalIgnoreCase)
                && !remoteFolder.Equals("var", StringComparison.OrdinalIgnoreCase))
            {
                path = "/" + remoteFolder.Trim('/') + "/";
            }
        }

        var encodedFile = Uri.EscapeDataString(fileName).Replace("%2E", ".", StringComparison.OrdinalIgnoreCase);
        return root + path + encodedFile;
    }

    private static string BuildPermissionHelp(
        BillShareSettings cfg, string home, List<string> attempts, Exception? lastError)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Permission denied — your SFTP login cannot write to the web folder.");
        sb.AppendLine();
        sb.AppendLine($"SFTP user: {cfg.SftpUsername}");
        sb.AppendLine($"Login folder: {home}");
        sb.AppendLine("Tried:");
        foreach (var a in attempts)
            sb.AppendLine("  • " + a);
        sb.AppendLine();
        sb.AppendLine("Most SFTP users are jailed (chroot) and cannot use /var/www/html/...");
        sb.AppendLine();
        sb.AppendLine("Do this on the VPS as root:");
        sb.AppendLine();
        sb.AppendLine("1) Find your web root, then create bills under it:");
        sb.AppendLine("   sudo mkdir -p /var/www/html/bills");
        sb.AppendLine($"   sudo chown -R {cfg.SftpUsername}:www-data /var/www/html/bills");
        sb.AppendLine("   sudo chmod -R 775 /var/www/html/bills");
        sb.AppendLine();
        sb.AppendLine("2) In PharmaPOS Settings set Remote folder to one of:");
        sb.AppendLine("   /bills");
        sb.AppendLine("   public_html/bills");
        sb.AppendLine("   (whichever your SFTP home can see — NOT /var/www/html/bills if chrooted)");
        sb.AppendLine();
        sb.AppendLine("3) Public base URL:");
        sb.AppendLine("   http://50.6.251.47/bills/");
        if (lastError is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Server message: " + lastError.Message);
        }
        return sb.ToString().TrimEnd();
    }

    private static bool IsPermissionDenied(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e.GetType().Name.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase))
                return true;
            if (e.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("Access denied", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsPathError(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            var msg = e.Message;
            if (msg.Contains("No such file", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("Failure", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task EnsurePublicUrlReachableAsync(string publicUrl, string remotePath, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, publicUrl);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            if ((int)response.StatusCode is >= 200 and < 400)
                return;

            throw new InvalidOperationException(
                $"PDF uploaded to SFTP path:\n{remotePath}\n\n" +
                $"But web link returned HTTP {(int)response.StatusCode}:\n{publicUrl}\n\n" +
                "Your VPS is a Node app (redirects to /login). nginx does not serve /bills/ as files yet.\n\n" +
                "On the VPS (SSH as root), find the real disk path of SFTP /bills, then add BEFORE the app proxy:\n\n" +
                "location /bills/ {\n" +
                "    alias /home/YOUR_SFTP_USER/bills/;   # real path of SFTP /bills\n" +
                "    default_type application/pdf;\n" +
                "}\n\n" +
                "Then: sudo nginx -t && sudo systemctl reload nginx\n" +
                "Open the link in a browser — it must show the PDF (not 404).");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"PDF uploaded to:\n{remotePath}\n\n" +
                $"Could not open:\n{publicUrl}\n\n{ex.Message}",
                ex);
        }
    }

    private static void EnsureRemoteDirectory(SftpClient client, string remoteDir)
    {
        if (string.IsNullOrWhiteSpace(remoteDir) || remoteDir == "/") return;

        var normalized = remoteDir.Replace('\\', '/');
        var absolute = normalized.StartsWith('/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var path = absolute ? "/" : "";
        foreach (var part in parts)
        {
            path = path is "" or "/" ? (absolute ? "/" + part : part) : path.TrimEnd('/') + "/" + part;
            if (!client.Exists(path))
                client.CreateDirectory(path);
        }
    }
}
