using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Web;

namespace Vakthund.UI.Components;

public partial class App
{
    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;

    private string _themeClass => HttpContext.Request.Cookies["theme"] != "light" ? "dark" : "";
    private IComponentRenderMode? PageRenderMode => HttpContext.AcceptsInteractiveRouting() ? new InteractiveServerRenderMode(prerender: false) : null;
}
