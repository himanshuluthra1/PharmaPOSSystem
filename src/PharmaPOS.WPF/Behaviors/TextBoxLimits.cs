using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PharmaPOS.WPF.Behaviors;

/// <summary>Attached helpers for logical TextBox input limits (digits-only, etc.).</summary>
public static class TextBoxLimits
{
    public static readonly DependencyProperty DigitsOnlyProperty =
        DependencyProperty.RegisterAttached(
            "DigitsOnly",
            typeof(bool),
            typeof(TextBoxLimits),
            new PropertyMetadata(false, OnDigitsOnlyChanged));

    public static bool GetDigitsOnly(DependencyObject obj) => (bool)obj.GetValue(DigitsOnlyProperty);

    public static void SetDigitsOnly(DependencyObject obj, bool value) => obj.SetValue(DigitsOnlyProperty, value);

    private static void OnDigitsOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox box) return;

        box.PreviewTextInput -= OnPreviewTextInput;
        DataObject.RemovePastingHandler(box, OnPaste);
        box.PreviewKeyDown -= OnPreviewKeyDown;

        if (e.NewValue is true)
        {
            box.PreviewTextInput += OnPreviewTextInput;
            DataObject.AddPastingHandler(box, OnPaste);
            box.PreviewKeyDown += OnPreviewKeyDown;
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Block space (not raised as PreviewTextInput on all keyboards).
        if (e.Key == Key.Space)
            e.Handled = true;
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsDigits(e.Text);
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(DataFormats.Text))
        {
            var text = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            if (!IsDigits(text))
                e.CancelCommand();
        }
        else
        {
            e.CancelCommand();
        }
    }

    private static bool IsDigits(string text) => DigitsRegex.IsMatch(text);

    private static readonly Regex DigitsRegex = new(@"^\d+$", RegexOptions.Compiled);
}
