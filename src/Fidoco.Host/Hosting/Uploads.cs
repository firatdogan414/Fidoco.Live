using Fidoco.Contracts.Identity;
using Fidoco.SharedKernel.Files;
using Fidoco.SharedKernel.Http;
using Fidoco.SharedKernel.Tenancy;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace Fidoco.Host.Hosting;

/// <summary>Image uploads for organizers (logo, event images) and serving the stored files.</summary>
internal static class Uploads
{
    public sealed record UploadResponse(string Url);

    public static IServiceCollection AddFidocoFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FileStorageOptions>().Bind(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<LocalFileStorage>();
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalFileStorage>());
        return services;
    }

    public static void UseUploadedFiles(this WebApplication app)
    {
        var storage = app.Services.GetRequiredService<LocalFileStorage>();
        Directory.CreateDirectory(storage.RootPath);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(storage.RootPath),
            RequestPath = app.Services.GetRequiredService<IOptions<FileStorageOptions>>().Value.PublicPath,
            // File names are random and never reused, so they can be cached for a long time.
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable",
        });
    }

    public static void MapUploads(this IEndpointRouteBuilder api) =>
        api.MapPost("/admin/uploads", Upload)
            .RequireAuthorization(AuthPolicies.TenantAdmin)
            .DisableAntiforgery(); // CSRF is covered by SameSite cookies + the required X-Fidoco-Client header.

    private static async Task<IResult> Upload(IFormFile? file, IFileStorage storage, ITenantContext tenant, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return new ValidationErrors().Add("file", "required").ToResult();
        }

        if (file.Length > ImageFormats.MaxBytes)
        {
            return new ValidationErrors().Add("file", "file_too_large").ToResult();
        }

        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        var extension = ImageFormats.DetectExtension(header.AsSpan(0, read));
        if (extension is null)
        {
            return new ValidationErrors().Add("file", "unsupported_image").ToResult();
        }

        stream.Position = 0;
        var url = await storage.SaveAsync(tenant.RequiredTenantId.ToString("N"), $"{Guid.CreateVersion7():N}{extension}", stream, cancellationToken);
        return Results.Ok(new UploadResponse(url));
    }
}
