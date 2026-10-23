using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Vakthund.UI.Options;

namespace Vakthund.UI.Services;

public sealed class UiAccountAccess(IOptions<UiAuthenticationOptions> options) : AuthorizationHandler<UiAccessRequirement>
{
    public const string PolicyName = "VakthundUi";
    public bool IsAllowed(ClaimsPrincipal? user) =>
        !options.Value.Enabled || user?.Identity?.IsAuthenticated == true &&
        user.FindFirst("sub")?.Value is { Length: > 0 } subject && options.Value.AllowedSubjects.Contains(subject, StringComparer.Ordinal);
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, UiAccessRequirement requirement)
    {
        if (IsAllowed(context.User))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
