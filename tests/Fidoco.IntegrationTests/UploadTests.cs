using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Fidoco.IntegrationTests.Infrastructure;

namespace Fidoco.IntegrationTests;

public sealed class UploadTests(FidocoApiFactory factory) : IClassFixture<FidocoApiFactory>
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

    private sealed record Upload(string Url);

    private sealed record Branding(string Name, string? LogoUrl, string PrimaryColor, string AccentColor);

    private sealed record TenantResponse(string Kind, Branding Tenant);

    [Fact]
    public async Task Admin_uploads_an_image_that_is_then_served()
    {
        var tenant = await factory.CreateTenantAsync();
        var admin = await factory.AdminClientAsync(tenant);

        var response = await admin.PostAsync("/api/v1/admin/uploads", Form(Png, "logo.png")).EnsureSuccessAsync();
        var upload = (await response.Content.ReadFromJsonAsync<Upload>())!;
        var served = await factory.ClientFor(tenant.Host).GetAsync(upload.Url);

        Assert.Matches("^/files/[0-9a-f]{32}/[0-9a-f]{32}\\.png$", upload.Url);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Non_images_are_rejected_whatever_their_name()
    {
        var tenant = await factory.CreateTenantAsync();
        var admin = await factory.AdminClientAsync(tenant);

        var response = await admin.PostAsync("/api/v1/admin/uploads", Form("<script>alert(1)</script>"u8.ToArray(), "evil.png"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("unsupported_image", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Upload_requires_an_admin()
    {
        var tenant = await factory.CreateTenantAsync();

        var response = await factory.ClientFor(tenant.Host).PostAsync("/api/v1/admin/uploads", Form(Png, "logo.png"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Admin_updates_branding_and_viewers_see_it_immediately()
    {
        var tenant = await factory.CreateTenantAsync();
        var admin = await factory.AdminClientAsync(tenant);
        var viewer = factory.ClientFor(tenant.Host);
        await viewer.GetAsync("/api/v1/tenant"); // warm the cache

        await admin.PutAsJsonAsync("/api/v1/admin/branding", new { logoUrl = "/files/x/logo.png", primaryColor = "#000000", accentColor = "#ff0000", supportEmail = "hej@example.se" }).EnsureSuccessAsync();
        var invalid = await admin.PutAsJsonAsync("/api/v1/admin/branding", new { primaryColor = "red", accentColor = "#ff0000" });
        var branding = await viewer.GetFromJsonAsync<TenantResponse>("/api/v1/tenant");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(("/files/x/logo.png", "#000000", "#ff0000"), (branding!.Tenant.LogoUrl, branding.Tenant.PrimaryColor, branding.Tenant.AccentColor));
    }

    private static MultipartFormDataContent Form(byte[] bytes, string name)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { content, "file", name } };
    }
}
