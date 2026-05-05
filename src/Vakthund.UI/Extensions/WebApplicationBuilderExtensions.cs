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
            builder.Configuration["Vakthund:MaxAuditEntries"] = maxEntries;
        }

        if (Environment.GetEnvironmentVariable("JWE_KEY_TYPE") is { Length: > 0 } jweKeyType)
        {
            builder.Configuration["Vakthund:Jwe:KeyType"] = jweKeyType;
        }

        if (Environment.GetEnvironmentVariable("JWE_KEY") is { Length: > 0 } jweKey)
        {
            builder.Configuration["Vakthund:Jwe:Key"] = jweKey;
        }

        return builder;
    }
}
