using Microsoft.Extensions.Options;

namespace Fidoco.SharedKernel.Files;

public sealed class FileStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Folder for uploaded files. On Azure App Service use a path under /home (persistent and shared
    /// between instances), e.g. /home/data/files.
    /// </summary>
    public string LocalPath { get; set; } = "App_Data/files";

    /// <summary>URL prefix the files are served from.</summary>
    public string PublicPath { get; set; } = "/files";
}

/// <summary>Stores uploaded files (logos, event images). Local disk now; Azure Blob Storage later (TODO).</summary>
public interface IFileStorage
{
    /// <summary>Saves the content and returns its public URL path.</summary>
    Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken cancellationToken);
}

public sealed class LocalFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    public string RootPath => Path.GetFullPath(options.Value.LocalPath);

    public async Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken cancellationToken)
    {
        if (folder.Contains("..", StringComparison.Ordinal) || fileName.Contains("..", StringComparison.Ordinal)
            || fileName.IndexOfAny(['/', '\\']) >= 0 || folder.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new ArgumentException("Invalid file path.");
        }

        var directory = Path.Combine(RootPath, folder);
        Directory.CreateDirectory(directory);
        await using (var file = File.Create(Path.Combine(directory, fileName)))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return $"{options.Value.PublicPath.TrimEnd('/')}/{folder}/{fileName}";
    }
}

/// <summary>Recognizes the allowed image formats by their first bytes (never trust the file name).</summary>
public static class ImageFormats
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public static string? DetectExtension(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return ".png";
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
        {
            return ".webp";
        }

        return null;
    }
}
