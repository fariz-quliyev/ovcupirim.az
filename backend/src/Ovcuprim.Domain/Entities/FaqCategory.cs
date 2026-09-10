using Ovcuprim.Domain.Common;

namespace Ovcuprim.Domain.Entities;

public class FaqCategory : AuditableEntity
{
    public int Id { get; set; }

    public string NameAz { get; set; } = null!;

    public string? NameRu { get; set; }

    public string Slug { get; set; } = null!;

    public int SortOrder { get; set; }

    public ICollection<FaqItem> Items { get; set; } = [];
}
