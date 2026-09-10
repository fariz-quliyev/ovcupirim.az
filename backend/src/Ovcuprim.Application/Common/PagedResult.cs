namespace Ovcuprim.Application.Common;

/// <summary>The envelope every list endpoint returns.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);

    public static PagedResult<T> Empty(int page, int pageSize) => new([], page, pageSize, 0);
}
