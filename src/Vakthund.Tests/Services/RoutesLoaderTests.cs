using Vakthund.Proxy.Models;
using Vakthund.Proxy.Services;

namespace Vakthund.Tests.Services;

public class RoutesLoaderTests
{
    [Fact]
    public void TryLoad_ReturnsNull_WhenFileIsMissing()
    {
        string missingFile = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");

        Assert.Null(RoutesLoader.TryLoad(missingFile));
    }

    [Fact]
    public void TryLoad_MapsCamelCaseYamlRoutes()
    {
        string filePath = Path.Combine(AppContext.BaseDirectory, $"{Guid.NewGuid():N}.yaml");
        File.WriteAllText(
            filePath,
            """
            routes:
              - path: /api/{**catch-all}
                target: https://backend.example.test
            """);

        try
        {
            VakthundRoute route = Assert.Single(RoutesLoader.TryLoad(filePath)!);

            Assert.Equal("/api/{**catch-all}", route.Path);
            Assert.Equal("https://backend.example.test", route.Target);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
