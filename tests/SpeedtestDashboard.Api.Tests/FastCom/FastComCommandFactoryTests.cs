using Microsoft.Extensions.Options;
using SpeedtestDashboard.Infrastructure.Providers.FastCom;

namespace SpeedtestDashboard.Api.Tests.FastCom;

public sealed class FastComCommandFactoryTests
{
    [Fact]
    public void VersionCommand_UsesBoundedHelpProbe()
    {
        var command = CreateFactory().CreateVersionCommand();

        Assert.Equal("/opt/fixture/fast-cli", command.Executable);
        Assert.Equal(["--help"], command.ArgumentList);
        Assert.Equal(TimeSpan.FromSeconds(5), command.Timeout);
        Assert.Equal(Path.GetTempPath(), command.WorkingDirectory);
        Assert.Equal(Path.GetTempPath(), command.EnvironmentVariables?["HOME"]);
    }

    [Fact]
    public void TestCommand_UsesUpstreamJsonUploadHttpsAndConfiguredDuration()
    {
        var options = FastComTestFactory.Options();
        options.DurationSeconds = 17;

        var command = CreateFactory(options).CreateTestCommand();

        Assert.Equal(["--https", "--upload", "--json", "--duration", "17"], command.ArgumentList);
        Assert.Equal(TimeSpan.FromSeconds(90), command.Timeout);
        Assert.DoesNotContain("--no-https", command.ArgumentList);
    }

    private static FastComCommandFactory CreateFactory(FastComOptions? options = null) =>
        new(Options.Create(options ?? FastComTestFactory.Options()));
}
