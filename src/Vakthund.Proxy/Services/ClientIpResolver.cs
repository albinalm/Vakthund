using System.Net;

namespace Vakthund.Proxy.Services;

public static class ClientIpResolver
{
    public static string? Resolve(HttpContext context) => Format(context.Connection.RemoteIpAddress);
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
