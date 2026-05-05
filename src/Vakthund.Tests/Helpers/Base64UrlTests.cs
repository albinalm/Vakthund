using System.Text;
using Vakthund.UI.Helpers;

namespace Vakthund.Tests.Helpers;

public class Base64UrlTests
{
    [Fact]
    public void DecodeString_DecodesUnpaddedBase64Url()
    {
        string encoded = Encode("{}");

        string decoded = Base64Url.DecodeString(encoded);

        Assert.Equal("{}", decoded);
    }

    [Fact]
    public void DecodeBytes_DecodesUrlSafeCharacters()
    {
        byte[] decoded = Base64Url.DecodeBytes("-_8");

        Assert.Equal(new byte[] { 251, 255 }, decoded);
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
