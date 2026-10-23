using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Vakthund.UI.Options;

namespace Vakthund.UI.Extensions;

public static class UiAuthenticationEndpointExtensions
{
    public static WebApplication MapUiAuthentication(this WebApplication app)
    {
        if (!app.Services.GetRequiredService<IOptions<UiAuthenticationOptions>>().Value.Enabled)
            return app;
        app.MapPost("/auth/sign-in", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await IsValidFormAsync(context, antiforgery))
                return Results.BadRequest("The form could not be validated. Reload the page and try again.");
            IFormCollection form = await context.Request.ReadFormAsync();
            string returnUrl = LocalReturnUrl(context.Request.PathBase, form["returnUrl"].FirstOrDefault());
            await context.SignOutAsync(UiAuthenticationServiceCollectionExtensions.CookieScheme);
            return Results.Challenge(new AuthenticationProperties { RedirectUri = returnUrl }, [UiAuthenticationServiceCollectionExtensions.OidcScheme]);
        }).AllowAnonymous();
        app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            if (!await IsValidFormAsync(context, antiforgery))
                return Results.BadRequest("The form could not be validated. Reload the page and try again.");
            return Results.SignOut(new AuthenticationProperties { RedirectUri = context.Request.PathBase + "/auth/signed-out" },
                [UiAuthenticationServiceCollectionExtensions.CookieScheme, UiAuthenticationServiceCollectionExtensions.OidcScheme]);
        });
        return app;
    }
    private static async Task<bool> IsValidFormAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (!context.Request.HasFormContentType)
            return false;
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
    private static string LocalReturnUrl(PathString pathBase, string? returnUrl)
    {
        string root = pathBase + "/";
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            returnUrl.Contains('\\') || returnUrl.Any(char.IsControl))
            return root;
        return !pathBase.HasValue || returnUrl == pathBase.Value || returnUrl.StartsWith(root, StringComparison.Ordinal) ? returnUrl : root;
    }
}
