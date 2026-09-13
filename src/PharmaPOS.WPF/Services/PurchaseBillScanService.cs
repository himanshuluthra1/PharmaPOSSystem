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
        var imagePaths = PromptForImages();
        if (imagePaths is null || imagePaths.Count == 0) return null;

        var window = new PurchaseBillScanWindow(_purchases, _medicinePicker)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(true);

        window.ShowScanning();
        window.Show();

        ScannedPurchaseDraftDto draft;
        try
        {
            draft = await ExtractAndMergeDraftsAsync(imagePaths, ct);
            await MatchSupplierAndMedicinesAsync(draft, ct);
            await window.Dispatcher.InvokeAsync(() => window.ApplyDraft(draft, imagePaths[0]));
        }
        catch (Exception ex)
        {
            _dialog.ShowError($"Could not read the bill: {ex.Message}");
            window.Close();
            return null;
        }

        await closed.Task;
        return window.AcceptedDraft;
    }

    private async Task<ScannedPurchaseDraftDto> ExtractAndMergeDraftsAsync(
        IReadOnlyList<string> imagePaths,
        CancellationToken ct)
    {
        if (imagePaths.Count == 1)
            return await ExtractDraftAsync(imagePaths[0], ct);

        var parts = new List<ScannedPurchaseDraftDto>(imagePaths.Count);
        foreach (var path in imagePaths)
        {
            ct.ThrowIfCancellationRequested();
            parts.Add(await ExtractDraftAsync(path, ct));
        }

        return MergeDrafts(parts, imagePaths.Count);
    }

    private static ScannedPurchaseDraftDto MergeDrafts(IReadOnlyList<ScannedPurchaseDraftDto> parts, int pageCount)
    {
        var merged = new ScannedPurchaseDraftDto();
        var rawChunks = new List<string>();

        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(merged.SupplierName) && !string.IsNullOrWhiteSpace(part.SupplierName))
                merged.SupplierName = part.SupplierName;
            if (string.IsNullOrWhiteSpace(merged.SupplierInvoiceNumber)
                && !string.IsNullOrWhiteSpace(part.SupplierInvoiceNumber))
                merged.SupplierInvoiceNumber = part.SupplierInvoiceNumber;
            if (merged.InvoiceDate is null && part.InvoiceDate is not null)
                merged.InvoiceDate = part.InvoiceDate;
            if (merged.MatchedSupplierId is null && part.MatchedSupplierId is not null)
                merged.MatchedSupplierId = part.MatchedSupplierId;
            if (string.IsNullOrWhiteSpace(merged.MatchedSupplierPhone)
                && !string.IsNullOrWhiteSpace(part.MatchedSupplierPhone))
                merged.MatchedSupplierPhone = part.MatchedSupplierPhone;
            if (merged.GrandTotalHint is null && part.GrandTotalHint is not null)
                merged.GrandTotalHint = part.GrandTotalHint;

            merged.Lines.AddRange(part.Lines);
            merged.Warnings.AddRange(part.Warnings);

            if (!string.IsNullOrWhiteSpace(part.RawText))
                rawChunks.Add(part.RawText);
        }

        merged.RawText = rawChunks.Count > 0 ? string.Join("\n\n---\n\n", rawChunks) : null;
        if (pageCount > 1)
            merged.Warnings.Insert(0, $"Merged {pageCount} scanned page(s).");

        return merged;
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

    private IReadOnlyList<string>? PromptForImages()
    {
        var choice = System.Windows.MessageBox.Show(
            "Scan a supplier purchase bill.\n\nYes = Browse image / PDF scan\nNo = Capture with camera\nCancel = Abort",
            "Scan purchase bill",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question);

        if (choice == System.Windows.MessageBoxResult.Cancel) return null;
        if (choice == System.Windows.MessageBoxResult.No)
        {
            var captured = CaptureWithCamera();
            return captured is null ? null : [captured];
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open scanned purchase bill (you can select multiple pages)",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0)
            return null;

        return dlg.FileNames;
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
