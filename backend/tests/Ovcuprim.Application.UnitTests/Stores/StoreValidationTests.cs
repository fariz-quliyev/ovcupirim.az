using FluentValidation;
using Ovcuprim.Application.Stores;

namespace Ovcuprim.Application.UnitTests.Stores;

/// <summary>
/// The store request validators. These matter more than shape checks usually do: without them an
/// oversized field travels all the way to a varchar column and comes back as an unhandled write
/// error rather than a field message, which is exactly the defect they were added to close.
/// </summary>
public class StoreValidationTests
{
    private static readonly IValidator<ApplyForStoreRequest> Apply = new ApplyForStoreRequestValidator();
    private static readonly IValidator<UpdateStoreRequest> Update = new UpdateStoreRequestValidator();

    private static ApplyForStoreRequest Application(
        string name = "Ovçu Dünyası",
        string? description = null,
        string? address = null,
        string? phone = null) =>
        new(name, description, address, phone);

    private static string[] ErrorsFor(FluentValidation.Results.ValidationResult result, string property) =>
        [.. result.Errors.Where(e => e.PropertyName == property).Select(e => e.ErrorMessage)];

    [Fact]
    public void A_complete_application_passes()
    {
        var result = Apply.Validate(Application(
            description: "Ov və kamp avadanlıqları.", address: "Bakı ş.", phone: "0501112233"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void An_application_with_nothing_but_a_name_passes()
    {
        // Description, address and phone are all optional; a storefront can open with just a name.
        Assert.True(Apply.Validate(Application()).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("X")]
    public void A_name_that_is_missing_or_too_short_is_refused(string name)
    {
        var result = Apply.Validate(Application(name: name));

        Assert.False(result.IsValid);
        Assert.NotEmpty(ErrorsFor(result, "Name"));
    }

    [Fact]
    public void A_name_of_exactly_the_column_width_is_accepted()
    {
        Assert.True(Apply.Validate(Application(name: new string('a', 100))).IsValid);
    }

    [Fact]
    public void A_name_one_character_over_the_column_width_is_refused()
    {
        // 101 characters is the case that used to reach PostgreSQL and surface as a 500.
        var result = Apply.Validate(Application(name: new string('a', 101)));

        Assert.False(result.IsValid);
        Assert.Contains("100", ErrorsFor(result, "Name")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void An_oversized_description_is_refused_at_the_column_width()
    {
        Assert.True(Apply.Validate(Application(description: new string('a', 2000))).IsValid);

        var result = Apply.Validate(Application(description: new string('a', 2001)));

        Assert.False(result.IsValid);
        Assert.NotEmpty(ErrorsFor(result, "Description"));
    }

    [Fact]
    public void An_oversized_address_is_refused_at_the_column_width()
    {
        Assert.True(Apply.Validate(Application(address: new string('a', 200))).IsValid);

        var result = Apply.Validate(Application(address: new string('a', 201)));

        Assert.False(result.IsValid);
        Assert.NotEmpty(ErrorsFor(result, "Address"));
    }

    [Theory]
    [InlineData("0501112233")]
    [InlineData("+994501112233")]
    [InlineData("050 111 22 33")]
    public void A_number_in_any_of_the_accepted_spellings_passes(string phone)
    {
        Assert.True(Apply.Validate(Application(phone: phone)).IsValid);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("not a phone")]
    [InlineData("+9945011122334455")]
    public void A_number_that_is_not_a_mobile_number_is_refused(string phone)
    {
        var result = Apply.Validate(Application(phone: phone));

        Assert.False(result.IsValid);
        Assert.NotEmpty(ErrorsFor(result, "Phone"));
    }

    [Fact]
    public void A_very_long_phone_string_is_refused_on_length_before_shape()
    {
        var result = Apply.Validate(Application(phone: new string('0', 400)));

        Assert.False(result.IsValid);
        Assert.Contains("20", ErrorsFor(result, "Phone")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void The_update_request_is_held_to_the_same_limits()
    {
        var tooLong = Update.Validate(new UpdateStoreRequest(new string('a', 101), null, null, null));
        var valid = Update.Validate(new UpdateStoreRequest("Yeni Ad", null, null, "0501112233"));

        Assert.False(tooLong.IsValid);
        Assert.True(valid.IsValid);
    }

    [Fact]
    public void A_moderation_reason_is_required_and_bounded()
    {
        var reject = new RejectStoreRequestValidator();
        var suspend = new SuspendStoreRequestValidator();

        Assert.False(reject.Validate(new RejectStoreRequest("")).IsValid);
        Assert.False(reject.Validate(new RejectStoreRequest(new string('a', 501))).IsValid);
        Assert.True(reject.Validate(new RejectStoreRequest("Ad qaydalara uyğun deyil.")).IsValid);

        Assert.False(suspend.Validate(new SuspendStoreRequest("   ")).IsValid);
        Assert.True(suspend.Validate(new SuspendStoreRequest("Yoxlama tələb olunur.")).IsValid);
    }
}
