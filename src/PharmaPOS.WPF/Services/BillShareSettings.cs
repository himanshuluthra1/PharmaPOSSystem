namespace PharmaPOS.WPF.Services;

/// <summary>Machine-local bill-share settings (%LocalAppData%\PharmaPOS\bill-share-settings.json).</summary>
public sealed class BillShareSettings
{
    public bool EnableWhatsApp { get; set; } = true;
    public bool EnableSms { get; set; } = true;
    public bool AskAfterSave { get; set; } = true;

    /// <summary>
    /// When true, bill PDFs are uploaded to the VPS over SFTP and the public URL
    /// is included in the WhatsApp / SMS text (customer opens the link).
    /// </summary>
    public bool EnableVpsUpload { get; set; }

    /// <summary>Public base URL that serves uploaded files, e.g. https://bills.myshop.com/bills/</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    public string SftpHost { get; set; } = string.Empty;
    public int SftpPort { get; set; } = 22;
    public string SftpUsername { get; set; } = string.Empty;
    public string SftpPassword { get; set; } = string.Empty;

    /// <summary>Remote folder on the VPS, e.g. /var/www/bills</summary>
    public string SftpRemoteDirectory { get; set; } = "/var/www/html/bills";

    /// <summary>When true, shorten the public PDF URL via TinyURL before WhatsApp/SMS.</summary>
    public bool EnableTinyUrl { get; set; } = true;

    /// <summary>
    /// When true and Cloud API credentials are set, WhatsApp sends via Meta Graph API
    /// (no WhatsApp Desktop). Otherwise the app opens WhatsApp chat as before.
    /// </summary>
    public bool EnableWhatsAppApi { get; set; }

    /// <summary>Meta permanent / system-user access token for WhatsApp Cloud API.</summary>
    public string WhatsAppAccessToken { get; set; } = string.Empty;

    /// <summary>WhatsApp Business phone number ID from Meta Developer Console.</summary>
    public string WhatsAppPhoneNumberId { get; set; } = string.Empty;

    /// <summary>Graph API version, e.g. v21.0</summary>
    public string WhatsAppApiVersion { get; set; } = "v21.0";

    /// <summary>
    /// Optional approved utility template name for bills (required to message customers
    /// outside the 24-hour window). Body params: {{1}} customer, {{2}} invoice,
    /// {{3}} amount, {{4}} PDF link or "—".
    /// </summary>
    public string WhatsAppBillTemplateName { get; set; } = string.Empty;

    /// <summary>Template language code, e.g. en or en_US.</summary>
    public string WhatsAppBillTemplateLanguage { get; set; } = "en";

    /// <summary>If API send fails, fall back to opening WhatsApp Desktop.</summary>
    public bool WhatsAppApiDesktopFallback { get; set; } = true;
}
