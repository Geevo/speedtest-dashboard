using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

namespace SpeedtestDashboard.Api.Tests.LibreSpeed;

public sealed class LibreSpeedServerCatalogClientTests
{
    [Fact]
    public async Task ClientUsesCanonicalServerSideUrlAndParsesResponse()
    {
        Uri? requestedUri = null;
        var client = CreateClient((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(LibreSpeedTestFactory.Fixture("servers-normal.json"))
            });
        });

        var servers = await client.GetServersAsync(CancellationToken.None);

        Assert.Equal(LibreSpeedOptions.ServerCatalogUrl, requestedUri?.AbsoluteUri);
        Assert.Equal(2, servers.Count);
    }

    [Fact]
    public async Task OversizedCatalogueIsRejectedBeforeParsing()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[2 * 1024 * 1024 + 1])
        };
        var client = CreateClient((_, _) => Task.FromResult(response));

        var exception = await Assert.ThrowsAsync<LibreSpeedOutputException>(() =>
            client.GetServersAsync(CancellationToken.None));

        Assert.Contains("oversized", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestTimeoutIsSanitized()
    {
        var options = LibreSpeedTestFactory.Options();
        options.ServerListTimeoutSeconds = 1;
        var client = CreateClient(
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException();
            },
            options);

        var exception = await Assert.ThrowsAsync<LibreSpeedOutputException>(() =>
            client.GetServersAsync(CancellationToken.None));

        Assert.Equal("LibreSpeed server discovery timed out.", exception.Message);
    }

    private static LibreSpeedServerCatalogClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        LibreSpeedOptions? options = null) => new(
            new HttpClient(new LibreSpeedTestFactory.StubHandler(handler))
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            new LibreSpeedServerCatalogParser(),
            Options.Create(options ?? LibreSpeedTestFactory.Options()),
            NullLogger<LibreSpeedServerCatalogClient>.Instance);
}
