using Radzen;
using Microsoft.AspNetCore.DataProtection;
using Vakthund.UI.Enums;
using Vakthund.UI.Options;
using Vakthund.UI.Services;
using Vakthund.UI.Services.Interfaces;

namespace Vakthund.UI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVakthundUi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<UiOptions>(config.GetSection("UI"));
        if (config["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddRadzenComponents();
        services.AddSingleton<AuditHubConnection>();
        services.AddKeyedSingleton<IAuditStore, AuditMemoryStore>(StorageMode.Memory);
        services.AddKeyedSingleton<IAuditStore, AuditDiskStore>(StorageMode.Disk);
        services.AddSingleton<MetricsStore>();
        services.AddSingleton<MetricsService>();
        services.AddSingleton<JwtTokenParser>();
        services.AddSingleton<JwtSignatureValidator>();
        services.AddSingleton<ProxyRouteMatcher>();
        services.AddSingleton<AuthVerdictService>();
        services.AddScoped<ToastService>();
        services.AddTransient<ProxyConfigService>();
        services.AddHttpClient();

        string hubUrl = config["Proxy:AuditHubUrl"] ?? "";
        string managementUrl = hubUrl.EndsWith("/connect/audit")
            ? hubUrl[..^"/connect/audit".Length]
            : hubUrl;
        services.AddHttpClient("proxy-management", c => c.BaseAddress = new Uri(managementUrl));

        return services;
    }
}
