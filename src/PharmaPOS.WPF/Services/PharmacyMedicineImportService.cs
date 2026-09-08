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
/// Uses HTML meta/JSON-LD and site-specific salt patterns first; if Gemini is enabled, refines extraction.
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
        }

        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");

        if (!_http.DefaultRequestHeaders.AcceptLanguage.Any())
            _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-IN,en;q=0.9");
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

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-IN,en;q=0.9");
        request.Headers.TryAddWithoutValidation("Referer", $"{uri.Scheme}://{uri.Host}/");
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache");

        using var response = await _http.SendAsync(request, ct);
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
                    MergeRefined(extracted, refined);
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

    private static void MergeRefined(PharmacyMedicineImportResult extracted, PharmacyMedicineImportResult refined)
    {
        if (!string.IsNullOrWhiteSpace(refined.Name)) extracted.Name = refined.Name.Trim();
        if (!string.IsNullOrWhiteSpace(refined.GenericName))
        {
            var candidate = NormalizeSalt(refined.GenericName);
            if (SaltQuality(candidate) >= SaltQuality(extracted.GenericName))
                extracted.GenericName = candidate;
        }

        // Brand in POS = pharmaceutical company / marketer, not product or website brand.
        if (!string.IsNullOrWhiteSpace(refined.ManufacturerName))
            ConsiderCompany(extracted, refined.ManufacturerName);
        if (!string.IsNullOrWhiteSpace(refined.Brand))
            ConsiderCompany(extracted, refined.Brand);
        if (refined.Mrp > 0) extracted.Mrp = refined.Mrp;
        if (refined.GstPercent > 0) extracted.GstPercent = refined.GstPercent;
        FinalizeBrandAsCompany(extracted);
    }

    private static PharmacyMedicineImportResult ExtractFromHtml(string html, string url, string label)
    {
        var result = new PharmacyMedicineImportResult
        {
            SourceUrl = url,
            SourceLabel = label,
            GstPercent = 12m
        };

        // JSON-LD Product / Drug
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

        TryEnrichFromNextData(html, result);
        TryEnrichSaltFromHtml(html, result);
        TryEnrichCompanyAndMrpFromHtml(html, result);

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

        // Prefer a clean H1 when og/meta titles are marketing copy ("Buy … online").
        var h1 = Decode(Regex.Match(html, @"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline).Groups[1].Value);
        var h1Clean = CleanTitle(h1);
        if (!string.IsNullOrWhiteSpace(h1Clean)
            && (string.IsNullOrWhiteSpace(result.Name)
                || result.Name.StartsWith("Buy ", StringComparison.OrdinalIgnoreCase)
                || result.Name.Contains(" online", StringComparison.OrdinalIgnoreCase)))
        {
            result.Name = h1Clean;
        }

        if (result.Mrp <= 0)
        {
            var price = MetaContent(html, "product:price:amount")
                        ?? MetaContent(html, "og:price:amount");
            if (decimal.TryParse(price, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var mrp))
                result.Mrp = mrp;
        }

        if (!string.IsNullOrWhiteSpace(result.GenericName))
            result.GenericName = NormalizeSalt(result.GenericName);

        FinalizeBrandAsCompany(result);
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
        var isProductish = type.Contains("Product", StringComparison.OrdinalIgnoreCase)
                           || type.Contains("Drug", StringComparison.OrdinalIgnoreCase);
        if (!isProductish) return;

        if (string.IsNullOrWhiteSpace(result.Name)
            && root.TryGetProperty("name", out var nameProp))
        {
            var n = nameProp.GetString();
            if (!string.IsNullOrWhiteSpace(n))
                result.Name = n.Trim();
        }

        // Do not use schema.org "brand" (often product family or site name). Company → Brand.
        if (root.TryGetProperty("manufacturer", out var mfr))
        {
            var company = mfr.ValueKind == JsonValueKind.Object
                ? (mfr.TryGetProperty("legalName", out var ln) ? ln.GetString()
                    : mfr.TryGetProperty("name", out var mn) ? mn.GetString() : null)
                : mfr.GetString();
            ConsiderCompany(result, company);
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

        // Apollo / schema.org Drug: nonProprietaryName holds salt+strength (e.g. SPIRONOLACTONE-50MG+TORSEMIDE-10MG)
        foreach (var key in new[] { "nonProprietaryName", "activeIngredient", "drugUnit" })
        {
            if (root.TryGetProperty(key, out var prop))
                ConsiderSalt(result, prop.GetString());
        }

        if (string.IsNullOrWhiteSpace(result.GenericName)
            && root.TryGetProperty("description", out var desc))
        {
            var d = desc.GetString();
            if (!string.IsNullOrWhiteSpace(d) && d.Length < 120 && LooksLikeSalt(d))
                ConsiderSalt(result, d);
        }
    }

    private static void TryEnrichFromNextData(string html, PharmacyMedicineImportResult result)
    {
        var m = Regex.Match(html,
            @"<script[^>]*id\s*=\s*[""']__NEXT_DATA__[""'][^>]*>(.*?)</script>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) return;

        try
        {
            using var doc = JsonDocument.Parse(m.Groups[1].Value);
            WalkNextData(doc.RootElement, result, path: "");
        }
        catch
        {
            // ignore
        }
    }

    private static bool IsProductSaltPath(string path)
    {
        // TrueMeds embeds many substitute compositions under apiCardData — ignore those.
        if (path.Contains("apicarddata", StringComparison.OrdinalIgnoreCase)
            || path.Contains("suggestion", StringComparison.OrdinalIgnoreCase)
            || path.Contains("subsmedicine", StringComparison.OrdinalIgnoreCase))
            return false;

        return path.Contains("currentmed", StringComparison.OrdinalIgnoreCase)
               || path.Contains("medicinedetails", StringComparison.OrdinalIgnoreCase)
               || path.Contains("originalmedicine", StringComparison.OrdinalIgnoreCase)
               || path.Contains("productpagereducer", StringComparison.OrdinalIgnoreCase)
               || path.Contains("currentopenedmed", StringComparison.OrdinalIgnoreCase)
               || path.Contains("productdetails", StringComparison.OrdinalIgnoreCase)
               || path.Contains(".product.", StringComparison.OrdinalIgnoreCase)
               || path.EndsWith(".product", StringComparison.OrdinalIgnoreCase);
    }

    private static void WalkNextData(JsonElement el, PharmacyMedicineImportResult result, string path)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
            {
                string? molecule = null;
                string? strengthValue = null;
                string? strengthUnit = null;
                var allowSaltHere = IsProductSaltPath(path) || path.Length == 0;

                foreach (var prop in el.EnumerateObject())
                {
                    var key = prop.Name;
                    var keyLower = key.ToLowerInvariant();
                    var childPath = string.IsNullOrEmpty(path) ? key : path + "." + key;

                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var val = prop.Value.GetString();
                        if (string.IsNullOrWhiteSpace(val))
                        {
                            WalkNextData(prop.Value, result, childPath);
                            continue;
                        }

                        var isSaltKey = keyLower is "composition" or "salt" or "saltcomposition" or "salt_composition"
                            or "saltcontent" or "drugcomposition" or "drug_composition"
                            or "genericname" or "generic_name" or "nonproprietaryname"
                            or "activesubstance" or "molecules"
                            || (keyLower == "name" && path.Contains("composition", StringComparison.OrdinalIgnoreCase));
                        if (isSaltKey)
                        {
                            if (allowSaltHere || IsProductSaltPath(childPath) || path.Contains("composition", StringComparison.OrdinalIgnoreCase))
                                ConsiderSalt(result, val);
                        }
                        else if (keyLower is "molecule" or "moleculename")
                        {
                            molecule = val;
                        }
                        else if (keyLower is "drugstrengthvalue")
                        {
                            strengthValue = val;
                        }
                        else if (keyLower is "drugstrengthunit")
                        {
                            strengthUnit = val;
                        }
                        else if ((keyLower is "name" or "medicinename" or "displayname" or "productname")
                                 && !path.Contains("composition", StringComparison.OrdinalIgnoreCase)
                                 && string.IsNullOrWhiteSpace(result.Name)
                                 && val.Length is > 2 and < 120
                                 && !val.Contains("http", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Name = val.Trim();
                        }
                        else if (keyLower is "manufacturer" or "manufacturername" or "mfgname"
                                 or "marketer" or "marketername" or "company" or "companyname"
                                 or "legalname")
                        {
                            if (allowSaltHere || IsProductSaltPath(childPath) || path.Contains("productdetails", StringComparison.OrdinalIgnoreCase))
                                ConsiderCompany(result, val, boost: 80);
                        }
                    }
                    else if (prop.Value.ValueKind is JsonValueKind.Number
                             && (keyLower is "mrp" or "price" or "sellingprice" or "maxretailprice")
                             && result.Mrp <= 0
                             && (allowSaltHere || IsProductSaltPath(path) || path.Contains("productdetails", StringComparison.OrdinalIgnoreCase)))
                    {
                        result.Mrp = prop.Value.GetDecimal();
                    }

                    WalkNextData(prop.Value, result, childPath);
                }

                // PharmEasy: molecule + drugStrengthValue/Unit on the same object
                if (!string.IsNullOrWhiteSpace(molecule))
                {
                    var combined = CombineMoleculeAndStrength(molecule, strengthValue, strengthUnit);
                    ConsiderSalt(result, combined);
                }

                break;
            }
            case JsonValueKind.Array:
                var i = 0;
                foreach (var child in el.EnumerateArray().Take(80))
                {
                    WalkNextData(child, result, $"{path}[{i}]");
                    i++;
                }
                break;
        }
    }

    private static string CombineMoleculeAndStrength(string molecule, string? strengthValue, string? strengthUnit)
    {
        molecule = molecule.Trim();
        if (string.IsNullOrWhiteSpace(strengthValue))
            return molecule;

        var parts = molecule.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var strengths = strengthValue.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var units = (strengthUnit ?? "mg")
            .Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0) return molecule;

        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append(" + ");
            sb.Append(parts[i]);
            if (i < strengths.Length)
            {
                var unit = i < units.Length ? units[i] : (units.Length > 0 ? units[^1] : "mg");
                if (decimal.TryParse(strengths[i], System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var qty))
                    sb.Append(' ').Append(qty.ToString("0.##")).Append(unit.ToLowerInvariant());
                else
                    sb.Append(' ').Append(strengths[i]).Append(unit.ToLowerInvariant());
            }
        }

        return sb.ToString();
    }

    private static void TryEnrichSaltFromHtml(string html, PharmacyMedicineImportResult result)
    {
        // 1mg: salt_composition.display_text with HTML wrapper
        foreach (Match m in Regex.Matches(html,
                     @"""salt_composition""\s*:\s*\{[^\}]{0,400}?""display_text""\s*:\s*""([^""]{8,400})""",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            ConsiderSalt(result, CleanHtmlSnippet(m.Groups[1].Value));
        }

        // Apollo / others: Composition:SPIRONOLACTONE-50MG+TORSEMIDE-10MG
        foreach (Match m in Regex.Matches(html,
                     @"(?:Composition|Salt\s*Composition|Salt\s*Content|Active\s*Ingredient)\s*[:：]?\s*</?(?:[^>]+>)?\s*([A-Za-z][A-Za-z0-9+\-/\s().%]{6,160})",
                     RegexOptions.IgnoreCase))
        {
            ConsiderSalt(result, CleanHtmlSnippet(m.Groups[1].Value));
        }

        // PharmEasy markdown/table style: Salt Content | Spironolactone(...)
        foreach (Match m in Regex.Matches(html,
                     @"Salt\s*Content[^|]{0,60}\|\s*\[?\s*([^\]|<\n]{6,200})",
                     RegexOptions.IgnoreCase))
        {
            ConsiderSalt(result, CleanHtmlSnippet(m.Groups[1].Value));
        }

        // TrueMeds: composition on opened product (not suggestion / substitute cards)
        foreach (Match m in Regex.Matches(html,
                     @"""(?:currentMed|originalMedicineDetails|currentOpenedMed)""[\s\S]{0,400}?""product""[\s\S]{0,2000}?""composition""\s*:\s*""([^""]{4,200})""",
                     RegexOptions.IgnoreCase))
        {
            ConsiderSalt(result, m.Groups[1].Value);
        }

        foreach (Match m in Regex.Matches(html,
                     @"""medicineDetails""[\s\S]{0,2000}?""composition""\s*:\s*""([^""]{4,200})""",
                     RegexOptions.IgnoreCase))
        {
            ConsiderSalt(result, m.Groups[1].Value);
        }

        // TrueMeds saltComposition array on product card
        foreach (Match m in Regex.Matches(html,
                     @"""saltComposition""\s*:\s*\[(.*?)\]",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var salts = Regex.Matches(m.Groups[1].Value,
                @"""saltName""\s*:\s*""([^""]+)""\s*,\s*""quantity""\s*:\s*""([^""]+)""",
                RegexOptions.IgnoreCase);
            if (salts.Count is >= 1 and <= 4)
            {
                var joined = string.Join(" + ",
                    salts.Select(s => $"{s.Groups[1].Value.Trim()} {s.Groups[2].Value.Trim()}"));
                ConsiderSalt(result, joined);
            }
        }

        // Explicit JSON fields often present even without __NEXT_DATA__
        foreach (Match m in Regex.Matches(html,
                     @"""(?:genericname|generic_name|saltComposition|salt_composition|nonProprietaryName|molecule)""\s*:\s*""([^""]{4,200})""",
                     RegexOptions.IgnoreCase))
        {
            ConsiderSalt(result, m.Groups[1].Value);
        }

        // Generic "composition" JSON — skip when already have a strong salt (substitutes pollute pages)
        if (SaltQuality(result.GenericName) < 70)
        {
            foreach (Match m in Regex.Matches(html,
                         @"""composition""\s*:\s*""([^""]{4,200})""",
                         RegexOptions.IgnoreCase))
            {
                ConsiderSalt(result, m.Groups[1].Value);
            }
        }

        // "Drug (50 Mg) + Drug (10 Mg)" — TrueMeds / PharmEasy
        foreach (Match m in Regex.Matches(html,
                     @"([A-Za-z][A-Za-z0-9\s/.\-]{1,40}\(\s*\d+(?:\.\d+)?\s*M[cg]\s*\)(?:\s*\+\s*[A-Za-z][A-Za-z0-9\s/.\-]{1,40}\(\s*\d+(?:\.\d+)?\s*M[cg]\s*\))+)",
                     RegexOptions.IgnoreCase))
        {
            ConsiderSalt(result, m.Groups[1].Value);
        }

        // "Drug 50 mg+Drug 10 mg" — Netmeds
        foreach (Match m in Regex.Matches(html,
                     @"([A-Za-z][A-Za-z0-9\s.\-]{1,40}\s+\d+(?:\.\d+)?\s*mg(?:\s*\+\s*[A-Za-z][A-Za-z0-9\s.\-]{1,40}\s+\d+(?:\.\d+)?\s*mg)+)",
                     RegexOptions.IgnoreCase))
        {
            ConsiderSalt(result, m.Groups[1].Value);
        }

        // Apollo compact: SPIRONOLACTONE-50MG+TORSEMIDE-10MG
        foreach (Match m in Regex.Matches(html,
                     @"\b([A-Z][A-Z0-9]*(?:-\d+(?:\.\d+)?MG)?(?:\+[A-Z][A-Z0-9]*(?:-\d+(?:\.\d+)?MG)?)+)\b"))
        {
            if (m.Groups[1].Value.Contains("MG", StringComparison.Ordinal))
                ConsiderSalt(result, m.Groups[1].Value);
        }
    }

    private static void TryEnrichCompanyAndMrpFromHtml(string html, PharmacyMedicineImportResult result)
    {
        // Prefer product-scoped company / MRP first (boosted), then fall back to page-wide fields.

        // PharmEasy
        foreach (Match m in Regex.Matches(html,
                     @"""productDetails""[\s\S]{0,2000}?""manufacturer""\s*:\s*""([^""]+)""",
                     RegexOptions.IgnoreCase))
        {
            ConsiderCompany(result, m.Groups[1].Value, boost: 80);
        }

        // TrueMeds / Netmeds-style product card
        foreach (Match m in Regex.Matches(html,
                     @"""(?:currentMed|originalMedicineDetails|currentOpenedMed)""[\s\S]{0,400}?""product""[\s\S]{0,2000}?""manufacturerName""\s*:\s*""([^""]+)""",
                     RegexOptions.IgnoreCase))
        {
            ConsiderCompany(result, m.Groups[1].Value, boost: 80);
        }

        foreach (Match m in Regex.Matches(html,
                     @"""(?:currentMed|originalMedicineDetails|currentOpenedMed)""[\s\S]{0,400}?""product""[\s\S]{0,2000}?""mrp""\s*:\s*([0-9]+(?:\.[0-9]+)?)""",
                     RegexOptions.IgnoreCase))
        {
            if (decimal.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var mrp)
                && mrp > 0)
                result.Mrp = mrp;
        }

        // Apollo Drug schema manufacturer
        foreach (Match m in Regex.Matches(html,
                     @"""@type""\s*:\s*""MedicalOrganization""\s*,\s*""legalName""\s*:\s*""([^""]+)""",
                     RegexOptions.IgnoreCase))
        {
            ConsiderCompany(result, m.Groups[1].Value, boost: 80);
        }

        // 1mg marketer link text
        foreach (Match m in Regex.Matches(html,
                     @"/marketer/[^""']+""[^>]*>[\s\S]{0,240}?<div[^>]*>([^<]{2,80})</div>",
                     RegexOptions.IgnoreCase))
        {
            ConsiderCompany(result, m.Groups[1].Value, boost: 70);
        }

        // 1mg / others: "manufacturer":"by Cipla Ltd"
        foreach (Match m in Regex.Matches(html,
                     @"""(?:manufacturer|manufacturerName|manufacturers_name|marketer|marketer_name|companyName|company_name|legalName)""\s*:\s*""([^""]{2,120})""",
                     RegexOptions.IgnoreCase))
        {
            ConsiderCompany(result, m.Groups[1].Value);
        }

        foreach (Match m in Regex.Matches(html,
                     @"\bby\s+([A-Z][A-Za-z0-9&.,'’\-\s]{2,80}?(?:Ltd\.?|Limited|Pvt\.?|Private Limited|Healthcare|Pharma(?:ceuticals)?|Laboratories))\b"))
        {
            ConsiderCompany(result, m.Groups[1].Value);
        }

        // MRP near product sku (1mg)
        foreach (Match m in Regex.Matches(html,
                     @"""sku_name""\s*:\s*""[^""]{3,120}""\s*,\s*""mrp""\s*:\s*([0-9]+(?:\.[0-9]+)?)""",
                     RegexOptions.IgnoreCase))
        {
            if (result.Mrp <= 0
                && decimal.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var mrp))
                result.Mrp = mrp;
        }

        if (result.Mrp <= 0)
        {
            foreach (Match m in Regex.Matches(html,
                         @"""mrp""\s*:\s*([0-9]+(?:\.[0-9]+)?)"))
            {
                if (decimal.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var mrp)
                    && mrp > 0)
                {
                    result.Mrp = mrp;
                    break;
                }
            }
        }

        // Visible "MRP ₹122.56" (PharmEasy and others) — prefer over discounted offer price
        foreach (Match m in Regex.Matches(html,
                     @"MRP(?:\s|<!--\s*-->|₹|Rs\.?|INR|&nbsp;){0,12}([0-9]+(?:\.[0-9]+)?)",
                     RegexOptions.IgnoreCase))
        {
            if (decimal.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var mrp)
                && mrp > 0)
            {
                result.Mrp = mrp;
                break;
            }
        }
    }

    private static void ConsiderCompany(PharmacyMedicineImportResult result, string? raw, int boost = 0)
    {
        var company = CleanCompanyName(raw);
        if (string.IsNullOrWhiteSpace(company) || IsMarketplaceBrand(company))
            return;

        var score = CompanyQuality(company) + boost;
        if (score <= 0) return;

        // Product-scoped hits (boosted) always win over page footer / substitute companies.
        if (boost >= 70)
        {
            result.ManufacturerName = company;
            result.Brand = company;
            return;
        }

        if (string.IsNullOrWhiteSpace(result.ManufacturerName))
        {
            result.ManufacturerName = company;
            result.Brand = company;
            return;
        }

        if (CompanyQuality(result.ManufacturerName) < score)
        {
            result.ManufacturerName = company;
            result.Brand = company;
        }
    }

    private static void FinalizeBrandAsCompany(PharmacyMedicineImportResult result)
    {
        var company = CleanCompanyName(result.ManufacturerName)
                      ?? CleanCompanyName(result.Brand);
        if (string.IsNullOrWhiteSpace(company) || IsMarketplaceBrand(company))
        {
            if (IsMarketplaceBrand(result.Brand))
                result.Brand = null;
            return;
        }

        result.ManufacturerName = company;
        result.Brand = company;
    }

    private static string? CleanCompanyName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = System.Net.WebUtility.HtmlDecode(raw);
        s = Regex.Replace(s, @"<[^>]+>", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"^(?:by|from|marketer|manufacturer|mfg)\s*[:：]?\s*", "", RegexOptions.IgnoreCase);
        s = s.Trim('"', '\'', ' ', '.');
        if (s.Length is < 2 or > 100) return null;
        if (s.Contains('{') || s.Contains('}')) return null;
        return s;
    }

    private static bool IsMarketplaceBrand(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand)) return true;
        var b = brand.Trim();
        string[] markets =
        [
            "Apollo Pharmacy", "Apollo", "PharmEasy", "Pharmeasy", "Netmeds", "NetMeds",
            "Tata 1mg", "1mg", "1MG", "TrueMeds", "Truemeds", "True Meds",
            "Threpsi Solutions Pvt Ltd", "Threpsi Solutions", "API Holdings"
        ];
        return markets.Any(m => b.Equals(m, StringComparison.OrdinalIgnoreCase)
                                || b.Contains(m, StringComparison.OrdinalIgnoreCase) && m.Length >= 10);
    }

    private static int CompanyQuality(string? company)
    {
        if (string.IsNullOrWhiteSpace(company) || IsMarketplaceBrand(company)) return -1;
        var score = 10;
        if (Regex.IsMatch(company, @"\b(Ltd\.?|Limited|Pvt\.?|Private|Pharma|Laboratories|Healthcare|Inc\.?)\b",
                RegexOptions.IgnoreCase))
            score += 40;
        if (company.Contains(' ')) score += 10;
        score += Math.Min(company.Length, 30);
        return score;
    }

    private static void ConsiderSalt(PharmacyMedicineImportResult result, string? raw)
    {
        var normalized = NormalizeSalt(raw);
        if (string.IsNullOrWhiteSpace(normalized) || !LooksLikeSalt(normalized))
            return;

        if (SaltQuality(normalized) > SaltQuality(result.GenericName))
            result.GenericName = normalized;
    }

    private static bool LooksLikeSalt(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length is < 4 or > 220)
            return false;
        if (value.Contains("http", StringComparison.OrdinalIgnoreCase))
            return false;
        if (value.Contains("return policy", StringComparison.OrdinalIgnoreCase))
            return false;
        if (Regex.IsMatch(value, @"\b(contains|available|buy online|side effects|substitutes)\b",
                RegexOptions.IgnoreCase))
            return false;
        // Prefer chemical-ish tokens / strengths; reject long marketing sentences.
        if (value.Count(c => c == ' ') > 24)
            return false;
        // Must look like a salt list, not truncated sentence fragments ("t 15 composition…")
        if (!Regex.IsMatch(value.TrimStart(), @"^[A-Za-z]"))
            return false;
        return Regex.IsMatch(value, @"[A-Za-z]{3,}")
               && (value.Contains('+')
                   || Regex.IsMatch(value, @"\d")
                   || Regex.IsMatch(value, @"\bmg\b", RegexOptions.IgnoreCase));
    }

    /// <summary>Higher = better generic (prefer strengths).</summary>
    private static int SaltQuality(string? salt)
    {
        if (string.IsNullOrWhiteSpace(salt)) return -1;
        var score = 0;
        if (Regex.IsMatch(salt, @"\d")) score += 40;
        if (Regex.IsMatch(salt, @"\bmg\b", RegexOptions.IgnoreCase) || salt.Contains("MG", StringComparison.Ordinal))
            score += 30;
        if (salt.Contains('+')) score += 10;
        var ingredients = salt.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        if (ingredients is >= 2 and <= 3) score += 20;
        if (ingredients > 4) score -= 50; // multivitamin / substitute cards on TrueMeds etc.
        // Prefer strength-bearing salts; avoid long substitute names winning on length alone.
        if (Regex.IsMatch(salt, @"\d"))
            score += 5;
        else
            score += Math.Min(salt.Length, 40);
        return score;
    }

    private static string? NormalizeSalt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = System.Net.WebUtility.HtmlDecode(raw).Trim();
        s = Regex.Replace(s, @"<[^>]+>", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = s.Trim('"', '\'', '[', ']', '|', ':', ' ');
        s = Regex.Replace(s, @"^(?:Contains|Composition|Salt(?:\s*Composition|\s*Content)?)\s*[:：]?\s*", "",
            RegexOptions.IgnoreCase);
        s = s.Trim();

        // SPIRONOLACTONE-50MG+TORSEMIDE-10MG
        if (Regex.IsMatch(s, @"^[A-Z0-9+\-/%.\s]+$") && s.Contains("MG", StringComparison.Ordinal))
        {
            var parts = s.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var formatted = parts.Select(FormatApolloSaltPart);
            s = string.Join(" + ", formatted);
        }
        else
        {
            // Spironolactone(50.0 Mg) / Spironolactone (50mg) → Spironolactone 50mg
            s = Regex.Replace(s, @"\((\s*\d+(?:\.\d+)?)\s*(M[cg])\s*\)", " $1$2", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"([A-Za-z\)])(\d+(?:\.\d+)?(?:mg|mcg)\b)", "$1 $2", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"(\d+)\.0+(?=mg|mcg\b)", "$1", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s+", " ").Trim();
            s = Regex.Replace(s, @"(?<=\d)\s*(M[cg])\b", m => m.Groups[1].Value.ToLowerInvariant(), RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s*\+\s*", " + ");
        }

        // Title-case all-caps words (keep mg/mcg lower)
        s = Regex.Replace(s, @"\b([A-Z]{2,}(?:\s*/\s*[A-Z]{2,})?)\b", m =>
        {
            var token = m.Groups[1].Value;
            if (token is "MG" or "MCG") return token.ToLowerInvariant();
            return string.Join('/', token.Split('/').Select(TitleCaseWord));
        });

        // Final canonical spacing: "Salt1 50mg + Salt2 10mg"
        s = Regex.Replace(s, @"\s*\+\s*", " + ");
        s = Regex.Replace(s, @"\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static string FormatApolloSaltPart(string part)
    {
        // TORSEMIDE-10MG or SPIRONOLACTONE
        var m = Regex.Match(part, @"^([A-Z0-9]+)(?:-(\d+(?:\.\d+)?)(MG|MCG))?$", RegexOptions.IgnoreCase);
        if (!m.Success) return TitleCaseWord(part);
        var name = TitleCaseWord(m.Groups[1].Value);
        if (!m.Groups[2].Success) return name;
        return $"{name} {m.Groups[2].Value}{m.Groups[3].Value.ToLowerInvariant()}";
    }

    private static string TitleCaseWord(string word)
    {
        word = word.Trim();
        if (word.Length == 0) return word;
        if (word.Equals("MG", StringComparison.OrdinalIgnoreCase)
            || word.Equals("MCG", StringComparison.OrdinalIgnoreCase))
            return word.ToLowerInvariant();
        return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
    }

    private static string CleanHtmlSnippet(string value)
    {
        value = Regex.Replace(value, @"<[^>]+>", " ");
        value = System.Net.WebUtility.HtmlDecode(value);
        value = Regex.Replace(value, @"\s+", " ").Trim();
        // Stop at common trailing labels
        value = Regex.Split(value, @"\s{2,}|(?:Uses|Side Effects|Substitutes|Introduction)\b",
            RegexOptions.IgnoreCase)[0].Trim();
        return value;
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
            Use the retail pack name as name.
            genericName MUST be salt/composition as "Salt1 strength1 + Salt2 strength2"
            (example: "Spironolactone 50mg + Torasemide 10mg"). Prefer Composition / Salt Content.
            brand and manufacturerName MUST be the pharmaceutical company / marketer
            (example: "Cipla Ltd"), NOT the product brand (Dytor) and NOT the website name (1mg, Apollo, PharmEasy).
            mrp is the printed MRP / list price when shown.
            If unknown, use null.
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
        // Keep __NEXT_DATA__ / JSON-LD text cues by stripping tags only after removing huge script noise carefully.
        html = Regex.Replace(html, @"<script(?![^>]*id\s*=\s*[""']__NEXT_DATA__[""'])[\s\S]*?</script>", " ",
            RegexOptions.IgnoreCase);
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
        title = Regex.Replace(title, @"^\s*Buy\s+", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s+online\s*$", "", RegexOptions.IgnoreCase);
        title = Regex.Replace(title, @"\s+", " ").Trim();
        return title.Length == 0 ? null : title;
    }
}
