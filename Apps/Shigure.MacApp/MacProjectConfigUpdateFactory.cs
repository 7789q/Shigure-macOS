using Shigure.Platform;
using Shigure.Platform.MacOS;

namespace Shigure.MacApp;

public static class MacProjectConfigUpdateFactory
{
    public static ProjectConfigUpdateService Create(string runtimeBaseDirectory)
    {
        var baseDirectory = Path.GetFullPath(runtimeBaseDirectory);
        return new ProjectConfigUpdateService(baseDirectory, CreateAddonSync(baseDirectory));
    }

    public static FuyutsuiAddonSyncService CreateAddonSync(string runtimeBaseDirectory)
    {
        var baseDirectory = Path.GetFullPath(runtimeBaseDirectory);
        ITargetWindowLocator targetLocator = OperatingSystem.IsMacOS()
            ? new MacTargetWindowLocator(baseDirectory)
            : new UnavailableTargetWindowLocator();
        return new FuyutsuiAddonSyncService(
            Path.Combine(baseDirectory, "Fuyutsui"),
            targetLocator);
    }

    private sealed class UnavailableTargetWindowLocator : ITargetWindowLocator
    {
        public TargetWindow? FindFrontmostTarget() => null;

        public string DescribeConfiguredProcesses() => "macOS only";
    }
}
