using Shigure.Platform;

namespace Shigure;

/// <summary>
/// 从目标窗口进程定位 Fuyutsui 插件目录与相关文件。
/// </summary>
public static class WowAddonLocator
{
    private const string InterfaceDirectoryName = "Interface";
    private const string AddOnsDirectoryName = "AddOns";
    private const string AddonDirectoryName = "Fuyutsui";

    public static string? FindClassDirectory(ITargetWindowLocator targetLocator)
    {
        var addonRoot = FindAddonRoot(targetLocator);
        if (addonRoot is null)
        {
            return null;
        }

        var classDirectory = Path.Combine(addonRoot, "class");
        return Directory.Exists(classDirectory) ? classDirectory : null;
    }

    /// <summary>定位 Fuyutsui 插件根目录（含 class/、core/）。</summary>
    public static string? FindAddonRoot(ITargetWindowLocator targetLocator)
    {
        var addOnsDirectory = FindAddOnsDirectory(targetLocator);
        if (addOnsDirectory is null)
        {
            return null;
        }

        var addonRoot = Path.Combine(addOnsDirectory, AddonDirectoryName);
        return Directory.Exists(addonRoot) ? addonRoot : null;
    }

    /// <summary>
    /// 从目标游戏进程定位 Interface\AddOns。即使 AddOns 或 Fuyutsui 尚未创建，
    /// 也会在找到 Interface 时返回预期路径；最后回退到游戏可执行文件同级目录。
    /// </summary>
    public static string? FindAddOnsDirectory(ITargetWindowLocator targetLocator)
    {
        var processPath = targetLocator.FindFrontmostTarget()?.ProcessPath;
        return string.IsNullOrWhiteSpace(processPath)
            ? null
            : ResolveAddOnsDirectory(processPath);
    }

    public static string? ResolveAddOnsDirectory(string processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return null;
        }

        var executableDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath));
        var directory = executableDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            var interfaceDirectory = Path.Combine(directory, InterfaceDirectoryName);
            var addOnsDirectory = Path.Combine(interfaceDirectory, AddOnsDirectoryName);
            if (Directory.Exists(addOnsDirectory) || Directory.Exists(interfaceDirectory))
            {
                return addOnsDirectory;
            }

            if (Path.GetExtension(directory).Equals(".app", StringComparison.OrdinalIgnoreCase))
            {
                var appParent = Path.GetDirectoryName(directory);
                return string.IsNullOrWhiteSpace(appParent)
                    ? null
                    : Path.Combine(appParent, InterfaceDirectoryName, AddOnsDirectoryName);
            }

            directory = Path.GetDirectoryName(directory);
        }

        return string.IsNullOrWhiteSpace(executableDirectory)
            ? null
            : Path.Combine(executableDirectory, InterfaceDirectoryName, AddOnsDirectoryName);
    }

    public static string? FindClassMacrosPath(ITargetWindowLocator targetLocator)
    {
        var addonRoot = FindAddonRoot(targetLocator);
        if (addonRoot is null)
        {
            return null;
        }

        var macrosPath = Path.Combine(addonRoot, "core", "classmacros.lua");
        return File.Exists(macrosPath) ? macrosPath : null;
    }
}
