using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http.Connections;
using Vakthund.UI.Components;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.UI.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseVakthundUI(this WebApplication app)
    {
        app.UseForwardedHeaders();
        string pathBase = app.Services.GetRequiredService<IOptions<UiOptions>>().Value.PathBase.Trim();
        if (!string.IsNullOrEmpty(pathBase))
        {
            app.UsePathBase(pathBase);
        }

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.MapUiAuthentication();
        app.MapGet("/health", () => Results.Text("Healthy")).AllowAnonymous();
        app.MapStaticAssets().AllowAnonymous();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode().Add(endpoint =>
        {
            // .NET 10 exposes connection lifetime settings through endpoint metadata.
            if (endpoint.Metadata.OfType<HttpConnectionDispatcherOptions>().FirstOrDefault() is { } connection)
                connection.CloseOnAuthenticationExpiration = true;
        });

        return app;
    }
}
