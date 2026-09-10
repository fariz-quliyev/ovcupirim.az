using FluentValidation;
using Ovcuprim.Application.Common;

namespace Ovcuprim.Application.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Telefon nömrəsi tələb olunur.")
            .Must(PhoneNumber.IsValid).WithMessage("Telefon nömrəsi düzgün deyil. Nümunə: +994501234567");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Ad və soyad tələb olunur.")
            .MinimumLength(2).WithMessage("Ad ən azı 2 simvol olmalıdır.")
            .MaximumLength(100).WithMessage("Ad 100 simvoldan uzun ola bilməz.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Telefon nömrəsi tələb olunur.")
            .Must(PhoneNumber.IsValid).WithMessage("Telefon nömrəsi düzgün deyil. Nümunə: +994501234567");
    }
}

public sealed class ResendOtpRequestValidator : AbstractValidator<ResendOtpRequest>
{
    public ResendOtpRequestValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Telefon nömrəsi tələb olunur.")
            .Must(PhoneNumber.IsValid).WithMessage("Telefon nömrəsi düzgün deyil.");

        RuleFor(x => x.Purpose).IsInEnum();
    }
}

public sealed class VerifyOtpRequestValidator : AbstractValidator<VerifyOtpRequest>
{
    public VerifyOtpRequestValidator()
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Telefon nömrəsi tələb olunur.")
            .Must(PhoneNumber.IsValid).WithMessage("Telefon nömrəsi düzgün deyil.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Kod tələb olunur.")
            .Matches("^[0-9]{4,8}$").WithMessage("Kod yalnız rəqəmlərdən ibarət olmalıdır.");

        RuleFor(x => x.Purpose).IsInEnum();
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Ad və soyad tələb olunur.")
            .MinimumLength(2)
            .MaximumLength(100);

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("E-mail ünvanı düzgün deyil.")
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
