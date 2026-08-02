namespace PharmaPOS.WPF.Services;

/// <summary>Persisted UI widths for Sales/Purchase sections and grids.</summary>
public sealed class UiLayoutSettings
{
    public double SalesSidePanelWidth { get; set; } = 250;
    public double PurchaseSidePanelWidth { get; set; } = 240;

    /// <summary>Column display widths keyed by header text (or "Col{n}" when header is empty).</summary>
    public Dictionary<string, double> SalesGridColumns { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, double> PurchaseGridColumns { get; set; } = new(StringComparer.Ordinal);
}
