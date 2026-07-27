using System.Text;
using System.Windows.Input;

namespace PharmaPOS.WPF.Services;

/// <summary>
/// Detects USB barcode scanners that type characters very quickly and end with Enter.
/// </summary>
public sealed class UsbBarcodeWedge
{
    private readonly StringBuilder _buffer = new();
    private DateTime _lastKeyUtc = DateTime.MinValue;
    private readonly TimeSpan _maxGap;
    private readonly int _minLength;

    public UsbBarcodeWedge(int minLength = 4, int maxGapMs = 60)
    {
        _minLength = minLength;
        _maxGap = TimeSpan.FromMilliseconds(maxGapMs);
    }

    public event Action<string>? BarcodeScanned;

    /// <summary>
    /// Returns true when the key was consumed as part of a completed barcode scan
    /// (Enter terminator), so the view should mark the event handled.
    /// </summary>
    public bool ProcessKeyDown(Key key, ModifierKeys modifiers, Func<string?>? getTextFromKey = null)
    {
        if (modifiers is ModifierKeys.Control or ModifierKeys.Alt or ModifierKeys.Windows)
        {
            Reset();
            return false;
        }

        var now = DateTime.UtcNow;
        if (_buffer.Length > 0 && now - _lastKeyUtc > _maxGap)
            Reset();

        if (key is Key.Enter or Key.Return)
        {
            var code = _buffer.ToString().Trim();
            Reset();
            if (code.Length >= _minLength)
            {
                BarcodeScanned?.Invoke(code);
                return true;
            }
            return false;
        }

        if (key is Key.Escape or Key.Tab or Key.Back or Key.Delete)
        {
            Reset();
            return false;
        }

        var ch = getTextFromKey?.Invoke() ?? KeyToChar(key, modifiers);
        if (ch is null)
        {
            // Non-character navigation keys while buffering — ignore timing gaps lightly
            if (_buffer.Length == 0) return false;
            _lastKeyUtc = now;
            return false;
        }

        _buffer.Append(ch);
        _lastKeyUtc = now;
        return false;
    }

    public void Reset() => _buffer.Clear();

    private static string? KeyToChar(Key key, ModifierKeys modifiers)
    {
        var shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (key is >= Key.D0 and <= Key.D9)
        {
            var digit = (char)('0' + (key - Key.D0));
            if (!shift) return digit.ToString();
            return key switch
            {
                Key.D1 => "!",
                Key.D2 => "@",
                Key.D3 => "#",
                Key.D4 => "$",
                Key.D5 => "%",
                Key.D6 => "^",
                Key.D7 => "&",
                Key.D8 => "*",
                Key.D9 => "(",
                Key.D0 => ")",
                _ => digit.ToString()
            };
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return ((char)('0' + (key - Key.NumPad0))).ToString();

        if (key is >= Key.A and <= Key.Z)
        {
            var c = (char)('A' + (key - Key.A));
            return shift ? c.ToString() : char.ToLowerInvariant(c).ToString();
        }

        return key switch
        {
            Key.OemMinus => shift ? "_" : "-",
            Key.OemPlus => shift ? "+" : "=",
            Key.OemPeriod => shift ? ">" : ".",
            Key.OemComma => shift ? "<" : ",",
            Key.OemQuestion => shift ? "?" : "/",
            Key.Space => " ",
            _ => null
        };
    }
}
