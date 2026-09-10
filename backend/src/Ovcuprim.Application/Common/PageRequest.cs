namespace Ovcuprim.Application.Common;

/// <summary>Pagination input. The page size is capped so a client cannot ask for the whole table.</summary>
public sealed class PageRequest
{
    public const int MaxPageSize = 60;
    public const int DefaultPageSize = 24;

    private int _page = 1;
    private int _pageSize = DefaultPageSize;

    public int Page
    {
        get => _page;
        set => _page = value < 1 ? 1 : value;
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value
        };
    }

    public int Skip => (Page - 1) * PageSize;
}
