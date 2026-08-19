using System.IO;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.Util.Store;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace PharmaPOS.WPF.Services;

public interface IGoogleDriveBackupService
{
    bool HasSavedToken { get; }
    Task<string> ConnectAsync(string clientId, string clientSecret, CancellationToken ct = default);
    void Disconnect();
    Task UploadFileAsync(string localPath, string clientId, string clientSecret, CancellationToken ct = default);
}

public sealed class GoogleDriveBackupService : IGoogleDriveBackupService
{
    private const string FolderName = "PharmaPOS Backups";
    private static readonly string[] Scopes = [DriveService.Scope.DriveFile];

    private readonly string _tokenFolder;

    public GoogleDriveBackupService()
    {
        _tokenFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PharmaPOS", "GoogleDriveTokens");
        Directory.CreateDirectory(_tokenFolder);
    }

    public bool HasSavedToken =>
        Directory.Exists(_tokenFolder) && Directory.EnumerateFiles(_tokenFolder).Any();

    public async Task<string> ConnectAsync(string clientId, string clientSecret, CancellationToken ct = default)
    {
        var credential = await AuthorizeAsync(clientId, clientSecret, ct);
        using var service = CreateService(credential);
        try
        {
            var about = service.About.Get();
            about.Fields = "user";
            var result = await about.ExecuteAsync(ct);
            return string.IsNullOrWhiteSpace(result.User?.EmailAddress)
                ? "Connected"
                : result.User.EmailAddress;
        }
        catch
        {
            return "Connected";
        }
    }

    public void Disconnect()
    {
        if (!Directory.Exists(_tokenFolder)) return;
        foreach (var file in Directory.EnumerateFiles(_tokenFolder))
        {
            try { File.Delete(file); } catch { /* ignore */ }
        }
    }

    public async Task UploadFileAsync(string localPath, string clientId, string clientSecret, CancellationToken ct = default)
    {
        if (!File.Exists(localPath))
            throw new FileNotFoundException("Backup file was not found.", localPath);

        var credential = await AuthorizeAsync(clientId, clientSecret, ct);
        using var service = CreateService(credential);
        var folderId = await EnsureFolderAsync(service, ct);

        var metadata = new DriveFile
        {
            Name = Path.GetFileName(localPath),
            Parents = [folderId]
        };

        await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var request = service.Files.Create(metadata, stream, "application/octet-stream");
        request.Fields = "id";
        var result = await request.UploadAsync(ct);
        if (result.Status != UploadStatus.Completed)
            throw new InvalidOperationException(result.Exception?.Message ?? "Google Drive upload did not complete.");
    }

    private async Task<UserCredential> AuthorizeAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("Enter Google OAuth Client ID and Client Secret first.");

        return await GoogleWebAuthorizationBroker.AuthorizeAsync(
            new ClientSecrets { ClientId = clientId.Trim(), ClientSecret = clientSecret.Trim() },
            Scopes,
            "pharmapos",
            ct,
            new FileDataStore(_tokenFolder, true));
    }

    private static DriveService CreateService(UserCredential credential) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "PharmaPOS"
        });

    private static async Task<string> EnsureFolderAsync(DriveService service, CancellationToken ct)
    {
        var list = service.Files.List();
        list.Q = $"name = '{FolderName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        list.Fields = "files(id, name)";
        list.Spaces = "drive";
        var found = await list.ExecuteAsync(ct);
        var existing = found.Files?.FirstOrDefault();
        if (existing?.Id is { Length: > 0 } id)
            return id;

        var folder = new DriveFile
        {
            Name = FolderName,
            MimeType = "application/vnd.google-apps.folder"
        };
        var create = service.Files.Create(folder);
        create.Fields = "id";
        var created = await create.ExecuteAsync(ct);
        return created.Id ?? throw new InvalidOperationException("Could not create Google Drive folder.");
    }
}
