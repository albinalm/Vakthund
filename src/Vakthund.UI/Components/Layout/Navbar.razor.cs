using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Layout;

public partial class Navbar
{
    [Inject] private AuditHubConnection AuditHubConnection { get; set; } = null!;
    [Inject] private IOptions<UiAuthenticationOptions> AuthenticationOptions { get; set; } = null!;

    private bool _connected;

    protected override void OnInitialized()
    {
        _connected = AuditHubConnection.State == HubConnectionState.Connected;
        AuditHubConnection.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(HubConnectionState state)
    {
        _connected = state == HubConnectionState.Connected;
        InvokeAsync(StateHasChanged);
    }

    public void Dispose() => AuditHubConnection.StateChanged -= OnStateChanged;
}
