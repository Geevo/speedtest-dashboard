using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SpeedtestDashboard.Api.Endpoints;

namespace SpeedtestDashboard.Api.Tests.ApiKeys;

public sealed class ApiKeyEndpointTests
{
    [Fact]
    public async Task GenerateApiKey_ReturnsStdPrefixedKeyWithNoStoreCache()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsync("/api/api-key/regenerate", null);
        var body = await response.Content.ReadFromJsonAsync<ApiKeyResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.NotNull(body);
        Assert.True(body!.Enabled);
        Assert.StartsWith("std_", body.Key);
        Assert.NotNull(body.CreatedAtUtc);
        Assert.Null(body.LastUsedAtUtc);
    }

    [Fact]
    public async Task ViewApiKey_ReturnsTheFullSecretAgain()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();

        var generated = await (await client.PostAsync("/api/api-key/regenerate", null))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();

        using var viewResponse = await client.GetAsync("/api/api-key");
        var viewed = await viewResponse.Content.ReadFromJsonAsync<ApiKeyResponse>();

        Assert.Equal(HttpStatusCode.OK, viewResponse.StatusCode);
        Assert.Equal("no-store", viewResponse.Headers.CacheControl?.ToString());
        Assert.Equal(generated!.Key, viewed!.Key);
    }

    [Fact]
    public async Task NoKeyConfigured_ReportsDisabled()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<ApiKeyResponse>("/api/api-key");

        Assert.False(body!.Enabled);
        Assert.Null(body.Key);
    }

    [Fact]
    public async Task RegenerateApiKey_InvalidatesThePreviousKey()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();

        var first = await (await client.PostAsync("/api/api-key/regenerate", null))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();
        var second = await (await client.PostAsync("/api/api-key/regenerate", null))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();

        Assert.NotEqual(first!.Key, second!.Key);

        using var oldKeyResponse = await ApiKeyTestHelpers.AuthorizedClient(factory, first.Key!)
            .GetAsync("/api/v1/providers");
        using var newKeyResponse = await ApiKeyTestHelpers.AuthorizedClient(factory, second.Key!)
            .GetAsync("/api/v1/providers");

        Assert.Equal(HttpStatusCode.Unauthorized, oldKeyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, newKeyResponse.StatusCode);
    }

    [Fact]
    public async Task RevokeApiKey_InvalidatesTheKeyAndReturns404OnSecondRevoke()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();

        var generated = await (await client.PostAsync("/api/api-key/regenerate", null))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();

        using var revoked = await client.DeleteAsync("/api/api-key");
        using var revokedAgain = await client.DeleteAsync("/api/api-key");
        using var afterRevoke = await ApiKeyTestHelpers.AuthorizedClient(factory, generated!.Key!)
            .GetAsync("/api/v1/providers");
        var status = await client.GetFromJsonAsync<ApiKeyResponse>("/api/api-key");

        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revokedAgain.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
        Assert.False(status!.Enabled);
    }

    [Fact]
    public async Task ApiKey_SurvivesApplicationReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "speedtest-apikey-tests", Guid.NewGuid().ToString("N"));
        string? key;
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "speedtest.db");
        var dataProtectionPath = Path.Combine(root, "dataprotection");

        using (var firstFactory = new DashboardWebApplicationFactory())
        using (var pinnedFactory = firstFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:DatabasePath"] = databasePath,
                    ["Authentication:DataProtectionPath"] = dataProtectionPath
                }))))
        using (var client = pinnedFactory.CreateClient())
        {
            var generated = await (await client.PostAsync("/api/api-key/regenerate", null))
                .Content.ReadFromJsonAsync<ApiKeyResponse>();
            key = generated!.Key;
        }

        using (var secondFactory = new DashboardWebApplicationFactory())
        using (var pinnedFactory = secondFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storage:DatabasePath"] = databasePath,
                    ["Authentication:DataProtectionPath"] = dataProtectionPath
                }))))
        {
            using var response = await ApiKeyTestHelpers.AuthorizedClient(pinnedFactory, key!)
                .GetAsync("/api/v1/providers");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task LastUsedAtUtc_UpdatesOnlyAfterSuccessfulAuthentication()
    {
        using var factory = new DashboardWebApplicationFactory();
        using var client = factory.CreateClient();
        var generated = await (await client.PostAsync("/api/api-key/regenerate", null))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();

        using var wrongKeyResponse = await ApiKeyTestHelpers.AuthorizedClient(factory, "std_not-the-real-key")
            .GetAsync("/api/v1/providers");
        var afterFailure = await client.GetFromJsonAsync<ApiKeyResponse>("/api/api-key");

        using var correctKeyResponse = await ApiKeyTestHelpers.AuthorizedClient(factory, generated!.Key!)
            .GetAsync("/api/v1/providers");
        var afterSuccess = await client.GetFromJsonAsync<ApiKeyResponse>("/api/api-key");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongKeyResponse.StatusCode);
        Assert.Null(afterFailure!.LastUsedAtUtc);
        Assert.Equal(HttpStatusCode.OK, correctKeyResponse.StatusCode);
        Assert.NotNull(afterSuccess!.LastUsedAtUtc);
    }

    [Fact]
    public async Task ApiKey_IsNeverLogged()
    {
        var capturing = new CapturingLoggerProvider();
        using var factory = new DashboardWebApplicationFactory();
        using var loggedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(capturing)));
        using var client = loggedFactory.CreateClient();

        var generated = await (await client.PostAsync("/api/api-key/regenerate", null))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();
        Assert.NotNull(generated?.Key);

        using var correctResponse = await ApiKeyTestHelpers.AuthorizedClient(loggedFactory, generated!.Key!)
            .GetAsync("/api/v1/providers");
        using var wrongResponse = await ApiKeyTestHelpers.AuthorizedClient(loggedFactory, "std_wrong-value")
            .GetAsync("/api/v1/providers");

        Assert.DoesNotContain(capturing.Messages, message => message.Contains(generated.Key!, StringComparison.Ordinal));
    }
}
