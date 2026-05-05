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
            builder.Configuration["UI:MaxStoredAuditEntries"] = maxEntries;
        }

        if (Environment.GetEnvironmentVariable("STORAGE_MODE") is { Length: > 0 } storageMode)
        {
            builder.Configuration["UI:StorageMode"] = storageMode;
        }

        if (Environment.GetEnvironmentVariable("STORAGE_PATH") is { Length: > 0 } storagePath)
        {
            builder.Configuration["UI:StoragePath"] = storagePath;
        }

        if (Environment.GetEnvironmentVariable("RETENTION") is { Length: > 0 } retention)
        {
            builder.Configuration["UI:Retention"] = retention;
        }

        return builder;
    }
}
