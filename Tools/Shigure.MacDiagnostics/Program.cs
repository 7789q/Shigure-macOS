using System.Runtime.Versioning;
using Shigure;
using Shigure.MacDiagnostics;
using Shigure.Platform;
using Shigure.Platform.MacOS;

if (MacDiagnosticCommand.IsHelpRequest(args))
{
    MacDiagnosticCommand.PrintHelp(Console.Out);
    return 0;
}

if (!OperatingSystem.IsMacOS())
{
    Console.Error.WriteLine("Shigure.MacDiagnostics 只能在 macOS 上运行。");
    return 1;
}

return RunMac(args);

[SupportedOSPlatform("macos")]
static int RunMac(string[] arguments)
{
    var environment = new ProductionMacDiagnosticEnvironment(AppContext.BaseDirectory);
    return MacDiagnosticCommand.Run(arguments, environment, Console.Out, Console.Error);
}

[SupportedOSPlatform("macos")]
internal sealed class ProductionMacDiagnosticEnvironment : IMacDiagnosticEnvironment
{
    private readonly Lazy<MacTargetWindowLocator> _targetLocator;
    private readonly Lazy<MacPermissionService> _permissionService;
    private readonly Lazy<MacScreenCapturer> _capturer;
    private readonly Lazy<RegionPixelScanner> _scanner;
    private readonly Lazy<MacKeySender> _keyOutput;

    public ProductionMacDiagnosticEnvironment(string baseDirectory)
    {
        _targetLocator = new Lazy<MacTargetWindowLocator>(
            () => new MacTargetWindowLocator(baseDirectory));
        _permissionService = new Lazy<MacPermissionService>(() => new MacPermissionService());
        _capturer = new Lazy<MacScreenCapturer>(
            () => new MacScreenCapturer(_permissionService.Value));
        _scanner = new Lazy<RegionPixelScanner>(
            () => new RegionPixelScanner(_targetLocator.Value, _capturer.Value));
        _keyOutput = new Lazy<MacKeySender>(
            () => new MacKeySender(_targetLocator.Value, _permissionService.Value));
    }

    public TargetWindow? LocateTarget() => _targetLocator.Value.FindFrontmostTarget();

    public PlatformPermissionSnapshot CheckPermissions() => _permissionService.Value.Check();

    public ScreenCaptureResult Capture(TargetBounds bounds) => _capturer.Value.Capture(bounds);

    public ScreenScanResult Decode() => _scanner.Value.ScanScreenData();

    public KeySendResult Send(string hotkey, TargetIdentity expectedTarget) =>
        _keyOutput.Value.Send(hotkey, expectedTarget);

    public string? ResolveAddOnsDirectory(TargetWindow? target) =>
        string.IsNullOrWhiteSpace(target?.ProcessPath)
            ? null
            : WowAddonLocator.ResolveAddOnsDirectory(target.ProcessPath);

    public void ExportPpm(CapturedRegion frame, string path) => PpmFrameExporter.Write(frame, path);
}
