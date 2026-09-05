using System.Collections.Concurrent;

namespace SpeedtestDashboard.Api.Tests.Network;

internal sealed class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) => handler(request, cancellationToken);
}

internal sealed class FakeHttpClientFactory : IHttpClientFactory, IDisposable
{
    private readonly ConcurrentDictionary<string, HttpClient> _clients = new(StringComparer.Ordinal);

    public void Add(string name, HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        _clients[name] = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.test/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };
    }

    public HttpClient CreateClient(string name) => _clients[name];

    public void Dispose()
    {
        foreach (var client in _clients.Values)
        {
            client.Dispose();
        }
    }
}

