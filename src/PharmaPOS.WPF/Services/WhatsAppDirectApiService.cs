using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PharmaPOS.WPF.Services;

public sealed record WhatsAppSendResult(bool Success, string? MessageId, string? Error);

public interface IWhatsAppDirectApiService
{
    bool IsConfigured { get; }

    /// <summary>Send a free-form text message (works inside the 24-hour customer-care window).</summary>
    Task<WhatsAppSendResult> SendTextAsync(string phoneDigits, string message, CancellationToken ct = default);

    /// <summary>
    /// Send a sale bill: uses the configured utility template when set,
    /// otherwise free-form text (optionally with PDF link preview).
    /// </summary>
    Task<WhatsAppSendResult> SendBillAsync(
        string phoneDigits,
        string customerName,
        string invoiceNumber,
        decimal amount,
        string? billUrl,
        string plainMessage,
        CancellationToken ct = default);
}

/// <summary>WhatsApp Business Cloud API (Meta Graph) direct messaging client.</summary>
public sealed class WhatsAppDirectApiService : IWhatsAppDirectApiService
{
    public const string HttpClientName = "WhatsAppCloudApi";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IHttpClientFactory _httpFactory;
    private readonly IBillShareSettingsService _settings;

    public WhatsAppDirectApiService(IHttpClientFactory httpFactory, IBillShareSettingsService settings)
    {
        _httpFactory = httpFactory;
        _settings = settings;
    }

    public bool IsConfigured => _settings.IsWhatsAppApiConfigured;

    public Task<WhatsAppSendResult> SendTextAsync(string phoneDigits, string message, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = NormalizeTo(phoneDigits),
            ["type"] = "text",
            ["text"] = new Dictionary<string, object?>
            {
                ["preview_url"] = ContainsHttpUrl(message),
                ["body"] = message
            }
        };
        return PostAsync(body, ct);
    }

    public async Task<WhatsAppSendResult> SendBillAsync(
        string phoneDigits,
        string customerName,
        string invoiceNumber,
        decimal amount,
        string? billUrl,
        string plainMessage,
        CancellationToken ct = default)
    {
        var cfg = _settings.Current;
        if (!string.IsNullOrWhiteSpace(cfg.WhatsAppBillTemplateName))
        {
            var templateResult = await SendTemplateAsync(
                phoneDigits,
                cfg.WhatsAppBillTemplateName.Trim(),
                string.IsNullOrWhiteSpace(cfg.WhatsAppBillTemplateLanguage)
                    ? "en"
                    : cfg.WhatsAppBillTemplateLanguage.Trim(),
                [
                    string.IsNullOrWhiteSpace(customerName) ? "Customer" : customerName.Trim(),
                    invoiceNumber.Trim(),
                    amount.ToString("0.00"),
                    string.IsNullOrWhiteSpace(billUrl) ? "—" : billUrl.Trim()
                ],
                ct).ConfigureAwait(false);

            if (templateResult.Success)
                return templateResult;

            var textFallback = await SendTextAsync(phoneDigits, plainMessage, ct).ConfigureAwait(false);
            if (textFallback.Success)
                return textFallback;

            return new WhatsAppSendResult(
                false,
                null,
                templateResult.Error ?? textFallback.Error ?? "WhatsApp API send failed.");
        }

        return await SendTextAsync(phoneDigits, plainMessage, ct).ConfigureAwait(false);
    }

    private Task<WhatsAppSendResult> SendTemplateAsync(
        string phoneDigits,
        string templateName,
        string languageCode,
        IReadOnlyList<string> bodyParams,
        CancellationToken ct)
    {
        var parameters = bodyParams
            .Select(p => new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = Truncate(p, 1024)
            })
            .ToList();

        var body = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = NormalizeTo(phoneDigits),
            ["type"] = "template",
            ["template"] = new Dictionary<string, object?>
            {
                ["name"] = templateName,
                ["language"] = new Dictionary<string, object?> { ["code"] = languageCode },
                ["components"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = "body",
                        ["parameters"] = parameters
                    }
                }
            }
        };
        return PostAsync(body, ct);
    }

    private async Task<WhatsAppSendResult> PostAsync(Dictionary<string, object?> payload, CancellationToken ct)
    {
        var cfg = _settings.Current;
        if (!_settings.IsWhatsAppApiConfigured)
            return new WhatsAppSendResult(false, null, "WhatsApp Cloud API is not configured.");

        var version = string.IsNullOrWhiteSpace(cfg.WhatsAppApiVersion)
            ? "v21.0"
            : cfg.WhatsAppApiVersion.Trim().Trim('/');
        var phoneNumberId = cfg.WhatsAppPhoneNumberId.Trim();
        var url = $"https://graph.facebook.com/{version}/{phoneNumberId}/messages";

        var http = _httpFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.WhatsAppAccessToken.Trim());
        req.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        try
        {
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (resp.IsSuccessStatusCode)
            {
                string? messageId = null;
                try
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("messages", out var messages)
                        && messages.ValueKind == JsonValueKind.Array
                        && messages.GetArrayLength() > 0
                        && messages[0].TryGetProperty("id", out var idEl))
                    {
                        messageId = idEl.GetString();
                    }
                }
                catch { /* ignore parse */ }

                return new WhatsAppSendResult(true, messageId, null);
            }

            return new WhatsAppSendResult(false, null, ExtractError(raw, (int)resp.StatusCode));
        }
        catch (Exception ex)
        {
            return new WhatsAppSendResult(false, null, ex.Message);
        }
    }

    private static string ExtractError(string raw, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                var message = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                var code = err.TryGetProperty("code", out var c) ? c.GetRawText() : null;
                var details = err.TryGetProperty("error_user_msg", out var u) ? u.GetString() : null;
                var parts = new[] { message, details, code is null ? null : $"code {code}" }
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                var joined = string.Join(" — ", parts);
                if (!string.IsNullOrWhiteSpace(joined))
                    return joined;
            }
        }
        catch { /* ignore */ }

        return string.IsNullOrWhiteSpace(raw)
            ? $"WhatsApp API HTTP {statusCode}"
            : $"WhatsApp API HTTP {statusCode}: {Truncate(raw, 400)}";
    }

    private static string NormalizeTo(string phoneDigits)
    {
        var digits = new string((phoneDigits ?? string.Empty).Where(char.IsDigit).ToArray());
        return digits;
    }

    private static bool ContainsHttpUrl(string message) =>
        message.Contains("http://", StringComparison.OrdinalIgnoreCase)
        || message.Contains("https://", StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
