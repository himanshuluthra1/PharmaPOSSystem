using System.Windows;
using System.Windows.Input;

namespace PharmaPOS.WPF.Views;

public partial class MasterEditorWindow : Window
{
    public MasterEditorWindow()
    {
        InitializeComponent();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
    }
}
