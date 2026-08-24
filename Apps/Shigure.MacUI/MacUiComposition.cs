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
    ProjectConfigUpdateService ConfigUpdates,
    MacUiStateStore UiStateStore,
    string RuntimeBaseDirectory,
    RuntimeResourceWorkspaceResult Workspace,
    FuyutsuiAddonSyncResult AddonSync);

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
        var addonSyncResult = addonSync.SynchronizeAll();
        var moduleStore = new ModuleStore(UserDataLayout.ResolveModuleDirectory(userDataDirectory));
        var moduleDependencies = new ModuleDependencyService(workspace.WorkspaceDirectory);
        var runtimeFactory = new MacApplicationRuntimeFactory(workspace.WorkspaceDirectory, moduleStore);
        var coordinator = new RuntimeSessionCoordinator(runtimeFactory);
        var runtime = new RuntimeSessionController(
            coordinator,
            runtimeLeaseFactory: () => SingleInstanceLease.TryAcquire());
        var permissions = OperatingSystem.IsMacOS() ? new MacPermissionService() : null;
        var configUpdates = new ProjectConfigUpdateService(workspace.WorkspaceDirectory, addonSync);
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
            addonSyncResult);
    }

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
}
