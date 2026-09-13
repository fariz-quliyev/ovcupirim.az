using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ovcuprim.Application.Auth;
using Ovcuprim.Domain.Enums;

namespace Ovcuprim.Api.IntegrationTests;

public class AuthEndpointsTests
{
    private const string Phone = "+994501234567";

    private static async Task<(HttpClient Client, AuthResponse Auth)> RegisteredClientAsync(
        ApiFactory factory, string phone = Phone)
    {
        var client = factory.CreateApiClient();

        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(phone, "Test İstifadəçi"));
        register.EnsureSuccessStatusCode();

        var verify = await client.PostAsJsonAsync("/api/v1/auth/verify",
            new VerifyOtpRequest(phone, factory.Sms.LastCodeFor(phone), OtpPurpose.Registration));
        verify.EnsureSuccessStatusCode();

        var auth = (await verify.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return (client, auth);
    }

    [Fact]
    public async Task Register_then_verify_issues_a_token_and_sets_the_refresh_cookie()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();

        var register = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("0501234567", "Aydın Məmmədov"));

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var verify = await client.PostAsJsonAsync("/api/v1/auth/verify",
            new VerifyOtpRequest(Phone, factory.Sms.LastCodeFor(Phone), OtpPurpose.Registration));

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var auth = await verify.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.NotEmpty(auth.AccessToken);
        Assert.Equal("Aydın Məmmədov", auth.User.FullName);

        var cookie = Assert.Single(verify.Headers.GetValues("Set-Cookie"));
        Assert.Contains("ovcupirim_rt=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);

        // The refresh token must never be part of the JSON body.
        var body = await verify.Content.ReadAsStringAsync();
        Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Verify_with_a_wrong_code_returns_problem_details()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(Phone, "Test"));

        var response = await client.PostAsJsonAsync("/api/v1/auth/verify",
            new VerifyOtpRequest(Phone, "000000", OtpPurpose.Registration));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.True(problem.TryGetProperty("title", out _));
        Assert.True(problem.TryGetProperty("detail", out _));
    }

    [Fact]
    public async Task Invalid_input_is_rejected_with_per_field_validation_errors()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest("123", ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // RFC 7807 body with a field-keyed errors dictionary the form can bind to.
        var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(400, problem.GetProperty("status").GetInt32());

        var errors = problem.GetProperty("errors");
        Assert.NotEmpty(errors.GetProperty("phoneNumber").EnumerateArray());
        Assert.NotEmpty(errors.GetProperty("fullName").EnumerateArray());
    }

    [Fact]
    public async Task Errors_are_served_as_problem_json_when_the_client_asks_for_it()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(new RegisterRequest("123", ""))
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/problem+json"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Login_gives_the_same_answer_for_known_and_unknown_numbers()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();

        await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(Phone, "Test"));

        var known = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(Phone));
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("+994559999999"));

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(
            await known.Content.ReadAsStringAsync(),
            await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Me_requires_authentication()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        // The challenge path writes RFC 7807 directly rather than negotiating.
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task Me_returns_the_caller_with_a_valid_token()
    {
        using var factory = new ApiFactory();
        var (client, auth) = await RegisteredClientAsync(factory);

        var response = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var me = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(auth.User.Id, me!.Id);
        Assert.Equal(Phone, me.PhoneNumber);
    }

    [Fact]
    public async Task A_malformed_or_forged_token_is_rejected()
    {
        using var factory = new ApiFactory();
        var (client, auth) = await RegisteredClientAsync(factory);

        var parts = auth.AccessToken.Split('.');
        var signature = parts[2].ToCharArray();
        signature[0] = signature[0] == 'A' ? 'B' : 'A';

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", $"{parts[0]}.{parts[1]}.{new string(signature)}");

        var forged = await client.GetAsync("/api/v1/users/me");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");
        var garbage = await client.GetAsync("/api/v1/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, garbage.StatusCode);
    }

    [Fact]
    public async Task Profile_can_be_updated_by_its_owner()
    {
        using var factory = new ApiFactory();
        var (client, _) = await RegisteredClientAsync(factory);

        var response = await client.PutAsJsonAsync("/api/v1/users/me",
            new UpdateProfileRequest("Yenilənmiş Ad", "yeni@ovcuprim.az"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("Yenilənmiş Ad", updated!.FullName);
        Assert.Equal("yeni@ovcuprim.az", updated.Email);
    }

    [Fact]
    public async Task Refresh_rotates_the_cookie_and_returns_a_new_access_token()
    {
        using var factory = new ApiFactory();
        var (client, first) = await RegisteredClientAsync(factory);

        var response = await client.PostAsync("/api/v1/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var refreshed = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(refreshed);
        Assert.Equal(first.User.Id, refreshed.User.Id);
        Assert.Contains("ovcupirim_rt=", Assert.Single(response.Headers.GetValues("Set-Cookie")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_without_a_cookie_is_unauthorized()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();

        var response = await client.PostAsync("/api/v1/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_so_refresh_stops_working()
    {
        using var factory = new ApiFactory();
        var (client, _) = await RegisteredClientAsync(factory);

        var logout = await client.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task The_admin_endpoint_is_forbidden_to_a_normal_user_and_open_to_an_admin()
    {
        using var factory = new ApiFactory();
        var (client, _) = await RegisteredClientAsync(factory);

        var asUser = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, asUser.StatusCode);

        // Promote, then sign in again so the new role is inside a freshly minted token.
        await factory.SetRoleAsync(Phone, UserRole.Admin);

        var adminClient = factory.CreateApiClient();
        await adminClient.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(Phone));
        var verify = await adminClient.PostAsJsonAsync("/api/v1/auth/verify",
            new VerifyOtpRequest(Phone, factory.Sms.LastCodeFor(Phone), OtpPurpose.Login));

        var auth = (await verify.Content.ReadFromJsonAsync<AuthResponse>())!;
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var asAdmin = await adminClient.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.OK, asAdmin.StatusCode);
        Assert.Equal("Admin", auth.User.Role);
    }

    [Fact]
    public async Task The_admin_endpoint_rejects_an_anonymous_caller_with_401_not_403()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Repeated_otp_requests_are_rate_limited()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();

        HttpResponseMessage? limited = null;

        // The per-IP limiter allows 5 sends per 15 minutes.
        for (var i = 0; i < 8 && limited is null; i++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest($"+9945012345{i:D2}"));

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
        }

        Assert.NotNull(limited);
        Assert.Contains("application/problem+json", limited.Content.Headers.ContentType!.MediaType!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repeated_session_refreshes_are_rate_limited()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateApiClient();

        HttpResponseMessage? limited = null;

        // The per-IP limiter allows 30 session calls a minute. The limit is configurable so a
        // single-address browser suite is not throttled by a control aimed at the public internet;
        // this proves the production number is still the one an unconfigured host applies.
        for (var i = 0; i < 40 && limited is null; i++)
        {
            var response = await client.PostAsync("/api/v1/auth/refresh", content: null);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                limited = response;
            }
        }

        Assert.NotNull(limited);
        Assert.Contains("application/problem+json", limited.Content.Headers.ContentType!.MediaType!, StringComparison.Ordinal);
    }
}
