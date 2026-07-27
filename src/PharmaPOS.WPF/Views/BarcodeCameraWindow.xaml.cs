using System.Windows;
using System.Windows.Threading;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using OpenCvSharp.WpfExtensions;
using PharmaPOS.WPF.Services;
using Window = System.Windows.Window;

namespace PharmaPOS.WPF.Views;

public partial class BarcodeCameraWindow : Window
{
    private readonly IBarcodeCodec _codec;
    private readonly DispatcherTimer _timer;
    private VideoCapture? _capture;
    private int _frameSkip;
    private bool _closing;

    public string? DecodedValue { get; private set; }

    public BarcodeCameraWindow(IBarcodeCodec codec, string title)
    {
        InitializeComponent();
        _codec = codec;
        TitleText.Text = title;
        Title = title;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => StartCamera();
    }

    private void StartCamera()
    {
        try
        {
            _capture = new VideoCapture(0);
            if (!_capture.IsOpened())
            {
                StatusText.Text = "No camera found — use Browse image...";
                return;
            }

            _capture.FrameWidth = 1280;
            _capture.FrameHeight = 720;
            StatusText.Text = "Point camera at barcode...";
            _timer.Start();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Camera unavailable: {ex.Message}";
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_closing || _capture is null || !_capture.IsOpened()) return;

        using var frame = new Mat();
        if (!_capture.Read(frame) || frame.Empty()) return;

        PreviewImage.Source = frame.ToBitmapSource();

        // Decode every few frames for CPU relief.
        if (++_frameSkip % 3 != 0) return;

        try
        {
            using var bmp = BitmapConverter.ToBitmap(frame);
            var value = _codec.Decode(bmp);
            if (string.IsNullOrWhiteSpace(value)) return;

            DecodedValue = value.Trim();
            DialogResult = true;
            Close();
        }
        catch
        {
            // Ignore transient decode failures.
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open barcode image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var value = _codec.DecodeFromFile(dlg.FileName);
            if (string.IsNullOrWhiteSpace(value))
            {
                StatusText.Text = "No barcode found in image.";
                return;
            }

            DecodedValue = value.Trim();
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _closing = true;
        _timer.Stop();
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
    }
}
