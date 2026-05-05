using Vakthund.Proxy.Services;

namespace Vakthund.Tests.Services;

public class RoutesFileResolverTests
{
    [Fact]
    public void Resolve_ReturnsConfiguredAbsolutePath_WhenConfigured()
    {
        string contentRootPath = CreateTemporaryDirectory();
        string configuredPath = Path.Combine(contentRootPath, "explicit.yaml");

        try
        {
            string? resolvedPath = RoutesFileResolver.Resolve(configuredPath, contentRootPath, isDevelopment: true);

            Assert.Equal(configuredPath, resolvedPath);
        }
        finally
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ReturnsConfiguredRelativePath_FromContentRoot()
    {
        string contentRootPath = CreateTemporaryDirectory();
        string expectedPath = Path.Combine(contentRootPath, "config", "routes.yaml");

        try
        {
            string? resolvedPath = RoutesFileResolver.Resolve("config/routes.yaml", contentRootPath, isDevelopment: true);

            Assert.Equal(expectedPath, resolvedPath);
        }
        finally
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ReturnsDevelopmentRoutesFile_WhenNoPathIsConfigured()
    {
        string contentRootPath = CreateTemporaryDirectory();
        string localRoutesPath = Path.Combine(contentRootPath, RoutesFileResolver.DevelopmentRoutesFileName);
        File.WriteAllText(localRoutesPath, "routes: []");

        try
        {
            string? resolvedPath = RoutesFileResolver.Resolve("", contentRootPath, isDevelopment: true);

            Assert.Equal(localRoutesPath, resolvedPath);
        }
        finally
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }

    [Fact]
    public void Resolve_PrefersConfiguredPath_OverDevelopmentRoutesFile()
    {
        string contentRootPath = CreateTemporaryDirectory();
        string localRoutesPath = Path.Combine(contentRootPath, RoutesFileResolver.DevelopmentRoutesFileName);
        string configuredPath = Path.Combine(contentRootPath, "configured.yaml");
        File.WriteAllText(localRoutesPath, "routes: []");

        try
        {
            string? resolvedPath = RoutesFileResolver.Resolve(configuredPath, contentRootPath, isDevelopment: true);

            Assert.Equal(configuredPath, resolvedPath);
        }
        finally
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }

    [Fact]
    public void Resolve_IgnoresDevelopmentRoutesFile_OutsideDevelopment()
    {
        string contentRootPath = CreateTemporaryDirectory();
        string localRoutesPath = Path.Combine(contentRootPath, RoutesFileResolver.DevelopmentRoutesFileName);
        File.WriteAllText(localRoutesPath, "routes: []");

        try
        {
            string missingDockerPath = Path.Combine(contentRootPath, "missing-docker-routes.yaml");

            string? resolvedPath = RoutesFileResolver.Resolve(
                "",
                contentRootPath,
                isDevelopment: false,
                dockerRoutesFilePath: missingDockerPath);

            Assert.Null(resolvedPath);
        }
        finally
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ReturnsDockerRoutesFile_WhenNoOtherRoutesFileIsConfigured()
    {
        string contentRootPath = CreateTemporaryDirectory();
        string dockerRoutesPath = Path.Combine(contentRootPath, "docker-routes.yaml");
        File.WriteAllText(dockerRoutesPath, "routes: []");

        try
        {
            string? resolvedPath = RoutesFileResolver.Resolve(
                "",
                contentRootPath,
                isDevelopment: false,
                dockerRoutesFilePath: dockerRoutesPath);

            Assert.Equal(dockerRoutesPath, resolvedPath);
        }
        finally
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directoryPath = Path.Combine(Path.GetTempPath(), $"vakthund-routes-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directoryPath);
        return directoryPath;
    }
}
