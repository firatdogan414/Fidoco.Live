using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Fidoco.Modules.Notifications.Email;

internal sealed record OutgoingEmail(
    string FromName,
    string FromAddress,
    string? ReplyTo,
    string To,
    string Subject,
    string Html,
    string Text);

/// <summary>Thin transport abstraction. SMTP keeps us provider-neutral (Azure Communication Services, Postmark, ...).</summary>
internal interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken);
}

internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var smtp = options.Value.Smtp;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(email.FromName, email.FromAddress));
        if (!string.IsNullOrEmpty(email.ReplyTo))
        {
            message.ReplyTo.Add(MailboxAddress.Parse(email.ReplyTo));
        }

        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Subject;
        message.Body = new BodyBuilder { HtmlBody = email.Html, TextBody = email.Text }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, cancellationToken);
        if (!string.IsNullOrEmpty(smtp.Username))
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password ?? "", cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

/// <summary>Development sender: logs the email (including links) and writes the HTML to a file.</summary>
internal sealed class FileEmailSender(IOptions<EmailOptions> options, ILogger<FileEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken)
    {
        var directory = options.Value.FileDirectory;
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(path, email.Html, cancellationToken);
        logger.LogInformation("Email to {To}: {Subject}\n{Text}\nSaved to {Path}", email.To, email.Subject, email.Text, path);
    }
}
