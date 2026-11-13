using Microsoft.EntityFrameworkCore;

namespace Servisyo.Web.ViewModels;

/// One page of a longer list, worked out by the DATABASE (COUNT, then OFFSET / FETCH): only the rows of this page are read.
public sealed class PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    public IReadOnlyList<T> Items { get; }
    public int Page { get; }                 // 1, 2, 3 ... (always a page that exists)
    public int PageSize { get; }
    public int TotalCount { get; }           // every matching row, not only this page

    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < PageCount;

    /// The same page with each row turned into something else (e.g. a row for the view).
    public PagedResult<TOut> Map<TOut>(Func<T, TOut> map) => new(Items.Select(map).ToList(), Page, PageSize, TotalCount);
}

public static class PagedResultExtensions
{
    /// Counts the matching rows, keeps the page number inside 1..last page, then reads only that page.
    /// The query must already be ordered (OrderBy...), otherwise the pages are not stable.
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(this IQueryable<T> ordered, int page, int pageSize, CancellationToken ct = default)
    {
        if (pageSize < 1) throw new ArgumentOutOfRangeException(nameof(pageSize));
        var total = await ordered.CountAsync(ct);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var current = Math.Clamp(page, 1, pageCount);
        var items = await ordered.Skip((current - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, current, pageSize, total);
    }
}
