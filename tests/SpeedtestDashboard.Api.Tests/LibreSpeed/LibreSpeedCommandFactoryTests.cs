using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Providers.LibreSpeed;

namespace SpeedtestDashboard.Api.Tests.LibreSpeed;

public sealed class LibreSpeedCommandFactoryTests
{
    [Fact]
    public void VersionCommand_IsShortAndBounded()
    {
        var command = CreateFactory().CreateVersionCommand();

        Assert.Equal("/opt/fixture/librespeed-cli", command.Executable);
        Assert.Equal(["--version"], command.ArgumentList);
        Assert.Equal(TimeSpan.FromSeconds(5), command.Timeout);
        Assert.Equal(Path.GetTempPath(), command.WorkingDirectory);
        Assert.Equal(Path.GetTempPath(), command.EnvironmentVariables?["HOME"]);
    }

    [Fact]
    public void AutomaticTest_UsesJsonHttpPingAndHttpsWithoutServerSelector()
    {
        var command = CreateFactory().CreateTestCommand(null);

        Assert.Equal(["--json", "--no-icmp", "--secure"], command.ArgumentList);
        Assert.Equal(TimeSpan.FromSeconds(180), command.Timeout);
        Assert.DoesNotContain("--server", command.ArgumentList);
    }

    [Fact]
    public void ExplicitTest_UsesSeparateValidatedServerArguments()
    {
        var command = CreateFactory().CreateTestCommand("49");

        Assert.Equal(["--json", "--no-icmp", "--secure", "--server", "49"], command.ArgumentList);
        Assert.Equal(1, command.ArgumentList.Count(argument => argument == "--server"));
    }

    [Fact]
    public void NormalCommands_NeverEnableTelemetrySharingOrUnsafeControls()
    {
        var commands = new[] { CreateFactory().CreateTestCommand(null), CreateFactory().CreateTestCommand("49") };
        var forbidden = new[]
        {
            "--share", "--telemetry-json", "--telemetry-server", "--telemetry-path",
            "--telemetry-share", "--telemetry-extra", "--skip-cert-verify", "--interface",
            "--source", "--fwmark", "--server-json", "--local-json"
        };

        Assert.All(commands, command =>
            Assert.DoesNotContain(command.ArgumentList, argument => forbidden.Contains(argument, StringComparer.Ordinal)));
    }

    [Fact]
    public void AdministrativeOptions_CanDisableHttpPingAndHttpsPreference()
    {
        var options = LibreSpeedTestFactory.Options();
        options.DisableIcmp = false;
        options.PreferHttps = false;

        var command = CreateFactory(options).CreateTestCommand(null);

        Assert.Equal(["--json"], command.ArgumentList);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("2147483648")]
    [InlineData("49;touch /tmp/pwned")]
    [InlineData("49 50")]
    public void InvalidServerIds_AreRejectedBeforeProcessConstruction(string serverId)
    {
        Assert.Throws<ArgumentException>(() => CreateFactory().CreateTestCommand(serverId));
    }

    private static LibreSpeedCommandFactory CreateFactory(LibreSpeedOptions? options = null) =>
        new(Options.Create(options ?? LibreSpeedTestFactory.Options()));
}
