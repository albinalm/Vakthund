using System.Net;
using Microsoft.AspNetCore.Http;
using Vakthund.Proxy.Services;

namespace Vakthund.Tests.Services;

public class ClientIpResolverTests
{
    [Fact]
    public void Resolve_IgnoresUnprocessedForwardedForAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.42, 198.51.100.10";

        Assert.Equal("10.0.0.1", ClientIpResolver.Resolve(context));
    }

    [Fact]
    public void Resolve_IgnoresUntrustedRealIp()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        context.Request.Headers["X-Real-IP"] = "198.51.100.10";

        Assert.Equal("10.0.0.1", ClientIpResolver.Resolve(context));
    }

    [Fact]
    public void Resolve_FallsBackToRemoteIpAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:203.0.113.42");

        Assert.Equal("203.0.113.42", ClientIpResolver.Resolve(context));
    }
}
