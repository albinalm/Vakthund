namespace Vakthund.UI.Helpers;

public static class HttpStyle
{
    public static string StatusColor(int? code) => code switch
    {
        >= 200 and < 300 => "bg-emerald-600 text-white",
        >= 300 and < 400 => "bg-yellow-600 text-white",
        >= 400 and < 500 => "bg-orange-600 text-white",
        >= 500 => "bg-red-600 text-white",
        _ => "bg-gray-600 text-white"
    };

    public static string MethodColor(string method) => method switch
    {
        "GET" => "bg-blue-600",
        "POST" => "bg-emerald-600",
        "PUT" or "PATCH" => "bg-yellow-600",
        "DELETE" => "bg-red-600",
        _ => "bg-gray-600"
    };
}
