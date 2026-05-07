namespace Vakthund.Proxy.Services;

public static class RoutesFileResolver
{
    public const string LocalRoutesFileName = "routes.local.yaml";
    private const string DockerRoutesFilePath = "/etc/vakthund/routes.yaml";

    public static string? Resolve(
        string? configuredFilePath,
        string contentRootPath,
        string dockerRoutesFilePath = DockerRoutesFilePath)
    {
        if (!string.IsNullOrWhiteSpace(configuredFilePath))
        {
            return ResolvePath(configuredFilePath, contentRootPath);
        }

        if (TryResolveExistingPath(LocalRoutesFileName, contentRootPath) is { Length: > 0 } localPath)
        {
            return localPath;
        }

        if (File.Exists(dockerRoutesFilePath))
        {
            return dockerRoutesFilePath;
        }

        return null;
    }

    private static string ResolvePath(string filePath, string contentRootPath)
    {
        string trimmedFilePath = filePath.Trim();
        
        return Path.IsPathRooted(trimmedFilePath)
            ? trimmedFilePath
            : Path.GetFullPath(Path.Combine(contentRootPath, trimmedFilePath));
    }

    private static string? TryResolveExistingPath(string filePath, string contentRootPath)
    {
        string resolvedPath = ResolvePath(filePath, contentRootPath);
        return File.Exists(resolvedPath) ? resolvedPath : null;
    }
}