using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Vakthund.Shared.Models;
using Vakthund.UI.Helpers;
using Vakthund.UI.Models;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Pages;

public partial class RequestDetail
{
    [Parameter] public Guid Id { get; set; }
    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] private AuditStore AuditStore { get; set; } = null!;
    [Inject] private JwtTokenParser JwtTokenParser { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    private AuditEntry? _entry;
    private string _statusColor = "";
    private IReadOnlyList<ParsedToken> _parsedTokens = [];

    private bool _bodyExpanded;
    private bool _responseBodyExpanded;
    private bool _queriesExpanded;
    private bool _headersExpanded;
    private bool _cookiesExpanded;
    private bool _authExpanded = true;

    protected override void OnParametersSet()
    {
        _entry = AuditStore.Get(Id);
        _statusColor = HttpStyle.StatusColor(_entry?.StatusCode);
        _bodyExpanded = false;
        _responseBodyExpanded = false;
        _queriesExpanded = false;
        _headersExpanded = true;
        _cookiesExpanded = false;
        _parsedTokens = _entry is not null
            ? JwtTokenParser.Parse(_entry.Headers)
            : [];
    }

    private bool IsFormEncoded(string? contentType) =>
        contentType?.Split(';')[0].Trim().Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true;

    private static Dictionary<string, string> ParseFormEncoded(string body)
    {
        var result = new Dictionary<string, string>();
        foreach (string pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int idx = pair.IndexOf('=');
            if (idx < 0) continue;
            result[Uri.UnescapeDataString(pair[..idx].Replace('+', ' '))] =
                Uri.UnescapeDataString(pair[(idx + 1)..].Replace('+', ' '));
        }
        return result;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await SetTitle(_entry is not null ? $"{_entry.Method} {_entry.Path} — Vakthund" : "Request — Vakthund");
    }

    private async Task SetTitle(string title) =>
        await JsRuntime.InvokeVoidAsync("setDocumentTitle", title);

    private void NavigateBack() => Nav.NavigateTo("/requests");

    private void ToggleBody() => _bodyExpanded = !_bodyExpanded;
    private void ToggleResponseBody() => _responseBodyExpanded = !_responseBodyExpanded;
    private void ToggleQueries() => _queriesExpanded = !_queriesExpanded;
    private void ToggleHeaders() => _headersExpanded = !_headersExpanded;
    private void ToggleCookies() => _cookiesExpanded = !_cookiesExpanded;
    private void ToggleAuth() => _authExpanded = !_authExpanded;
}
