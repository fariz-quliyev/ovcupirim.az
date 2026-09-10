using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ovcuprim.Application.Abstractions;
using Ovcuprim.Application.Auth;
using Ovcuprim.Application.Users;
using Ovcuprim.Domain.Entities;
using Ovcuprim.Domain.Enums;
using Ovcuprim.Infrastructure.Persistence;
using Ovcuprim.Infrastructure.Security;

namespace Ovcuprim.Application.UnitTests.Auth;

/// <summary>A controllable clock so expiry and cooldown rules can be tested without waiting.</summary>
public sealed class FakeClock(DateTimeOffset now) : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; private set; } = now;

    public void Advance(TimeSpan by) => UtcNow += by;

    /// <summary>Jumps to an exact instant, for tests about a specific boundary rather than an elapsed duration.</summary>
    public void Set(DateTimeOffset instant) => UtcNow = instant;
}

/// <summary>Captures what would have been sent, and exposes the code the way a phone would receive it.</summary>
public sealed class FakeSmsSender : ISmsSender
{
    public List<(string PhoneNumber, string Message)> Sent { get; } = [];

    public Task SendAsync(string phoneNumber, string message, CancellationToken cancellationToken = default)
    {
        Sent.Add((phoneNumber, message));
        return Task.CompletedTask;
    }

    public string LastCode()
    {
        var message = Sent[^1].Message;
        return new string(message.SkipWhile(c => !char.IsAsciiDigit(c)).TakeWhile(char.IsAsciiDigit).ToArray());
    }

    public void Clear() => Sent.Clear();
}

/// <summary>
/// Wires the real services against the real DbContext on the in-memory provider, so the tests
/// exercise production code paths rather than doubles.
/// </summary>
public sealed class AuthTestHarness : IDisposable
{
    public AuthTestHarness(OtpOptions? otpOptions = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"auth-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        Clock = new FakeClock(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));
        Db = new AppDbContext(options, Clock);
        Sms = new FakeSmsSender();

        Otp = otpOptions ?? new OtpOptions();
        Jwt = new JwtOptions
        {
            Issuer = "ovcuprim-test",
            Audience = "ovcuprim-test",
            SigningKey = "test-signing-key-that-is-long-enough-000",
            AccessTokenLifetime = TimeSpan.FromMinutes(15),
            RefreshTokenLifetime = TimeSpan.FromDays(30)
        };

        Hasher = new SecretHasher(Options.Create(new SecurityOptions
        {
            HashingKey = "test-hashing-key-that-is-long-enough-000"
        }));

        var generator = new SecureTokenGenerator();
        TokenService = new JwtTokenService(Options.Create(Jwt), Clock);

        OtpService = new OtpService(
            Db, Hasher, generator, Sms, Clock,
            Options.Create(Otp),
            NullLogger<OtpService>.Instance);

        AuthService = new AuthService(
            Db, OtpService, TokenService, Hasher, generator, Clock,
            Options.Create(Jwt), Options.Create(Otp),
            NullLogger<AuthService>.Instance);

        UserService = new UserService(Db, CurrentUser, Clock);
    }

    public AppDbContext Db { get; }

    public FakeClock Clock { get; }

    /// <summary>Role changes are actor-scoped, so the user service needs to know who is calling.</summary>
    public Catalog.StubCurrentUser CurrentUser { get; } = new() { Role = "Admin" };

    public FakeSmsSender Sms { get; }

    public SecretHasher Hasher { get; }

    public OtpOptions Otp { get; }

    public JwtOptions Jwt { get; }

    public ITokenService TokenService { get; }

    public IOtpService OtpService { get; }

    public IAuthService AuthService { get; }

    public IUserService UserService { get; }

    public const string Phone = "+994501234567";

    public async Task<User> SeedUserAsync(
        string phone = Phone,
        UserRole role = UserRole.User,
        UserStatus status = UserStatus.Active,
        bool verified = true)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phone,
            FullName = "Test İstifadəçi",
            Role = role,
            Status = status,
            IsPhoneVerified = verified,
            CreatedAt = Clock.UtcNow
        };

        Db.Users.Add(user);
        await Db.SaveChangesAsync();

        return user;
    }

    /// <summary>Registers and verifies in one step, returning the issued tokens.</summary>
    public async Task<AuthTokens> RegisterAndVerifyAsync(string phone = Phone)
    {
        await AuthService.RegisterAsync(new RegisterRequest(phone, "Test İstifadəçi"));

        var result = await AuthService.VerifyOtpAsync(
            new VerifyOtpRequest(phone, Sms.LastCode(), OtpPurpose.Registration), "127.0.0.1");

        return result.Value!;
    }

    public void Dispose() => Db.Dispose();
}
