using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Infrastructure.Providers;

namespace SpeedtestDashboard.Api.Tests.Providers;

public sealed class SpeedTestProviderRegistryTests
{
    [Fact]
    public void RegisteredProvider_ResolvesWithCapabilitiesPreserved()
    {
        var provider = new FakeSpeedTestProvider
        {
            Capabilities = ProviderCapabilities.Download | ProviderCapabilities.IPv6
        };
        var registry = new SpeedTestProviderRegistry([provider]);

        var found = registry.TryGet(provider.Id, out var resolved);

        Assert.True(found);
        Assert.Same(provider, resolved);
        Assert.Equal(ProviderCapabilities.Download | ProviderCapabilities.IPv6, resolved.Capabilities);
        Assert.Equal([provider], registry.GetAll());
    }

    [Fact]
    public void ProductionProviders_ListLibreSpeedFirstRegardlessOfRegistrationOrder()
    {
        var ookla = new FakeSpeedTestProvider { Id = ProviderId.Ookla };
        var libreSpeed = new FakeSpeedTestProvider { Id = ProviderId.LibreSpeed };
        var registry = new SpeedTestProviderRegistry([ookla, libreSpeed]);

        Assert.Equal([libreSpeed, ookla], registry.GetAll());
    }

    [Fact]
    public void UnknownProvider_DoesNotResolveOrFallback()
    {
        var provider = new FakeSpeedTestProvider();
        var registry = new SpeedTestProviderRegistry([provider]);

        Assert.False(registry.TryGet(ProviderId.Ookla, out _));
    }

    [Fact]
    public void DuplicateProviderIds_FailClearly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new SpeedTestProviderRegistry(
        [
            new FakeSpeedTestProvider(),
            new FakeSpeedTestProvider()
        ]));

        Assert.Contains("Duplicate speed-test provider ID 'fixture'", exception.Message);
    }

    [Theory]
    [InlineData("ookla", true)]
    [InlineData("librespeed", true)]
    [InlineData("fixture-2", true)]
    [InlineData("Fixture", false)]
    [InlineData("bad_id", false)]
    [InlineData("", false)]
    public void ProviderId_ValidatesCanonicalLowercaseIdentifiers(string value, bool expected)
    {
        Assert.Equal(expected, ProviderId.TryParse(value, out _));
    }
}
