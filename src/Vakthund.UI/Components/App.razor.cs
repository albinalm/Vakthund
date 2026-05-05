using Microsoft.AspNetCore.Components;

namespace Vakthund.UI.Components;

public partial class App
{
    [CascadingParameter] private HttpContext HttpContext { get; set; } = default!;

    private string _themeClass => HttpContext.Request.Cookies["theme"] != "light" ? "dark" : "";
}
