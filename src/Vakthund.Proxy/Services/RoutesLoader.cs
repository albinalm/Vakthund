using Vakthund.Proxy.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Vakthund.Proxy.Services;

public static class RoutesLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static List<VakthundRoute>? TryLoad(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        string yaml = File.ReadAllText(filePath);
        var file = Deserializer.Deserialize<RoutesFile>(yaml);
        return file.Routes.Count > 0 ? file.Routes : null;
    }
}
