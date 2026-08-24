using System.Globalization;
using System.Text;
using Shigure.Platform;

namespace Shigure.MacDiagnostics;

internal interface IMacDiagnosticEnvironment
{
    TargetWindow? LocateTarget();

    PlatformPermissionSnapshot CheckPermissions();

    ScreenCaptureResult Capture(TargetBounds bounds);

    ScreenScanResult Decode();

    KeySendResult Send(string hotkey, TargetIdentity expectedTarget);

    string? ResolveAddOnsDirectory(TargetWindow? target);

    void ExportPpm(CapturedRegion frame, string path);
}

internal static class MacDiagnosticCommand
{
    private const string Usage = """
        Shigure Mac 诊断

        用法:
          Shigure.MacDiagnostics [status]
          Shigure.MacDiagnostics locate
          Shigure.MacDiagnostics permissions
          Shigure.MacDiagnostics addon-path
          Shigure.MacDiagnostics capture [--export <path.ppm>]
          Shigure.MacDiagnostics decode
          Shigure.MacDiagnostics send --hotkey <key> [--execute]

        无参数默认执行只读 status。capture/decode 会读取屏幕；send 只有带
        --execute 才会发送真实事件。工具不会请求系统权限。
        """;

    public static int Run(
        IReadOnlyList<string> arguments,
        IMacDiagnosticEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var parse = Parse(arguments);
        if (parse.Error is not null)
        {
            error.WriteLine($"参数错误: {parse.Error}");
            error.WriteLine(Usage);
            return 2;
        }

        try
        {
            return parse.Options!.Command switch
            {
                DiagnosticCommand.Help => WriteHelp(output),
                DiagnosticCommand.Status => RunStatus(environment, output),
                DiagnosticCommand.Locate => RunLocate(environment, output),
                DiagnosticCommand.Permissions => RunPermissions(environment, output),
                DiagnosticCommand.AddOnPath => RunAddOnPath(environment, output),
                DiagnosticCommand.Capture => RunCapture(parse.Options, environment, output, error),
                DiagnosticCommand.Decode => RunDecode(environment, output, error),
                DiagnosticCommand.Send => RunSend(parse.Options, environment, output, error),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine($"诊断失败: {ex.Message}");
            return 1;
        }
    }

    public static bool IsHelpRequest(IReadOnlyList<string> arguments) =>
        arguments.Count == 1 && arguments[0] is "help" or "--help" or "-h";

    public static void PrintHelp(TextWriter output) => output.WriteLine(Usage);

    private static int WriteHelp(TextWriter output)
    {
        PrintHelp(output);
        return 0;
    }

    private static int RunStatus(IMacDiagnosticEnvironment environment, TextWriter output)
    {
        var target = environment.LocateTarget();
        var permissions = environment.CheckPermissions();
        var addOnsDirectory = environment.ResolveAddOnsDirectory(target);

        WriteTarget(target, output);
        WritePermissions(permissions, output);
        output.WriteLine($"AddOns 路径: {addOnsDirectory ?? "未解析"}");
        return target is not null && permissions.IsReady ? 0 : 1;
    }

    private static int RunLocate(IMacDiagnosticEnvironment environment, TextWriter output)
    {
        var target = environment.LocateTarget();
        WriteTarget(target, output);
        return target is null ? 1 : 0;
    }

    private static int RunPermissions(IMacDiagnosticEnvironment environment, TextWriter output)
    {
        var permissions = environment.CheckPermissions();
        WritePermissions(permissions, output);
        return permissions.IsReady ? 0 : 1;
    }

    private static int RunAddOnPath(IMacDiagnosticEnvironment environment, TextWriter output)
    {
        var target = environment.LocateTarget();
        var addOnsDirectory = environment.ResolveAddOnsDirectory(target);
        WriteTarget(target, output);
        output.WriteLine($"AddOns 路径: {addOnsDirectory ?? "未解析"}");
        return addOnsDirectory is null ? 1 : 0;
    }

    private static int RunCapture(
        DiagnosticOptions options,
        IMacDiagnosticEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        var target = environment.LocateTarget();
        if (target?.Bounds is not { IsValid: true } bounds)
        {
            error.WriteLine("捕获失败: 未找到具有有效区域的目标窗口");
            return 1;
        }

        var capture = environment.Capture(bounds);
        if (!capture.Succeeded)
        {
            error.WriteLine($"捕获失败 [{capture.FailureKind}]: {capture.FailureReason ?? "未提供原因"}");
            return 1;
        }

        var frame = capture.Frame!;
        output.WriteLine(
            FormattableString.Invariant(
                $"捕获成功: {frame.PixelWidth}x{frame.PixelHeight}, scale={frame.ScaleX:0.###}x{frame.ScaleY:0.###}, {frame.PixelFormat}/{frame.ColorSpace}"));

        if (options.ExportPath is null)
        {
            output.WriteLine("画面未保存");
            return 0;
        }

        var exportPath = Path.GetFullPath(options.ExportPath);
        output.WriteLine("敏感性警告: 导出的屏幕图像可能包含账号、角色、聊天和桌面隐私。");
        output.WriteLine($"导出路径: {exportPath}");
        output.Flush();
        environment.ExportPpm(frame, exportPath);
        output.WriteLine("导出完成");
        return 0;
    }

    private static int RunDecode(
        IMacDiagnosticEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        var result = environment.Decode();
        output.WriteLine($"状态字段数: {result.RowData?.Count ?? 0}");
        output.WriteLine($"CountBars 字段数: {result.BarData.Count}");
        output.WriteLine($"治疗吸收字段数: {result.HealAbsorbData.Count}");
        output.WriteLine(FormattableString.Invariant(
            $"扫描耗时: 定位={result.Timing.Locate.TotalMilliseconds:0.000} ms, 捕获={result.Timing.Capture.TotalMilliseconds:0.000} ms, 解码={result.Timing.Decode.TotalMilliseconds:0.000} ms, 总计={result.Timing.Total.TotalMilliseconds:0.000} ms"));
        if (result.Target is not null)
        {
            WriteTarget(result.Target, output);
        }

        if (result.FailureReason is null)
        {
            return 0;
        }

        error.WriteLine($"解码未完成: {result.FailureReason}");
        return 1;
    }

    private static int RunSend(
        DiagnosticOptions options,
        IMacDiagnosticEnvironment environment,
        TextWriter output,
        TextWriter error)
    {
        var binding = HotkeyParser.Parse(options.Hotkey!);
        if (binding is null)
        {
            error.WriteLine($"发键失败: 无法解析按键“{options.Hotkey}”");
            return 1;
        }

        var target = environment.LocateTarget();
        if (target is null || !target.Identity.IsValid)
        {
            error.WriteLine("发键失败: 未找到有效目标窗口");
            return 1;
        }

        output.WriteLine($"目标: {FormatIdentity(target.Identity)}");
        output.WriteLine($"热键: {options.Hotkey}");
        if (!options.ExecuteSend)
        {
            output.WriteLine("dry-run: 未发送事件；增加 --execute 才会执行真实发键");
            return 0;
        }

        output.WriteLine("执行真实发键");
        output.Flush();
        var result = environment.Send(options.Hotkey!, target.Identity);
        if (!result.Succeeded)
        {
            error.WriteLine($"发键失败 [{result.FailureKind}]: {result.FailureReason ?? "未提供原因"}");
            return 1;
        }

        output.WriteLine("发键成功");
        return 0;
    }

    private static void WriteTarget(TargetWindow? target, TextWriter output)
    {
        if (target is null)
        {
            output.WriteLine("目标窗口: 未找到");
            return;
        }

        output.WriteLine($"目标窗口: {FormatIdentity(target.Identity)}");
        output.WriteLine($"进程路径: {target.ProcessPath ?? "未知"}");
        output.WriteLine(
            target.Bounds is { } bounds
                ? $"窗口区域: x={bounds.X}, y={bounds.Y}, width={bounds.Width}, height={bounds.Height}"
                : "窗口区域: 未知");
    }

    private static void WritePermissions(PlatformPermissionSnapshot permissions, TextWriter output)
    {
        output.WriteLine($"屏幕录制权限: {FormatPermission(permissions.ScreenCapture)}");
        output.WriteLine($"辅助功能权限: {FormatPermission(permissions.Accessibility)}");
    }

    private static string FormatPermission(PlatformPermissionStatus status) =>
        status.RestartRequired ? $"{status.State} (需要重启进程)" : status.State.ToString();

    private static string FormatIdentity(TargetIdentity identity) =>
        $"platform={identity.Platform}, pid={identity.ProcessId}, window={identity.WindowId}";

    private static DiagnosticParseResult Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
        {
            return DiagnosticParseResult.Success(new DiagnosticOptions(DiagnosticCommand.Status, null, null, false));
        }

        var command = arguments[0].ToLowerInvariant();
        return command switch
        {
            "help" or "--help" or "-h" => RequireNoArguments(DiagnosticCommand.Help, arguments),
            "status" => RequireNoArguments(DiagnosticCommand.Status, arguments),
            "locate" => RequireNoArguments(DiagnosticCommand.Locate, arguments),
            "permissions" => RequireNoArguments(DiagnosticCommand.Permissions, arguments),
            "addon-path" => RequireNoArguments(DiagnosticCommand.AddOnPath, arguments),
            "capture" => ParseCapture(arguments),
            "decode" => RequireNoArguments(DiagnosticCommand.Decode, arguments),
            "send" => ParseSend(arguments),
            _ => DiagnosticParseResult.Failure($"未知命令“{arguments[0]}”")
        };
    }

    private static DiagnosticParseResult RequireNoArguments(
        DiagnosticCommand command,
        IReadOnlyList<string> arguments) =>
        arguments.Count == 1
            ? DiagnosticParseResult.Success(new DiagnosticOptions(command, null, null, false))
            : DiagnosticParseResult.Failure($"命令“{arguments[0]}”不接受额外参数");

    private static DiagnosticParseResult ParseCapture(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 1)
        {
            return DiagnosticParseResult.Success(new DiagnosticOptions(DiagnosticCommand.Capture, null, null, false));
        }

        if (arguments.Count == 3 && arguments[1] == "--export" && !string.IsNullOrWhiteSpace(arguments[2]))
        {
            return DiagnosticParseResult.Success(
                new DiagnosticOptions(DiagnosticCommand.Capture, arguments[2], null, false));
        }

        return DiagnosticParseResult.Failure("capture 仅接受可选的 --export <path.ppm>");
    }

    private static DiagnosticParseResult ParseSend(IReadOnlyList<string> arguments)
    {
        string? hotkey = null;
        var execute = false;
        for (var index = 1; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--hotkey" when hotkey is null
                    && index + 1 < arguments.Count
                    && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal):
                    hotkey = arguments[++index];
                    break;
                case "--execute" when !execute:
                    execute = true;
                    break;
                default:
                    return DiagnosticParseResult.Failure($"send 参数无效或重复: {arguments[index]}");
            }
        }

        return string.IsNullOrWhiteSpace(hotkey)
            ? DiagnosticParseResult.Failure("send 必须提供 --hotkey <key>")
            : DiagnosticParseResult.Success(
                new DiagnosticOptions(DiagnosticCommand.Send, null, hotkey, execute));
    }

    private enum DiagnosticCommand
    {
        Help,
        Status,
        Locate,
        Permissions,
        AddOnPath,
        Capture,
        Decode,
        Send
    }

    private sealed record DiagnosticOptions(
        DiagnosticCommand Command,
        string? ExportPath,
        string? Hotkey,
        bool ExecuteSend);

    private sealed record DiagnosticParseResult(DiagnosticOptions? Options, string? Error)
    {
        public static DiagnosticParseResult Success(DiagnosticOptions options) => new(options, null);

        public static DiagnosticParseResult Failure(string error) => new(null, error);
    }
}

internal static class PpmFrameExporter
{
    public static void Write(CapturedRegion frame, string path)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        int pixelCount;
        try
        {
            pixelCount = checked(frame.PixelWidth * frame.PixelHeight);
        }
        catch (OverflowException ex)
        {
            throw new IOException("捕获帧尺寸溢出，无法导出", ex);
        }

        if (frame.PixelWidth <= 0
            || frame.PixelHeight <= 0
            || frame.PixelFormat != CapturedPixelFormat.Argb32
            || frame.ColorSpace != CapturedColorSpace.Srgb
            || frame.ArgbPixels.Length != pixelCount)
        {
            throw new IOException("捕获帧格式或像素缓冲区无效，无法导出");
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"导出目录不存在: {directory}");
        }

        using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var header = Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"P6\n{frame.PixelWidth} {frame.PixelHeight}\n255\n"));
        stream.Write(header);

        Span<byte> rgb = stackalloc byte[3];
        foreach (var pixel in frame.ArgbPixels.Span)
        {
            rgb[0] = (byte)((pixel >> 16) & 0xFF);
            rgb[1] = (byte)((pixel >> 8) & 0xFF);
            rgb[2] = (byte)(pixel & 0xFF);
            stream.Write(rgb);
        }
    }
}
