namespace Vakthund.Proxy.Services;

public static class RoutesFileResolver
{
    public const string DevelopmentRoutesFileName = "routes.local.yaml";
    public const string DockerRoutesFilePath = "/etc/vakthund/routes.yaml";

    public static string? Resolve(
        string? configuredFilePath,
        string contentRootPath,
        bool isDevelopment,
        string dockerRoutesFilePath = DockerRoutesFilePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredFilePath))
        {
            return ResolvePath(configuredFilePath, contentRootPath);
        }

        if (isDevelopment && TryResolveExistingPath(DevelopmentRoutesFileName, contentRootPath) is { Length: > 0 } developmentPath)
        {
            return developmentPath;
        }

        if (File.Exists(dockerRoutesFilePath))
        {
            return dockerRoutesFilePath;
        }

        return null;
    }

    public static string ResolvePath(string filePath, string contentRootPath)
    {
        string trimmedFilePath = filePath.Trim();
        if (Path.IsPathRooted(trimmedFilePath))
        {
            return trimmedFilePath;
        }

        return Path.GetFullPath(Path.Combine(contentRootPath, trimmedFilePath));
    }

    private static string? TryResolveExistingPath(string filePath, string contentRootPath)
    {
        string resolvedPath = ResolvePath(filePath, contentRootPath);
        return File.Exists(resolvedPath) ? resolvedPath : null;
    }
}
