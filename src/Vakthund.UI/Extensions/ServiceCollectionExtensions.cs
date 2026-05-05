using Radzen;
using Vakthund.UI.Helpers;
using Vakthund.UI.Options;
using Vakthund.UI.Services;

namespace Vakthund.UI.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddVakthundUi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<VakthundOptions>(config.GetSection("Vakthund"));
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddRadzenComponents();
        services.AddSingleton<AuditHubConnection>();
        services.AddSingleton<AuditStore>();
        services.AddSingleton<MetricsService>();
        services.AddSingleton<JwtTokenParser>();
        services.AddTransient<ProxyConfigService>();

        string hubUrl = config["Proxy:AuditHubUrl"] ?? "";
        string managementUrl = hubUrl.EndsWith("/connect/audit")
            ? hubUrl[..^"/connect/audit".Length]
            : hubUrl;
        services.AddHttpClient("proxy-management", c => c.BaseAddress = new Uri(managementUrl));

        return services;
    }
}
