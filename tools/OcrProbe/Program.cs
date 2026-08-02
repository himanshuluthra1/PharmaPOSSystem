using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using PharmaPOS.Application.Features.Purchases;

var path = args.Length > 0 ? args[0] : @"d:\Code\PharmaPOSSystem\tools\sample-bill.png";
var engine = OcrEngine.TryCreateFromUserProfileLanguages()
    ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
    ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));
if (engine is null) { Console.WriteLine("NO OCR ENGINE"); return 1; }

var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
using var stream = await file.OpenReadAsync();
var decoder = await BitmapDecoder.CreateAsync(stream);
using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
var result = await engine.RecognizeAsync(bitmap);

var words = new List<OcrWordDto>();
foreach (var line in result.Lines)
{
    foreach (var word in line.Words)
    {
        var r = word.BoundingRect;
        words.Add(new OcrWordDto
        {
            Text = word.Text ?? string.Empty,
            X = r.X,
            Y = r.Y,
            Width = r.Width,
            Height = r.Height
        });
    }
}

var page = new OcrPageDto { FullText = result.Text ?? string.Empty, Words = words };
var draft = PurchaseBillTextParser.Parse(page);

Console.WriteLine($"Supplier: {draft.SupplierName}");
Console.WriteLine($"Invoice: {draft.SupplierInvoiceNumber}");
Console.WriteLine($"Date: {draft.InvoiceDate:yyyy-MM-dd}");
Console.WriteLine($"Total: {draft.GrandTotalHint}");
Console.WriteLine($"Lines: {draft.Lines.Count}");
foreach (var l in draft.Lines)
{
    Console.WriteLine(
        $"  {l.OcrItemName} | batch={l.BatchNumber} exp={l.ExpiryDate:MM-yyyy} qty={l.Quantity} free={l.FreeQuantity} mrp={l.Mrp} rate={l.PurchasePrice} disc={l.DiscountPercent} gst={l.GstPercent} amt={l.LineAmountHint}");
}
foreach (var w in draft.Warnings) Console.WriteLine($"  ! {w}");
return 0;
