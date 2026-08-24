using Shigure.Platform;

namespace Shigure.MacApp;

public static class MacPermissionCommand
{
    public const int ReadyExitCode = 0;
    public const int InvalidArgumentsExitCode = 2;
    public const int RestartRequiredExitCode = 10;
    public const int UserActionRequiredExitCode = 11;

    public static bool IsCommand(IReadOnlyList<string> args) =>
        args.Count > 0
        && string.Equals(args[0], "permission", StringComparison.OrdinalIgnoreCase);

    public static int Execute(
        IReadOnlyList<string> args,
        IPlatformPermissionService permissionService,
        Action<MacApplicationEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(permissionService);
        ArgumentNullException.ThrowIfNull(emit);

        if (!TryParse(args, out var permission))
        {
            emit(CreateEvent(
                "permission-command-rejected",
                "权限命令格式无效。请使用 permission request screen-capture|accessibility。"));
            return InvalidArgumentsExitCode;
        }

        var result = permissionService.Request(permission);
        var displayName = permission == PlatformPermissionKind.ScreenCapture
            ? "屏幕录制"
            : "辅助功能";
        var (exitCode, message) = result.Outcome switch
        {
            PlatformPermissionRequestOutcome.AlreadyGranted =>
                (ReadyExitCode, $"{displayName}权限已经可用。"),
            PlatformPermissionRequestOutcome.Granted =>
                (ReadyExitCode, $"{displayName}权限已经授权。"),
            PlatformPermissionRequestOutcome.RestartRequired =>
                (RestartRequiredExitCode, $"{displayName}权限已经授权，需要重启业务进程。"),
            PlatformPermissionRequestOutcome.UserActionRequired =>
                (UserActionRequiredExitCode, $"{displayName}权限仍需在系统设置中处理。"),
            _ => throw new ArgumentOutOfRangeException(nameof(result.Outcome), result.Outcome, null)
        };

        emit(CreateEvent("permission-requested", message));
        return exitCode;
    }

    private static bool TryParse(
        IReadOnlyList<string> args,
        out PlatformPermissionKind permission)
    {
        permission = default;
        if (args.Count != 3
            || !string.Equals(args[0], "permission", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(args[1], "request", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(args[2], "screen-capture", StringComparison.OrdinalIgnoreCase))
        {
            permission = PlatformPermissionKind.ScreenCapture;
            return true;
        }

        if (string.Equals(args[2], "accessibility", StringComparison.OrdinalIgnoreCase))
        {
            permission = PlatformPermissionKind.Accessibility;
            return true;
        }

        return false;
    }

    private static MacApplicationEvent CreateEvent(string stage, string message) =>
        new(DateTimeOffset.UtcNow, stage, message);
}
