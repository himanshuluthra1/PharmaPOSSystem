using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public interface IBarcodeCameraService
{
    /// <summary>Opens webcam scanner; returns decoded text or null if cancelled.</summary>
    string? ScanWithCamera(string? title = null);

    /// <summary>Decode barcode/QR from an image file.</summary>
    string? ScanFromImageFile();
}

public sealed class BarcodeCameraService : IBarcodeCameraService
{
    private readonly IBarcodeCodec _codec;
    private readonly IDialogService _dialog;

    public BarcodeCameraService(IBarcodeCodec codec, IDialogService dialog)
    {
        _codec = codec;
        _dialog = dialog;
    }

    public string? ScanWithCamera(string? title = null)
    {
        try
        {
            var window = new BarcodeCameraWindow(_codec, title ?? "Scan barcode")
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            return window.ShowDialog() == true ? window.DecodedValue : null;
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Camera scanner failed: {ex.Message}");
            return null;
        }
    }

    public string? ScanFromImageFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open barcode image",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return null;

        try
        {
            var value = _codec.DecodeFromFile(dlg.FileName);
            if (string.IsNullOrWhiteSpace(value))
            {
                _dialog.ShowInfo("No barcode found in the selected image.");
                return null;
            }
            return value;
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
            return null;
        }
    }
}
