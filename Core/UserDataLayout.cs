namespace Shigure;

public static class UserDataLayout
{
    public const string ApplicationDirectoryName = "Shigure";
    public const string ModuleDirectoryName = "module";
    public const string CacheDirectoryName = "cache";
    public const string LogsDirectoryName = "logs";
    public const string MigrationDirectoryName = "migration";
    public const string RuntimeDirectoryName = "runtime";

    public static string ResolveUserDataDirectory(string platformDataDirectory)
    {
        if (string.IsNullOrWhiteSpace(platformDataDirectory))
        {
            throw new ArgumentException("平台用户数据目录不能为空。", nameof(platformDataDirectory));
        }

        return Path.Combine(Path.GetFullPath(platformDataDirectory), ApplicationDirectoryName);
    }

    public static string ResolveModuleDirectory(string userDataDirectory) =>
        Path.Combine(Path.GetFullPath(userDataDirectory), ModuleDirectoryName);

    public static string ResolveCacheDirectory(string userDataDirectory) =>
        Path.Combine(Path.GetFullPath(userDataDirectory), CacheDirectoryName);

    public static string ResolveLogsDirectory(string userDataDirectory) =>
        Path.Combine(Path.GetFullPath(userDataDirectory), LogsDirectoryName);

    public static string ResolveMigrationDirectory(string userDataDirectory) =>
        Path.Combine(Path.GetFullPath(userDataDirectory), MigrationDirectoryName);

    public static string ResolveRuntimeDirectory(string userDataDirectory) =>
        Path.Combine(Path.GetFullPath(userDataDirectory), RuntimeDirectoryName);
}
