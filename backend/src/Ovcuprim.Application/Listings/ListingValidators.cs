using FluentValidation;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.Listings;

/// <summary>
/// Shape checks that run before a service is reached. The limits mirror the database columns, so
/// an oversized title is refused with a field message instead of a truncation error.
/// </summary>
internal static class ListingRules
{
    internal const int TitleMaxLength = 70;
    internal const int DescriptionMaxLength = 3000;
    internal const int BrandMaxLength = 60;

    /// <summary>numeric(12,2) with a non-negative check constraint.</summary>
    internal const decimal PriceMax = 9_999_999_999.99m;

    internal static IRuleBuilderOptions<T, string> Title<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Başlıq tələb olunur.")
            .MaximumLength(TitleMaxLength).WithMessage($"Başlıq {TitleMaxLength} simvoldan uzun ola bilməz.");

    internal static IRuleBuilderOptions<T, string> Description<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Təsvir tələb olunur.")
            .MaximumLength(DescriptionMaxLength).WithMessage($"Təsvir {DescriptionMaxLength} simvoldan uzun ola bilməz.");

    internal static IRuleBuilderOptions<T, decimal?> Price<T>(IRuleBuilder<T, decimal?> rule) =>
        rule.Must(p => p is null || p >= 0).WithMessage("Qiymət mənfi ola bilməz.")
            .Must(p => p is null || p <= PriceMax).WithMessage("Qiymət çox böyükdür.");

    internal static IRuleBuilderOptions<T, string> Condition<T>(IRuleBuilder<T, string> rule) =>
        rule.Must(v => Enum.TryParse<ListingCondition>(v, ignoreCase: true, out _))
            .WithMessage("Vəziyyət düzgün deyil.");

    internal static IRuleBuilderOptions<T, string?> Brand<T>(IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(BrandMaxLength).WithMessage($"Marka {BrandMaxLength} simvoldan uzun ola bilməz.");
}

public sealed class CreateListingRequestValidator : AbstractValidator<CreateListingRequest>
{
    public CreateListingRequestValidator()
    {
        RuleFor(x => x.CategorySlug).NotEmpty().WithMessage("Kateqoriya seçin.");
        RuleFor(x => x.RegionSlug).NotEmpty().WithMessage("Şəhər / rayon seçin.");
        ListingRules.Title(RuleFor(x => x.Title));
        ListingRules.Description(RuleFor(x => x.Description));
        ListingRules.Price(RuleFor(x => x.Price));
        ListingRules.Condition(RuleFor(x => x.Condition));
        ListingRules.Brand(RuleFor(x => x.Brand));
        RuleFor(x => x.ContactPhone).NotEmpty().WithMessage("Əlaqə nömrəsi tələb olunur.");
    }
}

public sealed class UpdateListingRequestValidator : AbstractValidator<UpdateListingRequest>
{
    public UpdateListingRequestValidator()
    {
        RuleFor(x => x.RegionSlug).NotEmpty().WithMessage("Şəhər / rayon seçin.");
        ListingRules.Title(RuleFor(x => x.Title));
        ListingRules.Description(RuleFor(x => x.Description));
        ListingRules.Price(RuleFor(x => x.Price));
        ListingRules.Condition(RuleFor(x => x.Condition));
        ListingRules.Brand(RuleFor(x => x.Brand));
        RuleFor(x => x.ContactPhone).NotEmpty().WithMessage("Əlaqə nömrəsi tələb olunur.");
    }
}

public sealed class RejectListingRequestValidator : AbstractValidator<RejectListingRequest>
{
    public RejectListingRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Rədd səbəbi tələb olunur.")
            .MaximumLength(500).WithMessage("Səbəb 500 simvoldan uzun ola bilməz.");
}

public sealed class BlockListingRequestValidator : AbstractValidator<BlockListingRequest>
{
    public BlockListingRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Bloklama səbəbi tələb olunur.")
            .MaximumLength(500).WithMessage("Səbəb 500 simvoldan uzun ola bilməz.");
}

public sealed class ReorderMediaRequestValidator : AbstractValidator<ReorderMediaRequest>
{
    public ReorderMediaRequestValidator() =>
        RuleFor(x => x.MediaIds).NotEmpty().WithMessage("Şəkil sıralaması boş ola bilməz.");
}
