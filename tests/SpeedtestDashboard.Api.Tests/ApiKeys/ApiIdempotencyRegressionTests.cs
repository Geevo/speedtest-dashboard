using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeedtestDashboard.Api.Endpoints;
using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Infrastructure.ApiKeys;
using SpeedtestDashboard.Infrastructure.Persistence;

namespace SpeedtestDashboard.Api.Tests.ApiKeys;

public sealed class ApiIdempotencyRegressionTests
{
    [Fact]
    public async Task CancelledIdempotencyWaiterDoesNotBlockLaterRetries()
    {
        using var factory = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider(), runWorker: false);
        var store = factory.Services.GetRequiredService<IApiIdempotencyStore>();
        var first = await store.AcquireAsync("cancelled-waiter");
        using var cancellation = new CancellationTokenSource();
        try
        {
            var waiting = store.AcquireAsync("cancelled-waiter", cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }
        finally
        {
            first.Dispose();
        }
        using var retry = await store.AcquireAsync("cancelled-waiter").AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentKeysAdmitOnlyOneJob(bool differentRequest)
    {
        BarrierStore? store = null;
        using var parent = new ApiV1WebApplicationFactory(new FakeSpeedTestProvider(), runWorker: false);
        using var factory = parent.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IApiIdempotencyStore>();
            services.AddSingleton<IApiIdempotencyStore>(provider => store = new BarrierStore(new ApiIdempotencyStore(
                provider.GetRequiredService<SqliteConnectionFactory>(), TimeProvider.System)));
        }));
        var key = await ApiKeyTestHelpers.ProvisionApiKeyAsync(factory);
        using var client = ApiKeyTestHelpers.AuthorizedClient(factory, key);
        Task<HttpResponseMessage> CreateAsync(string providerId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tests")
            {
                Content = JsonContent.Create(new { providerId })
            };
            request.Headers.Add("Idempotency-Key", "concurrent-regression-key");
            return client.SendAsync(request);
        }
        // Pause after admission, before the first request records its key.
        store = (BarrierStore)factory.Services.GetRequiredService<IApiIdempotencyStore>();
        var first = CreateAsync("fixture");
        Task<HttpResponseMessage> second;
        try
        {
            await store.Saving.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = CreateAsync(differentRequest ? "different-provider" : "fixture");
            await store.SecondAcquire.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            store.Release.TrySetResult();
        }
        using var firstResponse = await first;
        using var secondResponse = await second;
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(differentRequest ? HttpStatusCode.Conflict : HttpStatusCode.Accepted, secondResponse.StatusCode);
        if (!differentRequest)
        {
            var firstBody = await firstResponse.Content.ReadFromJsonAsync<CreateTestResponse>();
            var secondBody = await secondResponse.Content.ReadFromJsonAsync<CreateTestResponse>();
            Assert.Equal(firstBody!.Id, secondBody!.Id);
        }
        var connectionFactory = factory.Services.GetRequiredService<SqliteConnectionFactory>();
        await using var database = await connectionFactory.OpenConnectionAsync();
        await using var command = connectionFactory.CreateCommand(database,
            "SELECT (SELECT COUNT(*) FROM SpeedTestJobs), (SELECT COUNT(*) FROM ApiIdempotencyRecords);");
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
    }

    private sealed class BarrierStore(IApiIdempotencyStore inner) : IApiIdempotencyStore
    {
        private int _acquisitions;
        public TaskCompletionSource Saving { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondAcquire { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _acquisitions) == 2) SecondAcquire.TrySetResult();
            return inner.AcquireAsync(key, cancellationToken);
        }

        public Task<ApiIdempotencyRecord?> TryGetAsync(string key, CancellationToken cancellationToken = default) =>
            inner.TryGetAsync(key, cancellationToken);

        public async Task SaveAsync(string key, string requestHash, Guid jobId, CancellationToken cancellationToken = default)
        {
            Saving.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            await inner.SaveAsync(key, requestHash, jobId, cancellationToken);
        }
    }
}
