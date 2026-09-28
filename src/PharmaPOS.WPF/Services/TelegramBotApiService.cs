using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaPOS.WPF.Services;

public sealed record TelegramSendResult(bool Success, string? MessageId, string? Error);

public interface ITelegramBotApiService
{
    bool IsConfigured { get; }

    /// <summary>Send a text message to a Telegram chat id or @username.</summary>
    Task<TelegramSendResult> SendTextAsync(string chatIdOrUsername, string message, CancellationToken ct = default);
}

/// <summary>Telegram Bot API client (api.telegram.org).</summary>
public sealed class TelegramBotApiService : ITelegramBotApiService
{
    public const string HttpClientName = "TelegramBotApi";

    private readonly IHttpClientFactory _httpFactory;
    private readonly IBillShareSettingsService _settings;

    public TelegramBotApiService(IHttpClientFactory httpFactory, IBillShareSettingsService settings)
    {
        _httpFactory = httpFactory;
        _settings = settings;
    }

    public bool IsConfigured => _settings.IsTelegramBotConfigured;

    public async Task<TelegramSendResult> SendTextAsync(
        string chatIdOrUsername, string message, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return new TelegramSendResult(false, null, "Telegram Bot API is not configured.");

        var chatId = (chatIdOrUsername ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(chatId))
            return new TelegramSendResult(false, null, "Telegram chat id / @username is required.");

        var cfg = _settings.Current;
        var token = cfg.TelegramBotToken.Trim();
        var url = $"https://api.telegram.org/bot{token}/sendMessage";

        try
        {
            var client = _httpFactory.CreateClient(HttpClientName);
            using var resp = await client.PostAsJsonAsync(url, new
            {
                chat_id = chatId,
                text = message,
                disable_web_page_preview = false
            }, ct).ConfigureAwait(false);

            var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            if (ok)
            {
                string? messageId = null;
                if (root.TryGetProperty("result", out var result)
                    && result.TryGetProperty("message_id", out var mid))
                    messageId = mid.ToString();
                return new TelegramSendResult(true, messageId, null);
            }

            var description = root.TryGetProperty("description", out var desc)
                ? desc.GetString()
                : null;
            return new TelegramSendResult(
                false,
                null,
                string.IsNullOrWhiteSpace(description)
                    ? $"Telegram API HTTP {(int)resp.StatusCode}"
                    : description);
        }
        catch (Exception ex)
        {
            return new TelegramSendResult(false, null, ex.Message);
        }
    }
}
