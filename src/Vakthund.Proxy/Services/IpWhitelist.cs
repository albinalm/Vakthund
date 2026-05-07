using System.Net;

namespace Vakthund.Proxy.Services;

public static class IpWhitelist
{
    public static IReadOnlyList<IpRange> Parse(IEnumerable<string>? entries, string routePath)
    {
        List<IpRange> ranges = [];

        foreach (string? entry in entries ?? [])
        {
            string value = entry?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Route '{routePath}' has an empty ip whitelist entry.");
            }

            ranges.Add(ParseEntry(value, routePath));
        }

        return ranges;
    }

    public static bool Allows(string? clientIp, IReadOnlyList<IpRange> ranges)
    {
        if (ranges.Count == 0)
        {
            return true;
        }

        if (!IPAddress.TryParse(clientIp, out IPAddress? address))
        {
            return false;
        }

        IPAddress normalized = Normalize(address);
        return ranges.Any(range => range.Contains(normalized));
    }

    private static IpRange ParseEntry(string value, string routePath)
    {
        if (value.Contains('*', StringComparison.Ordinal))
        {
            return ParseWildcardEntry(value, routePath);
        }

        int slashIndex = value.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex < 0)
        {
            if (IPAddress.TryParse(value, out IPAddress? address))
            {
                IPAddress normalized = Normalize(address);
                return new IpRange(normalized, PrefixLength(normalized));
            }

            throw InvalidEntry(value, routePath);
        }

        string ipPart = value[..slashIndex];
        string prefixPart = value[(slashIndex + 1)..];

        if (!IPAddress.TryParse(ipPart, out IPAddress? cidrAddress) ||
            !int.TryParse(prefixPart, out int prefixLength))
        {
            throw InvalidEntry(value, routePath);
        }

        IPAddress normalizedAddress = Normalize(cidrAddress);
        int maxPrefixLength = PrefixLength(normalizedAddress);
        if (prefixLength < 0 || prefixLength > maxPrefixLength)
        {
            throw InvalidEntry(value, routePath);
        }

        return new IpRange(normalizedAddress, prefixLength);
    }

    private static IpRange ParseWildcardEntry(string value, string routePath)
    {
        string[] parts = value.Split('.');
        if (parts.Length is < 1 or > 4)
        {
            throw InvalidEntry(value, routePath);
        }

        var octets = new byte[4];
        bool wildcardStarted = false;
        int fixedOctets = 0;

        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i].Trim();
            if (part == "*")
            {
                wildcardStarted = true;
                continue;
            }

            if (wildcardStarted ||
                !byte.TryParse(part, out byte octet))
            {
                throw InvalidEntry(value, routePath);
            }

            octets[i] = octet;
            fixedOctets++;
        }

        if (!wildcardStarted)
        {
            throw InvalidEntry(value, routePath);
        }

        return new IpRange(new IPAddress(octets), fixedOctets * 8);
    }

    private static InvalidOperationException InvalidEntry(string value, string routePath) =>
        new($"Route '{routePath}' has invalid ip whitelist entry '{value}'. Use an IP address, CIDR range, or trailing IPv4 wildcard.");

    private static IPAddress Normalize(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private static int PrefixLength(IPAddress address) =>
        address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 32 : 128;

    public sealed class IpRange(IPAddress network, int prefixLength)
    {
        private readonly IPAddress _network = network;
        private readonly int _prefixLength = prefixLength;

        public bool Contains(IPAddress address)
        {
            IPAddress normalized = Normalize(address);
            byte[] networkBytes = _network.GetAddressBytes();
            byte[] addressBytes = normalized.GetAddressBytes();
            if (networkBytes.Length != addressBytes.Length)
            {
                return false;
            }

            int fullBytes = _prefixLength / 8;
            int remainingBits = _prefixLength % 8;

            for (int i = 0; i < fullBytes; i++)
            {
                if (networkBytes[i] != addressBytes[i])
                {
                    return false;
                }
            }

            if (remainingBits == 0)
            {
                return true;
            }

            int mask = 0xff << (8 - remainingBits);
            return (networkBytes[fullBytes] & mask) == (addressBytes[fullBytes] & mask);
        }
    }
}
