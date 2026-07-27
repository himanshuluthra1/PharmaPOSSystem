using PharmaPOS.Application.Common;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using ZXing.Windows.Compatibility;

namespace PharmaPOS.WPF.Services;

public interface IBarcodeCodec
{
    string CreateUniqueValue();
    BitmapSource GenerateImage(string value, int width = 360, int height = 120);
    byte[] GeneratePngBytes(string value, int width = 360, int height = 120);
    string? Decode(Bitmap bitmap);
    string? DecodeFromFile(string path);
}

public sealed class BarcodeCodec : IBarcodeCodec
{
    public string CreateUniqueValue() => BarcodeValueGenerator.CreateUnique();

    public BitmapSource GenerateImage(string value, int width = 360, int height = 120)
    {
        using var bmp = RenderBitmap(value, width, height);
        return ToBitmapSource(bmp);
    }

    public byte[] GeneratePngBytes(string value, int width = 360, int height = 120)
    {
        using var bmp = RenderBitmap(value, width, height);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public string? Decode(Bitmap bitmap)
    {
        var reader = new BarcodeReader
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                PossibleFormats =
                [
                    BarcodeFormat.CODE_128,
                    BarcodeFormat.EAN_13,
                    BarcodeFormat.EAN_8,
                    BarcodeFormat.UPC_A,
                    BarcodeFormat.UPC_E,
                    BarcodeFormat.CODE_39,
                    BarcodeFormat.QR_CODE
                ]
            }
        };
        return reader.Decode(bitmap)?.Text;
    }

    public string? DecodeFromFile(string path)
    {
        using var bmp = new Bitmap(path);
        return Decode(bmp);
    }

    private static Bitmap RenderBitmap(string value, int width, int height)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new ArgumentException("Barcode value is required.", nameof(value));

        var writer = new BarcodeWriter
        {
            Format = GuessFormat(value),
            Options = new EncodingOptions
            {
                Width = width,
                Height = height,
                Margin = 2,
                PureBarcode = false
            }
        };
        return writer.Write(value);
    }

    private static BarcodeFormat GuessFormat(string value)
    {
        var digits = value.All(char.IsDigit);
        if (digits && value.Length == 13) return BarcodeFormat.EAN_13;
        if (digits && value.Length == 8) return BarcodeFormat.EAN_8;
        if (digits && value.Length == 12) return BarcodeFormat.UPC_A;
        return BarcodeFormat.CODE_128;
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = ms;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
