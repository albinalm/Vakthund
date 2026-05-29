using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Vakthund.Shared.Models;
using Vakthund.UI.Helpers;
using Vakthund.UI.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services;
using Vakthund.UI.Services.Interfaces;

namespace Vakthund.UI.Components.Pages;

public partial class RequestDetail
{
    [Parameter] public Guid Id { get; set; }
    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] private IServiceProvider ServiceProvider { get; set; } = null!;
    [Inject] private JwtTokenParser JwtTokenParser { get; set; } = null!;
    [Inject] private AuthVerdictService AuthVerdictService { get; set; } = null!;
    [Inject] private ProxyConfigService ProxyConfigService { get; set; } = null!;
    [Inject] private ProxyRouteMatcher ProxyRouteMatcher { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IOptions<UiOptions> Options { get; set; } = null!;
    private IAuditStore AuditStore { get; set; } = null!;
    private AuditEntry? _entry;
    private string _statusColor = "";
    private IReadOnlyList<ParsedToken> _parsedTokens = [];
    private ParsedToken? _bearerToken;
    private AuthVerdict? _authVerdict;
    private ProxyRouteInfo? _matchedRoute;

    private bool _bodyExpanded;
    private bool _responseBodyExpanded;
    private bool _queriesExpanded;
    private bool _headersExpanded;
    private bool _cookiesExpanded;
    private bool _authExpanded = true;
    private bool _authVerdictExpanded = true;

    protected override async Task OnParametersSetAsync()
    {
        AuditStore = ServiceProvider.GetRequiredKeyedService<IAuditStore>(Options.Value.StorageMode);
        _entry = AuditStore.Get(Id);
        _statusColor = HttpStyle.StatusColor(_entry?.StatusCode);
        _bodyExpanded = false;
        _responseBodyExpanded = false;
        _queriesExpanded = false;
        _headersExpanded = true;
        _cookiesExpanded = false;
        ProxyConfig? config = await LoadProxyConfigAsync();
        _matchedRoute = _entry?.MatchedRoute
            ?? (_entry is not null ? ProxyRouteMatcher.FindMatchingRoute(config?.Routes, _entry.Path, _entry.IncomingHost ?? _entry.Host) : null);
        _parsedTokens = _entry is not null
            ? JwtTokenParser.Parse(_entry.Headers, _matchedRoute?.Auth)
            : [];
        _bearerToken = _parsedTokens.FirstOrDefault(IsBearerToken);
        _authVerdict = _entry is not null && ShouldShowAuthVerdict(_entry, _parsedTokens)
            ? await AuthVerdictService.EvaluateAsync(_entry, _parsedTokens, config)
            : null;
    }

    private bool IsFormEncoded(string? contentType) =>
        contentType?.Split(';')[0].Trim().Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true;

    private static Dictionary<string, string> ParseFormEncoded(string body)
    {
        var result = new Dictionary<string, string>();
        foreach (string pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int idx = pair.IndexOf('=');
            if (idx < 0)
            {
                continue;
            }

            result[Uri.UnescapeDataString(pair[..idx].Replace('+', ' '))] =
                Uri.UnescapeDataString(pair[(idx + 1)..].Replace('+', ' '));
        }
        return result;
    }

    private async Task<ProxyConfig?> LoadProxyConfigAsync()
    {
        ProxyConfigLoadResult result = await ProxyConfigService.GetAsync();
        return result.Config;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await SetTitle(_entry is not null ? $"{_entry.Method} {_entry.Path} — Vakthund" : "Request — Vakthund");
        }
    }

    private async Task SetTitle(string title) =>
        await JsRuntime.InvokeVoidAsync("setDocumentTitle", title);

    private void NavigateBack() => Nav.NavigateTo("requests");

    private void ToggleBody() => _bodyExpanded = !_bodyExpanded;
    private void ToggleResponseBody() => _responseBodyExpanded = !_responseBodyExpanded;
    private void ToggleQueries() => _queriesExpanded = !_queriesExpanded;
    private void ToggleHeaders() => _headersExpanded = !_headersExpanded;
    private void ToggleCookies() => _cookiesExpanded = !_cookiesExpanded;
    private void ToggleAuthVerdict() => _authVerdictExpanded = !_authVerdictExpanded;
    private void ToggleAuth() => _authExpanded = !_authExpanded;

    private static bool IsBearerToken(ParsedToken token) =>
        token.Scheme?.Equals("Bearer", StringComparison.OrdinalIgnoreCase) == true;

    private static bool ShouldShowAuthVerdict(AuditEntry entry, IReadOnlyList<ParsedToken> parsedTokens) =>
        parsedTokens.Any(IsBearerToken) || entry.StatusCode is 401 or 403;

    private bool IsProxyDenied => _entry is { Upstreamed: false };

    private bool IsProxyDeniedByEnforcedAuth =>
        IsProxyDenied && _matchedRoute?.Auth?.Enforced == true;

    private bool IsProxyDeniedByIpPolicy =>
        IsProxyDenied && !IsProxyDeniedByEnforcedAuth && (_matchedRoute?.Ips.Count ?? 0) > 0;

    private string ResponseOrigin
    {
        get
        {
            if (IsProxyDeniedByEnforcedAuth) return "Auth policy";
            if (IsProxyDeniedByIpPolicy) return "IP policy";
            if (IsProxyDenied) return "Route policy";
            return _entry?.TargetDurationMs is not null ? "Upstream" : "Proxy";
        }
    }

    private string ResponseOriginDetail
    {
        get
        {
            if (IsProxyDeniedByEnforcedAuth) return "Denied before forwarding";
            if (IsProxyDeniedByIpPolicy) return "IP not in allowlist";
            if (IsProxyDenied) return "Denied before forwarding";
            return _entry?.TargetDurationMs is not null ? "Forwarded to target" : "No target response";
        }
    }

    private static string AuthVerdictClasses(AuthVerdictSeverity severity) => severity switch
    {
        AuthVerdictSeverity.Error => "border-red-800 bg-red-950/40 text-red-200",
        AuthVerdictSeverity.Warning => "border-amber-800 bg-amber-950/30 text-amber-100",
        _ => "border-gray-800 bg-gray-950 text-gray-200"
    };

    private static string AuthVerdictBadgeClasses(AuthVerdictSeverity severity) => severity switch
    {
        AuthVerdictSeverity.Error => "border-red-700 text-red-300",
        AuthVerdictSeverity.Warning => "border-amber-700 text-amber-300",
        _ => "border-emerald-700 text-emerald-300"
    };
}
