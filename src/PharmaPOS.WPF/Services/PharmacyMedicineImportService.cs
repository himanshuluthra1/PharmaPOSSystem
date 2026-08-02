using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using PharmaPOS.Application.Features.Masters;

namespace PharmaPOS.WPF.Services;

public enum PharmacyCatalogSource
{
    OneMg,
    ApolloPharmacy,
    Pharmeasy,
    TrueMeds,
    NetMeds
}

public sealed class PharmacyMedicineImportResult
{
    public string Name { get; set; } = string.Empty;
    public string? GenericName { get; set; }
    public string? Brand { get; set; }
    public string? ManufacturerName { get; set; }
    public decimal Mrp { get; set; }
    public decimal GstPercent { get; set; } = 12m;
    public string? Notes { get; set; }
    public string SourceUrl { get; set; } = string.Empty;
    public string SourceLabel { get; set; } = string.Empty;
}

public interface IPharmacyMedicineImportService
{
    IReadOnlyList<(PharmacyCatalogSource Source, string DisplayName)> Sources { get; }
    Task<PharmacyMedicineImportResult> DownloadAsync(
        PharmacyCatalogSource source,
        string url,
        CancellationToken ct = default);
}

/// <summary>
/// Downloads a public pharmacy product page and extracts catalogue fields.
/// Uses HTML meta/JSON-LD first; if Gemini is enabled, refines extraction from page text.
/// </summary>
public sealed class PharmacyMedicineImportService : IPharmacyMedicineImportService
{
    private static readonly Dictionary<PharmacyCatalogSource, (string Label, string[] Hosts)> SourceMap = new()
    {
        [PharmacyCatalogSource.OneMg] = ("1MG", ["1mg.com", "www.1mg.com"]),
        [PharmacyCatalogSource.ApolloPharmacy] = ("Apollo Pharmacy", ["apollopharmacy.in", "www.apollopharmacy.in"]),
        [PharmacyCatalogSource.Pharmeasy] = ("Pharmeasy", ["pharmeasy.in", "www.pharmeasy.in"]),
        [PharmacyCatalogSource.TrueMeds] = ("TrueMeds", ["truemeds.in", "www.truemeds.in"]),
        [PharmacyCatalogSource.NetMeds] = ("NetMeds", ["netmeds.com", "www.netmeds.com"])
    };

    private readonly HttpClient _http;
    private readonly IAiBillSettingsService _aiSettings;

    public PharmacyMedicineImportService(HttpClient http, IAiBillSettingsService aiSettings)
    {
        _http = http;
        _aiSettings = aiSettings;
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 PharmaPOS/1.0");
            _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json");
        }
    }

    public IReadOnlyList<(PharmacyCatalogSource Source, string DisplayName)> Sources { get; } =
    [
        (PharmacyCatalogSource.OneMg, "1MG"),
        (PharmacyCatalogSource.ApolloPharmacy, "Apollo Pharmacy"),
        (PharmacyCatalogSource.Pharmeasy, "Pharmeasy"),
        (PharmacyCatalogSource.TrueMeds, "TrueMeds"),
        (PharmacyCatalogSource.NetMeds, "NetMeds")
    ];

    public async Task<PharmacyMedicineImportResult> DownloadAsync(
        PharmacyCatalogSource source,
        string url,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("Paste the medicine page URL.");

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Enter a valid http/https URL.");

        var (label, hosts) = SourceMap[source];
        var hostOk = hosts.Any(h =>
        {
            var bare = h.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h[4..] : h;
            return uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase)
                   || uri.Host.Equals(bare, StringComparison.OrdinalIgnoreCase)
                   || uri.Host.EndsWith("." + bare, StringComparison.OrdinalIgnoreCase);
        });
        if (!hostOk)
            throw new InvalidOperationException(
                $"URL host \"{uri.Host}\" does not match {label}. Paste a product link from {label}.");

        using var response = await _http.GetAsync(uri, ct);
        var html = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Could not download page ({(int)response.StatusCode}). Check the URL or try again.");

        if (html.Length < 200)
            throw new InvalidOperationException("Downloaded page was empty. The site may block automated access.");

        var extracted = ExtractFromHtml(html, uri.ToString(), label);

        _aiSettings.Load();
        if (_aiSettings.IsGeminiReady)
        {
            try
            {
                var refined = await RefineWithGeminiAsync(html, uri.ToString(), label, ct);
                if (refined is not null)
                {
                    if (!string.IsNullOrWhiteSpace(refined.Name)) extracted.Name = refined.Name.Trim();
                    if (!string.IsNullOrWhiteSpace(refined.GenericName)) extracted.GenericName = refined.GenericName.Trim();
                    if (!string.IsNullOrWhiteSpace(refined.Brand)) extracted.Brand = refined.Brand.Trim();
                    if (!string.IsNullOrWhiteSpace(refined.ManufacturerName))
                        extracted.ManufacturerName = refined.ManufacturerName.Trim();
                    if (refined.Mrp > 0) extracted.Mrp = refined.Mrp;
                    if (refined.GstPercent > 0) extracted.GstPercent = refined.GstPercent;
                }
            }
            catch
            {
                // Keep HTML extraction if Gemini fails.
            }
        }

        if (string.IsNullOrWhiteSpace(extracted.Name))
            throw new InvalidOperationException(
                "Could not read the medicine name from that page. Try another link, or enable Gemini AI for better extraction.");

        extracted.SourceUrl = uri.ToString();
        extracted.SourceLabel = label;
        extracted.Notes = $"Imported from {label}: {uri}";
        if (extracted.GstPercent <= 0) extracted.GstPercent = 12m;
        if (extracted.Mrp < 0) extracted.Mrp = 0;
        return extracted;
    }

    private static PharmacyMedicineImportResult ExtractFromHtml(string html, string url, string label)
    {
        var result = new PharmacyMedicineImportResult
        {
            SourceUrl = url,
            SourceLabel = label,
            GstPercent = 12m
        };

        // JSON-LD Product
        foreach (Match m in Regex.Matches(html,
                     @"<script[^>]*type\s*=\s*[""']application/ld\+json[""'][^>]*>(.*?)</script>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var json = m.Groups[1].Value.Trim();
            try
            {
                using var doc = JsonDocument.Parse(json);
                TryReadJsonLd(doc.RootElement, result);
            }
            catch { /* ignore malformed blocks */ }
        }

        if (string.IsNullOrWhiteSpace(result.Name))
        {
            var title = Decode(MetaContent(html, "og:title") ?? MetaContent(html, "twitter:title"))
                        ?? Decode(Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value);
            result.Name = CleanTitle(title) ?? string.Empty;
        }
        else
        {
            result.Name = CleanTitle(result.Name) ?? result.Name;
        }

        if (result.Mrp <= 0)
        {
            var price = MetaContent(html, "product:price:amount")
                        ?? MetaContent(html, "og:price:amount");
            if (decimal.TryParse(price, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var mrp))
                result.Mrp = mrp;
        }

        if (string.IsNullOrWhiteSpace(result.Brand))
            result.Brand = Decode(MetaContent(html, "product:brand") ?? MetaContent(html, "og:brand"));

        return result;
    }

    private static void TryReadJsonLd(JsonElement root, PharmacyMedicineImportResult result)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in root.EnumerateArray())
                TryReadJsonLd(el, result);
            return;
        }

        if (root.ValueKind != JsonValueKind.Object) return;

        if (root.TryGetProperty("@graph", out var graph))
            TryReadJsonLd(graph, result);

        var type = root.TryGetProperty("@type", out var t) ? t.ToString() : "";
        if (!type.Contains("Product", StringComparison.OrdinalIgnoreCase)
            && !type.Contains("Drug", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(result.Name)
            && root.TryGetProperty("name", out var nameProp))
        {
            var n = nameProp.GetString();
            if (!string.IsNullOrWhiteSpace(n))
                result.Name = n.Trim();
        }

        if (string.IsNullOrWhiteSpace(result.Brand)
            && root.TryGetProperty("brand", out var brand))
        {
            result.Brand = brand.ValueKind == JsonValueKind.Object && brand.TryGetProperty("name", out var bn)
                ? bn.GetString()
                : brand.GetString();
        }

        if (result.Mrp <= 0 && root.TryGetProperty("offers", out var offers))
        {
            JsonElement offer = offers.ValueKind == JsonValueKind.Array && offers.GetArrayLength() > 0
                ? offers[0]
                : offers;
            if (offer.TryGetProperty("price", out var priceEl))
            {
                if (priceEl.ValueKind == JsonValueKind.Number)
                    result.Mrp = priceEl.GetDecimal();
                else if (decimal.TryParse(priceEl.GetString(), System.Globalization.NumberStyles.Any,
                             System.Globalization.CultureInfo.InvariantCulture, out var p))
                    result.Mrp = p;
            }
        }

        if (string.IsNullOrWhiteSpace(result.GenericName)
            && root.TryGetProperty("description", out var desc))
        {
            var d = desc.GetString();
            if (!string.IsNullOrWhiteSpace(d) && d.Length < 120)
                result.GenericName = d.Trim();
        }
    }

    private async Task<PharmacyMedicineImportResult?> RefineWithGeminiAsync(
        string html, string url, string label, CancellationToken ct)
    {
        var cfg = _aiSettings.Current;
        var text = StripHtml(html);
        if (text.Length > 12000) text = text[..12000];

        var model = string.IsNullOrWhiteSpace(cfg.Model) ? "gemini-flash-lite-latest" : cfg.Model.Trim();
        model = model switch
        {
            "gemini-2.5-flash-lite" => "gemini-flash-lite-latest",
            "gemini-2.5-flash" => "gemini-flash-latest",
            _ => model
        };

        var prompt = $"""
            Extract medicine catalogue fields from this {label} product page text.
            URL: {url}
            Return JSON only with: name, genericName, brand, manufacturerName, mrp (number), gstPercent (number, usually 5 or 12).
            Use the retail pack name as name. If unknown, use null.
            Page text:
            {text}
            """;

        var body = new JsonObject
        {
            ["contents"] = new JsonArray
            {
                new JsonObject
                {
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = prompt } }
                }
            },
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.1,
                ["responseMimeType"] = "application/json"
            }
        };

        var apiUrl =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", cfg.ApiKey.Trim());
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(responseText);
        var payload = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();
        if (string.IsNullOrWhiteSpace(payload)) return null;

        payload = payload.Trim();
        if (payload.StartsWith("```"))
        {
            var nl = payload.IndexOf('\n');
            if (nl > 0) payload = payload[(nl + 1)..];
            if (payload.EndsWith("```")) payload = payload[..^3];
            payload = payload.Trim();
        }

        var parsed = JsonSerializer.Deserialize<PharmacyMedicineImportResult>(payload,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return parsed;
    }

    private static string? MetaContent(string html, string property)
    {
        var m = Regex.Match(html,
            $@"<meta[^>]+(?:property|name)\s*=\s*[""']{Regex.Escape(property)}[""'][^>]+content\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(html,
            $@"<meta[^>]+content\s*=\s*[""']([^""']+)[""'][^>]+(?:property|name)\s*=\s*[""']{Regex.Escape(property)}[""']",
            RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string StripHtml(string html)
    {
        html = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<[^>]+>", " ");
        html = System.Net.WebUtility.HtmlDecode(html);
        return Regex.Replace(html, @"\s+", " ").Trim();
    }

    private static string? Decode(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : System.Net.WebUtility.HtmlDecode(s).Trim();

    private static string? CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        title = Decode(Regex.Replace(title, "<.*?>", " ")) ?? title;
        // Drop site suffixes: "Foo | 1mg"
        var parts = title.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        title = parts.Length > 0 ? parts[0] : title;
        title = Regex.Replace(title, @"\s+", " ").Trim();
        return title.Length == 0 ? null : title;
    }
}
