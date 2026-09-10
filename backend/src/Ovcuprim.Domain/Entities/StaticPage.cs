using Ovcuprim.Domain.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Domain.Entities;

/// <summary>
/// Editable informational page. <see cref="StaticPageType.Info"/> covers terms, privacy, safe
/// shopping and age rules; <see cref="StaticPageType.Guide"/> covers outdoor guide articles.
/// </summary>
public class StaticPage : AuditableEntity
{
    public int Id { get; set; }

    public string Slug { get; set; } = null!;

    public StaticPageType PageType { get; set; } = StaticPageType.Info;

    public string TitleAz { get; set; } = null!;

    public string BodyAz { get; set; } = null!;

    public string? TitleRu { get; set; }

    public string? BodyRu { get; set; }

    public string? MetaDescriptionAz { get; set; }

    public string? MetaDescriptionRu { get; set; }

    public string? ExcerptAz { get; set; }

    public string? ExcerptRu { get; set; }

    public string? CoverImageKey { get; set; }

    public bool IsPublished { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    public int SortOrder { get; set; }
}
