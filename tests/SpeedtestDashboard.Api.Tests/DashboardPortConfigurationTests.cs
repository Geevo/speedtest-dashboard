using SpeedtestDashboard.Api.Hosting;

namespace SpeedtestDashboard.Api.Tests;

public sealed class DashboardPortConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetListenUrl_WhenUnset_LeavesAspNetCoreConfigurationAlone(string? value)
    {
        Assert.Null(DashboardPortConfiguration.GetListenUrl(value));
    }

    [Theory]
    [InlineData("1", "http://*:1")]
    [InlineData("8008", "http://*:8008")]
    [InlineData(" 8080 ", "http://*:8080")]
    [InlineData("65535", "http://*:65535")]
    public void GetListenUrl_WhenValid_ReturnsListenUrl(string value, string expected)
    {
        Assert.Equal(expected, DashboardPortConfiguration.GetListenUrl(value));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("8008.5")]
    [InlineData("http://localhost:8008")]
    public void GetListenUrl_WhenInvalid_ThrowsClearError(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => DashboardPortConfiguration.GetListenUrl(value));

        Assert.Equal("DASHBOARD_PORT must be a whole number between 1 and 65535.", exception.Message);
    }
}
