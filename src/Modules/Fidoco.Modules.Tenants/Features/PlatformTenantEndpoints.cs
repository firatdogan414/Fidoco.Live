using System.Text.RegularExpressions;
using Fidoco.Contracts.Identity;
using Fidoco.Modules.Tenants.Domain;
using Fidoco.Modules.Tenants.Infrastructure;
using Fidoco.Modules.Tenants.Integration;
using Fidoco.SharedKernel.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Fidoco.Modules.Tenants.Features;

/// <summary>Tenant management for the platform owner (PlatformAdmin on the platform host).</summary>
internal static partial class PlatformTenantEndpoints
{
    public sealed record DomainDto(Guid Id, string Host, bool IsPrimary);

    public sealed record TenantDto(
        Guid Id, string Name, string Slug, string? LogoUrl, string PrimaryColor, string AccentColor, string? SupportEmail, string Status,
        decimal CommissionPercent, long CommissionFixedAmountPerTicket, bool OrganizerVatRegistered, IReadOnlyList<DomainDto> Domains);

    public sealed record CreateTenantRequest(string? Name, string? Slug, string? SupportEmail);

    public sealed record UpdateTenantRequest(string? Name, string? SupportEmail, string? LogoUrl, string? PrimaryColor, string? AccentColor, bool Suspended);

    public sealed record TermsRequest(decimal CommissionPercent, long CommissionFixedAmountPerTicket, bool OrganizerVatRegistered);

    public sealed record DomainRequest(string? Host, bool IsPrimary);

    public static void Map(IEndpointRouteBuilder api)
    {
        var platform = api.MapGroup("/platform/tenants").RequireAuthorization(AuthPolicies.PlatformAdmin);
        platform.MapGet("/", List);
        platform.MapPost("/", Create);
        platform.MapGet("/{id:guid}", Get);
        platform.MapPut("/{id:guid}", Update);
        platform.MapPut("/{id:guid}/terms", UpdateTerms);
        platform.MapPost("/{id:guid}/domains", AddDomain);
        platform.MapDelete("/{id:guid}/domains/{domainId:guid}", RemoveDomain);
    }

    private static async Task<IResult> List(TenantsDbContext db, CancellationToken cancellationToken)
    {
        var tenants = await db.Tenants.AsNoTracking().Include(t => t.Domains).OrderBy(t => t.Name).ToListAsync(cancellationToken);
        return Results.Ok(tenants.Select(ToDto));
    }

    private static async Task<IResult> Get(Guid id, TenantsDbContext db, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Domains).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        return tenant is null ? ApiResults.NotFound("tenant_not_found") : Results.Ok(ToDto(tenant));
    }

    private static async Task<IResult> Create(CreateTenantRequest request, TenantsDbContext db, IOptions<PlatformOptions> platform, TimeProvider time, CancellationToken cancellationToken)
    {
        var slug = request.Slug?.Trim().ToLowerInvariant() ?? "";
        var errors = new ValidationErrors().Required("name", request.Name, maxLength: 200);
        if (!SlugPattern().IsMatch(slug))
        {
            errors.Add("slug", "invalid_slug");
        }

        if (!string.IsNullOrWhiteSpace(request.SupportEmail))
        {
            errors.Email("supportEmail", request.SupportEmail);
        }

        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var host = $"{slug}.{platform.Value.TenantDomainSuffix}";
        if (await db.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken) || await db.TenantDomains.AnyAsync(d => d.Host == host, cancellationToken))
        {
            return ApiResults.Conflict("slug_taken");
        }

        var id = Guid.CreateVersion7(time.GetUtcNow());
        var tenant = new Tenant
        {
            Id = id,
            Name = request.Name!.Trim(),
            Slug = slug,
            SupportEmail = string.IsNullOrWhiteSpace(request.SupportEmail) ? null : Emails.Normalize(request.SupportEmail),
            CreatedAt = time.GetUtcNow(),
            Domains = [new TenantDomain { Id = Guid.CreateVersion7(), TenantId = id, Host = host, IsPrimary = true }],
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/platform/tenants/{id}", ToDto(tenant));
    }

    private static async Task<IResult> Update(Guid id, UpdateTenantRequest request, TenantsDbContext db, TenantDirectory directory, CancellationToken cancellationToken)
    {
        var errors = new ValidationErrors().Required("name", request.Name, maxLength: 200).MaxLength("logoUrl", request.LogoUrl, 500);
        foreach (var (field, color) in new[] { ("primaryColor", request.PrimaryColor), ("accentColor", request.AccentColor) })
        {
            if (color is null || !ColorPattern().IsMatch(color))
            {
                errors.Add(field, "invalid_color");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SupportEmail))
        {
            errors.Email("supportEmail", request.SupportEmail);
        }

        if (errors.HasErrors)
        {
            return errors.ToResult();
        }

        var tenant = await db.Tenants.Include(t => t.Domains).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return ApiResults.NotFound("tenant_not_found");
        }

        tenant.Name = request.Name!.Trim();
        tenant.SupportEmail = string.IsNullOrWhiteSpace(request.SupportEmail) ? null : Emails.Normalize(request.SupportEmail);
        tenant.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
        tenant.PrimaryColor = request.PrimaryColor!;
        tenant.AccentColor = request.AccentColor!;
        tenant.Status = request.Suspended ? TenantStatus.Suspended : TenantStatus.Active;
        await db.SaveChangesAsync(cancellationToken);
        directory.Invalidate(tenant.Id, tenant.Domains.Select(d => d.Host));
        return Results.Ok(ToDto(tenant));
    }

    private static async Task<IResult> UpdateTerms(Guid id, TermsRequest request, TenantsDbContext db, CancellationToken cancellationToken)
    {
        if (request.CommissionPercent is < 0 or > 100 || request.CommissionFixedAmountPerTicket < 0)
        {
            return new ValidationErrors().Add("commission", "out_of_range").ToResult();
        }

        var tenant = await db.Tenants.Include(t => t.Domains).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return ApiResults.NotFound("tenant_not_found");
        }

        // Only new checkouts use the new terms; existing purchases keep the commission they were sold with.
        tenant.CommissionPercent = request.CommissionPercent;
        tenant.CommissionFixedAmountPerTicket = request.CommissionFixedAmountPerTicket;
        tenant.OrganizerVatRegistered = request.OrganizerVatRegistered;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToDto(tenant));
    }

    private static async Task<IResult> AddDomain(Guid id, DomainRequest request, TenantsDbContext db, TenantDirectory directory, CancellationToken cancellationToken)
    {
        var host = TenantDirectory.NormalizeHost(request.Host ?? "");
        if (!HostPattern().IsMatch(host))
        {
            return new ValidationErrors().Add("host", "invalid_host").ToResult();
        }

        var tenant = await db.Tenants.Include(t => t.Domains).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return ApiResults.NotFound("tenant_not_found");
        }

        if (await db.TenantDomains.AnyAsync(d => d.Host == host, cancellationToken))
        {
            return ApiResults.Conflict("domain_taken");
        }

        if (request.IsPrimary)
        {
            tenant.Domains.ForEach(d => d.IsPrimary = false);
        }

        tenant.Domains.Add(new TenantDomain { Id = Guid.CreateVersion7(), TenantId = id, Host = host, IsPrimary = request.IsPrimary || tenant.Domains.Count == 0 });
        await db.SaveChangesAsync(cancellationToken);
        directory.Invalidate(tenant.Id, tenant.Domains.Select(d => d.Host));
        return Results.Ok(ToDto(tenant));
    }

    private static async Task<IResult> RemoveDomain(Guid id, Guid domainId, TenantsDbContext db, TenantDirectory directory, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.Include(t => t.Domains).SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        var domain = tenant?.Domains.SingleOrDefault(d => d.Id == domainId);
        if (tenant is null || domain is null)
        {
            return ApiResults.NotFound("domain_not_found");
        }

        if (tenant.Domains.Count == 1)
        {
            return ApiResults.Conflict("last_domain");
        }

        var hosts = tenant.Domains.Select(d => d.Host).ToList();
        db.TenantDomains.Remove(domain);
        tenant.Domains.Remove(domain);
        if (domain.IsPrimary)
        {
            tenant.Domains[0].IsPrimary = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        directory.Invalidate(tenant.Id, hosts);
        return Results.Ok(ToDto(tenant));
    }

    private static TenantDto ToDto(Tenant t) => new(
        t.Id, t.Name, t.Slug, t.LogoUrl, t.PrimaryColor, t.AccentColor, t.SupportEmail, t.Status.ToString().ToLowerInvariant(),
        t.CommissionPercent, t.CommissionFixedAmountPerTicket, t.OrganizerVatRegistered,
        t.Domains.OrderByDescending(d => d.IsPrimary).ThenBy(d => d.Host).Select(d => new DomainDto(d.Id, d.Host, d.IsPrimary)).ToList());

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorPattern();

    [GeneratedRegex("^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)+$")]
    private static partial Regex HostPattern();
}
