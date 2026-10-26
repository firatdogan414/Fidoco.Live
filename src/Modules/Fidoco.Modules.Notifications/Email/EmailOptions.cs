namespace Fidoco.Modules.Notifications.Email;

internal sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Smtp" (production) or "File" (development: logs and writes .html files).</summary>
    public string Provider { get; set; } = "File";

    public string FromAddress { get; set; } = "noreply@fidoco.se";

    /// <summary>Appended to the tenant name in the From display name: "Apex Fight Night via Fidoco".</summary>
    public string FromDisplaySuffix { get; set; } = " via Fidoco";

    public string Language { get; set; } = "sv";

    public string FileDirectory { get; set; } = "App_Data/emails";

    public SmtpOptions Smtp { get; set; } = new();
}

internal sealed class SmtpOptions
{
    public string Host { get; set; } = "";

    public int Port { get; set; } = 587;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public bool UseStartTls { get; set; } = true;
}

/// <summary>The seller shown on receipts: Fidoco sells in its own name (merchant of record).</summary>
internal sealed class SellerOptions
{
    public const string SectionName = "Platform:Seller";

    public string LegalName { get; set; } = "Fidoco";

    public string? OrganizationNumber { get; set; }

    public string? VatNumber { get; set; }

    public string? Address { get; set; }
}
