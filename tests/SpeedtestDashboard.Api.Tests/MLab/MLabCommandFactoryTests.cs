using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Providers.MLab;

namespace SpeedtestDashboard.Api.Tests.MLab;

public sealed class MLabCommandFactoryTests
{
    [Fact]
    public void HealthCommandUsesBoundedHelpProbe()
    {
        var command = CreateFactory().CreateHealthCommand();

        Assert.Equal("/opt/fixture/mlab-ndt7-client", command.Executable);
        Assert.Equal(["-help"], command.ArgumentList);
        Assert.Equal(TimeSpan.FromSeconds(5), command.Timeout);
        Assert.Equal("/tmp", command.WorkingDirectory);
        Assert.Equal("/tmp", command.EnvironmentVariables?["HOME"]);
    }

    [Fact]
    public void TestCommandUsesQuietJsonAndAutomaticDiscovery()
    {
        var options = MLabTestFactory.Options();
        options.ClientTimeoutSeconds = 47;

        var command = CreateFactory(options).CreateTestCommand();

        Assert.Equal(
            ["-format=json", "-quiet", "-client-name=speedtest-dashboard", "-timeout=47s", "-download=true", "-upload=true"],
            command.ArgumentList);
        Assert.Equal(TimeSpan.FromSeconds(75), command.Timeout);
        Assert.DoesNotContain(command.ArgumentList, argument => argument.StartsWith("-server", StringComparison.Ordinal));
    }

    private static MLabCommandFactory CreateFactory(MLabOptions? options = null) =>
        new(Options.Create(options ?? MLabTestFactory.Options()));
}
