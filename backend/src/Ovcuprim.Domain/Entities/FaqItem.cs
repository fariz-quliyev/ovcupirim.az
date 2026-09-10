using Ovcuprim.Domain.Common;

namespace Ovcuprim.Domain.Entities;

public class FaqItem : AuditableEntity
{
    public int Id { get; set; }

    public int FaqCategoryId { get; set; }

    public FaqCategory FaqCategory { get; set; } = null!;

    public string QuestionAz { get; set; } = null!;

    public string AnswerAz { get; set; } = null!;

    public string? QuestionRu { get; set; }

    public string? AnswerRu { get; set; }

    public int SortOrder { get; set; }
}
