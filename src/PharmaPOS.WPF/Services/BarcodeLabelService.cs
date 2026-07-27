using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public interface IBarcodeLabelService
{
    void ShowLabel(string medicineName, string barcode);
}

public sealed class BarcodeLabelService : IBarcodeLabelService
{
    private readonly IBarcodeCodec _codec;
    private readonly IDialogService _dialog;

    public BarcodeLabelService(IBarcodeCodec codec, IDialogService dialog)
    {
        _codec = codec;
        _dialog = dialog;
    }

    public void ShowLabel(string medicineName, string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            _dialog.ShowInfo("Enter or generate a barcode first.");
            return;
        }

        try
        {
            var image = _codec.GenerateImage(barcode.Trim());
            var window = new BarcodeLabelWindow(
                string.IsNullOrWhiteSpace(medicineName) ? "Medicine" : medicineName.Trim(),
                barcode.Trim(),
                image)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            window.ShowDialog();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(ex.Message);
        }
    }
}
