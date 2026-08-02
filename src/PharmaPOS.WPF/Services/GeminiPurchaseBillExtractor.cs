using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PharmaPOS.Application.Features.Purchases;

namespace PharmaPOS.WPF.Services;

public interface IGeminiPurchaseBillExtractor
{
    Task<ScannedPurchaseDraftDto> ExtractAsync(string imagePath, CancellationToken ct = default);
}

/// <summary>
/// Sends a supplier bill image to Google Gemini and maps the JSON response
/// into <see cref="ScannedPurchaseDraftDto"/>.
/// </summary>
public sealed class GeminiPurchaseBillExtractor : IGeminiPurchaseBillExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private const string Prompt = """
        You are reading an Indian pharmacy wholesale / distributor purchase invoice image.
        Extract every product line and header fields. Return ONLY JSON matching the schema.
        Rules:
        - supplierName = seller/pharmacy/distributor name (not the buyer).
        - invoiceNumber = supplier invoice / bill number.
        - invoiceDate = invoice date as yyyy-MM-dd (not due date).
        - netAmount = final net / grand total payable.
        - For each line: itemName, batchNumber, expiry as yyyy-MM-dd (use last day of month if only MM-yyyy),
          quantity (billed qty, not free), freeQuantity, mrp, rate (purchase/trade rate),
          discountPercent, gstPercent, lineAmount.
        - Skip header/footer/tax-summary rows. Include free goods qty in freeQuantity.
        - If a field is unreadable, omit it or use null; do not invent medicines.
        """;

    private readonly IAiBillSettingsService _settings;
    private readonly HttpClient _http;

    public GeminiPurchaseBillExtractor(IAiBillSettingsService settings, HttpClient http)
    {
        _settings = settings;
        _http = http;
    }

    public async Task<ScannedPurchaseDraftDto> ExtractAsync(string imagePath, CancellationToken ct = default)
    {
        var cfg = _settings.Current;
        if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            throw new InvalidOperationException("Gemini API key is not configured. Set it under Settings → Preferences.");

        var model = string.IsNullOrWhiteSpace(cfg.Model) ? "gemini-flash-lite-latest" : cfg.Model.Trim();
        model = model switch
        {
            "gemini-2.5-flash-lite" => "gemini-flash-lite-latest",
            "gemini-2.5-flash" => "gemini-flash-latest",
            _ => model
        };
        var (mime, base64) = await ReadImageAsBase64Async(imagePath, ct);

        var body = BuildRequestBody(mime, base64);
        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", cfg.ApiKey.Trim());
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(FormatApiError(response.StatusCode, responseText));

        var jsonPayload = ExtractCandidateText(responseText);
        var parsed = JsonSerializer.Deserialize<GeminiBillResponse>(jsonPayload, JsonOptions)
                     ?? throw new InvalidOperationException("Gemini returned empty JSON.");

        return MapToDraft(parsed, jsonPayload);
    }

    private static JsonObject BuildRequestBody(string mime, string base64)
    {
        var schema = new JsonObject
        {
            ["type"] = "OBJECT",
            ["properties"] = new JsonObject
            {
                ["supplierName"] = new JsonObject { ["type"] = "STRING" },
                ["invoiceNumber"] = new JsonObject { ["type"] = "STRING" },
                ["invoiceDate"] = new JsonObject { ["type"] = "STRING" },
                ["netAmount"] = new JsonObject { ["type"] = "NUMBER" },
                ["lines"] = new JsonObject
                {
                    ["type"] = "ARRAY",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "OBJECT",
                        ["properties"] = new JsonObject
                        {
                            ["itemName"] = new JsonObject { ["type"] = "STRING" },
                            ["batchNumber"] = new JsonObject { ["type"] = "STRING" },
                            ["expiry"] = new JsonObject { ["type"] = "STRING" },
                            ["quantity"] = new JsonObject { ["type"] = "NUMBER" },
                            ["freeQuantity"] = new JsonObject { ["type"] = "NUMBER" },
                            ["mrp"] = new JsonObject { ["type"] = "NUMBER" },
                            ["rate"] = new JsonObject { ["type"] = "NUMBER" },
                            ["discountPercent"] = new JsonObject { ["type"] = "NUMBER" },
                            ["gstPercent"] = new JsonObject { ["type"] = "NUMBER" },
                            ["lineAmount"] = new JsonObject { ["type"] = "NUMBER" }
                        },
                        ["required"] = new JsonArray("itemName")
                    }
                }
            },
            ["required"] = new JsonArray("lines")
        };

        return new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray
                    {
                        new JsonObject { ["text"] = Prompt },
                        new JsonObject
                        {
                            ["inline_data"] = new JsonObject
                            {
                                ["mime_type"] = mime,
                                ["data"] = base64
                            }
                        }
                    }
                }
            },
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.1,
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = schema
            }
        };
    }

    private static async Task<(string Mime, string Base64)> ReadImageAsBase64Async(string imagePath, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(imagePath, ct);
        // Cap payload size (~8 MB) for inline Gemini requests.
        if (bytes.Length > 8 * 1024 * 1024)
            throw new InvalidOperationException(
                "Bill image is too large (over 8 MB). Please rescan at a lower resolution or compress the image.");

        var ext = Path.GetExtension(imagePath).ToLowerInvariant();
        var mime = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "image/jpeg"
        };

        return (mime, Convert.ToBase64String(bytes));
    }

    private static string ExtractCandidateText(string responseText)
    {
        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            if (root.TryGetProperty("error", out var err))
                throw new InvalidOperationException(err.ToString());
            throw new InvalidOperationException("Gemini returned no candidates.");
        }

        var content = candidates[0].GetProperty("content");
        if (!content.TryGetProperty("parts", out var parts) || parts.GetArrayLength() == 0)
            throw new InvalidOperationException("Gemini returned an empty response.");

        var text = parts[0].GetProperty("text").GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Gemini returned empty text.");

        // Strip accidental markdown fences.
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = text.IndexOf('\n');
            if (firstNl > 0) text = text[(firstNl + 1)..];
            if (text.EndsWith("```", StringComparison.Ordinal))
                text = text[..^3];
            text = text.Trim();
        }

        return text;
    }

    private static string FormatApiError(System.Net.HttpStatusCode status, string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var err)
                && err.TryGetProperty("message", out var msg))
                return $"Gemini API error ({(int)status}): {msg.GetString()}";
        }
        catch { /* ignore */ }

        var snippet = body.Length > 300 ? body[..300] + "…" : body;
        return $"Gemini API error ({(int)status}): {snippet}";
    }

    private static ScannedPurchaseDraftDto MapToDraft(GeminiBillResponse parsed, string rawJson)
    {
        var draft = new ScannedPurchaseDraftDto
        {
            RawText = rawJson,
            SupplierName = NullIfBlank(parsed.SupplierName),
            SupplierInvoiceNumber = NullIfBlank(parsed.InvoiceNumber),
            InvoiceDate = ParseDate(parsed.InvoiceDate) ?? DateTime.Today,
            GrandTotalHint = parsed.NetAmount is > 0 ? parsed.NetAmount : null
        };

        foreach (var line in parsed.Lines ?? [])
        {
            var name = NullIfBlank(line.ItemName);
            if (name is null) continue;

            var rate = line.Rate ?? 0;
            var mrp = line.Mrp ?? 0;
            if (rate <= 0 && mrp > 0) rate = mrp;
            if (mrp <= 0 && rate > 0) mrp = rate;

            draft.Lines.Add(new ScannedPurchaseLineDto
            {
                OcrItemName = name,
                BatchNumber = NullIfBlank(line.BatchNumber),
                ExpiryDate = ParseDate(line.Expiry),
                Quantity = line.Quantity is > 0 ? line.Quantity.Value : 1,
                FreeQuantity = line.FreeQuantity is > 0 ? line.FreeQuantity.Value : 0,
                PurchasePrice = rate,
                Mrp = mrp,
                SellingPrice = mrp > 0 ? mrp : rate,
                GstPercent = line.GstPercent ?? 0,
                DiscountPercent = line.DiscountPercent ?? 0,
                LineAmountHint = line.LineAmount
            });
        }

        draft.Warnings.Insert(0, "Engine: Gemini AI");
        draft.Warnings.Add(
            draft.Lines.Count == 0
                ? "Gemini found no item lines. Try a clearer photo or switch model to gemini-2.5-flash."
                : $"AI (Gemini) detected {draft.Lines.Count} item row(s). Verify matches, batch, qty and prices.");

        if (string.IsNullOrWhiteSpace(draft.SupplierInvoiceNumber))
            draft.Warnings.Add("Supplier invoice number was not detected clearly.");
        if (string.IsNullOrWhiteSpace(draft.SupplierName))
            draft.Warnings.Add("Supplier name was not detected clearly.");

        return draft;
    }

    private static string? NullIfBlank(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();

        // Month-year (expiry): 10-2025 / 10/25 / 2025-10
        var my = System.Text.RegularExpressions.Regex.Match(
            s, @"^(?:(0?[1-9]|1[0-2])[\/\-]((?:20)?\d{2})|((?:20)?\d{2})[\/\-](0?[1-9]|1[0-2]))$");
        if (my.Success)
        {
            int mm, yy;
            if (my.Groups[1].Success)
            {
                mm = int.Parse(my.Groups[1].Value, CultureInfo.InvariantCulture);
                yy = int.Parse(my.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                yy = int.Parse(my.Groups[3].Value, CultureInfo.InvariantCulture);
                mm = int.Parse(my.Groups[4].Value, CultureInfo.InvariantCulture);
            }
            if (yy < 100) yy += 2000;
            if (mm is >= 1 and <= 12)
                return new DateTime(yy, mm, DateTime.DaysInMonth(yy, mm));
        }

        string[] formats =
        [
            "yyyy-MM-dd", "yyyy-M-d", "dd-MM-yyyy", "d-M-yyyy",
            "dd/MM/yyyy", "d/M/yyyy", "yyyy/MM/dd"
        ];

        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var dt))
            return dt.Date;

        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out dt))
            return dt.Date;

        return null;
    }

    private sealed class GeminiBillResponse
    {
        public string? SupplierName { get; set; }
        public string? InvoiceNumber { get; set; }
        public string? InvoiceDate { get; set; }
        public decimal? NetAmount { get; set; }
        public List<GeminiBillLine>? Lines { get; set; }
    }

    private sealed class GeminiBillLine
    {
        public string? ItemName { get; set; }
        public string? BatchNumber { get; set; }
        public string? Expiry { get; set; }
        public decimal? Quantity { get; set; }
        public decimal? FreeQuantity { get; set; }
        public decimal? Mrp { get; set; }
        public decimal? Rate { get; set; }
        public decimal? DiscountPercent { get; set; }
        public decimal? GstPercent { get; set; }
        public decimal? LineAmount { get; set; }
    }
}
