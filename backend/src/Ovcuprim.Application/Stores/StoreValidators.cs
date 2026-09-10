using FluentValidation;
using Ovcuprim.Application.Common;

namespace Ovcuprim.Application.Stores;

/// <summary>
/// Shape checks that run before <see cref="StoreService"/> is reached. The limits mirror the
/// database columns exactly, so an oversized name is refused with a field message rather than
/// reaching PostgreSQL and surfacing as an unhandled write error.
/// </summary>
internal static class StoreRules
{
    internal const int NameMinLength = 2;
    internal const int NameMaxLength = 100;
    internal const int DescriptionMaxLength = 2000;
    internal const int AddressMaxLength = 200;

    /// <summary>The column is varchar(20); a normalised E.164 number is 13.</summary>
    internal const int PhoneMaxLength = 20;

    internal static IRuleBuilderOptions<T, string> Name<T>(IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().WithMessage("Mağazanın adı tələb olunur.")
            .MinimumLength(NameMinLength).WithMessage($"Ad ən azı {NameMinLength} simvol olmalıdır.")
            .MaximumLength(NameMaxLength).WithMessage($"Ad {NameMaxLength} simvoldan uzun ola bilməz.");

    internal static IRuleBuilderOptions<T, string?> Description<T>(IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(DescriptionMaxLength)
            .WithMessage($"Təsvir {DescriptionMaxLength} simvoldan uzun ola bilməz.");

    internal static IRuleBuilderOptions<T, string?> Address<T>(IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(AddressMaxLength)
            .WithMessage($"Ünvan {AddressMaxLength} simvoldan uzun ola bilməz.");

    /// <summary>
    /// Length first, then shape. The length check matters on its own because a caller can send a
    /// long string of digits that normalisation would reject anyway — this way the message names
    /// the actual problem.
    /// </summary>
    internal static IRuleBuilderOptions<T, string?> Phone<T>(IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(PhoneMaxLength)
            .WithMessage($"Nömrə {PhoneMaxLength} simvoldan uzun ola bilməz.")
            .Must(value => value is null || PhoneNumber.IsValid(value))
            .WithMessage("Telefon nömrəsi düzgün deyil.");
}

public sealed class ApplyForStoreRequestValidator : AbstractValidator<ApplyForStoreRequest>
{
    public ApplyForStoreRequestValidator()
    {
        StoreRules.Name(RuleFor(x => x.Name));
        StoreRules.Description(RuleFor(x => x.Description));
        StoreRules.Address(RuleFor(x => x.Address));
        StoreRules.Phone(RuleFor(x => x.Phone));
    }
}

public sealed class UpdateStoreRequestValidator : AbstractValidator<UpdateStoreRequest>
{
    public UpdateStoreRequestValidator()
    {
        StoreRules.Name(RuleFor(x => x.Name));
        StoreRules.Description(RuleFor(x => x.Description));
        StoreRules.Address(RuleFor(x => x.Address));
        StoreRules.Phone(RuleFor(x => x.Phone));
    }
}

public sealed class RejectStoreRequestValidator : AbstractValidator<RejectStoreRequest>
{
    public RejectStoreRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Rədd səbəbi tələb olunur.")
            .MaximumLength(500).WithMessage("Səbəb 500 simvoldan uzun ola bilməz.");
}

public sealed class SuspendStoreRequestValidator : AbstractValidator<SuspendStoreRequest>
{
    public SuspendStoreRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Dayandırma səbəbi tələb olunur.")
            .MaximumLength(500).WithMessage("Səbəb 500 simvoldan uzun ola bilməz.");
}
