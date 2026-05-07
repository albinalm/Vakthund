using System.Net;
using Vakthund.Proxy.Services;

namespace Vakthund.Proxy.Models;

public sealed class IpRange(IPAddress network, int prefixLength)
{
    private readonly IPAddress _network = network;
    private readonly int _prefixLength = prefixLength;

    public bool Contains(IPAddress address)
    {
        IPAddress normalized = IpWhitelist.Normalize(address);
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
