using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Vakthund.Shared.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Pages;

public partial class Config
{
    [Inject] private ProxyConfigService ProxyConfigService { get; set; } = null!;
    [Inject] private IOptions<VakthundOptions> UiOptions { get; set; } = null!;

    private ProxyConfig? _config;
    private bool _loading = true;
    private bool _error;

    protected override async Task OnInitializedAsync()
    {
        _config = await ProxyConfigService.GetAsync();
        _error = _config is null;
        _loading = false;
    }

    private static string FormatBytes(int bytes) => bytes == 0 ? "Disabled" : $"Up to {bytes / 1024} KB";
}
