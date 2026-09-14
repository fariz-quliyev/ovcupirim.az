using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Ovcuprim.Application.UnitTests.Auth;

public class Pbkdf2PasswordHasherTests
{
    private static Pbkdf2PasswordHasher Hasher(string key = "test-hashing-key-that-is-long-enough-000") =>
        new(Options.Create(new SecurityOptions { HashingKey = key }));

    [Fact]
    public void A_password_verifies_against_its_own_hash()
    {
        var hasher = Hasher();
        var hash = hasher.Hash("duzgun-parol-2026");

        Assert.True(hasher.Verify("duzgun-parol-2026", hash));
        Assert.False(hasher.Verify("duzgun-parol-2025", hash));
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        var hasher = Hasher();

        // The salt is what does this. Without it, two administrators with the same password would
        // be visibly identical in the database.
        var first = hasher.Hash("eyni-parol-2026");
        var second = hasher.Hash("eyni-parol-2026");

        Assert.NotEqual(first, second);
        Assert.True(hasher.Verify("eyni-parol-2026", first));
        Assert.True(hasher.Verify("eyni-parol-2026", second));
    }

    [Fact]
    public void A_hash_is_worthless_without_the_server_pepper()
    {
        var hash = Hasher().Hash("duzgun-parol-2026");

        // This is the whole point of peppering: a stolen database, opened with a different key,
        // yields nothing — the correct password no longer matches.
        var thief = Hasher("a-different-hashing-key-long-enough-0000");

        Assert.False(thief.Verify("duzgun-parol-2026", hash));
    }

    [Fact]
    public void The_decoy_matches_nothing_but_is_still_well_formed()
    {
        var hasher = Hasher();

        Assert.False(hasher.Verify("duzgun-parol-2026", hasher.DecoyHash));
        Assert.False(hasher.Verify(string.Empty, hasher.DecoyHash));

        // Well-formed matters: a decoy that failed to parse would return early and make the
        // "no such account" path measurably faster than a real check.
        Assert.Equal(4, hasher.DecoyHash.Split('.').Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("v2.210000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("v1.0.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("v1.999999999.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("v1.210000.not-base64.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("v1.210000.AAAAAAAAAAAAAAAAAAAAAA==.QUJD")]
    public void A_malformed_stored_hash_fails_instead_of_throwing(string stored)
    {
        // A corrupted or tampered row must be a failed sign-in, never an unhandled exception — and
        // an absurd iteration count must not become a way to burn the server's CPU one request at a time.
        Assert.False(Hasher().Verify("duzgun-parol-2026", stored));
    }
}

public class AdminPasswordLoginTests
{
    [Fact]
    public async Task An_administrator_signs_in_with_the_right_password()
    {
        using var h = new AuthTestHarness();
        var admin = await h.SeedAdminAsync();

        var result = await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, AuthTestHarness.AdminPassword), "94.20.42.13");

        Assert.True(result.Succeeded);
        Assert.Equal("Admin", result.Value!.Response.User.Role);
        Assert.Empty(h.Sms.Sent);

        var stored = await h.Db.Users.SingleAsync(u => u.Id == admin.Id);
        Assert.Equal(h.Clock.UtcNow, stored.LastLoginAt);
        Assert.Equal(0, stored.FailedLoginAttempts);

        Assert.Contains(h.Db.AuditLogs, a => a.Action == "AdminLoginSucceeded" && a.IpAddress == "94.20.42.13");
    }

    [Fact]
    public async Task A_wrong_password_is_refused_and_counted()
    {
        using var h = new AuthTestHarness();
        var admin = await h.SeedAdminAsync();

        var result = await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, "yanlis-parol-2026"), "94.20.42.13");

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Unauthorized, result.Error);

        var stored = await h.Db.Users.SingleAsync(u => u.Id == admin.Id);
        Assert.Equal(1, stored.FailedLoginAttempts);
        Assert.Null(stored.LockedUntil);
    }

    [Fact]
    public async Task An_unknown_number_and_a_wrong_password_are_indistinguishable()
    {
        using var h = new AuthTestHarness();
        await h.SeedAdminAsync();

        var unknown = await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest("+994559999999", AuthTestHarness.AdminPassword), null);

        var wrong = await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, "yanlis-parol-2026"), null);

        // Same error, same message: response content cannot be used to find the administrator.
        Assert.Equal(unknown.Error, wrong.Error);
        Assert.Equal(unknown.Message, wrong.Message);
    }

    [Fact]
    public async Task A_non_administrator_cannot_use_the_password_door_even_with_the_right_password()
    {
        using var h = new AuthTestHarness();

        var moderator = await h.SeedUserAsync("+994551112233", UserRole.Moderator);
        moderator.PasswordHash = h.Passwords.Hash(AuthTestHarness.AdminPassword);
        await h.Db.SaveChangesAsync();

        var result = await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest("+994551112233", AuthTestHarness.AdminPassword), null);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Unauthorized, result.Error);
    }

    [Fact]
    public async Task A_blocked_administrator_is_refused()
    {
        using var h = new AuthTestHarness();

        var admin = await h.SeedUserAsync(AuthTestHarness.AdminPhone, UserRole.Admin, UserStatus.Blocked);
        admin.PasswordHash = h.Passwords.Hash(AuthTestHarness.AdminPassword);
        await h.Db.SaveChangesAsync();

        var result = await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, AuthTestHarness.AdminPassword), null);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task The_account_locks_after_the_configured_number_of_failures_and_frees_itself_later()
    {
        using var h = new AuthTestHarness();
        var admin = await h.SeedAdminAsync();

        for (var attempt = 0; attempt < h.AdminLogin.MaxFailedAttempts; attempt++)
        {
            await h.AuthService.AdminPasswordLoginAsync(
                new AdminLoginRequest(AuthTestHarness.AdminPhone, "yanlis-parol-2026"), null);
        }

        var locked = await h.Db.Users.SingleAsync(u => u.Id == admin.Id);
        Assert.NotNull(locked.LockedUntil);

        // The correct password is refused while the lock stands — that is what makes the lock worth
        // anything against someone guessing from many addresses.
        var duringLock = await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, AuthTestHarness.AdminPassword), null);

        Assert.False(duringLock.Succeeded);
        Assert.Contains(h.Db.AuditLogs, a => a.Action == "AdminLoginRejectedLocked");

        h.Clock.Advance(h.AdminLogin.LockoutDuration + TimeSpan.FromSeconds(1));

        var afterLock = await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, AuthTestHarness.AdminPassword), null);

        Assert.True(afterLock.Succeeded);

        var freed = await h.Db.Users.SingleAsync(u => u.Id == admin.Id);
        Assert.Null(freed.LockedUntil);
        Assert.Equal(0, freed.FailedLoginAttempts);
    }

    [Fact]
    public async Task A_successful_sign_in_clears_the_failure_count()
    {
        using var h = new AuthTestHarness();
        var admin = await h.SeedAdminAsync();

        await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, "yanlis-parol-2026"), null);

        await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, AuthTestHarness.AdminPassword), null);

        Assert.Equal(0, (await h.Db.Users.SingleAsync(u => u.Id == admin.Id)).FailedLoginAttempts);
    }
}

public class AdministratorsAreOutsideTheSmsFlowTests
{
    [Fact]
    public async Task No_login_code_is_ever_sent_to_an_administrator()
    {
        using var h = new AuthTestHarness();
        await h.SeedAdminAsync();

        var result = await h.AuthService.RequestLoginOtpAsync(new LoginRequest(AuthTestHarness.AdminPhone));

        // The caller is told a code was sent, exactly as an unknown number is told. Answering
        // anything else here would identify the administrator's number to anyone who asked.
        Assert.True(result.Succeeded);
        Assert.Empty(h.Sms.Sent);
    }

    [Fact]
    public async Task Registering_an_administrators_number_sends_nothing_and_reveals_nothing()
    {
        using var h = new AuthTestHarness();
        await h.SeedAdminAsync();

        var result = await h.AuthService.RegisterAsync(
            new RegisterRequest(AuthTestHarness.AdminPhone, "Kimsə Başqası"));

        Assert.True(result.Succeeded);
        Assert.Empty(h.Sms.Sent);
    }

    [Fact]
    public async Task A_code_issued_before_the_account_became_an_administrator_no_longer_verifies()
    {
        using var h = new AuthTestHarness();
        var user = await h.SeedUserAsync(AuthTestHarness.AdminPhone);

        await h.AuthService.RequestLoginOtpAsync(new LoginRequest(AuthTestHarness.AdminPhone));
        var code = h.Sms.LastCode();

        user.Role = UserRole.Admin;
        await h.Db.SaveChangesAsync();

        var result = await h.AuthService.VerifyOtpAsync(
            new VerifyOtpRequest(AuthTestHarness.AdminPhone, code, OtpPurpose.Login), null);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Unauthorized, result.Error);
    }
}

public class AdminPasswordChangeTests
{
    private const string NewPassword = "yeni-uzun-parol-2026";

    [Fact]
    public async Task The_password_changes_and_every_session_ends()
    {
        using var h = new AuthTestHarness();
        var admin = await h.SeedAdminAsync();

        var signIn = await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, AuthTestHarness.AdminPassword), null);

        var result = await h.AuthService.ChangePasswordAsync(
            admin.Id, new ChangePasswordRequest(AuthTestHarness.AdminPassword, NewPassword), null);

        Assert.True(result.Succeeded);

        // The session held with the old password is gone — otherwise changing a leaked password
        // would leave the thief signed in.
        var refresh = await h.AuthService.RefreshAsync(signIn.Value!.RefreshToken, null);
        Assert.False(refresh.Succeeded);

        Assert.True((await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, NewPassword), null)).Succeeded);

        Assert.False((await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, AuthTestHarness.AdminPassword), null)).Succeeded);
    }

    [Fact]
    public async Task The_current_password_is_required()
    {
        using var h = new AuthTestHarness();
        var admin = await h.SeedAdminAsync();

        var result = await h.AuthService.ChangePasswordAsync(
            admin.Id, new ChangePasswordRequest("yanlis-parol-2026", NewPassword), null);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Validation, result.Error);

        Assert.True((await h.AuthService.AdminPasswordLoginAsync(
            new AdminLoginRequest(AuthTestHarness.AdminPhone, AuthTestHarness.AdminPassword), null)).Succeeded);
    }

    [Fact]
    public async Task An_ordinary_user_has_no_password_to_change()
    {
        using var h = new AuthTestHarness();
        var user = await h.SeedUserAsync();

        var result = await h.AuthService.ChangePasswordAsync(
            user.Id, new ChangePasswordRequest(AuthTestHarness.AdminPassword, NewPassword), null);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Forbidden, result.Error);
    }

    [Fact]
    public async Task Changing_the_password_clears_a_standing_lock()
    {
        using var h = new AuthTestHarness();
        var admin = await h.SeedAdminAsync();

        admin.LockedUntil = h.Clock.UtcNow.AddMinutes(10);
        admin.FailedLoginAttempts = h.AdminLogin.MaxFailedAttempts;
        await h.Db.SaveChangesAsync();

        await h.AuthService.ChangePasswordAsync(
            admin.Id, new ChangePasswordRequest(AuthTestHarness.AdminPassword, NewPassword), null);

        var stored = await h.Db.Users.SingleAsync(u => u.Id == admin.Id);
        Assert.Null(stored.LockedUntil);
        Assert.Equal(0, stored.FailedLoginAttempts);
    }
}
