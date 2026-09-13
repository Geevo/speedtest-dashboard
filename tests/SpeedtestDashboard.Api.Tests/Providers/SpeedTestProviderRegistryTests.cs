using SpeedtestDashboard.Api.Tests.Orchestration;
using SpeedtestDashboard.Core.Providers;
using SpeedtestDashboard.Infrastructure.Providers;

namespace SpeedtestDashboard.Api.Tests.Providers;

public sealed class SpeedTestProviderRegistryTests
{
    [Fact]
    public void ProviderId_DoesNotExposeABuiltInProviderCatalog()
    {
        var publicStaticFields = typeof(ProviderId).GetFields(
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

        Assert.Empty(publicStaticFields);
    }

    [Fact]
    public void RegisteredProvider_ResolvesWithCapabilitiesPreserved()
    {
        var provider = new FakeSpeedTestProvider
        {
            Capabilities = ProviderCapabilities.Download | ProviderCapabilities.IPv6
        };
        var registry = new SpeedTestProviderRegistry([provider]);

        var found = registry.TryGet(provider.Descriptor.Id, out var resolved);

        Assert.True(found);
        Assert.Same(provider, resolved);
        Assert.Equal(ProviderCapabilities.Download | ProviderCapabilities.IPv6, resolved.Descriptor.Capabilities);
        Assert.Equal([provider], registry.GetAll());
    }

    [Fact]
    public void Providers_AreOrderedByDescriptorRegardlessOfRegistrationOrder()
    {
        var last = new FakeSpeedTestProvider { Id = ProviderId.Parse("last"), DisplayOrder = 300 };
        var first = new FakeSpeedTestProvider { Id = ProviderId.Parse("first"), DisplayOrder = 100 };
        var middle = new FakeSpeedTestProvider { Id = ProviderId.Parse("middle"), DisplayOrder = 200 };
        var registry = new SpeedTestProviderRegistry([last, first, middle]);

        Assert.Equal([first, middle, last], registry.GetAll());
    }

    [Fact]
    public void EqualDisplayOrder_UsesProviderIdAsStableTieBreaker()
    {
        var second = new FakeSpeedTestProvider { Id = ProviderId.Parse("provider-b") };
        var first = new FakeSpeedTestProvider { Id = ProviderId.Parse("provider-a") };

        var registry = new SpeedTestProviderRegistry([second, first]);

        Assert.Equal([first, second], registry.GetAll());
    }

    [Fact]
    public void UnknownProvider_DoesNotResolveOrFallback()
    {
        var provider = new FakeSpeedTestProvider();
        var registry = new SpeedTestProviderRegistry([provider]);

        Assert.False(registry.TryGet(ProviderId.Parse("missing"), out _));
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
    [InlineData("fastcom", true)]
    [InlineData("mlab", true)]
    [InlineData("fixture-2", true)]
    [InlineData("Fixture", false)]
    [InlineData("bad_id", false)]
    [InlineData("", false)]
    public void ProviderId_ValidatesCanonicalLowercaseIdentifiers(string value, bool expected)
    {
        Assert.Equal(expected, ProviderId.TryParse(value, out _));
    }
}
