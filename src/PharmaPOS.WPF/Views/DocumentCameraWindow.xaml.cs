using System.IO;
using System.Windows;
using System.Windows.Threading;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using Window = System.Windows.Window;

namespace PharmaPOS.WPF.Views;

public partial class DocumentCameraWindow : Window
{
    private readonly DispatcherTimer _timer;
    private VideoCapture? _capture;
    private Mat? _lastFrame;
    private bool _closing;

    public string? CapturedFilePath { get; private set; }

    public DocumentCameraWindow(string title)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => StartCamera();
        Closed += (_, _) => Cleanup();
    }

    private void StartCamera()
    {
        try
        {
            _capture = new VideoCapture(0);
            if (!_capture.IsOpened())
            {
                StatusText.Text = "No camera found.";
                CaptureButton.IsEnabled = false;
                return;
            }

            _capture.FrameWidth = 1920;
            _capture.FrameHeight = 1080;
            StatusText.Text = "Align the purchase bill in frame, then Capture.";
            _timer.Start();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Camera unavailable: {ex.Message}";
            CaptureButton.IsEnabled = false;
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_closing || _capture is null || !_capture.IsOpened()) return;
        var frame = new Mat();
        if (!_capture.Read(frame) || frame.Empty())
        {
            frame.Dispose();
            return;
        }

        _lastFrame?.Dispose();
        _lastFrame = frame;
        PreviewImage.Source = frame.ToBitmapSource();
    }

    private void CaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastFrame is null || _lastFrame.Empty())
        {
            StatusText.Text = "No frame to capture.";
            return;
        }

        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"pharmapos-bill-{Guid.NewGuid():N}.jpg");
            Cv2.ImWrite(path, _lastFrame);
            CapturedFilePath = path;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Capture failed: {ex.Message}";
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Cleanup()
    {
        _closing = true;
        _timer.Stop();
        _lastFrame?.Dispose();
        _lastFrame = null;
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
    }
}
