using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Vakthund.UI.Services;

public sealed class UiAuthenticationStateProvider(ILoggerFactory loggerFactory, UiAccountAccess access, UiSessionStore sessions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);
    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        access.IsAllowed(authenticationState.User) && authenticationState.User.FindFirst(UiSessionStore.SessionClaim)?.Value is { } session &&
        await sessions.RetrieveAsync(session) is not null;
}
