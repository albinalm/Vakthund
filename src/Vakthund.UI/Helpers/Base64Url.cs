using System.Text;

namespace Vakthund.UI.Helpers;

public static class Base64Url
{
    public static byte[] DecodeBytes(string value)
    {
        string padded = Pad(value.Replace('-', '+').Replace('_', '/'));
        return Convert.FromBase64String(padded);
    }

    public static string DecodeString(string value) =>
        Encoding.UTF8.GetString(DecodeBytes(value));

    private static string Pad(string value) =>
        (value.Length % 4) switch
        {
            2 => value + "==",
            3 => value + "=",
            _ => value
        };
}
