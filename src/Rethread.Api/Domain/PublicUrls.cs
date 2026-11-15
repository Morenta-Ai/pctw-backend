namespace Rethread.Api.Domain;

/// <summary>Absolute links to public files (catalogue photos, avatars), on <c>Storage:PublicBaseUrl</c> or this host.</summary>
public sealed class PublicUrls(IHttpContextAccessor accessor, IConfiguration config)
{
    public string BaseUrl
    {
        get
        {
            if (config["Storage:PublicBaseUrl"] is { Length: > 0 } configured) return configured.TrimEnd('/');
            var request = accessor.HttpContext?.Request;
            return request is null ? "" : $"{request.Scheme}://{request.Host}";
        }
    }

    public string? File(string? fileId) => fileId is null ? null : $"{BaseUrl}/files/{fileId}";
}
