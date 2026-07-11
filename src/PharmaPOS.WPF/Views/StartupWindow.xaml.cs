using System.Windows;

namespace PharmaPOS.WPF.Views;

/// <summary>Shown immediately on launch while services and the database initialize.</summary>
public partial class StartupWindow : Window
{
    public StartupWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string message) => StatusText.Text = message;
}
