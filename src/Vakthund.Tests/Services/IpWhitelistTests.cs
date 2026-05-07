using Vakthund.Proxy.Services;

namespace Vakthund.Tests.Services;

public class IpWhitelistTests
{
    [Theory]
    [InlineData("203.0.113.42", "203.0.113.42")]
    [InlineData("203.0.113.42", "203.0.113.0/24")]
    [InlineData("203.0.113.42", "203.0.113.*")]
    [InlineData("203.0.113.42", "203.0.*")]
    [InlineData("2001:db8::1", "2001:db8::/32")]
    public void Allows_ReturnsTrue_WhenClientIpMatches(string clientIp, string entry)
    {
        IReadOnlyList<IpWhitelist.IpRange> ranges = IpWhitelist.Parse([entry], "/api/**");

        Assert.True(IpWhitelist.Allows(clientIp, ranges));
    }

    [Theory]
    [InlineData("198.51.100.10", "203.0.113.*")]
    [InlineData("203.1.113.42", "203.0.*")]
    [InlineData("2001:db9::1", "2001:db8::/32")]
    public void Allows_ReturnsFalse_WhenClientIpDoesNotMatch(string clientIp, string entry)
    {
        IReadOnlyList<IpWhitelist.IpRange> ranges = IpWhitelist.Parse([entry], "/api/**");

        Assert.False(IpWhitelist.Allows(clientIp, ranges));
    }

    [Theory]
    [InlineData("203.*.113.*")]
    [InlineData("203.0.113.0/33")]
    [InlineData("not-an-ip")]
    public void Parse_Throws_WhenEntryIsInvalid(string entry)
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => IpWhitelist.Parse([entry], "/api/**"));

        Assert.Contains("invalid ip whitelist entry", exception.Message);
    }
}
