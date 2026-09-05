using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SpeedtestDashboard.Api.Tests.Authentication;

internal sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _root;
    private readonly bool _ownsRoot;
    private readonly bool _validateSecurityStampImmediately;
    private readonly bool _allowInsecureHttp;

    public AuthWebApplicationFactory(
        string? root = null,
        bool validateSecurityStampImmediately = false,
        bool allowInsecureHttp = false)
    {
        _ownsRoot = root is null;
        _validateSecurityStampImmediately = validateSecurityStampImmediately;
        _allowInsecureHttp = allowInsecureHttp;
        _root = root ?? Path.Combine(Path.GetTempPath(), "speedtest-auth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;
    public string DatabasePath => Path.Combine(_root, "speedtest.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DatabasePath"] = DatabasePath,
                ["Authentication:AllowInsecureHttp"] = _allowInsecureHttp.ToString(),
                ["Authentication:DataProtectionPath"] = Path.Combine(_root, "dataprotection")
            }));
        builder.ConfigureTestServices(services =>
        {
            if (_validateSecurityStampImmediately)
            {
                services.PostConfigure<SecurityStampValidatorOptions>(options =>
                    options.ValidationInterval = TimeSpan.Zero);
            }
        });
    }

    public HttpClient CreateHttpsClient(bool handleCookies = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = handleCookies
    });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _ownsRoot && Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
