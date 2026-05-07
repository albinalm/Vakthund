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
            string? resolvedPath = RoutesFileResolver.Resolve(configuredPath, contentRootPath);

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
            string? resolvedPath = RoutesFileResolver.Resolve("config/routes.yaml", contentRootPath);

            Assert.Equal(expectedPath, resolvedPath);
        }
        finally
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }

    [Fact]
    public void Resolve_ReturnsLocalRoutesFile_WhenNoPathIsConfigured()
    {
        string contentRootPath = CreateTemporaryDirectory();
        string localRoutesPath = Path.Combine(contentRootPath, RoutesFileResolver.LocalRoutesFileName);
        File.WriteAllText(localRoutesPath, "routes: []");

        try
        {
            string? resolvedPath = RoutesFileResolver.Resolve("", contentRootPath);

            Assert.Equal(localRoutesPath, resolvedPath);
        }
        finally
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }

    [Fact]
    public void Resolve_PrefersConfiguredPath_OverLocalRoutesFile()
    {
        string contentRootPath = CreateTemporaryDirectory();
        string localRoutesPath = Path.Combine(contentRootPath, RoutesFileResolver.LocalRoutesFileName);
        string configuredPath = Path.Combine(contentRootPath, "configured.yaml");
        File.WriteAllText(localRoutesPath, "routes: []");

        try
        {
            string? resolvedPath = RoutesFileResolver.Resolve(configuredPath, contentRootPath);

            Assert.Equal(configuredPath, resolvedPath);
        }
        finally
        {
            Directory.Delete(contentRootPath, recursive: true);
        }
    }

    [Fact]
    public void Resolve_PrefersLocalRoutesFile_OverDockerRoutesFile()
    {
        string contentRootPath = CreateTemporaryDirectory();
        string localRoutesPath = Path.Combine(contentRootPath, RoutesFileResolver.LocalRoutesFileName);
        string dockerRoutesPath = Path.Combine(contentRootPath, "docker-routes.yaml");
        File.WriteAllText(localRoutesPath, "routes: []");
        File.WriteAllText(dockerRoutesPath, "routes: []");

        try
        {
            string? resolvedPath = RoutesFileResolver.Resolve(
                "",
                contentRootPath,
                dockerRoutesFilePath: dockerRoutesPath);

            Assert.Equal(localRoutesPath, resolvedPath);
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
