using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Providers.Ookla;

namespace SpeedtestDashboard.Api.Tests.Ookla;

public sealed class OoklaCommandFactoryTests
{
    [Fact]
    public void VersionCommand_IsShortBoundedAndDoesNotAcceptLicenses()
    {
        var command = CreateFactory(accepted: true).CreateVersionCommand();

        Assert.Equal("/opt/fixture/speedtest", command.Executable);
        Assert.Equal(["--version"], command.ArgumentList);
        Assert.Equal(TimeSpan.FromSeconds(5), command.Timeout);
        Assert.Equal(Path.GetTempPath(), command.WorkingDirectory);
        Assert.Equal(Path.GetTempPath(), command.EnvironmentVariables?["HOME"]);
    }

    [Fact]
    public void ServerListCommand_UsesLongFormAndExplicitAcceptanceFlags()
    {
        var command = CreateFactory(accepted: true).CreateServerListCommand();

        Assert.Equal(["--accept-license", "--accept-gdpr", "--servers"], command.ArgumentList);
        Assert.Equal(TimeSpan.FromSeconds(30), command.Timeout);
    }

    [Fact]
    public void AutomaticTest_RequestsJsonAndDisablesProgressWithoutServerSelector()
    {
        var command = CreateFactory(accepted: true).CreateTestCommand(null);

        Assert.Equal(
            ["--accept-license", "--accept-gdpr", "--format=json", "--progress=no"],
            command.ArgumentList);
        Assert.DoesNotContain(command.ArgumentList, argument => argument.StartsWith("--server-id", StringComparison.Ordinal));
        Assert.Equal(TimeSpan.FromSeconds(180), command.Timeout);
    }

    [Fact]
    public void ExplicitTest_AddsExactlyOneValidatedServerSelector()
    {
        var command = CreateFactory(accepted: true).CreateTestCommand("12345");

        Assert.Equal("--server-id=12345", command.ArgumentList[^1]);
        Assert.Single(command.ArgumentList, argument => argument.StartsWith("--server-id", StringComparison.Ordinal));
    }

    [Fact]
    public void CommandsWithoutAcceptanceConfiguration_DoNotInventConsent()
    {
        var command = CreateFactory(accepted: false).CreateTestCommand(null);

        Assert.DoesNotContain("--accept-license", command.ArgumentList);
        Assert.DoesNotContain("--accept-gdpr", command.ArgumentList);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0123")]
    [InlineData("123;touch /tmp/pwned")]
    [InlineData("123 456")]
    [InlineData("12345678901")]
    public void InvalidServerIds_AreRejectedBeforeProcessConstruction(string serverId)
    {
        Assert.Throws<ArgumentException>(() => CreateFactory(accepted: true).CreateTestCommand(serverId));
    }

    private static OoklaCommandFactory CreateFactory(bool accepted)
    {
        return new OoklaCommandFactory(Options.Create(OoklaTestFactory.Options(accepted)));
    }
}
