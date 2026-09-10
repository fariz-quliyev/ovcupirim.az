using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Auth;

public class RegistrationTests
{
    [Fact]
    public async Task Register_creates_an_unverified_user_and_sends_a_code()
    {
        using var h = new AuthTestHarness();

        var result = await h.AuthService.RegisterAsync(new RegisterRequest("0501234567", "Aydın Məmmədov"));

        Assert.True(result.Succeeded);
        Assert.Single(h.Sms.Sent);

        var user = await h.Db.Users.SingleAsync();
        Assert.Equal("+994501234567", user.PhoneNumber);
        Assert.Equal("Aydın Məmmədov", user.FullName);
        Assert.False(user.IsPhoneVerified);
        Assert.Equal(UserRole.User, user.Role);
    }

    [Fact]
    public async Task Register_normalises_the_phone_number()
    {
        using var h = new AuthTestHarness();

        await h.AuthService.RegisterAsync(new RegisterRequest("+994 50 123 45 67", "Aydın"));

        Assert.Equal("+994501234567", (await h.Db.Users.SingleAsync()).PhoneNumber);
    }

    [Fact]
    public async Task Register_rejects_an_invalid_phone_number()
    {
        using var h = new AuthTestHarness();

        var result = await h.AuthService.RegisterAsync(new RegisterRequest("12345", "Aydın"));

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Validation, result.Error);
        Assert.Empty(h.Sms.Sent);
    }

    [Fact]
    public async Task Register_on_an_existing_number_does_not_create_a_second_account_or_leak_that_it_exists()
    {
        using var h = new AuthTestHarness();
        await h.SeedUserAsync();

        var fresh = await new AuthTestHarness().AuthService.RegisterAsync(
            new RegisterRequest("+994559999999", "Yeni İstifadəçi"));

        h.Clock.Advance(TimeSpan.FromMinutes(5));
        var existing = await h.AuthService.RegisterAsync(new RegisterRequest(AuthTestHarness.Phone, "Fərqli Ad"));

        Assert.True(existing.Succeeded);
        // Identical body for a brand-new number and an already-registered one.
        Assert.Equal(fresh.Value!.Message, existing.Value!.Message);
        Assert.Equal(1, await h.Db.Users.CountAsync());
        // The existing profile name is never overwritten by a registration attempt.
        Assert.Equal("Test İstifadəçi", (await h.Db.Users.SingleAsync()).FullName);
    }

    [Fact]
    public async Task Register_verification_marks_the_phone_verified_and_returns_tokens()
    {
        using var h = new AuthTestHarness();
        await h.AuthService.RegisterAsync(new RegisterRequest(AuthTestHarness.Phone, "Aydın"));

        var result = await h.AuthService.VerifyOtpAsync(
            new VerifyOtpRequest(AuthTestHarness.Phone, h.Sms.LastCode(), OtpPurpose.Registration), "127.0.0.1");

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.Value!.Response.AccessToken);
        Assert.NotEmpty(result.Value.RefreshToken);
        Assert.True(result.Value.Response.User.IsPhoneVerified);
        Assert.True((await h.Db.Users.SingleAsync()).IsPhoneVerified);
    }
}

public class LoginTests
{
    [Fact]
    public async Task Login_sends_a_code_to_a_known_number()
    {
        using var h = new AuthTestHarness();
        await h.SeedUserAsync();

        var result = await h.AuthService.RequestLoginOtpAsync(new LoginRequest(AuthTestHarness.Phone));

        Assert.True(result.Succeeded);
        Assert.Single(h.Sms.Sent);
    }

    [Fact]
    public async Task Login_for_an_unknown_number_looks_identical_but_sends_nothing()
    {
        using var h = new AuthTestHarness();
        await h.SeedUserAsync();

        var known = await h.AuthService.RequestLoginOtpAsync(new LoginRequest(AuthTestHarness.Phone));
        h.Sms.Clear();

        var unknown = await h.AuthService.RequestLoginOtpAsync(new LoginRequest("+994559999999"));

        Assert.True(unknown.Succeeded);
        Assert.Equal(known.Value!.Message, unknown.Value!.Message);
        Assert.Equal(known.Value.ResendAfterSeconds, unknown.Value.ResendAfterSeconds);
        // No account, no SMS — the response alone cannot be used to enumerate users.
        Assert.Empty(h.Sms.Sent);
    }

    [Fact]
    public async Task Login_succeeds_with_the_issued_code()
    {
        using var h = new AuthTestHarness();
        var user = await h.SeedUserAsync();
        await h.AuthService.RequestLoginOtpAsync(new LoginRequest(AuthTestHarness.Phone));

        var result = await h.AuthService.VerifyOtpAsync(
            new VerifyOtpRequest(AuthTestHarness.Phone, h.Sms.LastCode(), OtpPurpose.Login), "127.0.0.1");

        Assert.True(result.Succeeded);
        Assert.Equal(user.Id, result.Value!.Response.User.Id);
        Assert.NotNull((await h.Db.Users.SingleAsync()).LastLoginAt);
    }

    [Fact]
    public async Task Login_with_a_wrong_code_fails_without_issuing_tokens()
    {
        using var h = new AuthTestHarness();
        await h.SeedUserAsync();
        await h.AuthService.RequestLoginOtpAsync(new LoginRequest(AuthTestHarness.Phone));

        var result = await h.AuthService.VerifyOtpAsync(
            new VerifyOtpRequest(AuthTestHarness.Phone, "000000", OtpPurpose.Login), "127.0.0.1");

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Empty(await h.Db.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task A_blocked_account_cannot_obtain_tokens()
    {
        using var h = new AuthTestHarness();
        await h.SeedUserAsync(status: UserStatus.Blocked);

        // No code is sent to a blocked account, so verification cannot succeed either.
        var request = await h.AuthService.RequestLoginOtpAsync(new LoginRequest(AuthTestHarness.Phone));

        Assert.True(request.Succeeded);
        Assert.Empty(h.Sms.Sent);

        var verify = await h.AuthService.VerifyOtpAsync(
            new VerifyOtpRequest(AuthTestHarness.Phone, "123456", OtpPurpose.Login), null);

        Assert.False(verify.Succeeded);
    }
}

public class RefreshTokenTests
{
    [Fact]
    public async Task Refresh_rotates_the_token()
    {
        using var h = new AuthTestHarness();
        var tokens = await h.RegisterAndVerifyAsync();

        h.Clock.Advance(TimeSpan.FromMinutes(1));
        var refreshed = await h.AuthService.RefreshAsync(tokens.RefreshToken, "127.0.0.1");

        Assert.True(refreshed.Succeeded);
        Assert.NotEqual(tokens.RefreshToken, refreshed.Value!.RefreshToken);

        var stored = await h.Db.RefreshTokens.OrderBy(t => t.CreatedAt).ToListAsync();
        Assert.Equal(2, stored.Count);
        // The presented token is revoked and points at its successor.
        Assert.NotNull(stored[0].RevokedAt);
        Assert.Equal(stored[1].Id, stored[0].ReplacedByTokenId);
        Assert.Null(stored[1].RevokedAt);
    }

    [Fact]
    public async Task A_rotated_token_cannot_be_used_again()
    {
        using var h = new AuthTestHarness();
        var tokens = await h.RegisterAndVerifyAsync();

        await h.AuthService.RefreshAsync(tokens.RefreshToken, "127.0.0.1");
        var replay = await h.AuthService.RefreshAsync(tokens.RefreshToken, "10.0.0.9");

        Assert.False(replay.Succeeded);
        Assert.Equal(ResultError.Unauthorized, replay.Error);
    }

    [Fact]
    public async Task Replay_revokes_every_session_and_records_an_audit_entry()
    {
        using var h = new AuthTestHarness();
        var tokens = await h.RegisterAndVerifyAsync();

        var rotated = await h.AuthService.RefreshAsync(tokens.RefreshToken, "127.0.0.1");
        await h.AuthService.RefreshAsync(tokens.RefreshToken, "10.0.0.9");

        // The successor issued to the legitimate client is killed too — the session is compromised.
        var afterReplay = await h.AuthService.RefreshAsync(rotated.Value!.RefreshToken, "127.0.0.1");
        Assert.False(afterReplay.Succeeded);

        Assert.All(await h.Db.RefreshTokens.ToListAsync(), t => Assert.NotNull(t.RevokedAt));

        // Every reuse is recorded; the first entry is the original attacker's attempt.
        var audits = await h.Db.AuditLogs.OrderBy(a => a.CreatedAt).ToListAsync();
        Assert.All(audits, a => Assert.Equal("RefreshTokenReuseDetected", a.Action));
        Assert.Equal("10.0.0.9", audits[0].IpAddress);
    }

    [Fact]
    public async Task An_unknown_refresh_token_is_rejected()
    {
        using var h = new AuthTestHarness();
        await h.RegisterAndVerifyAsync();

        var result = await h.AuthService.RefreshAsync("not-a-real-token", null);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Unauthorized, result.Error);
    }

    [Fact]
    public async Task An_expired_refresh_token_is_rejected()
    {
        using var h = new AuthTestHarness();
        var tokens = await h.RegisterAndVerifyAsync();

        h.Clock.Advance(h.Jwt.RefreshTokenLifetime + TimeSpan.FromMinutes(1));
        var result = await h.AuthService.RefreshAsync(tokens.RefreshToken, null);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Unauthorized, result.Error);
    }

    [Fact]
    public async Task Refresh_tokens_are_never_stored_in_plaintext()
    {
        using var h = new AuthTestHarness();
        var tokens = await h.RegisterAndVerifyAsync();

        var stored = await h.Db.RefreshTokens.SingleAsync();

        Assert.NotEqual(tokens.RefreshToken, stored.TokenHash);
        Assert.True(h.Hasher.Verify(tokens.RefreshToken, stored.TokenHash));
    }

    [Fact]
    public async Task Logout_revokes_the_token_and_blocks_further_refreshes()
    {
        using var h = new AuthTestHarness();
        var tokens = await h.RegisterAndVerifyAsync();

        var logout = await h.AuthService.LogoutAsync(tokens.RefreshToken);
        var afterLogout = await h.AuthService.RefreshAsync(tokens.RefreshToken, null);

        Assert.True(logout.Succeeded);
        Assert.NotNull((await h.Db.RefreshTokens.SingleAsync()).RevokedAt);
        Assert.False(afterLogout.Succeeded);
    }

    [Fact]
    public async Task Logout_without_a_token_is_a_no_op()
    {
        using var h = new AuthTestHarness();

        var result = await h.AuthService.LogoutAsync(null);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Blocking_a_user_stops_their_refresh_and_kills_their_sessions()
    {
        using var h = new AuthTestHarness();
        var tokens = await h.RegisterAndVerifyAsync();

        var user = await h.Db.Users.SingleAsync();
        user.Status = UserStatus.Blocked;
        await h.Db.SaveChangesAsync();

        var result = await h.AuthService.RefreshAsync(tokens.RefreshToken, null);

        Assert.False(result.Succeeded);
        Assert.All(await h.Db.RefreshTokens.ToListAsync(), t => Assert.NotNull(t.RevokedAt));
    }
}
