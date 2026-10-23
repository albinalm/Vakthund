using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Vakthund.UI.Options;

namespace Vakthund.UI.Components.Account;

public partial class AccountPage
{
    [Inject] private IOptions<UiAuthenticationOptions> AuthenticationOptions { get; set; } = null!;
    [CascadingParameter] private HttpContext HttpContext { get; set; } = null!;
    [SupplyParameterFromQuery(Name = "returnUrl")] public string? ReturnUrl { get; set; }
    private string Title => HttpContext.Request.Path.Value switch
    {
        "/auth/access-denied" => "Access denied",
        "/auth/error" => "Sign-in failed",
        "/auth/signed-out" => "Signed out",
        _ => "Sign in"
    };
    private string Message => !AuthenticationOptions.Value.Enabled ? string.Empty : HttpContext.Request.Path.Value switch
    {
        "/auth/access-denied" => "Ask for access or use another account.",
        "/auth/error" => "Try again.",
        _ => string.Empty
    };
}
