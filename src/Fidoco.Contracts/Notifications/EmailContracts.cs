namespace Fidoco.Contracts.Notifications;

/// <summary>Template names known by the Notifications module.</summary>
public static class EmailTemplates
{
    /// <summary>Values: link.</summary>
    public const string LoginLink = "login-link";

    /// <summary>Values: link, eventTitle. Rows: receipt lines.</summary>
    public const string Receipt = "receipt";

    /// <summary>Values: link. A login link sent by the organizer's support (single use, 24 hours).</summary>
    public const string SupportLoginLink = "support-login-link";

    /// <summary>Values: link, eventTitle, ticketTypeName. A complimentary ticket (friplats).</summary>
    public const string CompTicket = "comp-ticket";

    /// <summary>Values: link.</summary>
    public const string AdminInvite = "admin-invite";

    /// <summary>Values: link.</summary>
    public const string PasswordReset = "password-reset";
}

/// <summary>A label/value row rendered as a table, e.g. receipt lines.</summary>
public sealed record EmailRow(string Label, string Value, bool Emphasize = false);

/// <summary>
/// A templated email sent on behalf of the current tenant ("&lt;Tenant&gt; via Fidoco", reply-to the
/// tenant's support address). Values are plain text and are HTML-encoded by the renderer.
/// </summary>
public sealed record EmailRequest(
    string To,
    string Template,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyList<EmailRow>? Rows = null);

public interface IEmailDispatcher
{
    Task SendAsync(EmailRequest request, CancellationToken cancellationToken);
}
