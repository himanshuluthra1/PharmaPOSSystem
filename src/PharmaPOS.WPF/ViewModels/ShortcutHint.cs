namespace PharmaPOS.WPF.ViewModels;

/// <summary>A keyboard shortcut shown in the shell green app bar.</summary>
public sealed class ShortcutHint
{
    public ShortcutHint(string key, string description)
    {
        Key = key;
        Description = description;
    }

    public string Key { get; }
    public string Description { get; }
}
