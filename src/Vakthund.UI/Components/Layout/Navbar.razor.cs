using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Layout;

public partial class Navbar
{
    [Inject] private AuditHubConnection AuditHubConnection { get; set; } = null!;

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
