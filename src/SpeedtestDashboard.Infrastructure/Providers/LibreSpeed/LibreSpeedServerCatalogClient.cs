using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

public sealed class LibreSpeedServerCatalogClient(
    HttpClient httpClient,
    LibreSpeedServerCatalogParser parser,
    IOptions<LibreSpeedOptions> options,
    ILogger<LibreSpeedServerCatalogClient> logger)
{
    private const int MaximumResponseBytes = 2 * 1024 * 1024;
    private static readonly Uri CatalogUri = new(LibreSpeedOptions.ServerCatalogUrl);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<IReadOnlyList<LibreSpeedServerDefinition>> GetServersAsync(
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.ServerListTimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, CatalogUri);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SpeedtestDashboard", "0.6.0"));
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaximumResponseBytes)
            {
                throw new LibreSpeedOutputException("LibreSpeed returned an oversized server catalogue.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var bytes = await ReadBoundedAsync(stream, timeout.Token);
            return parser.Parse(StrictUtf8.GetString(bytes), options.Value.MaximumServers);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LibreSpeedOutputException("LibreSpeed server discovery timed out.");
        }
        catch (LibreSpeedOutputException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or DecoderFallbackException or IOException)
        {
            logger.LogWarning(exception, "LibreSpeed server catalogue retrieval failed.");
            throw new LibreSpeedOutputException("LibreSpeed server discovery failed.");
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0)
                {
                    return output.ToArray();
                }

                if (output.Length + read > MaximumResponseBytes)
                {
                    throw new LibreSpeedOutputException("LibreSpeed returned an oversized server catalogue.");
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
