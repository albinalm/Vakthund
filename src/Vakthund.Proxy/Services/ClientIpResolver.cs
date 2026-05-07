using System.Net;

namespace Vakthund.Proxy.Services;

public static class ClientIpResolver
{
    public static string? Resolve(HttpContext context)
    {
        string? forwardedFor = FirstForwardedFor(context);
        if (TryFormat(forwardedFor, out string? forwardedIp))
        {
            return forwardedIp;
        }

        string? realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (TryFormat(realIp, out string? realIpAddress))
        {
            return realIpAddress;
        }

        return Format(context.Connection.RemoteIpAddress);
    }

    private static string? FirstForwardedFor(HttpContext context)
    {
        string? header = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        return header?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
    }

    private static bool TryFormat(string? value, out string? formatted)
    {
        formatted = null;
        if (!IPAddress.TryParse(value, out IPAddress? address))
        {
            return false;
        }

        formatted = Format(address);
        return formatted is not null;
    }

    private static string? Format(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        return address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : address.ToString();
    }
}
