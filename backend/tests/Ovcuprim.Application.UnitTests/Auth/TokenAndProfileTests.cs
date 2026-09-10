using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Auth;

public class AccessTokenTests
{
    /// <summary>Mirrors the API's validation, but judges lifetime against the harness clock.</summary>
    private static TokenValidationParameters ValidationParameters(AuthTestHarness h) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = h.Jwt.Issuer,
        ValidAudience = h.Jwt.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(h.Jwt.SigningKey)),
        ClockSkew = TimeSpan.Zero,
        LifetimeValidator = (notBefore, expires, _, _) =>
            (notBefore is null || notBefore <= h.Clock.UtcNow.UtcDateTime)
            && (expires is null || expires > h.Clock.UtcNow.UtcDateTime)
    };

    [Fact]
    public async Task Access_token_validates_and_carries_the_expected_claims()
    {
        using var h = new AuthTestHarness();
        var user = await h.SeedUserAsync(role: UserRole.Moderator);

        var token = h.TokenService.CreateAccessToken(user);
        var principal = new JwtSecurityTokenHandler()
            .ValidateToken(token.Value, ValidationParameters(h), out _);

        Assert.Equal(user.Id.ToString(), principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("Moderator", principal.FindFirst(ClaimTypes.Role)?.Value);
        Assert.True(principal.IsInRole("Moderator"));
    }

    [Fact]
    public async Task Access_token_expires_after_the_configured_lifetime()
    {
        using var h = new AuthTestHarness();
        var user = await h.SeedUserAsync();

        var token = h.TokenService.CreateAccessToken(user);

        Assert.Equal(h.Clock.UtcNow + h.Jwt.AccessTokenLifetime, token.ExpiresAt);

        // Valid now...
        new JwtSecurityTokenHandler().ValidateToken(token.Value, ValidationParameters(h), out _);

        // ...and rejected once the lifetime has elapsed.
        h.Clock.Advance(h.Jwt.AccessTokenLifetime + TimeSpan.FromSeconds(1));

        Assert.ThrowsAny<SecurityTokenInvalidLifetimeException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token.Value, ValidationParameters(h), out _));
    }

    [Fact]
    public async Task A_token_signed_with_a_different_key_is_rejected()
    {
        using var h = new AuthTestHarness();
        var user = await h.SeedUserAsync();
        var token = h.TokenService.CreateAccessToken(user);

        var parameters = ValidationParameters(h);
        parameters.IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes("a-completely-different-key-0000000000000"));

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(token.Value, parameters, out _));
    }

    [Fact]
    public async Task A_tampered_signature_is_rejected()
    {
        using var h = new AuthTestHarness();
        var user = await h.SeedUserAsync();
        var token = h.TokenService.CreateAccessToken(user);

        var parts = token.Value.Split('.');
        var signature = parts[2].ToCharArray();
        signature[0] = signature[0] == 'A' ? 'B' : 'A';
        var tampered = $"{parts[0]}.{parts[1]}.{new string(signature)}";

        Assert.ThrowsAny<SecurityTokenInvalidSignatureException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(tampered, ValidationParameters(h), out _));
    }
}

public class UserProfileTests
{
    [Fact]
    public async Task Me_returns_the_current_user()
    {
        using var h = new AuthTestHarness();
        var user = await h.SeedUserAsync();

        var result = await h.UserService.GetByIdAsync(user.Id);

        Assert.True(result.Succeeded);
        Assert.Equal(user.PhoneNumber, result.Value!.PhoneNumber);
        Assert.Equal("User", result.Value.Role);
    }

    [Fact]
    public async Task Me_reports_not_found_for_an_unknown_id()
    {
        using var h = new AuthTestHarness();

        var result = await h.UserService.GetByIdAsync(Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.NotFound, result.Error);
    }

    [Fact]
    public async Task Profile_update_changes_name_and_email()
    {
        using var h = new AuthTestHarness();
        var user = await h.SeedUserAsync();

        var result = await h.UserService.UpdateProfileAsync(
            user.Id, new UpdateProfileRequest("Yeni Ad", "Yeni@Ovcuprim.AZ"));

        Assert.True(result.Succeeded);
        Assert.Equal("Yeni Ad", result.Value!.FullName);
        Assert.Equal("yeni@ovcuprim.az", result.Value.Email);
    }

    [Fact]
    public async Task Profile_update_rejects_an_email_another_account_already_uses()
    {
        using var h = new AuthTestHarness();
        var first = await h.SeedUserAsync();
        var second = await h.SeedUserAsync(phone: "+994559999999");

        await h.UserService.UpdateProfileAsync(first.Id, new UpdateProfileRequest("Bir", "ortaq@ovcuprim.az"));
        var clash = await h.UserService.UpdateProfileAsync(second.Id, new UpdateProfileRequest("İki", "ortaq@ovcuprim.az"));

        Assert.False(clash.Succeeded);
        Assert.Equal(ResultError.Conflict, clash.Error);
    }

    [Fact]
    public async Task Profile_update_cannot_change_the_phone_number_or_role()
    {
        using var h = new AuthTestHarness();
        var user = await h.SeedUserAsync();

        await h.UserService.UpdateProfileAsync(user.Id, new UpdateProfileRequest("Yeni Ad", null));

        var stored = await h.Db.Users.SingleAsync();
        // Neither field is part of the profile contract, so neither can be moved by this endpoint.
        Assert.Equal(AuthTestHarness.Phone, stored.PhoneNumber);
        Assert.Equal(UserRole.User, stored.Role);
    }

    [Fact]
    public async Task Admin_search_finds_users_by_name_and_paginates()
    {
        using var h = new AuthTestHarness();
        await h.SeedUserAsync();
        await h.SeedUserAsync(phone: "+994559999999");

        var all = await h.UserService.SearchAsync(null, new PageRequest { Page = 1, PageSize = 1 });
        var byName = await h.UserService.SearchAsync("test", new PageRequest());

        Assert.Equal(2, all.Total);
        Assert.Single(all.Items);
        Assert.Equal(2, all.TotalPages);
        Assert.Equal(2, byName.Total);
    }
}

public class PhoneNumberTests
{
    [Theory]
    [InlineData("0501234567", "+994501234567")]
    [InlineData("501234567", "+994501234567")]
    [InlineData("+994501234567", "+994501234567")]
    [InlineData("+994 50 123 45 67", "+994501234567")]
    [InlineData("00994501234567", "+994501234567")]
    [InlineData("(050) 123-45-67", "+994501234567")]
    public void Normalises_the_accepted_formats(string input, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("12345")]
    [InlineData("+9945012345678")]
    [InlineData("+994201234567")]
    [InlineData("not a phone")]
    public void Rejects_anything_that_is_not_an_azerbaijani_mobile_number(string? input)
    {
        Assert.Null(PhoneNumber.Normalize(input));
        Assert.False(PhoneNumber.IsValid(input));
    }

    [Fact]
    public void Masks_all_but_the_last_two_digits()
    {
        Assert.Equal("+994501***67", PhoneNumber.Mask("+994501234567"));
    }
}
