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

    public static string? ContentTypeShortName(string? contentType)
    {
        if (contentType is null)
        {
            return null;
        }

        string baseType = contentType.Split(';')[0].Trim().ToLowerInvariant();
        return baseType switch
        {
            "application/json" or "text/json" => "json",
            "application/x-www-form-urlencoded" => "form",
            "application/xml" or "text/xml" or "application/xhtml+xml" => "xml",
            "text/html" => "html",
            "text/plain" => "text",
            "multipart/form-data" => "multipart",
            "application/javascript" or "text/javascript" => "js",
            "text/css" => "css",
            "application/octet-stream" => "binary",
            _ => null
        };
    }

    public static string MethodColor(string method) => method switch
    {
        "GET" => "bg-blue-600",
        "POST" => "bg-emerald-600",
        "PUT" or "PATCH" => "bg-yellow-600",
        "DELETE" => "bg-red-600",
        _ => "bg-gray-600"
    };
}
