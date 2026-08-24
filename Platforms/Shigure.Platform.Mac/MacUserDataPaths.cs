namespace Shigure.Platform.MacOS;

public static class MacUserDataPaths
{
    public static string UserDataDirectory => ResolveUserDataDirectory(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));

    public static string ModuleDirectory =>
        UserDataLayout.ResolveModuleDirectory(UserDataDirectory);

    public static string CacheDirectory =>
        UserDataLayout.ResolveCacheDirectory(UserDataDirectory);

    public static string LogsDirectory =>
        UserDataLayout.ResolveLogsDirectory(UserDataDirectory);

    public static string RuntimeDirectory =>
        UserDataLayout.ResolveRuntimeDirectory(UserDataDirectory);

    public static string ResolveUserDataDirectory(string applicationSupportDirectory) =>
        UserDataLayout.ResolveUserDataDirectory(applicationSupportDirectory);
}
