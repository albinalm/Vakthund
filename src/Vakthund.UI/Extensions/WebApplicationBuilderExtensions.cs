namespace Vakthund.UI.Extensions;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder ApplyEnvironmentOverrides(this WebApplicationBuilder builder)
    {
        if (Environment.GetEnvironmentVariable("HUB") is { Length: > 0 } hub)
        {
            builder.Configuration["Proxy:AuditHubUrl"] = hub;
        }

        if (Environment.GetEnvironmentVariable("MAX_AUDIT_ENTRIES") is { Length: > 0 } maxEntries)
        {
            builder.Configuration["UI:MaxAuditEntries"] = maxEntries;
        }

        return builder;
    }
}
