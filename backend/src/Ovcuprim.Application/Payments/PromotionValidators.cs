using FluentValidation;

namespace Ovcuprim.Application.Payments;

internal static class PromotionPackageRules
{
    internal const int CodeMaxLength = 40;
    internal const int NameMaxLength = 100;
    internal const int DescriptionMaxLength = 500;
    internal const int MaxDurationDays = 365;

    internal static IRuleBuilderOptions<T, string> Name<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Ad tələb olunur.")
            .MaximumLength(NameMaxLength).WithMessage($"Ad {NameMaxLength} simvoldan uzun ola bilməz.");

    internal static IRuleBuilderOptions<T, string?> Description<T>(IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(DescriptionMaxLength).WithMessage($"Təsvir {DescriptionMaxLength} simvoldan uzun ola bilməz.");

    internal static IRuleBuilderOptions<T, int> Duration<T>(IRuleBuilder<T, int> rule) =>
        rule.InclusiveBetween(1, MaxDurationDays).WithMessage($"Müddət 1–{MaxDurationDays} gün arasında olmalıdır.");

    internal static IRuleBuilderOptions<T, decimal> Price<T>(IRuleBuilder<T, decimal> rule) =>
        rule.GreaterThanOrEqualTo(0).WithMessage("Qiymət mənfi ola bilməz.");
}

public sealed class CreatePromotionOrderRequestValidator : AbstractValidator<CreatePromotionOrderRequest>
{
    public CreatePromotionOrderRequestValidator() =>
        RuleFor(x => x.PackageId).GreaterThan(0).WithMessage("Paket seçilməyib.");
}

public sealed class CreatePromotionPackageRequestValidator : AbstractValidator<CreatePromotionPackageRequest>
{
    public CreatePromotionPackageRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithMessage("Kod tələb olunur.")
            .MaximumLength(PromotionPackageRules.CodeMaxLength)
            .WithMessage($"Kod {PromotionPackageRules.CodeMaxLength} simvoldan uzun ola bilməz.");
        PromotionPackageRules.Name(RuleFor(x => x.NameAz));
        PromotionPackageRules.Description(RuleFor(x => x.DescriptionAz));
        PromotionPackageRules.Duration(RuleFor(x => x.DurationDays));
        PromotionPackageRules.Price(RuleFor(x => x.PriceAzn));
    }
}

public sealed class UpdatePromotionPackageRequestValidator : AbstractValidator<UpdatePromotionPackageRequest>
{
    public UpdatePromotionPackageRequestValidator()
    {
        PromotionPackageRules.Name(RuleFor(x => x.NameAz));
        PromotionPackageRules.Description(RuleFor(x => x.DescriptionAz));
        PromotionPackageRules.Duration(RuleFor(x => x.DurationDays));
        PromotionPackageRules.Price(RuleFor(x => x.PriceAzn));
    }
}

public sealed class RefundPaymentOrderRequestValidator : AbstractValidator<RefundPaymentOrderRequest>
{
    public RefundPaymentOrderRequestValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Geri qaytarma səbəbi tələb olunur.")
            .MaximumLength(500).WithMessage("Səbəb 500 simvoldan uzun ola bilməz.");

        RuleFor(x => x.Amount!.Value).GreaterThan(0).WithMessage("Məbləğ müsbət olmalıdır.")
            .When(x => x.Amount.HasValue);
    }
}
