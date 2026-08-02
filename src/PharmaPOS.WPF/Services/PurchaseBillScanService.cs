using System.IO;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using PharmaPOS.Application.Features.Purchases;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

public interface IPurchaseBillScanService
{
    /// <summary>
    /// Lets the user pick/capture a supplier bill image, runs Gemini (if configured) or OCR,
    /// matches supplier/medicines, and returns an editable draft after review — or null if cancelled.
    /// </summary>
    Task<ScannedPurchaseDraftDto?> ScanAndReviewAsync(int? branchId, CancellationToken ct = default);
}

public sealed class PurchaseBillScanService : IPurchaseBillScanService
{
    private readonly IPurchaseService _purchases;
    private readonly IDialogService _dialog;
    private readonly IAiBillSettingsService _aiSettings;
    private readonly IGeminiPurchaseBillExtractor _gemini;
    private readonly IMedicinePickerService _medicinePicker;

    public PurchaseBillScanService(
        IPurchaseService purchases,
        IDialogService dialog,
        IAiBillSettingsService aiSettings,
        IGeminiPurchaseBillExtractor gemini,
        IMedicinePickerService medicinePicker)
    {
        _purchases = purchases;
        _dialog = dialog;
        _aiSettings = aiSettings;
        _gemini = gemini;
        _medicinePicker = medicinePicker;
    }

    public async Task<ScannedPurchaseDraftDto?> ScanAndReviewAsync(int? branchId, CancellationToken ct = default)
    {
        var imagePath = PromptForImage();
        if (imagePath is null) return null;

        ScannedPurchaseDraftDto draft;
        try
        {
            draft = await ExtractDraftAsync(imagePath, ct);
            await MatchSupplierAndMedicinesAsync(draft, ct);
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Could not read the bill: {ex.Message}");
            return null;
        }

        var window = new PurchaseBillScanWindow(draft, imagePath, _purchases, _medicinePicker)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        return window.ShowDialog() == true ? window.AcceptedDraft : null;
    }

    private async Task<ScannedPurchaseDraftDto> ExtractDraftAsync(string imagePath, CancellationToken ct)
    {
        // Preferences may have been saved after app start — always re-read from disk.
        _aiSettings.Load();

        if (_aiSettings.IsGeminiReady)
        {
            try
            {
                var draft = await _gemini.ExtractAsync(imagePath, ct);
                if (draft.Warnings.Count == 0 || !draft.Warnings[0].StartsWith("Engine:", StringComparison.Ordinal))
                    draft.Warnings.Insert(0, "Engine: Gemini AI");
                return draft;
            }
            catch (Exception geminiEx)
            {
                // Fall back to local OCR so the user is not blocked if the API is down / key invalid.
                try
                {
                    var page = await RecognizePageAsync(imagePath, ct);
                    var draft = PurchaseBillTextParser.Parse(page);
                    draft.Warnings.Insert(0,
                        $"Engine: Windows OCR (Gemini failed: {geminiEx.Message})");
                    return draft;
                }
                catch
                {
                    throw new InvalidOperationException(
                        $"AI (Gemini) failed: {geminiEx.Message}", geminiEx);
                }
            }
        }

        var ocrPage = await RecognizePageAsync(imagePath, ct);
        var ocrDraft = PurchaseBillTextParser.Parse(ocrPage);
        ocrDraft.Warnings.Insert(0,
            "Engine: Windows OCR (enable “Use Gemini” under Settings → Preferences for AI scan)");
        return ocrDraft;
    }

    private string? PromptForImage()
    {
        var choice = System.Windows.MessageBox.Show(
            "Scan a supplier purchase bill.\n\nYes = Browse image / PDF scan\nNo = Capture with camera\nCancel = Abort",
            "Scan purchase bill",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question);

        if (choice == System.Windows.MessageBoxResult.Cancel) return null;
        if (choice == System.Windows.MessageBoxResult.No)
            return CaptureWithCamera();

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open scanned purchase bill",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp|All files|*.*"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    private string? CaptureWithCamera()
    {
        try
        {
            var window = new DocumentCameraWindow("Capture purchase bill")
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            return window.ShowDialog() == true ? window.CapturedFilePath : null;
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Camera capture failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<OcrPageDto> RecognizePageAsync(string imagePath, CancellationToken ct)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"))
            ?? OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en"));

        if (engine is null)
            throw new InvalidOperationException(
                "Windows OCR is not available. Install an English language pack for OCR in Windows Settings.");

        var fullPath = Path.GetFullPath(imagePath);
        var file = await StorageFile.GetFileFromPathAsync(fullPath);
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        ct.ThrowIfCancellationRequested();
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

        return new OcrPageDto
        {
            FullText = result.Text ?? string.Empty,
            Words = words
        };
    }

    private async Task MatchSupplierAndMedicinesAsync(ScannedPurchaseDraftDto draft, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(draft.SupplierName))
        {
            var suppliers = await _purchases.SearchSuppliersAsync(draft.SupplierName, ct);
            var best = suppliers.FirstOrDefault();
            if (best is not null)
            {
                draft.MatchedSupplierId = best.Id;
                draft.MatchedSupplierPhone = best.Phone;
                if (NamesLikelySame(draft.SupplierName, best.Name))
                    draft.SupplierName = best.Name;
            }
        }

        foreach (var line in draft.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.OcrItemName)) continue;
            var term = line.OcrItemName.Length > 40 ? line.OcrItemName[..40] : line.OcrItemName;
            var hits = await _purchases.SearchMedicinesAsync(term, ct);
            var match = hits.FirstOrDefault(h => NamesLikelySame(line.OcrItemName, h.Name))
                        ?? hits.FirstOrDefault();
            if (match is null) continue;

            line.MatchedMedicineId = match.Id;
            line.MatchedMedicineName = match.Name;
            if (line.GstPercent <= 0) line.GstPercent = match.GstPercent;
            if (line.PurchasePrice <= 0) line.PurchasePrice = match.PurchasePrice;
            if (line.Mrp <= 0) line.Mrp = match.Mrp;
            if (line.SellingPrice <= 0)
                line.SellingPrice = match.SellingPrice > 0 ? match.SellingPrice : match.Mrp;
        }
    }

    private static bool NamesLikelySame(string a, string b)
    {
        static string Norm(string s) =>
            new string(s.Where(ch => char.IsLetterOrDigit(ch) || char.IsWhiteSpace(ch)).ToArray())
                .Trim()
                .ToLowerInvariant();

        var na = Norm(a);
        var nb = Norm(b);
        if (na.Length == 0 || nb.Length == 0) return false;
        if (na == nb) return true;
        if (na.StartsWith(nb) || nb.StartsWith(na)) return true;

        var ta = na.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tb = nb.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (ta.Length == 0 || tb.Length == 0) return false;
        return ta[0] == tb[0] && (ta.Length == 1 || tb.Length == 1 || ta.Take(2).SequenceEqual(tb.Take(2)));
    }
}
