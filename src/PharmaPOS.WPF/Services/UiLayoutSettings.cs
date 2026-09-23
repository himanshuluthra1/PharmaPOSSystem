namespace PharmaPOS.WPF.Services;

/// <summary>Persisted UI widths for side panels and DataGrid columns.</summary>
public sealed class UiLayoutSettings
{
    public double SalesSidePanelWidth { get; set; } = 250;
    public double PurchaseSidePanelWidth { get; set; } = 240;

    /// <summary>
    /// Legacy Sales cart column widths (migrated into <see cref="GridColumnsByView"/> on load).
    /// </summary>
    public Dictionary<string, double> SalesGridColumns { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Legacy Purchase grid column widths (migrated into <see cref="GridColumnsByView"/> on load).
    /// </summary>
    public Dictionary<string, double> PurchaseGridColumns { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Column widths by view key → (column header → pixels).
    /// Keys include Sales, Purchase, and auto keys like InventoryView.A1B2C3D4.
    /// </summary>
    public Dictionary<string, Dictionary<string, double>> GridColumnsByView { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
