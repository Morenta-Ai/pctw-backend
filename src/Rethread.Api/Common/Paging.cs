using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Rethread.Api.Common;

/// <summary>A short list that is never paged: <c>{ "items": [...] }</c>.</summary>
public sealed record ItemList<T>(IReadOnlyList<T> Items);

/// <summary>The apps' cursor pages: <c>{ "items": [...], "nextCursor": "..." }</c>.</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>The portal's table pages: <c>{ "items", "page", "pageSize", "totalCount" }</c>.</summary>
public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Rows that can be paged newest first by <c>(createdAt, id)</c>.</summary>
public interface ICursorRow
{
    string Id { get; }
    DateTime CreatedAt { get; }
}

public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultSize = 25;
    public const int MaxSize = 100;

    public int Skip => (Page - 1) * PageSize;

    public static PageRequest From(int? page, int? pageSize) =>
        new(Math.Max(1, page ?? 1), Math.Clamp(pageSize ?? DefaultSize, 1, MaxSize));
}

public static class Cursor
{
    public const string InvalidCursor = "INVALID_CURSOR";

    public static string Encode(DateTime createdAt, string id) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            $"{UtcDateTimeConverter.ToUtc(createdAt).Ticks.ToString(CultureInfo.InvariantCulture)}|{id}"));

    public static (DateTime CreatedAt, string Id)? Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return null;
        try
        {
            var text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
            var bar = text.IndexOf('|', StringComparison.Ordinal);
            var ticks = long.Parse(text.AsSpan(0, bar), CultureInfo.InvariantCulture);
            return (new DateTime(ticks, DateTimeKind.Utc), text[(bar + 1)..]);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            throw ApiException.BadRequest(InvalidCursor, "The cursor isn't valid. Start from the first page.");
        }
    }
}

public static class PagingExtensions
{
    /// <summary>Newest first, keyset-paged by <c>(createdAt, id)</c>.</summary>
    public static async Task<CursorPage<TOut>> ToCursorPageAsync<T, TOut>(this IQueryable<T> query, string? cursor,
        int limit, Func<T, TOut> map, CancellationToken ct) where T : class, ICursorRow
    {
        var after = Cursor.Decode(cursor);
        if (after is { } c)
        {
            var (at, id) = c;
            query = query.Where(x => x.CreatedAt < at || (x.CreatedAt == at && string.Compare(x.Id, id) < 0));
        }

        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Take(limit + 1).ToListAsync(ct);
        var next = rows.Count > limit ? Cursor.Encode(rows[limit - 1].CreatedAt, rows[limit - 1].Id) : null;
        return new CursorPage<TOut>(rows.Take(limit).Select(map).ToList(), next);
    }

    public static async Task<PagedList<TOut>> ToPageAsync<T, TOut>(this IQueryable<T> query, PageRequest page,
        Func<T, TOut> map, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var rows = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedList<TOut>(rows.Select(map).ToList(), page.Page, page.PageSize, total);
    }
}
