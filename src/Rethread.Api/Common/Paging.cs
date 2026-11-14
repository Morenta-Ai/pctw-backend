using System.Buffers.Text;
using System.Globalization;
using System.Linq.Expressions;
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
    public static Task<CursorPage<TOut>> ToCursorPageAsync<T, TOut>(this IQueryable<T> query, string? cursor,
        int limit, Func<T, TOut> map, CancellationToken ct) where T : class, ICursorRow =>
        query.ToCursorPageAsync(cursor, limit, x => x.CreatedAt, x => x.Id, map, ct);

    /// <summary>Newest first, keyset-paged by a timestamp column and the ID.</summary>
    public static async Task<CursorPage<TOut>> ToCursorPageAsync<T, TOut>(this IQueryable<T> query, string? cursor,
        int limit, Expression<Func<T, DateTime>> timestamp, Expression<Func<T, string>> id, Func<T, TOut> map,
        CancellationToken ct) where T : class
    {
        if (Cursor.Decode(cursor) is { } after)
        {
            // timestamp < at || (timestamp == at && id < lastId), built over the caller's columns.
            var row = Expression.Parameter(typeof(T), "row");
            var at = Replace(timestamp, row);
            var key = Replace(id, row);
            var atValue = Expression.Constant(after.CreatedAt);
            var compare = Expression.Call(typeof(string).GetMethod(nameof(string.Compare), [typeof(string), typeof(string)])!,
                key, Expression.Constant(after.Id));
            var before = Expression.OrElse(
                Expression.LessThan(at, atValue),
                Expression.AndAlso(Expression.Equal(at, atValue), Expression.LessThan(compare, Expression.Constant(0))));
            query = query.Where(Expression.Lambda<Func<T, bool>>(before, row));
        }

        var rows = await query.OrderByDescending(timestamp).ThenByDescending(id).Take(limit + 1).ToListAsync(ct);
        var getAt = timestamp.Compile();
        var getId = id.Compile();
        var next = rows.Count > limit ? Cursor.Encode(getAt(rows[limit - 1]), getId(rows[limit - 1])) : null;
        return new CursorPage<TOut>(rows.Take(limit).Select(map).ToList(), next);
    }

    private static Expression Replace<T, TValue>(Expression<Func<T, TValue>> selector, ParameterExpression row) =>
        new ParameterSwap(selector.Parameters[0], row).Visit(selector.Body);

    private sealed class ParameterSwap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : node;
    }

    public static async Task<PagedList<TOut>> ToPageAsync<T, TOut>(this IQueryable<T> query, PageRequest page,
        Func<T, TOut> map, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var rows = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedList<TOut>(rows.Select(map).ToList(), page.Page, page.PageSize, total);
    }
}
