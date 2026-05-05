using Vakthund.UI.Helpers;

namespace Vakthund.Tests.Helpers;

public class HttpStyleTests
{
    [Theory]
    [InlineData(200, "bg-emerald-600 text-white")]
    [InlineData(302, "bg-yellow-600 text-white")]
    [InlineData(404, "bg-orange-600 text-white")]
    [InlineData(500, "bg-red-600 text-white")]
    [InlineData(null, "bg-gray-600 text-white")]
    public void StatusColor_ReturnsExpectedClass_ForStatusFamily(int? statusCode, string expectedClass)
    {
        Assert.Equal(expectedClass, HttpStyle.StatusColor(statusCode));
    }

    [Theory]
    [InlineData("GET", "bg-blue-600")]
    [InlineData("POST", "bg-emerald-600")]
    [InlineData("PUT", "bg-yellow-600")]
    [InlineData("PATCH", "bg-yellow-600")]
    [InlineData("DELETE", "bg-red-600")]
    [InlineData("OPTIONS", "bg-gray-600")]
    public void MethodColor_ReturnsExpectedClass_ForKnownMethods(string method, string expectedClass)
    {
        Assert.Equal(expectedClass, HttpStyle.MethodColor(method));
    }
}
