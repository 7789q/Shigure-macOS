using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Shigure.Platform.MacOS;

namespace Shigure.MacApp;

internal static class Program
{
    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("Shigure.MacApp [--toggle KEY] [--mode switch|click|hold] [--module ID] [--logic-ms N] [--render-ms N]");
            Console.WriteLine("Shigure.MacApp permission request screen-capture|accessibility");
            Console.WriteLine("Shigure.MacApp modules import <legacy-data-directory>");
            return 0;
        }

        if (!OperatingSystem.IsMacOS())
        {
            WriteEvent("host-rejected", "Shigure.MacApp 只能在 macOS 上运行。");
            return 2;
        }

        using var instanceLease = SingleInstanceLease.TryAcquire();
        if (instanceLease is null)
        {
            WriteEvent("duplicate-instance", "Shigure.MacApp 已在运行。");
            return 3;
        }

        MacLauncherParentMonitor? launcherParentMonitor;
        try
        {
            launcherParentMonitor = MacLauncherParentMonitor.FromEnvironment();
        }
        catch (Exception exception)
        {
            WriteEvent("launcher-parent-monitor-failed", $"外壳进程监视初始化失败：{exception.GetType().Name}。");
            return 1;
        }

        if (MacPermissionCommand.IsCommand(args))
        {
            try
            {
                var permissionService = new MacPermissionService();
                return await MacLauncherBoundCommand.RunAsync(
                    () => MacPermissionCommand.Execute(
                        args,
                        permissionService,
                        WriteEvent),
                    launcherParentMonitor,
                    WriteEvent).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                WriteEvent("permission-request-failed", $"权限请求失败：{exception.GetType().Name}。");
                return 1;
            }
        }

        if (MacModuleImportCommand.IsCommand(args))
        {
            try
            {
                var migration = new LegacyModuleMigrationService();
                return await MacLauncherBoundCommand.RunAsync(
                    () => MacModuleImportCommand.Execute(
                        args,
                        MacUserDataPaths.UserDataDirectory,
                        migration.Migrate,
                        WriteEvent),
                    launcherParentMonitor,
                    WriteEvent).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                WriteEvent("module-import-command-failed", $"模块导入命令失败：{exception.GetType().Name}。");
                return 1;
            }
        }

        using var shutdown = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        using var sigTerm = RegisterSignal(PosixSignal.SIGTERM, shutdown);
        using var sigInt = RegisterSignal(PosixSignal.SIGINT, shutdown);
        using var sigHup = RegisterSignal(PosixSignal.SIGHUP, shutdown);
        Task? launcherParentMonitorTask = null;

        try
        {
            if (launcherParentMonitor is not null)
            {
                launcherParentMonitorTask = MonitorLauncherParentAsync(
                    launcherParentMonitor,
                    shutdown);
            }

            var workspace = new RuntimeResourceWorkspaceService().Initialize(
                AppContext.BaseDirectory,
                MacUserDataPaths.UserDataDirectory);
            var baseDirectory = workspace.WorkspaceDirectory;
            WriteEvent(
                "runtime-resources",
                $"运行资源就绪：新增 {workspace.CreatedFiles.Count}，更新 {workspace.UpdatedFiles.Count}，保留冲突 {workspace.ConflictingFiles.Count}。");
            SynchronizeAddon(baseDirectory);

            var moduleStore = new ModuleStore(MacUserDataPaths.ModuleDirectory);
            var runtimeFactory = new MacApplicationRuntimeFactory(baseDirectory, moduleStore);
            var coordinator = new RuntimeSessionCoordinator(runtimeFactory);
            await using var host = new MacApplicationHost(coordinator);
            host.EventEmitted += WriteEvent;

            await host.RunAsync(AppOptions.FromArgs(args), shutdown.Token).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            WriteEvent("host-failed", $"应用宿主失败：{exception.GetType().Name}。");
            return 1;
        }
        finally
        {
            shutdown.Cancel();
            if (launcherParentMonitorTask is not null)
            {
                await launcherParentMonitorTask.ConfigureAwait(false);
            }

            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task MonitorLauncherParentAsync(
        MacLauncherParentMonitor monitor,
        CancellationTokenSource shutdown)
    {
        try
        {
            await monitor.WaitForParentExitAsync(shutdown.Token).ConfigureAwait(false);
            WriteEvent("launcher-parent-exited", "AppKit 外壳已退出，正在停止业务运行时。");
            shutdown.Cancel();
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            WriteEvent("launcher-parent-monitor-failed", $"外壳进程监视失败：{exception.GetType().Name}。");
            shutdown.Cancel();
        }
    }

    private static PosixSignalRegistration RegisterSignal(
        PosixSignal signal,
        CancellationTokenSource shutdown) =>
        PosixSignalRegistration.Create(signal, context =>
        {
            context.Cancel = true;
            shutdown.Cancel();
        });

    [SupportedOSPlatform("macos")]
    private static void SynchronizeAddon(string baseDirectory)
    {
        try
        {
            var service = new FuyutsuiAddonSyncService(
                Path.Combine(baseDirectory, "Fuyutsui"),
                new MacTargetWindowLocator(baseDirectory));
            var result = service.SynchronizeAll();
            WriteEvent(
                "addon-sync",
                result.TargetFound
                    ? $"插件同步完成：复制 {result.CopiedFiles.Count}，跳过 {result.SkippedFiles.Count}，失败 {result.Failures.Count}。"
                    : "未找到目标游戏，已跳过插件同步。");
        }
        catch (Exception exception)
        {
            WriteEvent("addon-sync-failed", $"插件同步失败：{exception.GetType().Name}。");
        }
    }

    private static void WriteEvent(MacApplicationEvent applicationEvent) =>
        Console.WriteLine(JsonSerializer.Serialize(applicationEvent, LogJsonOptions));

    private static void WriteEvent(string stage, string message) =>
        WriteEvent(new MacApplicationEvent(DateTimeOffset.UtcNow, stage, message));
}
