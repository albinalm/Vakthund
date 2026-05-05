using Microsoft.AspNetCore.Components;
using Vakthund.Shared.Models;
using Vakthund.UI.Helpers;
using Vakthund.UI.Models;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Pages;

public partial class RequestDetail
{
    [Parameter] public Guid Id { get; set; }
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

    private void NavigateBack() => Nav.NavigateTo("/requests");

    private void ToggleBody() => _bodyExpanded = !_bodyExpanded;
    private void ToggleResponseBody() => _responseBodyExpanded = !_responseBodyExpanded;
    private void ToggleQueries() => _queriesExpanded = !_queriesExpanded;
    private void ToggleHeaders() => _headersExpanded = !_headersExpanded;
    private void ToggleCookies() => _cookiesExpanded = !_cookiesExpanded;
    private void ToggleAuth() => _authExpanded = !_authExpanded;
}
