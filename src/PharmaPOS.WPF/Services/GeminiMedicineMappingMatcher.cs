using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using PharmaPOS.Application.Common;
using PharmaPOS.Application.Features.Masters;

namespace PharmaPOS.WPF.Services;

public sealed record GeminiMedicineMatchSuggestion(
    int? OneMgMedicineId,
    double Confidence,
    string? Reason);

public interface IGeminiMedicineMappingMatcher
{
    /// <summary>
    /// Picks the best OneMG candidate for a MedWin medicine using Gemini.
    /// Returns null when Gemini is not ready or the call fails.
    /// </summary>
    Task<GeminiMedicineMatchSuggestion?> SuggestAsync(
        MedicineMappingListItemDto medWin,
        IReadOnlyList<MedicineMappingListItemDto> candidates,
        CancellationToken ct = default);
}

/// <summary>
/// Uses Gemini to rank OneMG catalogue candidates against a MedWin medicine
/// (name, salt/formula, strength, packing).
/// </summary>
public sealed class GeminiMedicineMappingMatcher : IGeminiMedicineMappingMatcher
{
    private const int MaxCandidates = 40;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly IAiBillSettingsService _settings;
    private readonly HttpClient _http;

    public GeminiMedicineMappingMatcher(IAiBillSettingsService settings, HttpClient http)
    {
        _settings = settings;
        _http = http;
    }

    public async Task<GeminiMedicineMatchSuggestion?> SuggestAsync(
        MedicineMappingListItemDto medWin,
        IReadOnlyList<MedicineMappingListItemDto> candidates,
        CancellationToken ct = default)
    {
        _settings.Load();
        if (!_settings.IsGeminiReady)
            return null;

        if (candidates.Count == 0)
            return new GeminiMedicineMatchSuggestion(null, 0, "No OneMG candidates.");

        if (candidates.Count == 1)
        {
            return new GeminiMedicineMatchSuggestion(
                candidates[0].Id, 1.0, "Only one OneMG candidate for this brand prefix.");
        }

        var cfg = _settings.Current;
        var model = string.IsNullOrWhiteSpace(cfg.Model) ? "gemini-flash-lite-latest" : cfg.Model.Trim();
        model = model switch
        {
            "gemini-2.5-flash-lite" => "gemini-flash-lite-latest",
            "gemini-2.5-flash" => "gemini-flash-latest",
            _ => model
        };

        var limited = candidates.Take(MaxCandidates).ToList();
        var prompt = BuildPrompt(medWin, limited);
        var body = BuildRequestBody(prompt);

        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("x-goog-api-key", cfg.ApiKey.Trim());
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var payload = ExtractCandidateText(responseText);
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        var parsed = JsonSerializer.Deserialize<GeminiMatchResponse>(payload, JsonOptions);
        if (parsed is null)
            return null;

        int? chosenId = parsed.OneMgMedicineId;
        if (chosenId is int id && limited.All(c => c.Id != id))
            chosenId = null;

        var confidence = parsed.Confidence is >= 0 and <= 1
            ? parsed.Confidence.Value
            : chosenId is null ? 0 : 0.5;

        return new GeminiMedicineMatchSuggestion(chosenId, confidence, parsed.Reason);
    }

    private static string BuildPrompt(
        MedicineMappingListItemDto medWin,
        IReadOnlyList<MedicineMappingListItemDto> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "You match Indian pharmacy MedWin medicines to OneMG catalogue rows.");
        sb.AppendLine(
            "Pick the single best OneMG candidate id, or null if none is a good match.");
        sb.AppendLine(
            "Compare carefully: brand/name, salt/formula (generic), strength/dose, packing, pack size, and MRP when helpful.");
        sb.AppendLine(
            "Prefer same strength and compatible pack (e.g. strip of 10 vs 10 tablets).");
        sb.AppendLine(
            "Do not invent ids. Return ONLY JSON with: oneMgMedicineId (number|null), confidence (0-1), reason (short).");
        sb.AppendLine();
        sb.AppendLine("MedWin medicine:");
        sb.AppendLine($"- name: {medWin.Name}");
        sb.AppendLine($"- formula/salt: {medWin.GenericName ?? "(none)"}");
        sb.AppendLine($"- strength: {FormatStrength(medWin)}");
        sb.AppendLine($"- packing: {medWin.PackInfo ?? "(none)"}");
        sb.AppendLine($"- pack size (units): {medWin.PackSize?.ToString() ?? "(none)"}");
        sb.AppendLine($"- mrp: {medWin.Mrp?.ToString("0.##") ?? "(none)"}");
        sb.AppendLine($"- medWinId: {medWin.ExternalId ?? "(none)"}");
        sb.AppendLine();
        sb.AppendLine("OneMG candidates:");
        foreach (var c in candidates)
        {
            sb.AppendLine(
                $"- id={c.Id}; name={c.Name}; formula/salt={c.GenericName ?? "(none)"}; " +
                $"strength={FormatStrength(c)}; packing={c.PackInfo ?? "(none)"}; " +
                $"pack size={c.PackSize?.ToString() ?? "(none)"}; mrp={c.Mrp?.ToString("0.##") ?? "(none)"}; " +
                $"oneMgId={c.ExternalId ?? "(none)"}");
        }

        return sb.ToString();
    }

    private static string FormatStrength(MedicineMappingListItemDto item)
    {
        if (!string.IsNullOrWhiteSpace(item.Strength))
            return item.Strength.Trim();
        return MedicineMappingHelper.ExtractStrengthKey(item.Name) ?? "(unknown)";
    }

    private static JsonObject BuildRequestBody(string prompt) => new()
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
            ["responseMimeType"] = "application/json",
            ["responseSchema"] = new JsonObject
            {
                ["type"] = "OBJECT",
                ["properties"] = new JsonObject
                {
                    ["oneMgMedicineId"] = new JsonObject
                    {
                        ["type"] = "INTEGER",
                        ["nullable"] = true
                    },
                    ["confidence"] = new JsonObject { ["type"] = "NUMBER" },
                    ["reason"] = new JsonObject { ["type"] = "STRING" }
                },
                ["required"] = new JsonArray("oneMgMedicineId", "confidence", "reason")
            }
        }
    };

    private static string? ExtractCandidateText(string responseText)
    {
        using var doc = JsonDocument.Parse(responseText);
        if (!doc.RootElement.TryGetProperty("candidates", out var candidates)
            || candidates.GetArrayLength() == 0)
            return null;

        var text = candidates[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(text))
            return null;

        text = text.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        var nl = text.IndexOf('\n');
        if (nl > 0) text = text[(nl + 1)..];
        if (text.EndsWith("```", StringComparison.Ordinal)) text = text[..^3];
        return text.Trim();
    }

    private sealed class GeminiMatchResponse
    {
        public int? OneMgMedicineId { get; set; }
        public double? Confidence { get; set; }
        public string? Reason { get; set; }
    }
}
