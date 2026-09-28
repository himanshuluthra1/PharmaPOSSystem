using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PharmaPOS.WPF.Services;

public sealed record WhatsAppSendResult(
    bool Success,
    string? MessageId,
    string? Error,
    bool PdfAttached = false,
    string? PdfError = null);

public interface IWhatsAppDirectApiService
{
    bool IsConfigured { get; }

    /// <summary>Send a free-form text message (works inside the 24-hour customer-care window).</summary>
    Task<WhatsAppSendResult> SendTextAsync(string phoneDigits, string message, CancellationToken ct = default);

    /// <summary>
    /// Send a sale bill: uses the configured utility template when set,
    /// otherwise free-form text. When <paramref name="pdfPath"/> is given the PDF is
    /// uploaded and sent as a document (with the bill text as caption when it fits).
    /// </summary>
    Task<WhatsAppSendResult> SendBillAsync(
        string phoneDigits,
        string customerName,
        string invoiceNumber,
        decimal amount,
        string? billUrl,
        string plainMessage,
        string? pdfPath = null,
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
        string? pdfPath = null,
        CancellationToken ct = default)
    {
        var cfg = _settings.Current;
        var hasPdf = !string.IsNullOrWhiteSpace(pdfPath) && File.Exists(pdfPath);
        var hasTemplate = !string.IsNullOrWhiteSpace(cfg.WhatsAppBillTemplateName);
        var fileName = BuildPdfFileName(invoiceNumber);

        // No template: one message — the PDF with the bill text as caption.
        if (hasPdf && !hasTemplate && plainMessage.Length <= MaxCaptionLength)
        {
            var docResult = await SendDocumentAsync(phoneDigits, pdfPath!, fileName, plainMessage, ct)
                .ConfigureAwait(false);
            if (docResult.Success)
                return docResult with { PdfAttached = true };

            var textOnly = await SendBillTextAsync(
                phoneDigits, customerName, invoiceNumber, amount, billUrl, plainMessage, cfg, ct).ConfigureAwait(false);
            return textOnly with { PdfError = docResult.Error };
        }

        var textResult = await SendBillTextAsync(
            phoneDigits, customerName, invoiceNumber, amount, billUrl, plainMessage, cfg, ct).ConfigureAwait(false);
        if (!textResult.Success || !hasPdf)
            return textResult;

        var followUp = await SendDocumentAsync(phoneDigits, pdfPath!, fileName, caption: null, ct)
            .ConfigureAwait(false);
        return textResult with { PdfAttached = followUp.Success, PdfError = followUp.Error };
    }

    private async Task<WhatsAppSendResult> SendBillTextAsync(
        string phoneDigits,
        string customerName,
        string invoiceNumber,
        decimal amount,
        string? billUrl,
        string plainMessage,
        BillShareSettings cfg,
        CancellationToken ct)
    {
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

    private const int MaxCaptionLength = 1024;

    private async Task<WhatsAppSendResult> SendDocumentAsync(
        string phoneDigits,
        string pdfPath,
        string fileName,
        string? caption,
        CancellationToken ct)
    {
        var upload = await UploadMediaAsync(pdfPath, fileName, ct).ConfigureAwait(false);
        if (upload.Error is not null)
            return new WhatsAppSendResult(false, null, upload.Error);

        var document = new Dictionary<string, object?>
        {
            ["id"] = upload.MediaId,
            ["filename"] = fileName
        };
        if (!string.IsNullOrWhiteSpace(caption))
            document["caption"] = Truncate(caption, MaxCaptionLength);

        var body = new Dictionary<string, object?>
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",
            ["to"] = NormalizeTo(phoneDigits),
            ["type"] = "document",
            ["document"] = document
        };
        return await PostAsync(body, ct).ConfigureAwait(false);
    }

    private async Task<(string? MediaId, string? Error)> UploadMediaAsync(
        string filePath,
        string fileName,
        CancellationToken ct)
    {
        if (!_settings.IsWhatsAppApiConfigured)
            return (null, "WhatsApp Cloud API is not configured.");

        var cfg = _settings.Current;
        var http = _httpFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, BuildGraphUrl(cfg, "media"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.WhatsAppAccessToken.Trim());

        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");

            using var form = new MultipartFormDataContent
            {
                { new StringContent("whatsapp"), "messaging_product" },
                { new StringContent("application/pdf"), "type" },
                { fileContent, "file", fileName }
            };
            req.Content = form;

            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return (null, ExtractError(raw, (int)resp.StatusCode));

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("id", out var idEl)
                && idEl.GetString() is { Length: > 0 } mediaId)
            {
                return (mediaId, null);
            }

            return (null, "WhatsApp media upload returned no id.");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static string BuildPdfFileName(string invoiceNumber)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string((invoiceNumber ?? string.Empty).Trim()
            .Select(ch => invalid.Contains(ch) ? '-' : ch)
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "Bill.pdf" : $"Bill-{safe}.pdf";
    }

    private static string BuildGraphUrl(BillShareSettings cfg, string edge)
    {
        var version = string.IsNullOrWhiteSpace(cfg.WhatsAppApiVersion)
            ? "v21.0"
            : cfg.WhatsAppApiVersion.Trim().Trim('/');
        return $"https://graph.facebook.com/{version}/{cfg.WhatsAppPhoneNumberId.Trim()}/{edge}";
    }

    private async Task<WhatsAppSendResult> PostAsync(Dictionary<string, object?> payload, CancellationToken ct)
    {
        var cfg = _settings.Current;
        if (!_settings.IsWhatsAppApiConfigured)
            return new WhatsAppSendResult(false, null, "WhatsApp Cloud API is not configured.");

        var http = _httpFactory.CreateClient(HttpClientName);
        using var req = new HttpRequestMessage(HttpMethod.Post, BuildGraphUrl(cfg, "messages"));
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
