using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeedtestDashboard.Api.Authentication;
using SpeedtestDashboard.Api.Tests.ApiKeys;
using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Infrastructure.Authentication;
using SpeedtestDashboard.Infrastructure.Persistence;

namespace SpeedtestDashboard.Api.Tests.Authentication;

public sealed class AuthenticationRegressionTests
{
    [Fact]
    public async Task DefaultHttpAnonymousBrowserCanInitializeMutationProtection()
    {
        using var factory = new AuthWebApplicationFactory();
        using var client = factory.CreateClient();
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        Assert.Equal("none", session.GetProperty("mode").GetString());
        using var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.DoesNotContain("; secure", cookie, StringComparison.OrdinalIgnoreCase);
        var csrf = await response.Content.ReadFromJsonAsync<JsonElement>();
        using var missingToken = await client.PostAsJsonAsync("/api/auth/preferences", new { showDisabledWarning = false });
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        using var preference = await client.PostAsJsonAsync("/api/auth/preferences", new { showDisabledWarning = false });
        Assert.Equal(HttpStatusCode.OK, preference.StatusCode);
        using var setup = await client.PostAsJsonAsync("/api/auth/setup", new { username = "admin", password = "review-password" });
        Assert.Equal(HttpStatusCode.BadRequest, setup.StatusCode);
        var problem = await setup.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https_required", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AnonymousHttpBrowserCanQueueATestAfterCsrfBootstrap()
    {
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider(), runWorker: false);
        using var client = factory.CreateClient();
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        using var response = await client.PostAsJsonAsync("/api/tests", new { providerId = "fixture" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentPreferenceChangeDoesNotDisableNewlyEnabledAuthentication()
    {
        var state = new ObservedAuthenticationState();
        using var parent = new AuthWebApplicationFactory();
        using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DashboardAuthenticationState>();
            services.AddSingleton<DashboardAuthenticationState>(state);
        }));
        using var preferences = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var setup = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        async Task AddCsrfAsync(HttpClient client)
        {
            var body = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", body.GetProperty("token").GetString());
        }
        await AddCsrfAsync(preferences);
        await AddCsrfAsync(setup);
        var pendingPreference = preferences.PostAsJsonAsync("/api/auth/preferences", new { showDisabledWarning = false });
        Task<HttpResponseMessage> enabling;
        try
        {
            await state.FirstMutationEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            enabling = setup.PostAsJsonAsync("/api/auth/setup", new { username = "admin", password = "review-password" });
            // Observe the real semaphore wait, not elapsed time or HTTP scheduling.
            var setupIsWaiting = await state.SecondMutationWait.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(setupIsWaiting, "Setup must wait while preferences hold the authentication mutation lock.");
            Assert.False(enabling.IsCompleted);
        }
        finally
        {
            state.ReleaseFirstMutation.TrySetResult();
        }
        using var enabled = await enabling;
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        using var preferenceResponse = await pendingPreference;
        Assert.Equal(HttpStatusCode.OK, preferenceResponse.StatusCode);
        using var anonymous = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var protectedResponse = await anonymous.GetAsync("/api/history");
        Assert.Equal(HttpStatusCode.Unauthorized, protectedResponse.StatusCode);
        var settings = await factory.Services.GetRequiredService<LocalAccountService>().GetSettingsAsync();
        Assert.True(settings.AuthenticationEnabled);
        Assert.False(settings.ShowDisabledWarning);
        using var httpClient = factory.CreateClient();
        using var httpCsrf = await httpClient.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.BadRequest, httpCsrf.StatusCode);
        var httpProblem = await httpCsrf.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https_required", httpProblem.GetProperty("code").GetString());
        using var httpsCsrf = await anonymous.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, httpsCsrf.StatusCode);
        Assert.Contains("; secure", httpsCsrf.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConcurrentFailedLoginsReachLockoutWithoutLostUpdates()
    {
        using var parent = new AuthWebApplicationFactory();
        using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordHasher<ApplicationUser>>();
            services.AddSingleton<IPasswordHasher<ApplicationUser>, CoordinatedPasswordHasher>();
        }));
        using var client = factory.CreateClient(new()
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        using var setup = await client.PostAsJsonAsync("/api/auth/setup", new
        {
            username = "admin",
            password = "review-password"
        });
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        var accounts = factory.Services.GetRequiredService<LocalAccountService>();

        var attempts = Enumerable.Range(0, 5)
            .Select(_ => Task.Run(() => accounts.AuthenticateAsync("admin", CoordinatedPasswordHasher.InvalidPassword)))
            .ToArray();
        var results = await Task.WhenAll(attempts);

        Assert.Equal(4, results.Count(result => result.Status == LocalLoginStatus.InvalidCredentials));
        Assert.Single(results, result => result.Status == LocalLoginStatus.LockedOut);
        var locked = await accounts.AuthenticateAsync("admin", "review-password");
        Assert.Equal(LocalLoginStatus.LockedOut, locked.Status);

        var connectionFactory = factory.Services.GetRequiredService<SqliteConnectionFactory>();
        await using var connection = await connectionFactory.OpenConnectionAsync();
        await using var command = connectionFactory.CreateCommand(connection,
            "SELECT AccessFailedCount, LockoutEndUtc IS NOT NULL FROM Users WHERE NormalizedUserName = 'ADMIN';");
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(0, reader.GetInt32(0));
        Assert.True(reader.GetBoolean(1));
    }

    private sealed class ObservedAuthenticationState : DashboardAuthenticationState
    {
        private int _mutationCalls;
        public TaskCompletionSource<bool> SecondMutationWait { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FirstMutationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstMutation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async Task WaitForMutationAsync(CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _mutationCalls);
            var wait = base.WaitForMutationAsync(cancellationToken);
            if (call == 2)
            {
                SecondMutationWait.TrySetResult(!wait.IsCompleted);
            }
            await wait;
            if (call == 1)
            {
                FirstMutationEntered.TrySetResult();
                await ReleaseFirstMutation.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class CoordinatedPasswordHasher : IPasswordHasher<ApplicationUser>
    {
        public const string InvalidPassword = "wrong-password";
        private readonly PasswordHasher<ApplicationUser> _inner = new();
        private readonly ManualResetEventSlim _release = new();
        private int _invalidVerifications;

        public string HashPassword(ApplicationUser user, string password) =>
            _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(
            ApplicationUser user,
            string hashedPassword,
            string providedPassword)
        {
            if (providedPassword == InvalidPassword)
            {
                if (Interlocked.Increment(ref _invalidVerifications) >= 2)
                {
                    _release.Set();
                }
                _release.Wait(TimeSpan.FromMilliseconds(250));
            }

            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
