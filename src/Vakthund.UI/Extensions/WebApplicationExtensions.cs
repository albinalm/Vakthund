using Microsoft.Extensions.Options;
using Vakthund.UI.Components;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.UI.Extensions;

public static class WebApplicationExtensions
{
    public static WebApplication UseVakthundUI(this WebApplication app)
    {
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
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        return app;
    }
}
