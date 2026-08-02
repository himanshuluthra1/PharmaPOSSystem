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
}
