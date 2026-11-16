using Microsoft.EntityFrameworkCore;
using Rethread.Api.Common;
using Rethread.Api.Data;
using Rethread.Api.Data.Entities;

namespace Rethread.Api.Domain;

/// <summary>Where uploaded files live. Cloud storage can slot in behind this.</summary>
public interface IFileStore
{
    /// <summary>Reads the bytes into a record (with its content) for the caller to add to the context.</summary>
    Task<StoredFile> SaveAsync(string prefix, Stream content, string contentType, string uploadedBy,
        CancellationToken ct);

    /// <summary>The file's bytes, or null if they're missing.</summary>
    Task<Stream?> OpenReadAsync(StoredFile file, CancellationToken ct);
}

/// <summary>
/// Keeps uploads in Postgres. Every instance sees every file, which hosts with a throwaway disk (Vercel)
/// need, and uploads are small (photos up to 5 MB).
/// </summary>
public sealed class DatabaseFileStore(AppDbContext db, TimeProvider time) : IFileStore
{
    public async Task<StoredFile> SaveAsync(string prefix, Stream content, string contentType, string uploadedBy,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var id = Ids.New(prefix);
        return new StoredFile
        {
            Id = id,
            ContentType = contentType,
            SizeBytes = buffer.Length,
            UploadedBy = uploadedBy,
            CreatedAt = time.UtcNow(),
            Content = new StoredFileContent { FileId = id, Bytes = buffer.ToArray() },
        };
    }

    public async Task<Stream?> OpenReadAsync(StoredFile file, CancellationToken ct)
    {
        var bytes = await db.Set<StoredFileContent>().AsNoTracking()
            .Where(c => c.FileId == file.Id).Select(c => c.Bytes).SingleOrDefaultAsync(ct);
        return bytes is null ? null : new MemoryStream(bytes, writable: false);
    }
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
