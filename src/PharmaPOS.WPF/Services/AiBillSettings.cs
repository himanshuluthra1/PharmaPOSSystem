namespace PharmaPOS.WPF.Services;

/// <summary>Machine-local Gemini bill-scan settings (%LocalAppData%\PharmaPOS\ai-settings.json).</summary>
public sealed class AiBillSettings
{
    /// <summary>When true and an API key is set, purchase bill scan uses Gemini first.</summary>
    public bool UseGemini { get; set; }

    /// <summary>Google AI Studio / Gemini API key.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Model id, e.g. gemini-flash-lite-latest or gemini-flash-latest.</summary>
    public string Model { get; set; } = "gemini-flash-lite-latest";
}
