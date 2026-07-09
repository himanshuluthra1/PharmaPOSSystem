using MaterialDesignThemes.Wpf;

namespace PharmaPOS.WPF.Services;

public interface IThemeService
{
    bool IsDarkMode { get; }
    void SetDarkMode(bool isDark);
    void Toggle();
}

/// <summary>Switches the Material Design base theme between light and dark.</summary>
public class ThemeService : IThemeService
{
    private readonly PaletteHelper _paletteHelper = new();

    public bool IsDarkMode { get; private set; }

    public void SetDarkMode(bool isDark)
    {
        var theme = _paletteHelper.GetTheme();
        theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
        _paletteHelper.SetTheme(theme);
        IsDarkMode = isDark;
    }

    public void Toggle() => SetDarkMode(!IsDarkMode);
}
