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
        "/auth/signed-out" => "You have signed out",
        _ => "Sign in to Vakthund"
    };
    private string Message => !AuthenticationOptions.Value.Enabled ? "Authentication is disabled for this installation." : HttpContext.Request.Path.Value switch
    {
        "/auth/access-denied" => "Your account is not allowed to access this installation. Ask its operator for access or sign in with a different account.",
        "/auth/error" => "The sign-in could not be completed. Try again or contact the operator if the problem continues.",
        "/auth/signed-out" => "Your Vakthund session has ended. You can sign in again when you are ready.",
        _ => "Sign in with your identity provider to inspect this installation's captured traffic."
    };
}
