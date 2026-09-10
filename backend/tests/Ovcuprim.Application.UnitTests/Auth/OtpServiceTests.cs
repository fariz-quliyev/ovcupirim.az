using Microsoft.EntityFrameworkCore;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Common;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Application.UnitTests.Auth;

public class OtpServiceTests
{
    [Fact]
    public async Task Issue_sends_a_code_of_the_configured_length()
    {
        using var h = new AuthTestHarness();

        var result = await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);

        Assert.True(result.Succeeded);
        Assert.Single(h.Sms.Sent);
        Assert.Equal(h.Otp.CodeLength, h.Sms.LastCode().Length);
        Assert.All(h.Sms.LastCode(), c => Assert.True(char.IsAsciiDigit(c)));
    }

    [Fact]
    public async Task Issue_never_stores_the_code_in_plaintext()
    {
        using var h = new AuthTestHarness();

        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);

        var code = h.Sms.LastCode();
        var stored = await h.Db.OtpCodes.SingleAsync();

        Assert.NotEqual(code, stored.CodeHash);
        Assert.DoesNotContain(code, stored.CodeHash, StringComparison.Ordinal);
        // A keyed hash, not the digits themselves.
        Assert.Equal(64, stored.CodeHash.Length);
        Assert.True(h.Hasher.Verify(code, stored.CodeHash));
    }

    [Fact]
    public async Task Verify_accepts_the_issued_code_once()
    {
        using var h = new AuthTestHarness();
        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);
        var code = h.Sms.LastCode();

        var first = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, code, OtpPurpose.Login);
        var second = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, code, OtpPurpose.Login);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
    }

    [Fact]
    public async Task Verify_rejects_an_expired_code()
    {
        using var h = new AuthTestHarness();
        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);
        var code = h.Sms.LastCode();

        h.Clock.Advance(h.Otp.Lifetime + TimeSpan.FromSeconds(1));

        var result = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, code, OtpPurpose.Login);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultError.Validation, result.Error);
    }

    [Fact]
    public async Task Verify_rejects_a_wrong_code_and_counts_the_attempt()
    {
        using var h = new AuthTestHarness();
        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);

        var result = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, "000000", OtpPurpose.Login);

        Assert.False(result.Succeeded);
        Assert.Equal(1, (await h.Db.OtpCodes.SingleAsync()).AttemptCount);
    }

    [Fact]
    public async Task Verify_burns_the_code_after_the_attempt_limit()
    {
        using var h = new AuthTestHarness();
        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);
        var code = h.Sms.LastCode();

        for (var i = 0; i < h.Otp.MaxVerificationAttempts; i++)
        {
            await h.OtpService.VerifyAsync(AuthTestHarness.Phone, "000000", OtpPurpose.Login);
        }

        // Even the correct code is worthless once the limit is hit.
        var result = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, code, OtpPurpose.Login);

        Assert.False(result.Succeeded);
        Assert.NotNull((await h.Db.OtpCodes.SingleAsync()).ConsumedAt);
    }

    [Fact]
    public async Task Issue_enforces_the_resend_cooldown()
    {
        using var h = new AuthTestHarness();
        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);

        var tooSoon = await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);

        Assert.False(tooSoon.Succeeded);
        Assert.Equal(ResultError.RateLimited, tooSoon.Error);
        Assert.Single(h.Sms.Sent);
    }

    [Fact]
    public async Task Issue_allows_a_resend_once_the_cooldown_elapses()
    {
        using var h = new AuthTestHarness();
        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);

        h.Clock.Advance(h.Otp.ResendCooldown + TimeSpan.FromSeconds(1));
        var result = await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);

        Assert.True(result.Succeeded);
        Assert.Equal(2, h.Sms.Sent.Count);
    }

    [Fact]
    public async Task Issuing_a_new_code_invalidates_the_previous_one()
    {
        using var h = new AuthTestHarness();
        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);
        var firstCode = h.Sms.LastCode();

        h.Clock.Advance(h.Otp.ResendCooldown + TimeSpan.FromSeconds(1));
        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);
        var secondCode = h.Sms.LastCode();

        var oldResult = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, firstCode, OtpPurpose.Login);
        var newResult = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, secondCode, OtpPurpose.Login);

        Assert.False(oldResult.Succeeded);
        Assert.True(newResult.Succeeded);
    }

    [Fact]
    public async Task Issue_enforces_the_per_number_window_cap()
    {
        using var h = new AuthTestHarness(new OtpOptions
        {
            ResendCooldown = TimeSpan.FromSeconds(1),
            MaxRequestsPerWindow = 3,
            RequestWindow = TimeSpan.FromHours(1)
        });

        for (var i = 0; i < 3; i++)
        {
            var ok = await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);
            Assert.True(ok.Succeeded);
            h.Clock.Advance(TimeSpan.FromSeconds(2));
        }

        var blocked = await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);

        Assert.False(blocked.Succeeded);
        Assert.Equal(ResultError.RateLimited, blocked.Error);
        Assert.Equal(3, h.Sms.Sent.Count);
    }

    [Fact]
    public async Task Verify_failures_all_return_the_same_message()
    {
        using var h = new AuthTestHarness();

        var noCode = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, "123456", OtpPurpose.Login);

        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Login);
        var wrongCode = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, "000000", OtpPurpose.Login);

        h.Clock.Advance(h.Otp.Lifetime + TimeSpan.FromSeconds(1));
        var expired = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, h.Sms.LastCode(), OtpPurpose.Login);

        // Nothing in the response distinguishes "no code", "wrong code" and "expired".
        Assert.Equal(noCode.Message, wrongCode.Message);
        Assert.Equal(noCode.Message, expired.Message);
    }

    [Fact]
    public async Task A_code_issued_for_one_purpose_does_not_work_for_another()
    {
        using var h = new AuthTestHarness();
        await h.OtpService.IssueAsync(AuthTestHarness.Phone, OtpPurpose.Registration);
        var code = h.Sms.LastCode();

        var result = await h.OtpService.VerifyAsync(AuthTestHarness.Phone, code, OtpPurpose.Login);

        Assert.False(result.Succeeded);
    }
}
