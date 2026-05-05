using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Vakthund.Shared.Models;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Pages;

public partial class Config
{
    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] private ProxyConfigService ProxyConfigService { get; set; } = null!;
    [Inject] private IOptions<UiOptions> UiOptions { get; set; } = null!;

    private ProxyConfig? _config;
    private string? _errorMessage;
    private string? _errorDetail;
    private bool _loading = true;

    private bool _routesExpanded = true;
    private bool _captureExpanded = true;
    private bool _queueExpanded = true;
    private bool _tokenDecryptionExpanded = true;

    protected override async Task OnInitializedAsync()
    {
        ProxyConfigLoadResult result = await ProxyConfigService.GetAsync();
        _config = result.Config;
        _errorMessage = result.ErrorMessage;
        _errorDetail = result.ErrorDetail;
        _loading = false;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await SetTitle("Configuration — Vakthund");
        }
    }

    private async Task SetTitle(string title) =>
        await JsRuntime.InvokeVoidAsync("setDocumentTitle", title);

    private void ToggleRoutes() => _routesExpanded = !_routesExpanded;
    private void ToggleCapture() => _captureExpanded = !_captureExpanded;
    private void ToggleQueue() => _queueExpanded = !_queueExpanded;
    private void ToggleTokenDecryption() => _tokenDecryptionExpanded = !_tokenDecryptionExpanded;

    private static string FormatBytes(int bytes) => bytes == 0 ? "Disabled" : $"Up to {bytes / 1024} KB";
}
