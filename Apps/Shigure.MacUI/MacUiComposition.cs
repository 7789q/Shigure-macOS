using Shigure.MacApp;
using Shigure.Platform;
using Shigure.Platform.MacOS;
using Shigure.Presentation;

namespace Shigure.MacUI;

internal sealed record MacUiServices(
    ModuleStore ModuleStore,
    ModuleDependencyService ModuleDependencies,
    RuntimeSessionController Runtime,
    IPlatformPermissionService? Permissions,
    ProjectConfigUpdateService? ConfigUpdates,
    MacUiStateStore UiStateStore,
    string RuntimeBaseDirectory,
    RuntimeResourceWorkspaceResult Workspace,
    FuyutsuiAddonSyncService AddonSyncService,
    FuyutsuiAddonSyncResult AddonSync,
    BundledModuleInstallResult BundledModules,
    string? RuntimeBlockedReason);

internal static class MacUiComposition
{
    public static MacUiServices Create()
    {
        var userDataDirectory = UserDataLayout.ResolveUserDataDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        var versionResourceDirectory = ResolveVersionResourceDirectory();
        var workspace = new RuntimeResourceWorkspaceService().Initialize(
            versionResourceDirectory,
            userDataDirectory);
        var addonSync = MacProjectConfigUpdateFactory.CreateAddonSync(workspace.WorkspaceDirectory);
        var runtimeBlockedReason = BuildRuntimeBlockedReason(workspace.ProtocolConflictingFiles);
        var addonSyncResult = runtimeBlockedReason is null
            ? addonSync.SynchronizeAll()
            : FuyutsuiAddonSyncResult.Skipped(workspace.WorkspaceDirectory, runtimeBlockedReason);
        var moduleDirectory = UserDataLayout.ResolveModuleDirectory(userDataDirectory);
        var bundledModules = new BundledModuleInstaller().Install(
            ResolveBundledModuleDirectory(),
            moduleDirectory);
        var moduleStore = new ModuleStore(moduleDirectory);
        var moduleDependencies = new ModuleDependencyService(workspace.WorkspaceDirectory);
        var runtimeFactory = new MacApplicationRuntimeFactory(workspace.WorkspaceDirectory, moduleStore);
        var coordinator = new RuntimeSessionCoordinator(runtimeFactory);
        var runtime = new RuntimeSessionController(
            coordinator,
            runtimeLeaseFactory: () => SingleInstanceLease.TryAcquire());
        var permissions = OperatingSystem.IsMacOS() ? new MacPermissionService() : null;
        var configUpdates = runtimeBlockedReason is null
            ? new ProjectConfigUpdateService(workspace.WorkspaceDirectory, addonSync)
            : null;
        var uiStateStore = new MacUiStateStore(userDataDirectory);
        return new MacUiServices(
            moduleStore,
            moduleDependencies,
            runtime,
            permissions,
            configUpdates,
            uiStateStore,
            workspace.WorkspaceDirectory,
            workspace,
            addonSync,
            addonSyncResult,
            bundledModules,
            runtimeBlockedReason);
    }

    private static string? BuildRuntimeBlockedReason(IReadOnlyList<string> conflicts) =>
        conflicts.Count == 0
            ? null
            : $"运行资源包含未迁移的插件协议冲突，已禁止同步和启动：{string.Join("、", conflicts)}。请备份自定义后恢复这些文件，再重新打开 Shigure。";

    internal static string ResolveVersionResourceDirectory()
    {
        var bundledResources = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "Resources",
            "runtime-baseline"));
        return Directory.Exists(Path.Combine(bundledResources, "Fuyutsui"))
            ? bundledResources
            : AppContext.BaseDirectory;
    }

    internal static string ResolveBundledModuleDirectory()
    {
        var packagedModules = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "Resources",
            "bundled-modules"));
        return Directory.Exists(packagedModules)
            ? packagedModules
            : Path.Combine(AppContext.BaseDirectory, "bundled-modules");
    }
}
