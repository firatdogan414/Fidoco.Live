using Fidoco.Contracts.Notifications;
using Fidoco.Contracts.Tenants;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Notifications.Email;

/// <summary>Sends emails from Fidoco on behalf of the current tenant: "&lt;Tenant&gt; via Fidoco" with the tenant as reply-to.</summary>
internal sealed class EmailDispatcher(
    ITenantContext tenantContext,
    ITenantDirectory tenants,
    EmailTemplateRenderer renderer,
    IEmailSender sender,
    IOptions<EmailOptions> options) : IEmailDispatcher
{
    public async Task SendAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var tenant = tenantContext.TenantId is { } tenantId ? await tenants.GetAsync(tenantId, cancellationToken) : null;
        var branding = tenant is null
            ? new EmailBranding("Fidoco", null, "#111827", "#e11d48")
            : new EmailBranding(tenant.Name, tenant.LogoUrl, tenant.PrimaryColor, tenant.AccentColor);

        var rendered = renderer.Render(settings.Language, request, branding);
        var fromName = tenant is null ? "Fidoco" : tenant.Name + settings.FromDisplaySuffix;
        await sender.SendAsync(
            new OutgoingEmail(fromName, settings.FromAddress, tenant?.SupportEmail, request.To, rendered.Subject, rendered.Html, rendered.Text),
            cancellationToken);
    }
}
