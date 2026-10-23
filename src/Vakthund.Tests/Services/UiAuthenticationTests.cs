using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vakthund.Tests.Helpers;
using Vakthund.UI.Extensions;

namespace Vakthund.Tests.Services;

public sealed class UiAuthenticationTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/requests")]
    [InlineData("/config")]
    [InlineData("/test/protected")]
    public async Task AnonymousRequests_CannotAccessUiPages(string path)
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        using HttpResponseMessage response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/login", response.Headers.Location!.AbsolutePath);
        Assert.DoesNotContain("protected audit content", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task AnonymousBlazorNegotiation_ReturnsUnauthorizedInsteadOfLoginHtml()
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        using HttpResponseMessage response = await host.Client.PostAsync("/_blazor/negotiate?negotiateVersion=1", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.True(host.HasExpiringBlazorConnections);
    }
    [Fact]
    public async Task AllowedSubject_CanCompleteSignedOidcLoginAndUseBlazor()
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        using HttpResponseMessage login = await host.LoginAsync("/requests");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/requests", login.Headers.Location!.OriginalString);
        using HttpResponseMessage page = await host.Client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using HttpResponseMessage negotiation = await host.Client.PostAsync("/_blazor/negotiate?negotiateVersion=1", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.OK, negotiation.StatusCode);
        Cookie session = Assert.Single(host.Cookies.GetCookies(host.Client.BaseAddress!).Cast<Cookie>(), cookie => cookie.Name == "__Host-vakthund");
        Assert.True(session.Secure);
        Assert.True(session.HttpOnly);
        Assert.DoesNotContain("operator-123", session.Value);
    }
    [Theory]
    [InlineData("other-user")]
    [InlineData("Operator-123")]
    public async Task UnlistedSubject_IsDeniedWithoutCreatingASession(string subject)
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        host.Provider.Subject = subject;
        using HttpResponseMessage login = await host.LoginAsync();
        Assert.Equal("/auth/access-denied", login.Headers.Location!.OriginalString);
        using HttpResponseMessage page = await host.Client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.DoesNotContain(host.Cookies.GetCookies(host.Client.BaseAddress!).Cast<Cookie>(), cookie => cookie.Name == "__Host-vakthund");
    }
    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expiration")]
    [InlineData("signature")]
    [InlineData("nonce")]
    public async Task InvalidIdToken_CannotCreateASession(string fault)
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        host.Provider.TokenIssuer = fault == "issuer" ? "https://untrusted.example.test" : TestOidcProvider.Issuer;
        host.Provider.Audience = fault == "audience" ? "different-client" : TestOidcProvider.ClientId;
        host.Provider.ExpiredToken = fault == "expiration";
        host.Provider.UseWrongSigningKey = fault == "signature";
        host.Provider.UseWrongNonce = fault == "nonce";
        using HttpResponseMessage login = await host.LoginAsync();
        Assert.Equal("/auth/error", login.Headers.Location!.OriginalString);
        using HttpResponseMessage page = await host.Client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
    }
    [Fact]
    public async Task LoginAndLogout_RequireAntiforgeryTokens()
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        using HttpResponseMessage login = await host.Client.PostAsync("/auth/sign-in", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        using HttpResponseMessage signedIn = await host.LoginAsync();
        using HttpResponseMessage logout = await host.Client.PostAsync("/auth/logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Equal("protected audit content", await host.Client.GetStringAsync("/test/protected"));
    }
    [Fact]
    public async Task Logout_RevokesTheServerSessionAndStartsProviderLogout()
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        using HttpResponseMessage login = await host.LoginAsync();
        string cookie = host.Cookies.GetCookieHeader(host.Client.BaseAddress!);
        string token = await host.Client.GetStringAsync("/test/logout-token");
        using HttpResponseMessage logout = await host.Client.PostAsync("/auth/logout", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal(TestOidcProvider.Issuer + "/logout", logout.Headers.Location!.GetLeftPart(UriPartial.Path));
        Assert.False(string.IsNullOrEmpty(QueryHelpers.ParseQuery(logout.Headers.Location.Query)["id_token_hint"]));
        using HttpClient replay = host.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(host.Client.BaseAddress!, "/test/protected"));
        request.Headers.Add("Cookie", cookie);
        using HttpResponseMessage response = await replay.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/auth/login", response.Headers.Location!.AbsolutePath);
    }
    [Theory]
    [InlineData("https://evil.example.test")]
    [InlineData("//evil.example.test")]
    [InlineData("/\\evil.example.test")]
    public async Task Login_DoesNotRedirectToAnExternalReturnUrl(string returnUrl)
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        using HttpResponseMessage response = await host.LoginAsync(returnUrl);
        Assert.Equal("/", response.Headers.Location!.OriginalString);
    }
    [Fact]
    public async Task PathBase_IsPreservedForLoginCallbackAndReturnNavigation()
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync(pathBase: "/audit");
        using HttpResponseMessage challenge = await host.ChallengeAsync("/audit/requests");
        string callback = QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["redirect_uri"].ToString();
        Assert.Equal("/audit/signin-oidc", new Uri(callback).AbsolutePath);
        using HttpResponseMessage login = await host.LoginAsync("/audit/requests");
        Assert.Equal("/audit/requests", login.Headers.Location!.OriginalString);
        Assert.Equal("protected audit content", await host.Client.GetStringAsync("/audit/test/protected"));
    }
    [Fact]
    public async Task ExpiredSession_CannotAccessProtectedPages()
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync(sessionSeconds: 1);
        using HttpResponseMessage login = await host.LoginAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(1200));
        using HttpResponseMessage response = await host.Client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
    [Fact]
    public async Task DisabledAuthentication_PreservesAnonymousLocalUse()
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync(enabled: false);
        Assert.Equal("protected audit content", await host.Client.GetStringAsync("/test/protected"));
        using HttpResponseMessage response = await host.Client.PostAsync("/_blazor/negotiate?negotiateVersion=1", new StringContent(string.Empty));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    [Fact]
    public async Task InvalidCallbackState_DoesNotCreateASessionOrExposeProviderDetails()
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        using HttpResponseMessage response = await host.Client.PostAsync("/signin-oidc", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = "synthetic-code", ["state"] = "forged-state"
        }));
        Assert.Equal("/auth/error", response.Headers.Location!.OriginalString);
        using HttpResponseMessage page = await host.Client.GetAsync("/test/protected");
        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
    }
    [Theory]
    [InlineData("/health")]
    [InlineData("/logo.svg")]
    public async Task PublicHealthAndStaticAssets_DoNotRequireALogin(string path)
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        using HttpResponseMessage response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    [Fact]
    public async Task ActiveBlazorConnection_ClosesWhenItsSessionExpires()
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync(sessionSeconds: 2);
        using HttpResponseMessage login = await host.LoginAsync();
        await using HubConnection connection = new HubConnectionBuilder().WithUrl(new Uri(host.Client.BaseAddress!, "/_blazor"), options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => new UiTestCookieHandler(host.Cookies, host.App.GetTestServer().CreateHandler());
        }).Build();
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        await connection.StartAsync();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }
    [Theory]
    [InlineData("10.0.0.10", "https", "audit.example.test")]
    [InlineData("198.51.100.10", "http", "internal-ui")]
    public async Task LoginCallback_UsesForwardedOriginOnlyFromATrustedIngress(string peer, string scheme, string hostname)
    {
        await using UiAuthenticationTestHost host = await UiAuthenticationTestHost.StartAsync();
        string page = await host.Client.GetStringAsync("/auth/login");
        Match token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
        });
        byte[] body = Encoding.UTF8.GetBytes(await form.ReadAsStringAsync());
        HttpContext result = await host.App.GetTestServer().SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Method = "POST";
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("internal-ui");
            context.Request.Path = "/auth/sign-in";
            context.Request.ContentType = "application/x-www-form-urlencoded";
            context.Request.ContentLength = body.Length;
            context.Request.Body = new MemoryStream(body);
            context.Request.Headers.Cookie = host.Cookies.GetCookieHeader(host.Client.BaseAddress!);
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-Host"] = "audit.example.test";
        });
        Assert.Equal(StatusCodes.Status302Found, result.Response.StatusCode);
        var location = new Uri(result.Response.Headers.Location.ToString());
        var callback = new Uri(QueryHelpers.ParseQuery(location.Query)["redirect_uri"].ToString());
        Assert.Equal(scheme, callback.Scheme);
        Assert.Equal(hostname, callback.Host);
    }
    [Theory]
    [InlineData("Authority", "http://insecure.example.test")]
    [InlineData("ClientId", "")]
    [InlineData("AllowedSubjects:0", "")]
    [InlineData("SessionMinutes", "0")]
    public void EnabledAuthentication_RejectsIncompleteOrUnsafeConfiguration(string key, string value)
    {
        var values = new Dictionary<string, string?>
        {
            ["UI:Authentication:Enabled"] = "true", ["UI:Authentication:Authority"] = TestOidcProvider.Issuer,
            ["UI:Authentication:ClientId"] = TestOidcProvider.ClientId, ["UI:Authentication:AllowedSubjects:0"] = "operator-123",
            [$"UI:Authentication:{key}"] = value
        };
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() => services.AddUiAuthentication(configuration));
    }
    [Fact]
    public void MissingConfiguredSecretFile_FailsAtStartup()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["UI:Authentication:Enabled"] = "true", ["UI:Authentication:Authority"] = TestOidcProvider.Issuer,
            ["UI:Authentication:ClientId"] = TestOidcProvider.ClientId, ["UI:Authentication:AllowedSubjects:0"] = "operator-123",
            ["UI:Authentication:ClientSecretFile"] = Path.Combine(Path.GetTempPath(), "vakthund-missing-" + Guid.NewGuid().ToString("N"))
        }).Build();
        var services = new ServiceCollection();
        Assert.Throws<FileNotFoundException>(() => services.AddUiAuthentication(configuration));
    }
}
