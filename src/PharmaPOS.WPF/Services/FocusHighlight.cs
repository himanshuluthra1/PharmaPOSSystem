using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PharmaPOS.WPF.Services;

/// <summary>
/// App-wide keyboard-focus cue: only the active control's fill color changes
/// (no orange border ring). Previous cues are fully cleared when focus moves.
/// </summary>
public static class FocusHighlight
{
    private static readonly Brush FocusFill;
    private static readonly Brush StrongFocusFill;

    private static readonly ConditionalWeakTable<DependencyObject, Snapshot> Originals = new();
    private static FrameworkElement? _active;

    private sealed class Snapshot
    {
        public bool HadBackground;
        public bool HadBorderBrush;
        public bool HadBorderThickness;
        public Brush? Background;
        public Brush? BorderBrush;
        public Thickness BorderThickness;
        public List<(DependencyObject Target, DependencyProperty Property, bool HadLocal, object? Value)> Children { get; } = new();
    }

    static FocusHighlight()
    {
        // Light green fill — matches App.xaml TextBoxFocusBackgroundBrush / FocusGridCellBrush.
        FocusFill = CreateBrush(0xC8, 0xE6, 0xC9);
        StrongFocusFill = CreateBrush(0xC8, 0xE6, 0xC9);
    }

    private static SolidColorBrush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static void Register()
    {
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnGotFocus),
            handledEventsToo: true);

        EventManager.RegisterClassHandler(
            typeof(UIElement),
            Keyboard.LostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnLostFocus),
            handledEventsToo: true);
    }

    private static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (FindHighlightTarget(e.NewFocus as DependencyObject) is not FrameworkElement target)
            return;

        if (ReferenceEquals(_active, target))
        {
            // Still the same control (e.g. focus moved to an inner part) — keep highlight.
            return;
        }

        Apply(target);
    }

    private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_active is null) return;

        // Focus moved inside the active control — keep highlighting it.
        if (e.NewFocus is DependencyObject neu && IsDescendant(_active, neu))
            return;

        // Focus moved to a different highlight target — GotFocus will swap; avoid flicker.
        if (FindHighlightTarget(e.NewFocus as DependencyObject) is FrameworkElement next
            && !ReferenceEquals(next, _active))
            return;

        // Focus left interactive controls entirely.
        ClearActive();
    }

    private static FrameworkElement? FindHighlightTarget(DependencyObject? start)
    {
        DataGridCell? cell = null;
        FrameworkElement? other = null;

        for (var d = start; d is not null; d = GetParent(d))
        {
            switch (d)
            {
                case DataGridCell c:
                    cell = c;
                    break;
                case DatePickerTextBox:
                    // Prefer the outer DatePicker; focus fill on the text box blanks the date.
                    break;
                case TextBox or PasswordBox or ComboBox or DatePicker or ButtonBase:
                    other ??= (FrameworkElement)d;
                    break;
                case FrameworkElement { TemplatedParent: FrameworkElement parent }
                    when parent is ComboBox or DatePicker or ButtonBase:
                    other ??= parent;
                    break;
            }
        }

        // Prefer the grid cell so tabbing highlights one cell, not the editor inside it.
        return cell ?? other;
    }

    private static void Apply(FrameworkElement target)
    {
        // DatePicker / its text box: never wash with green fill — MD paints date text
        // on the same layer and the fill makes the value look blank.
        if (target is DatePicker or DatePickerTextBox)
            return;

        ClearActive();

        var fill = target is DataGridCell or ButtonBase ? StrongFocusFill : FocusFill;
        var snap = new Snapshot();

        if (target is Control control)
        {
            snap.HadBackground = control.ReadLocalValue(Control.BackgroundProperty) != DependencyProperty.UnsetValue;
            snap.HadBorderBrush = control.ReadLocalValue(Control.BorderBrushProperty) != DependencyProperty.UnsetValue;
            snap.HadBorderThickness = control.ReadLocalValue(Control.BorderThicknessProperty) != DependencyProperty.UnsetValue;
            snap.Background = control.Background;
            snap.BorderBrush = control.BorderBrush;
            snap.BorderThickness = control.BorderThickness;

            // Fill only — do not thicken or recolor the border (no orange ring).
            control.SetCurrentValue(Control.BackgroundProperty, fill);
        }

        Originals.Add(target, snap);
        _active = target;
        PaintChrome(target, snap, fill);

        target.Dispatcher.BeginInvoke(() =>
        {
            if (!ReferenceEquals(_active, target) || !target.IsKeyboardFocusWithin) return;
            if (!Originals.TryGetValue(target, out var existing)) return;
            PaintChrome(target, existing, fill);
        }, DispatcherPriority.Loaded);
    }

    private static void ClearActive()
    {
        if (_active is null) return;
        var previous = _active;
        _active = null;
        Restore(previous);
    }

    private static void PaintChrome(FrameworkElement root, Snapshot snap, Brush fill)
    {
        // For DataGrid cells, painting only the cell background is enough and avoids
        // leaving chrome dirty on recycled/virtualized cells.
        if (root is DataGridCell)
            return;

        // DatePicker: outer Background style trigger is enough. Painting nested chrome
        // blanks the date text in MaterialDesign's outlined template.
        if (root is DatePicker)
            return;

        foreach (var child in WalkVisual(root))
        {
            if (ReferenceEquals(child, root)) continue;
            if (child is ButtonBase && root is not ButtonBase)
                continue;
            if (IsCalendarChrome(child))
                continue;

            if (child is Border border)
            {
                Capture(snap, border, Border.BackgroundProperty);
                border.SetCurrentValue(Border.BackgroundProperty, fill);
            }
            else if (child is Panel panel && IsWashoutBrush(panel.Background))
            {
                Capture(snap, panel, Panel.BackgroundProperty);
                panel.SetCurrentValue(Panel.BackgroundProperty, fill);
            }
            else if (child is Control nestedControl
                     and not DatePickerTextBox
                     && IsWashoutBrush(nestedControl.Background))
            {
                Capture(snap, nestedControl, Control.BackgroundProperty);
                nestedControl.SetCurrentValue(Control.BackgroundProperty, fill);
            }
            // Do not paint DatePickerTextBox / TextBoxView — that can blank the date text.
            else if (TryGetBackgroundProperty(child, out var bgDp)
                     && child is not DatePickerTextBox)
            {
                var current = child.GetValue(bgDp) as Brush;
                if (current is null || IsWashoutBrush(current))
                {
                    Capture(snap, child, bgDp);
                    child.SetCurrentValue(bgDp, fill);
                }
            }
        }
    }

    private static bool TryGetBackgroundProperty(DependencyObject child, out DependencyProperty dp)
    {
        dp = null!;
        var name = child.GetType().Name;
        if (name is not ("TextBoxView" or "DatePickerTextBox"))
            return false;

        var field = child.GetType().GetField(
            "BackgroundProperty",
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.FlattenHierarchy);
        if (field?.GetValue(null) is not DependencyProperty found)
            return false;

        dp = found;
        return true;
    }

    private static void Capture(Snapshot snap, DependencyObject target, DependencyProperty property)
    {
        if (snap.Children.Any(c => ReferenceEquals(c.Target, target) && c.Property == property))
            return;

        var hadLocal = target.ReadLocalValue(property) != DependencyProperty.UnsetValue;
        snap.Children.Add((target, property, hadLocal, target.GetValue(property)));
    }

    private static void Restore(FrameworkElement target)
    {
        if (!Originals.TryGetValue(target, out var snap))
        {
            // Still clear any leftover local focus paints.
            if (target is Control c)
            {
                c.ClearValue(Control.BackgroundProperty);
                c.ClearValue(Control.BorderBrushProperty);
                c.ClearValue(Control.BorderThicknessProperty);
            }
            return;
        }

        if (target is Control control)
        {
            RestoreProperty(control, Control.BackgroundProperty, snap.HadBackground, snap.Background);
            RestoreProperty(control, Control.BorderBrushProperty, snap.HadBorderBrush, snap.BorderBrush);
            if (snap.HadBorderThickness)
                control.SetCurrentValue(Control.BorderThicknessProperty, snap.BorderThickness);
            else
                control.ClearValue(Control.BorderThicknessProperty);
        }

        foreach (var (child, property, hadLocal, value) in snap.Children)
        {
            try { RestoreProperty(child, property, hadLocal, value); }
            catch { /* visual disposed / recycled */ }
        }

        Originals.Remove(target);
    }

    private static void RestoreProperty(DependencyObject target, DependencyProperty property, bool hadLocal, object? value)
    {
        if (hadLocal)
            target.SetCurrentValue(property, value);
        else
            target.ClearValue(property);
    }

    private static bool IsWashoutBrush(Brush? brush)
    {
        if (brush is null) return true;
        if (brush == Brushes.Transparent) return false;
        if (brush is SolidColorBrush solid)
        {
            var c = solid.Color;
            return c.A > 200 && c.R > 240 && c.G > 240 && c.B > 240;
        }
        return false;
    }

    private static bool IsCalendarChrome(DependencyObject d) =>
        d is Calendar or CalendarItem or CalendarButton or CalendarDayButton;

    private static bool IsDescendant(DependencyObject ancestor, DependencyObject? node)
    {
        for (var d = node; d is not null; d = GetParent(d))
            if (ReferenceEquals(d, ancestor)) return true;
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject d)
        => d is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(d) ?? (d as FrameworkElement)?.Parent
            : (d as FrameworkElement)?.Parent;

    private static IEnumerable<DependencyObject> WalkVisual(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        var count = 0;
        while (queue.Count > 0 && count < 100)
        {
            var current = queue.Dequeue();
            yield return current;
            count++;
            if (IsCalendarChrome(current) || current is Popup)
                continue;
            var n = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < n; i++)
                queue.Enqueue(VisualTreeHelper.GetChild(current, i));
        }
    }
}
