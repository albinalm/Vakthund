using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vakthund.UI.Components;
using Vakthund.UI.Extensions;
using Vakthund.UI.Services;

namespace Vakthund.Tests.Helpers;

public sealed class UiAuthenticationTestHost : IAsyncDisposable
{
    private readonly string _keyDirectory;
    public WebApplication App { get; }
    public HttpClient Client { get; private set; } = null!;
    public CookieContainer Cookies { get; } = new();
    public TestOidcProvider Provider { get; } = new();
    public string PathBase { get; }
    public bool HasExpiringBlazorConnections => ((IEndpointRouteBuilder)App).DataSources.SelectMany(source => source.Endpoints)
        .Select(endpoint => endpoint.Metadata.GetMetadata<HttpConnectionDispatcherOptions>()).Any(options => options?.CloseOnAuthenticationExpiration == true);
    private UiAuthenticationTestHost(bool enabled, string pathBase, int? sessionSeconds)
    {
        PathBase = pathBase;
        _keyDirectory = Path.Combine(Path.GetTempPath(), "vakthund-oidc-test-" + Guid.NewGuid().ToString("N"));
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(App).Assembly.GetName().Name,
            ContentRootPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Vakthund.UI")),
            EnvironmentName = "Development"
        });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UI:Authentication:Enabled"] = enabled.ToString(),
            ["UI:Authentication:Authority"] = TestOidcProvider.Issuer,
            ["UI:Authentication:ClientId"] = TestOidcProvider.ClientId,
            ["UI:Authentication:AllowedSubjects:0"] = "operator-123",
            ["UI:PathBase"] = pathBase,
            ["UI:ForwardedHeaders:KnownProxies:0"] = "10.0.0.10",
            ["UI:ForwardedHeaders:AllowedHosts:0"] = "audit.example.test",
            ["Proxy:AuditHubUrl"] = "http://127.0.0.1:1/connect/audit",
            ["DataProtection:KeysPath"] = _keyDirectory
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddVakthundUi(builder.Configuration);
        builder.Services.AddServerSideBlazor().AddHubOptions(options => options.SupportedProtocols = ["blazorpack", "json"]);
        if (enabled)
        {
            builder.Services.Configure<OpenIdConnectOptions>(UiAuthenticationServiceCollectionExtensions.OidcScheme, options =>
            {
                options.Configuration = Provider.Configuration;
                options.BackchannelHttpHandler = Provider;
                options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
            });
            if (sessionSeconds.HasValue)
                builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(
                    UiAuthenticationServiceCollectionExtensions.CookieScheme, options => options.ExpireTimeSpan = TimeSpan.FromSeconds(sessionSeconds.Value));
        }
        App = builder.Build();
        App.UseVakthundUI();
        App.MapGet("/test/protected", () => Results.Text("protected audit content"));
        App.MapGet("/test/logout-token", (HttpContext context, IAntiforgery antiforgery) => Results.Text(antiforgery.GetAndStoreTokens(context).RequestToken!));
    }
    public static async Task<UiAuthenticationTestHost> StartAsync(bool enabled = true, string pathBase = "", int? sessionSeconds = null)
    {
        var host = new UiAuthenticationTestHost(enabled, pathBase, sessionSeconds);
        await host.App.StartAsync();
        host.Client = host.CreateClient(useCookies: true);
        return host;
    }
    public HttpClient CreateClient(bool useCookies = false) => new(useCookies
        ? new UiTestCookieHandler(Cookies, App.GetTestServer().CreateHandler())
        : App.GetTestServer().CreateHandler()) { BaseAddress = new Uri("https://vakthund.test") };
    public async Task<HttpResponseMessage> ChallengeAsync(string returnUrl = "/")
    {
        string page = await Client.GetStringAsync(PathBase + "/auth/login");
        Match token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(token.Success, "Login form must include an antiforgery token.");
        HttpResponseMessage response = await Client.PostAsync(PathBase + "/auth/sign-in", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value), ["returnUrl"] = returnUrl
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Dictionary<string, Microsoft.Extensions.Primitives.StringValues> parameters = QueryHelpers.ParseQuery(response.Headers.Location.Query);
        Provider.Nonce = parameters["nonce"].ToString();
        Provider.CodeChallenge = parameters["code_challenge"].ToString();
        return response;
    }
    public async Task<HttpResponseMessage> LoginAsync(string returnUrl = "/")
    {
        using HttpResponseMessage challenge = await ChallengeAsync(returnUrl);
        string state = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"].ToString();
        return await Client.PostAsync(PathBase + "/signin-oidc", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = "synthetic-authorization-code", ["state"] = state
        }));
    }
    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.DisposeAsync();
        Provider.Dispose();
        if (Directory.Exists(_keyDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(_keyDirectory))
                File.Delete(file);
            Directory.Delete(_keyDirectory);
        }
    }
}
