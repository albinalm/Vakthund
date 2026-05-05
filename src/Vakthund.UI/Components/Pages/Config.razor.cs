using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Vakthund.Shared.Models;
using Vakthund.UI.Enums;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.UI.Components.Pages;

public partial class Config
{
    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;
    [Inject] private IConfiguration Configuration { get; set; } = null!;
    [Inject] private ProxyConfigService ProxyConfigService { get; set; } = null!;
    [Inject] private IOptions<UiOptions> UiOptions { get; set; } = null!;

    private ProxyConfig? _config;
    private string? _errorMessage;
    private string? _errorDetail;
    private bool _loading = true;

    private bool _routesExpanded = true;
    private bool _uiExpanded = true;
    private bool _captureExpanded = true;
    private bool _queueExpanded = true;
    private bool _tokenDecryptionExpanded = true;

    private UiOptions UiConfig => UiOptions.Value;
    private string AuditHubUrl => Configuration["Proxy:AuditHubUrl"] ?? "";
    private string AuditHubTextClass => string.IsNullOrWhiteSpace(AuditHubUrl) ? "text-gray-600" : "text-gray-200";
    private string StoragePathTextClass => UiConfig.StorageMode == StorageMode.Disk ? "text-gray-200" : "text-gray-600";
    private string RetentionTextClass => string.IsNullOrWhiteSpace(UiConfig.Retention)
        ? "text-gray-600"
        : UiConfig.RetentionPeriod.HasValue
            ? "text-gray-200"
            : "text-red-400";

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
    private void ToggleUi() => _uiExpanded = !_uiExpanded;
    private void ToggleCapture() => _captureExpanded = !_captureExpanded;
    private void ToggleQueue() => _queueExpanded = !_queueExpanded;
    private void ToggleTokenDecryption() => _tokenDecryptionExpanded = !_tokenDecryptionExpanded;

    private static string FormatBytes(int bytes) => bytes == 0 ? "Disabled" : $"Up to {bytes / 1024} KB";
    private static string FormatOptional(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    private static string FormatEntryLimit(int maxEntries) => maxEntries == 0 ? "Unlimited" : maxEntries.ToString("N0");

    private static string FormatStoragePath(UiOptions options)
    {
        string storagePath = options.StoragePath?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(storagePath))
        {
            return storagePath;
        }

        return options.StorageMode == StorageMode.Disk ? "data/audit.db (default)" : "Not used";
    }

    private static string FormatRetention(UiOptions options)
    {
        string retention = options.Retention?.Trim() ?? "";
        if (retention.Length == 0)
        {
            return "No age limit";
        }

        return options.RetentionPeriod.HasValue ? retention : $"{retention} (invalid)";
    }
}
