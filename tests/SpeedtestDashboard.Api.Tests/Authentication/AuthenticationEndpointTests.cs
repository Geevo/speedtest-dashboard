using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SpeedtestDashboard.Infrastructure.Authentication;
using SpeedtestDashboard.Infrastructure.Persistence;

namespace SpeedtestDashboard.Api.Tests.Authentication;

public sealed class AuthenticationEndpointTests
{
    private const string Password = "lab-secret1";

    [Fact]
    public async Task FreshInstanceStartsAnonymousAndNoticeCanBeHidden()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        using var network = await client.GetAsync("/api/network");
        using var withoutCsrf = await client.PostAsJsonAsync("/api/auth/preferences",
            new { showDisabledWarning = false });

        Assert.Equal("none", session.GetProperty("mode").GetString());
        Assert.True(session.GetProperty("authenticated").GetBoolean());
        Assert.False(session.GetProperty("loginConfigured").GetBoolean());
        Assert.True(session.GetProperty("showDisabledWarning").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, network.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);

        var token = await GetCsrfAsync(client);
        using var hidden = await PostWithCsrfAsync(client, "/api/auth/preferences", token,
            new { showDisabledWarning = false });
        var updated = await hidden.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        Assert.False(updated.GetProperty("showDisabledWarning").GetBoolean());
    }

    [Fact]
    public async Task SettingsSetupCreatesHashedAccountAndImmediatelyProtectsDashboard()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        using var withoutCsrf = await client.PostAsJsonAsync("/api/auth/setup",
            new { username = "admin", password = Password });
        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);
        Assert.Equal("csrf_validation_failed", await ProblemCodeAsync(withoutCsrf));

        using var setup = await SetupAsync(client);
        var body = await setup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        Assert.Equal("local", body.GetProperty("mode").GetString());
        Assert.Equal("admin", body.GetProperty("user").GetProperty("username").GetString());
        var cookie = setup.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("SpeedtestDashboard.Session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);

        var accounts = factory.Services.GetRequiredService<LocalAccountService>();
        Assert.Equal(1, await accounts.GetAccountCountAsync());
        var user = await accounts.FindByIdAsync(body.GetProperty("user").GetProperty("id").GetGuid());
        Assert.NotNull(user);
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.StartsWith("AQAAAA", user.PasswordHash);
        Assert.True((await accounts.GetSettingsAsync()).AuthenticationEnabled);
    }

    [Fact]
    public async Task SetupRejectsPasswordsBelowSixCharacters()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var token = await GetCsrfAsync(client);
        using var response = await PostWithCsrfAsync(client, "/api/auth/setup", token,
            new { username = "admin", password = "abcde" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_credentials", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task SetupAcceptsASixCharacterPassword()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var token = await GetCsrfAsync(client);
        using var response = await PostWithCsrfAsync(client, "/api/auth/setup", token,
            new { username = "admin", password = "abcdef" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetupAcceptsLongPassphrasesWithNoCompositionRules()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var token = await GetCsrfAsync(client);
        const string passphrase = "correct horse battery staple and a few more words for length";
        using var response = await PostWithCsrfAsync(client, "/api/auth/setup", token,
            new { username = "admin", password = passphrase });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HttpCredentialSetupRequiresExplicitLanOverride()
    {
        using var secureFactory = new AuthWebApplicationFactory();
        using var insecureClient = secureFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });
        using var rejected = await insecureClient.PostAsJsonAsync("/api/auth/setup",
            new { username = "admin", password = Password });
        Assert.Equal("https_required", await ProblemCodeAsync(rejected));

        using var lanFactory = new AuthWebApplicationFactory(allowInsecureHttp: true);
        using var lanClient = lanFactory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false
        });
        using var accepted = await SetupAsync(lanClient);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var cookie = accepted.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("SpeedtestDashboard.Session=", StringComparison.Ordinal));
        Assert.DoesNotContain("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisablingLoginDeletesTheAccountAndAllowsFreshCredentials()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateHttpsClient();
        using var setup = await SetupAsync(client);

        var token = await GetCsrfAsync(client);
        using var wrong = await PostWithCsrfAsync(client, "/api/auth/disable", token,
            new { currentPassword = "not the password" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        using var disabled = await PostWithCsrfAsync(client, "/api/auth/disable", token,
            new { currentPassword = Password });
        var disabledSession = await disabled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.Equal("none", disabledSession.GetProperty("mode").GetString());
        Assert.False(disabledSession.GetProperty("loginConfigured").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/providers")).StatusCode);
        Assert.Equal(0, await factory.Services.GetRequiredService<LocalAccountService>().GetAccountCountAsync());

        token = await GetCsrfAsync(client);
        using var reenabled = await PostWithCsrfAsync(client, "/api/auth/setup", token,
            new { username = "new-admin", password = "a-new-password" });
        Assert.Equal(HttpStatusCode.OK, reenabled.StatusCode);

        token = await GetCsrfAsync(client);
        using var logout = await PostWithCsrfAsync(client, "/api/auth/logout", token, new { });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/providers")).StatusCode);

        token = await GetCsrfAsync(client);
        using var oldCredentials = await PostWithCsrfAsync(client, "/api/auth/login", token,
            new { username = "admin", password = Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldCredentials.StatusCode);

        token = await GetCsrfAsync(client);
        using var newCredentials = await PostWithCsrfAsync(client, "/api/auth/login", token,
            new { username = "new-admin", password = "a-new-password" });
        Assert.Equal(HttpStatusCode.OK, newCredentials.StatusCode);
    }

    [Fact]
    public async Task PasswordChangeInvalidatesAnotherBrowserButKeepsCurrentBrowser()
    {
        using var factory = new AuthWebApplicationFactory(validateSecurityStampImmediately: true);
        using var first = factory.CreateHttpsClient();
        using var setup = await SetupAsync(first);
        using var second = factory.CreateHttpsClient();
        await LoginAsync(second);

        var token = await GetCsrfAsync(first);
        using var changed = await PostWithCsrfAsync(first, "/api/auth/change-password", token,
            new { currentPassword = Password, newPassword = "another memorable passphrase" });

        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync("/api/providers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetAsync("/api/providers")).StatusCode);
    }

    [Fact]
    public async Task PersistedSettingsKeysAndCookieSurviveApplicationReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "speedtest-auth-tests", Guid.NewGuid().ToString("N"));
        string cookie;
        using (var firstFactory = new AuthWebApplicationFactory(root))
        using (var first = firstFactory.CreateHttpsClient(handleCookies: false))
        {
            var csrf = await GetCsrfWithCookieAsync(first);
            using var setupRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/setup")
            {
                Content = JsonContent.Create(new { username = "admin", password = Password })
            };
            setupRequest.Headers.Add("X-CSRF-TOKEN", csrf.Token);
            setupRequest.Headers.Add("Cookie", csrf.Cookie);
            using var response = await first.SendAsync(setupRequest);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            cookie = response.Headers.GetValues("Set-Cookie")
                .Single(value => value.StartsWith("SpeedtestDashboard.Session=", StringComparison.Ordinal))
                .Split(';', 2)[0];
        }

        using (var secondFactory = new AuthWebApplicationFactory(root))
        using (var second = secondFactory.CreateHttpsClient(handleCookies: false))
        using (var request = new HttpRequestMessage(HttpMethod.Get, "/api/providers"))
        {
            request.Headers.Add("Cookie", cookie);
            using var response = await second.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Directory.Delete(root, recursive: true);
    }

    private static async Task<HttpResponseMessage> SetupAsync(HttpClient client)
    {
        var token = await GetCsrfAsync(client);
        return await PostWithCsrfAsync(client, "/api/auth/setup", token,
            new { username = "admin", password = Password });
    }

    private static async Task LoginAsync(HttpClient client)
    {
        var token = await GetCsrfAsync(client);
        using var response = await PostWithCsrfAsync(client, "/api/auth/login", token,
            new { username = "admin", password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var response = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        return response.GetProperty("token").GetString()!;
    }

    private static async Task<(string Token, string Cookie)> GetCsrfWithCookieAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cookie = response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];
        return (body.GetProperty("token").GetString()!, cookie);
    }

    private static Task<HttpResponseMessage> PostWithCsrfAsync(HttpClient client, string url, string token, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return client.SendAsync(request);
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString();
    }
}
