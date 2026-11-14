using Rethread.Api.Common;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>Where uploaded files live. Local disk for now; cloud storage can slot in behind this.</summary>
public interface IFileStore
{
    /// <summary>Saves the bytes and returns the record to add to the context.</summary>
    Task<StoredFile> SaveAsync(string prefix, Stream content, string contentType, string uploadedBy,
        CancellationToken ct);

    Stream OpenRead(StoredFile file);
}

public sealed class LocalFileStore(IConfiguration config, IHostEnvironment environment, TimeProvider time)
    : IFileStore
{
    private string Root => Path.GetFullPath(config["Storage:Root"] is { Length: > 0 } root
        ? root
        : Path.Combine(environment.ContentRootPath, "storage"));

    public async Task<StoredFile> SaveAsync(string prefix, Stream content, string contentType, string uploadedBy,
        CancellationToken ct)
    {
        Directory.CreateDirectory(Root);
        var id = Ids.New(prefix);
        var name = id + Extension(contentType);
        await using (var file = File.Create(Path.Combine(Root, name)))
        {
            await content.CopyToAsync(file, ct);
        }

        return new StoredFile
        {
            Id = id,
            ContentType = contentType,
            SizeBytes = new FileInfo(Path.Combine(Root, name)).Length,
            Path = name,
            UploadedBy = uploadedBy,
            CreatedAt = time.UtcNow(),
        };
    }

    public Stream OpenRead(StoredFile file) => File.OpenRead(Path.Combine(Root, file.Path));

    private static string Extension(string contentType) => contentType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => "",
    };
}

/// <summary>Checks an upload's real type from its first bytes, not the name or header the client sent.</summary>
public static class ImageSniffer
{
    public static async Task<string?> DetectAsync(IFormFile file, CancellationToken ct)
    {
        var head = new byte[12];
        await using var stream = file.OpenReadStream();
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        if (read >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF) return "image/jpeg";
        if (read >= 8 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return "image/png";
        if (read >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' &&
            head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P') return "image/webp";
        return null;
    }
}
