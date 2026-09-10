using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Content;

public sealed record StaticPageDto(
    string Slug,
    string PageType,
    string TitleAz,
    string BodyAz,
    string? MetaDescriptionAz,
    string? ExcerptAz,
    string? CoverImageKey,
    DateTimeOffset? PublishedAt);

public sealed record StaticPageSummaryDto(
    string Slug,
    string TitleAz,
    string? ExcerptAz,
    string? CoverImageKey,
    DateTimeOffset? PublishedAt);

public sealed record FaqItemDto(string QuestionAz, string AnswerAz);

public sealed record FaqCategoryDto(string Slug, string NameAz, IReadOnlyList<FaqItemDto> Items);

public interface IContentService
{
    Task<Result<StaticPageDto>> GetPageAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StaticPageSummaryDto>> ListPagesAsync(StaticPageType type, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FaqCategoryDto>> GetFaqAsync(CancellationToken cancellationToken = default);
}

public sealed class ContentService(IAppDbContext db, ITaxonomyCache cache) : IContentService
{
    public async Task<Result<StaticPageDto>> GetPageAsync(string slug, CancellationToken cancellationToken = default)
    {
        var page = await db.StaticPages.AsNoTracking()
            .Where(p => p.Slug == slug && p.IsPublished)
            .Select(p => new StaticPageDto(
                p.Slug,
                p.PageType == StaticPageType.Guide ? "Guide" : "Info",
                p.TitleAz,
                p.BodyAz,
                p.MetaDescriptionAz,
                p.ExcerptAz,
                p.CoverImageKey,
                p.PublishedAt))
            .FirstOrDefaultAsync(cancellationToken);

        // An unpublished page is indistinguishable from a missing one.
        return page is null
            ? Result<StaticPageDto>.NotFound("Səhifə tapılmadı.")
            : Result<StaticPageDto>.Success(page);
    }

    public async Task<IReadOnlyList<StaticPageSummaryDto>> ListPagesAsync(
        StaticPageType type,
        CancellationToken cancellationToken = default) =>
        await db.StaticPages.AsNoTracking()
            .Where(p => p.PageType == type && p.IsPublished)
            .OrderBy(p => p.SortOrder)
            .Select(p => new StaticPageSummaryDto(p.Slug, p.TitleAz, p.ExcerptAz, p.CoverImageKey, p.PublishedAt))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FaqCategoryDto>> GetFaqAsync(CancellationToken cancellationToken = default) =>
        await cache.GetOrCreateAsync(
            "content:faq",
            async token =>
            {
                var categories = await db.FaqCategories.AsNoTracking()
                    .OrderBy(c => c.SortOrder)
                    .Select(c => new FaqCategoryDto(
                        c.Slug,
                        c.NameAz,
                        c.Items.OrderBy(i => i.SortOrder)
                            .Select(i => new FaqItemDto(i.QuestionAz, i.AnswerAz))
                            .ToList()))
                    .ToListAsync(token);

                return (IReadOnlyList<FaqCategoryDto>)categories;
            },
            cancellationToken);
}
