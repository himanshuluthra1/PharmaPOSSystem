using System.Net.Http;

namespace PharmaPOS.WPF.Services;

public interface IUrlShortenerService
{
    /// <summary>Returns a short URL, or the original URL if shortening fails / is disabled.</summary>
    Task<string> ShortenAsync(string longUrl, CancellationToken ct = default);
}

/// <summary>Free TinyURL API (no API key): https://tinyurl.com/api-create.php</summary>
public sealed class TinyUrlShortenerService : IUrlShortenerService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };

    private readonly IBillShareSettingsService _settings;

    public TinyUrlShortenerService(IBillShareSettingsService settings)
    {
        _settings = settings;
    }

    public async Task<string> ShortenAsync(string longUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(longUrl))
            return longUrl;

        if (!_settings.Current.EnableTinyUrl)
            return longUrl.Trim();

        var trimmed = longUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return trimmed;

        try
        {
            var api = "https://tinyurl.com/api-create.php?url=" + Uri.EscapeDataString(trimmed);
            using var response = await Http.GetAsync(api, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var shortUrl = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();

            if (Uri.TryCreate(shortUrl, UriKind.Absolute, out var shortUri)
                && (shortUri.Scheme == Uri.UriSchemeHttp || shortUri.Scheme == Uri.UriSchemeHttps)
                && shortUri.Host.Contains("tinyurl", StringComparison.OrdinalIgnoreCase))
            {
                return shortUrl;
            }
        }
        catch
        {
            // Fall back to the original (working) public URL.
        }

        return trimmed;
    }
}
