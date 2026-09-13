using System.Windows.Media;

namespace PharmaPOS.WPF.ViewModels;

/// <summary>A keyboard shortcut shown in the bottom shortcut strip.</summary>
public sealed class ShortcutHint
{
    public ShortcutHint(string key, string description, string backgroundHex, string borderHex)
    {
        Key = key;
        Description = description;
        Background = BrushFromHex(backgroundHex);
        Border = BrushFromHex(borderHex);
    }

    public string Key { get; }
    public string Description { get; }
    public Brush Background { get; }
    public Brush Border { get; }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
