using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using PharmaPOS.Application.Features.Sales;
using PharmaPOS.WPF.Views;

namespace PharmaPOS.WPF.Services;

[Flags]
public enum BillShareChannel
{
    None = 0,
    WhatsApp = 1,
    Sms = 2,
    Both = WhatsApp | Sms
}

public interface IBillShareService
{
    bool ShouldOfferAfterSave(SaleReceiptDto receipt);
    void OfferShareAfterSave(SaleReceiptDto receipt);
}

/// <summary>
/// Shares sale bills via WhatsApp / SMS.
/// Preferred WhatsApp path: upload PDF to VPS over SFTP and include the public link in the message.
/// Fallback (no VPS): copy PDF to clipboard + Explorer for manual attach.
/// PDF upload and link open run in the background so the POS UI stays usable.
/// </summary>
public sealed class BillShareService : IBillShareService
{
    private readonly IBillShareSettingsService _settings;
    private readonly IInvoicePrintService _printService;
    private readonly IBillPdfUploadService _uploader;
    private readonly IUrlShortenerService _urlShortener;
    private readonly IDialogService _dialog;

    public BillShareService(
        IBillShareSettingsService settings,
        IInvoicePrintService printService,
        IBillPdfUploadService uploader,
        IUrlShortenerService urlShortener,
        IDialogService dialog)
    {
        _settings = settings;
        _printService = printService;
        _uploader = uploader;
        _urlShortener = urlShortener;
        _dialog = dialog;
    }

    public bool ShouldOfferAfterSave(SaleReceiptDto receipt)
    {
        var cfg = _settings.Current;
        if (!cfg.AskAfterSave) return false;
        if (!cfg.EnableWhatsApp && !cfg.EnableSms) return false;
        return !string.IsNullOrWhiteSpace(NormalizePhone(receipt.CustomerPhone));
    }

    public void OfferShareAfterSave(SaleReceiptDto receipt)
    {
        if (!ShouldOfferAfterSave(receipt)) return;

        var cfg = _settings.Current;
        var phone = NormalizePhone(receipt.CustomerPhone)!;
        var displayPhone = FormatDisplayPhone(phone);

        var window = new BillSharePromptWindow(
            receipt.InvoiceNumber,
            displayPhone,
            cfg.EnableWhatsApp,
            cfg.EnableSms)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };

        if (window.ShowDialog() != true || window.SelectedChannel == BillShareChannel.None)
            return;

        var channel = window.SelectedChannel;
        // Return immediately so Save can reset the bill form; upload/share continue in background.
        _ = ShareInBackgroundAsync(receipt, channel, phone, cfg);
    }

    private async Task ShareInBackgroundAsync(
        SaleReceiptDto receipt,
        BillShareChannel channel,
        string phone,
        BillShareSettings cfg)
    {
        try
        {
            string? pdfPath = null;
            string? publicUrl = null;

            var needsPdf = channel.HasFlag(BillShareChannel.WhatsApp) && cfg.EnableWhatsApp
                           || (channel.HasFlag(BillShareChannel.Sms) && cfg.EnableSms && _settings.IsVpsUploadConfigured);

            if (needsPdf)
            {
                // FlowDocument PDF export must run on the WPF UI thread.
                pdfPath = await RunOnUiAsync(() => _printService.ExportPrintablePdf(receipt))
                    .ConfigureAwait(false);
            }

            if (pdfPath is not null && _settings.IsVpsUploadConfigured)
            {
                publicUrl = await _uploader.UploadAsync(pdfPath).ConfigureAwait(false);
                publicUrl = await _urlShortener.ShortenAsync(publicUrl).ConfigureAwait(false);
            }

            await RunOnUiAsync(async () =>
            {
                if (channel.HasFlag(BillShareChannel.WhatsApp) && cfg.EnableWhatsApp)
                {
                    var waMessage = BuildWhatsAppMessage(receipt, publicUrl);
                    if (publicUrl is not null)
                    {
                        await OpenWhatsAppAndPasteAsync(phone, waMessage).ConfigureAwait(true);
                        _dialog.ShowInfo(
                            "Bill PDF uploaded.\n\n" +
                            "WhatsApp chat is open with the bill message and PDF link.\n" +
                            "Review and tap Send.\n\n" +
                            "If the box is empty, click the message box and press Ctrl+V.\n\n" +
                            $"Link:\n{publicUrl}",
                            "WhatsApp bill link");
                    }
                    else if (pdfPath is not null)
                    {
                        ShareWhatsAppWithLocalPdf(phone, waMessage, pdfPath);
                        _dialog.ShowInfo(
                            "VPS upload is not configured, so the PDF was prepared for manual attach.\n\n" +
                            "1. WhatsApp chat is open.\n" +
                            "2. Press Ctrl+V (or drag from Explorer) to attach the PDF.\n" +
                            "3. Tap Send.\n\n" +
                            "To send a link instead, configure VPS upload in Settings → Preferences.",
                            "WhatsApp bill PDF");
                    }
                }

                if (channel.HasFlag(BillShareChannel.Sms) && cfg.EnableSms)
                    OpenSms(phone, BuildMessage(receipt, publicUrl));
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => _dialog.ShowError($"Could not share bill: {ex.Message}"))
                .ConfigureAwait(false);
        }
    }

    private static Task RunOnUiAsync(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task;
    }

    private static async Task RunOnUiAsync(Func<Task> action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            await action().ConfigureAwait(false);
            return;
        }

        if (dispatcher.CheckAccess())
        {
            await action().ConfigureAwait(true);
            return;
        }

        await dispatcher.InvokeAsync(action, DispatcherPriority.Normal).Task.Unwrap().ConfigureAwait(false);
    }

    private static async Task<T> RunOnUiAsync<T>(Func<T> func)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            return func();

        return await dispatcher.InvokeAsync(func, DispatcherPriority.Normal).Task.ConfigureAwait(false);
    }

    internal static string BuildMessage(SaleReceiptDto receipt, string? billUrl = null)
    {
        var shop = string.IsNullOrWhiteSpace(receipt.CompanyName) ? "Pharmacy" : receipt.CompanyName.Trim();
        var sb = new StringBuilder();
        sb.Append(shop).AppendLine();
        sb.Append("Bill: ").AppendLine(receipt.InvoiceNumber);
        sb.Append("Date: ").AppendLine(receipt.InvoiceDate.ToString("dd-MMM-yyyy hh:mm tt"));
        if (!string.IsNullOrWhiteSpace(receipt.CustomerName)
            && !string.Equals(receipt.CustomerName, "Walk-in Customer", StringComparison.OrdinalIgnoreCase))
            sb.Append("Customer: ").AppendLine(receipt.CustomerName.Trim());
        sb.Append("Amount: Rs ").AppendLine(receipt.GrandTotal.ToString("N2"));
        if (receipt.PaidAmount > 0)
            sb.Append("Paid: Rs ").AppendLine(receipt.PaidAmount.ToString("N2"));
        if (!string.IsNullOrWhiteSpace(billUrl))
        {
            // URL alone on its own line — WhatsApp linkifies this more reliably.
            sb.AppendLine();
            sb.AppendLine("Bill PDF:");
            sb.AppendLine(billUrl.Trim());
        }
        sb.Append("Thank you!");
        return sb.ToString();
    }

    private static string BuildWhatsAppMessage(SaleReceiptDto receipt, string? billUrl)
    {
        // Put the URL first so it is never lost if WhatsApp / Windows truncates long text=.
        if (!string.IsNullOrWhiteSpace(billUrl))
        {
            var body = BuildMessage(receipt, billUrl: null);
            return billUrl.Trim() + "\n\n" + body;
        }

        return BuildMessage(receipt, billUrl: null)
               + "\n\n(Printable bill PDF — press Ctrl+V to attach, then Send)";
    }

    internal static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = Regex.Replace(raw, @"\D", "");
        if (digits.Length == 0) return null;

        digits = digits.TrimStart('0');
        if (digits.Length == 0) return null;

        if (digits.Length == 10)
            return "91" + digits;

        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
            return digits;

        if (digits.Length == 11 && digits.StartsWith("0", StringComparison.Ordinal))
            return "91" + digits[1..];

        if (digits.Length is >= 11 and <= 15)
            return digits;

        return null;
    }

    private static string FormatDisplayPhone(string normalized)
        => normalized.StartsWith("91", StringComparison.Ordinal) && normalized.Length == 12
            ? $"+91 {normalized[2..7]} {normalized[7..]}"
            : $"+{normalized}";

    private static async Task OpenWhatsAppAndPasteAsync(string phoneDigits, string message)
    {
        // WhatsApp Desktop on Windows often drops or truncates text= (especially with long URLs).
        // Copy the full message, open the chat without text=, then paste with Ctrl+V.
        try
        {
            Clipboard.SetText(message);
        }
        catch
        {
            /* ignore */
        }

        LaunchWhatsAppChat(phoneDigits);

        // Give WhatsApp Desktop time to focus the chat composer before pasting.
        await Task.Delay(1800).ConfigureAwait(true);
        try
        {
            SendCtrlV();
            await Task.Delay(200).ConfigureAwait(true);
        }
        catch
        {
            /* user can still press Ctrl+V manually */
        }
    }

    private static void OpenWhatsApp(string phoneDigits, string message)
    {
        // Used by local-PDF fallback; fire-and-forget paste.
        _ = OpenWhatsAppAndPasteAsync(phoneDigits, message);
    }

    private static void LaunchWhatsAppChat(string phoneDigits)
    {
        // Open chat only — do not put message in the URL (avoids truncation).
        var appUrl = $"whatsapp://send/?phone={phoneDigits}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{appUrl}\"",
                UseShellExecute = true
            });
        }
        catch
        {
            try
            {
                OpenUrl(appUrl);
            }
            catch
            {
                OpenUrl($"https://api.whatsapp.com/send?phone={phoneDigits}");
            }
        }
    }

    private static void SendCtrlV()
    {
        // keybd_event: Ctrl down, V down, V up, Ctrl up
        const byte vkControl = 0x11;
        const byte vkV = 0x56;
        const uint keyDown = 0;
        const uint keyUp = 2;
        keybd_event(vkControl, 0, keyDown, UIntPtr.Zero);
        keybd_event(vkV, 0, keyDown, UIntPtr.Zero);
        keybd_event(vkV, 0, keyUp, UIntPtr.Zero);
        keybd_event(vkControl, 0, keyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static void ShareWhatsAppWithLocalPdf(string phoneDigits, string message, string pdfPath)
    {
        try
        {
            Clipboard.SetFileDropList(new StringCollection { pdfPath });
        }
        catch { /* ignore */ }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{pdfPath}\"",
                UseShellExecute = true
            });
        }
        catch { /* ignore */ }

        OpenWhatsApp(phoneDigits, message);
    }

    private static void OpenSms(string phoneDigits, string message)
    {
        var encoded = Uri.EscapeDataString(message);
        var withPlus = phoneDigits.StartsWith("91", StringComparison.Ordinal)
            ? $"+{phoneDigits}"
            : phoneDigits;

        try
        {
            OpenUrl($"sms:{withPlus}?body={encoded}");
        }
        catch
        {
            OpenUrl($"sms:{withPlus}&body={encoded}");
        }
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}
