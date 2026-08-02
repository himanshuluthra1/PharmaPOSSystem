using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PharmaPOS.Application.Features.Purchases;

/// <summary>
/// Parses pharmacy wholesale invoices from OCR. Uses word bounding boxes to rebuild
/// table rows (Windows OCR often returns columns as separate lines).
/// </summary>
public static class PurchaseBillTextParser
{
    private static readonly Regex InvoiceNoRx = new(
        @"(?i)(?<![A-Za-z])(?:invoice|irwoice|invoic[e]?)\s*(?:no|number|#)?\s*[:.\-]?\s*[^\dA-Z]{0,6}(\d{1,8})",
        RegexOptions.Compiled);

    private static readonly Regex DateLabeledRx = new(
        @"(?i)(?<!due\s)(?<!expiry\s)(?<!exp(?:iry)?\s)(?:^|\b)date\s*[:.\-]?\s*(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{2,4})",
        RegexOptions.Compiled);

    private static readonly Regex DateRx = new(
        @"\b(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{2,4})\b",
        RegexOptions.Compiled);

    private static readonly Regex ExpMonthYearRx = new(
        @"\b(0?[1-9]|1[0-2])[\/\-]((?:20)?\d{2})\b",
        RegexOptions.Compiled);

    private static readonly string[] NoiseTokens =
    [
        "sr", "hsn", "code", "description", "pack", "mfr", "batch", "exp", "dt", "qty",
        "free", "mrp", "rate", "dis", "gst", "amount", "invoice", "sample", "template",
        "tempalte", "wholesale", "salesbill", "page", "total", "bank", "ifsc", "cgst",
        "sgst", "igst", "taxable", "round", "off", "sub", "discount", "net", "terms",
        "deals", "buyer", "detail", "details"
    ];

    private static readonly HashSet<string> PackUnits = new(StringComparer.OrdinalIgnoreCase)
    {
        "ml", "mi", "mg", "g", "gm", "tab", "tabs", "cap", "caps", "gross", "nos", "no", "amp", "amps", "eos"
    };

    public static ScannedPurchaseDraftDto Parse(string? ocrText)
        => Parse(new OcrPageDto { FullText = ocrText ?? string.Empty });

    public static ScannedPurchaseDraftDto Parse(OcrPageDto page)
    {
        var draft = new ScannedPurchaseDraftDto
        {
            RawText = page.FullText?.Trim()
        };

        if (string.IsNullOrWhiteSpace(page.FullText) && page.Words.Count == 0)
        {
            draft.Warnings.Add("No text could be read from the image. Try a clearer, flatter scan.");
            return draft;
        }

        var text = page.FullText ?? string.Empty;
        draft.SupplierInvoiceNumber = ExtractInvoiceNumber(text);
        draft.InvoiceDate = ExtractInvoiceDate(text) ?? DateTime.Today;
        draft.SupplierName = ExtractSupplierName(page);
        draft.GrandTotalHint = ExtractGrandTotal(text);

        var rows = page.Words.Count > 0
            ? BuildRowsFromWords(page.Words)
            : text.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => new List<string> { l })
                .ToList();

        foreach (var row in rows)
        {
            var parsed = TryParsePharmacyRow(row);
            if (parsed is not null)
                draft.Lines.Add(parsed);
        }

        draft.Lines = Deduplicate(draft.Lines);

        if (draft.Lines.Count == 0)
            draft.Warnings.Add("Could not detect item rows. Check image quality / lighting and try again.");
        else
            draft.Warnings.Add($"Detected {draft.Lines.Count} item row(s). Verify medicine matches, batch, qty and prices.");

        if (string.IsNullOrWhiteSpace(draft.SupplierInvoiceNumber))
            draft.Warnings.Add("Supplier invoice number was not detected clearly.");
        if (string.IsNullOrWhiteSpace(draft.SupplierName))
            draft.Warnings.Add("Supplier name was not detected clearly.");

        return draft;
    }

    private static List<List<string>> BuildRowsFromWords(IReadOnlyList<OcrWordDto> words)
    {
        if (words.Count == 0) return [];

        var ordered = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderBy(w => w.CenterY)
            .ThenBy(w => w.X)
            .ToList();

        var medianHeight = ordered
            .Select(w => w.Height)
            .OrderBy(h => h)
            .Skip(ordered.Count / 4)
            .Take(Math.Max(1, ordered.Count / 2))
            .DefaultIfEmpty(16)
            .Average();
        var yTol = Math.Max(8, medianHeight * 0.65);

        var rows = new List<List<OcrWordDto>>();
        foreach (var word in ordered)
        {
            if (rows.Count == 0)
            {
                rows.Add([word]);
                continue;
            }

            var current = rows[^1];
            var rowY = current.Average(w => w.CenterY);
            if (Math.Abs(word.CenterY - rowY) <= yTol)
                current.Add(word);
            else
                rows.Add([word]);
        }

        return rows
            .Select(r => r.OrderBy(w => w.X).Select(w => NormalizeToken(w.Text)).Where(t => t.Length > 0).ToList())
            .Where(r => r.Count > 0)
            .ToList();
    }

    private static string NormalizeToken(string raw)
    {
        var t = raw.Trim();
        if (t is "o" or "O" or "〇") return "0";
        if (t is "l" or "I" or "|") return t; // keep; may be noise
        // ".30" style invoice glitch kept for invoice extractor; for row nums strip leading dot if whole number
        return t;
    }

    private static ScannedPurchaseLineDto? TryParsePharmacyRow(List<string> tokens)
    {
        if (tokens.Count < 4) return null;

        var joined = string.Join(' ', tokens);
        var lower = joined.ToLowerInvariant();
        if (IsFooterOrHeaderRow(lower)) return null;
        if (NoiseTokens.Count(n => lower.Contains(n)) >= 3 && !tokens.Any(HasLetter))
            return null;

        var work = tokens.ToList();

        // Strip leading serial + HSN.
        if (work.Count > 0 && Regex.IsMatch(work[0], @"^\d{1,2}$"))
            work.RemoveAt(0);
        if (work.Count > 0 && Regex.IsMatch(work[0], @"^\d{6,8}$"))
            work.RemoveAt(0);

        DateTime? expiry = null;
        string? batch = null;
        for (var i = work.Count - 1; i >= 0; i--)
        {
            if (expiry is null && TryParseExpiry(work[i], out var exp))
            {
                expiry = exp;
                work.RemoveAt(i);
                continue;
            }
            if (batch is null && LooksLikeBatch(work[i]))
            {
                batch = work[i];
                work.RemoveAt(i);
            }
        }

        // Remove pack fragments: "100 ml", "1 GROSS", "30 100 mi"
        RemovePackTokens(work);

        // Drop leftover pack size (orphan integer before mfr/qty) when followed by mfr code + qty block.
        // Short mfr codes (WS, ZDef / zoef) immediately before the numeric tail.
        for (var i = work.Count - 1; i >= 0; i--)
        {
            if (TryParseNumber(work[i], out _)) continue;
            // non-numeric from the right: if short mfr-like, remove
            if (work[i].Length <= 5
                && work[i].Any(char.IsLetter)
                && !work[i].Any(char.IsDigit)
                && !IsLikelyNameWord(work[i]))
            {
                work.RemoveAt(i);
            }
            else break;
        }

        // Collect numeric candidates from remaining tokens (preserve order).
        var nums = new List<(int Index, decimal Value, string Raw)>();
        for (var i = 0; i < work.Count; i++)
        {
            if (TryParseNumber(work[i], out var value))
                nums.Add((i, value, work[i]));
        }

        if (nums.Count < 2) return null;

        // Amount = right-most money-like number (not a lone GST% — incomplete OCR row).
        var amountEntry = nums[^1];
        var amount = amountEntry.Value;
        if (amount <= 0) return null;
        if (IsGst(amount) && IsWhole(amount) && amount <= 28) return null;

        var leftNums = nums.Take(nums.Count - 1).Select(n => n.Value).ToList();
        if (!TryAssignColumns(leftNums, amount, out var qty, out var free, out var mrp, out var rate, out var disc, out var gst))
            return null;

        // Name = non-numeric leftover tokens (and unused numbers that weren't in the fit — already stripped conceptually).
        var usedNumericIndices = new HashSet<int> { amountEntry.Index };
        // Mark all numeric indices as used for name building (name should be letters).
        foreach (var n in nums)
            usedNumericIndices.Add(n.Index);

        var nameTokens = work
            .Where((_, i) => !usedNumericIndices.Contains(i))
            .Where(t => !IsPureNoiseToken(t))
            .Where(t => t is not ":" and not "-" and not "." and not "%")
            .ToList();

        var name = CleanItemName(string.Join(' ', nameTokens));
        if (IsHeaderName(name)) return null;
        if (LooksLikeBankOrTaxLabel(name)) return null;
        // OCR sometimes drops the description; still keep priced rows for manual naming.
        if (name.Length < 2 || !name.Any(char.IsLetter))
            name = "Unread item";

        // Reject absurd qty from residual OCR when amount doesn't support it (already validated in assign).
        if (qty > 50000) return null;

        return new ScannedPurchaseLineDto
        {
            OcrItemName = name,
            BatchNumber = batch,
            ExpiryDate = expiry,
            Quantity = qty > 0 ? qty : 1,
            FreeQuantity = free,
            PurchasePrice = rate > 0 ? rate : (mrp > 0 ? mrp : 0),
            Mrp = mrp > 0 ? mrp : rate,
            SellingPrice = mrp > 0 ? mrp : rate,
            GstPercent = gst,
            DiscountPercent = disc,
            LineAmountHint = amount
        };
    }

    /// <summary>
    /// Wholesale rows: Qty Free MRP Rate Disc% GST% Amount
    /// amount ≈ qty × rate × (1 − disc/100) × (1 + gst/100)
    /// </summary>
    private static bool TryAssignColumns(
        List<decimal> leftNums,
        decimal amount,
        out decimal qty,
        out decimal free,
        out decimal mrp,
        out decimal rate,
        out decimal disc,
        out decimal gst)
    {
        qty = 1; free = 0; mrp = 0; rate = 0; disc = 0; gst = 0;
        if (leftNums.Count == 0) return false;

        const decimal tol = 0.75m;
        var bestScore = double.MaxValue;
        Cols? best = null;

        // Slide a window of up to 6 trailing numbers as the price block.
        var maxTake = Math.Min(6, leftNums.Count);
        for (var take = Math.Min(2, maxTake); take <= maxTake; take++)
        {
            var block = leftNums.TakeLast(take).ToList();
            foreach (var candidate in EnumerateAssignments(block))
            {
                var expected = ComputeLineAmount(candidate.Qty, candidate.Rate, candidate.Disc, candidate.Gst);
                var err = (double)Math.Abs(expected - amount);
                if (err > (double)Math.Max(tol, amount * 0.02m)) continue;

                // Prefer exact fits; prefer having rate; prefer qty that isn't a batch-like giant.
                var score = err
                    + (candidate.Rate < 5m ? 100 : 0)
                    + (candidate.Qty >= 1000 ? 40 : 0)
                    + (candidate.Qty >= 10000 ? 30 : 0)
                    + (candidate.Gst is > 0 and <= 28 ? 0 : 2)
                    + (double)(candidate.Qty / 100m)
                    + Math.Abs(take - 6) * 0.1;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
        }

        // Prefer inferring missing rate (qty/free/disc/gst present) before inventing huge qty.
        if (best is null)
        {
            for (var take = Math.Min(4, leftNums.Count); take >= 2; take--)
            {
                var block = leftNums.TakeLast(take).ToList();
                if (TryInferMissingRate(block, amount, out var inferred))
                {
                    best = inferred;
                    break;
                }
            }
        }

        // Fallback: infer qty from amount when OCR dropped qty.
        if (best is null)
        {
            for (var take = Math.Min(5, leftNums.Count); take >= 2; take--)
            {
                var block = leftNums.TakeLast(take).ToList();
                if (TryInferMissingQty(block, amount, out var inferred))
                {
                    best = inferred;
                    break;
                }
            }
        }

        if (best is null) return false;

        qty = best.Value.Qty;
        free = best.Value.Free;
        mrp = best.Value.Mrp;
        rate = best.Value.Rate;
        disc = best.Value.Disc;
        gst = best.Value.Gst;

        // If MRP missing, use rate.
        if (mrp <= 0) mrp = rate;
        if (rate <= 0) rate = mrp;
        return rate > 0 || mrp > 0;
    }

    private readonly record struct Cols(decimal Qty, decimal Free, decimal Mrp, decimal Rate, decimal Disc, decimal Gst);

    private static IEnumerable<Cols> EnumerateAssignments(List<decimal> block)
    {
        var n = block.Count;
        // From the right: [Qty] [Free?] [MRP?] [Rate] [Disc%?] [GST%?]
        for (var hasGst = 0; hasGst <= 1; hasGst++)
        for (var hasDisc = 0; hasDisc <= 1; hasDisc++)
        for (var hasMrp = 0; hasMrp <= 1; hasMrp++)
        for (var hasFree = 0; hasFree <= 1; hasFree++)
        {
            var need = 1 /*qty*/ + hasFree + hasMrp + 1 /*rate*/ + hasDisc + hasGst;
            if (need != n) continue;

            var i = n - 1;
            decimal gst = 0, disc = 0, free = 0, mrp;

            if (hasGst == 1)
            {
                if (!IsGst(block[i])) continue;
                gst = block[i--];
            }
            if (hasDisc == 1)
            {
                if (!IsDisc(block[i])) continue;
                disc = block[i--];
            }

            var rate = block[i--];
            if (rate < 5m || rate > 100000) continue;

            if (hasMrp == 1)
            {
                mrp = block[i--];
                // MRP usually >= rate (trade), allow some OCR slack.
                if (mrp <= 0 || mrp > 100000) continue;
            }
            else mrp = rate;

            if (hasFree == 1)
            {
                if (!IsFreeOrQty(block[i]) || block[i] > 5000) continue;
                free = block[i--];
            }

            var qty = block[i];
            if (!IsFreeOrQty(qty) || qty < 1 || qty > 100000) continue;

            yield return new Cols(qty, free, mrp, rate, disc, gst);
        }
    }

    private static bool TryInferMissingQty(List<decimal> block, decimal amount, out Cols cols)
    {
        cols = default;
        // Patterns without qty: Free? MRP? Rate Disc? GST?
        for (var hasGst = 1; hasGst >= 0; hasGst--)
        for (var hasDisc = 1; hasDisc >= 0; hasDisc--)
        for (var hasFree = 0; hasFree <= 1; hasFree++)
        for (var hasMrp = 0; hasMrp <= 1; hasMrp++)
        {
            var need = hasFree + hasMrp + 1 + hasDisc + hasGst; // + rate
            if (need != block.Count) continue;
            var i = block.Count - 1;
            decimal gst = 0, disc = 0, free = 0, mrp;
            if (hasGst == 1)
            {
                if (!IsGst(block[i])) continue;
                gst = block[i--];
            }
            if (hasDisc == 1)
            {
                if (!IsDisc(block[i])) continue;
                disc = block[i--];
            }
            var rate = block[i--];
            if (rate < 5m) continue;
            if (hasMrp == 1) mrp = block[i--]; else mrp = rate;
            if (hasFree == 1)
            {
                if (!IsFreeOrQty(block[i]) || block[i] > 5000) continue;
                free = block[i];
            }

            var taxableUnit = rate * (1 - disc / 100m);
            if (taxableUnit <= 0) continue;
            var unitWithTax = taxableUnit * (1 + gst / 100m);
            if (unitWithTax <= 0) continue;
            var q = Math.Round(amount / unitWithTax, 0);
            if (q < 1 || q > 100000) continue;
            if (Math.Abs(ComputeLineAmount(q, rate, disc, gst) - amount) > Math.Max(0.75m, amount * 0.02m))
                continue;
            cols = new Cols(q, free, mrp, rate, disc, gst);
            return true;
        }
        return false;
    }

    /// <summary>When rate/MRP were dropped by OCR: block is Qty Free? Disc? GST?</summary>
    private static bool TryInferMissingRate(List<decimal> block, decimal amount, out Cols cols)
    {
        cols = default;
        for (var hasGst = 1; hasGst >= 0; hasGst--)
        for (var hasDisc = 1; hasDisc >= 0; hasDisc--)
        for (var hasFree = 0; hasFree <= 1; hasFree++)
        {
            var need = 1 + hasFree + hasDisc + hasGst;
            if (need != block.Count) continue;
            var i = block.Count - 1;
            decimal gst = 0, disc = 0, free = 0;
            if (hasGst == 1)
            {
                if (!IsGst(block[i])) continue;
                gst = block[i--];
            }
            if (hasDisc == 1)
            {
                if (!IsDisc(block[i])) continue;
                disc = block[i--];
            }
            if (hasFree == 1)
            {
                if (!IsFreeOrQty(block[i]) || block[i] > 5000) continue;
                free = block[i--];
            }
            var qty = block[i];
            if (!IsFreeOrQty(qty) || qty < 1) continue;

            var factor = (1 - disc / 100m) * (1 + gst / 100m);
            if (factor <= 0) continue;
            var rate = Math.Round(amount / (qty * factor), 2, MidpointRounding.AwayFromZero);
            if (rate <= 0 || rate > 100000) continue;
            if (Math.Abs(ComputeLineAmount(qty, rate, disc, gst) - amount) > Math.Max(0.75m, amount * 0.02m))
                continue;
            cols = new Cols(qty, free, rate, rate, disc, gst);
            return true;
        }
        return false;
    }

    private static decimal ComputeLineAmount(decimal qty, decimal rate, decimal disc, decimal gst)
        => Math.Round(qty * rate * (1 - disc / 100m) * (1 + gst / 100m), 2, MidpointRounding.AwayFromZero);

    private static bool IsGst(decimal v) => v is 0 or 5 or 12 or 18 or 28 or 3 or 40;
    private static bool IsDisc(decimal v) => v is >= 0 and <= 100 && IsWhole(v);
    private static bool IsFreeOrQty(decimal v) => v is >= 0 and <= 100000 && IsWhole(v);

    private static void RemovePackTokens(List<string> work)
    {
        // "30 100 ml" / "100 ml"
        for (var i = work.Count - 1; i >= 0; i--)
        {
            if (!PackUnits.Contains(work[i])) continue;
            // remove unit
            work.RemoveAt(i);
            // remove preceding number(s)
            if (i - 1 >= 0 && Regex.IsMatch(work[i - 1], @"^\d+(\.\d+)?"))
            {
                work.RemoveAt(i - 1);
                i--;
                if (i - 1 >= 0 && Regex.IsMatch(work[i - 1], @"^\d+(\.\d+)?"))
                    work.RemoveAt(i - 1);
            }
        }
        work.RemoveAll(t => t is "1/2" or "1\\2");
    }

    private static bool IsFooterOrHeaderRow(string lower)
    {
        if (lower.Contains("taxable") || lower.Contains("net amount") || lower.Contains("net arnount")
            || lower.Contains("sub total") || lower.Contains("round off")
            || lower.Contains("bank") || lower.Contains("ifsc") || lower.Contains("gstin")
            || lower.Contains("cgst") || lower.Contains("sgst") || lower.Contains("igst")
            || lower.Contains("ac no") || lower.Contains("terms") || lower.Contains("buyer")
            || lower.Contains("sample invoice") || lower.Contains("salesbill"))
            return true;
        return false;
    }

    private static bool LooksLikeBankOrTaxLabel(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower.Contains("cgst") || lower.Contains("sgst") || lower.Contains("ifsc")
            || lower.Contains("ac no") || lower.Contains("taxable") || lower.Contains("round");
    }

    private static bool IsLikelyNameWord(string t)
        => t.Length >= 4 && t.Count(char.IsLetter) >= 3;

    private static List<ScannedPurchaseLineDto> Deduplicate(List<ScannedPurchaseLineDto> lines)
    {
        var result = new List<ScannedPurchaseLineDto>();
        foreach (var line in lines)
        {
            var dup = result.LastOrDefault(x =>
                string.Equals(x.OcrItemName, line.OcrItemName, StringComparison.OrdinalIgnoreCase)
                && x.Quantity == line.Quantity
                && Math.Abs(x.PurchasePrice - line.PurchasePrice) < 0.01m
                && string.Equals(x.BatchNumber ?? "", line.BatchNumber ?? "", StringComparison.OrdinalIgnoreCase));
            if (dup is null) result.Add(line);
        }
        return result;
    }

    private static string? ExtractInvoiceNumber(string text)
    {
        foreach (Match m in InvoiceNoRx.Matches(text))
        {
            var value = m.Groups[1].Value.Trim().TrimStart('.');
            if (value.Length is 0 or > 12) continue;
            return value;
        }

        var m2 = Regex.Match(text, @"(?i)(?:invoice|irwoice|invoic)\s*no\.?\s*[^\d]{0,8}(\d{1,8})");
        return m2.Success ? m2.Groups[1].Value : null;
    }

    private static DateTime? ExtractInvoiceDate(string text)
    {
        var labeled = DateLabeledRx.Match(text);
        if (labeled.Success
            && TryParseDate(labeled.Groups[1].Value, labeled.Groups[2].Value, labeled.Groups[3].Value, out var labeledDt)
            && labeledDt.Year >= 2000 && labeledDt <= DateTime.Today.AddDays(3))
            return labeledDt;

        // Prefer earliest plausible bill date (invoice date usually before due date).
        DateTime? best = null;
        foreach (Match m in DateRx.Matches(text))
        {
            if (!TryParseDate(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, out var dt))
                continue;
            if (dt.Year < 2000 || dt > DateTime.Today.AddDays(3)) continue;
            if (best is null || dt < best)
                best = dt;
        }
        return best;
    }

    private static bool TryParseDate(string a, string b, string c, out DateTime dt)
    {
        dt = default;
        if (!int.TryParse(a, out var d) || !int.TryParse(b, out var m) || !int.TryParse(c, out var y))
            return false;
        if (y < 100) y += 2000;
        if (m > 12 && d <= 12) (d, m) = (m, d);
        if (m is < 1 or > 12 || d is < 1 or > 31) return false;
        try
        {
            dt = new DateTime(y, m, Math.Min(d, DateTime.DaysInMonth(y, m)));
            return true;
        }
        catch { return false; }
    }

    private static bool TryParseExpiry(string token, out DateTime exp)
    {
        exp = default;
        var m = ExpMonthYearRx.Match(token);
        if (!m.Success) return false;
        if (!int.TryParse(m.Groups[1].Value, out var mm)) return false;
        if (!int.TryParse(m.Groups[2].Value, out var yy)) return false;
        if (yy < 100) yy += 2000;
        if (mm is < 1 or > 12) return false;
        if (yy < 2018 || yy > DateTime.Today.Year + 20) return false;
        exp = new DateTime(yy, mm, DateTime.DaysInMonth(yy, mm));
        return true;
    }

    private static string? ExtractSupplierName(OcrPageDto page)
    {
        if (page.Words.Count > 0)
        {
            foreach (var row in BuildRowsFromWords(page.Words.OrderBy(w => w.Y).Take(80).ToList()).Take(12))
            {
                var line = string.Join(' ', row);
                if (line.Contains("software", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("salesbill", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("sample", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (line.Contains("pharma", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("medical", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("agency", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("distributor", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("traders", StringComparison.OrdinalIgnoreCase))
                {
                    // Keep "Name Pharmacy" only — drop address tail.
                    var cleaned = CleanItemName(line);
                    var m = Regex.Match(cleaned, @"(?i)\b([A-Za-z][A-Za-z0-9&.'\-]*(?:\s+[A-Za-z][A-Za-z0-9&.'\-]*){0,4}\s+(?:pharmacy|pharma|medicals?|agency|distributors?|traders?))\b");
                    if (m.Success) return CleanItemName(m.Groups[1].Value);
                    return cleaned.Split(',').FirstOrDefault()?.Trim();
                }
            }
        }

        foreach (var line in (page.FullText ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(12))
        {
            if (line.Contains("software", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.Contains("pharma", StringComparison.OrdinalIgnoreCase)
                || line.Contains("medical", StringComparison.OrdinalIgnoreCase))
            {
                var m = Regex.Match(line, @"(?i)\b([A-Za-z][A-Za-z0-9&.'\-]*(?:\s+[A-Za-z][A-Za-z0-9&.'\-]*){0,4}\s+(?:pharmacy|pharma|medicals?))\b");
                if (m.Success) return CleanItemName(m.Groups[1].Value);
                return CleanItemName(line.Split(',').FirstOrDefault() ?? line);
            }
        }

        return null;
    }

    private static decimal? ExtractGrandTotal(string text)
    {
        decimal? best = null;
        foreach (Match label in Regex.Matches(
                     text,
                     @"(?i)(?:net\s*am[ou0]unt|net\s*arn?o?unt|grand\s*total|bill\s*amount)"))
        {
            var start = label.Index + label.Length;
            var len = Math.Min(120, text.Length - start);
            if (len <= 0) continue;
            var window = text.Substring(start, len);
            foreach (Match n in Regex.Matches(window, @"\d{2,9}(?:[.,]\d{2})?"))
            {
                if (!decimal.TryParse(n.Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var v))
                    continue;
                v = Math.Abs(v);
                if (v < 10) continue;
                // Column-scrambled OCR puts line amounts before the real net; prefer the largest.
                if (best is null || v > best) best = v;
            }
        }

        return best;
    }

    private static bool TryParseNumber(string raw, out decimal value)
    {
        value = 0;
        var t = raw.Trim().TrimEnd('%').TrimStart('.');
        t = t.Replace(",", "");
        if (t.EndsWith('.')) t = t.TrimEnd('.');
        // Reject month-year and phone-like
        if (t.Contains('-') || t.Contains('/')) return false;
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsWhole(decimal value) => decimal.Truncate(value) == value;

    private static bool HasLetter(string t) => t.Any(char.IsLetter);

    private static bool LooksLikeBatch(string t)
    {
        if (t.Length is < 3 or > 16) return false;
        if (!t.Any(char.IsLetterOrDigit)) return false;
        if (Regex.IsMatch(t, @"^\d{5,}$")) return true;
        if (Regex.IsMatch(t, @"(?i)^[a-z]+\d+$") || Regex.IsMatch(t, @"(?i)^\d+[a-z]+$")) return true;
        if (Regex.IsMatch(t, @"(?i)^[a-z0-9\-]{4,}$") && t.Any(char.IsDigit) && t.Any(char.IsLetter)) return true;
        return false;
    }

    private static bool IsPureNoiseToken(string t)
    {
        var lower = t.Trim().ToLowerInvariant();
        return NoiseTokens.Contains(lower);
    }

    private static bool IsHeaderName(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower is "description" or "particulars" or "item" or "product"
            || lower.Contains("sample invoice");
    }

    private static string CleanItemName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsControl(ch)) continue;
            sb.Append(ch);
        }
        return Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim(" .-|:".ToCharArray());
    }
}
