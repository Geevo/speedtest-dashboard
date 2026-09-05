using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SpeedtestDashboard.Api.Tests.Authentication;

public sealed class AuthenticationNoneModeTests : IClassFixture<DashboardWebApplicationFactory>
{
    private readonly DashboardWebApplicationFactory _factory;

    public AuthenticationNoneModeTests(DashboardWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NoneModeReportsAuthenticatedInstanceAndDoesNotRequireCsrf()
    {
        using var client = _factory.CreateClient();
        var session = await client.GetFromJsonAsync<JsonElement>("/api/auth/session");
        using var mutation = await client.PostAsJsonAsync("/api/tests", new { providerId = "missing" });

        Assert.Equal("none", session.GetProperty("mode").GetString());
        Assert.True(session.GetProperty("authenticated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, session.GetProperty("user").ValueKind);
        Assert.NotEqual(HttpStatusCode.BadRequest, mutation.StatusCode);
    }
}
