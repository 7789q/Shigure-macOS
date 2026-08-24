namespace Shigure.MacApp;

public static class MacModuleImportCommand
{
    public const int CompletedExitCode = 0;
    public const int InvalidArgumentsExitCode = 2;
    public const int SkippedExitCode = 12;
    public const int FailedExitCode = 13;

    public static bool IsCommand(IReadOnlyList<string> args) =>
        args.Count > 0
        && string.Equals(args[0], "modules", StringComparison.OrdinalIgnoreCase);

    public static int Execute(
        IReadOnlyList<string> args,
        string targetUserDataDirectory,
        Func<string, string, LegacyModuleMigrationResult> migrate,
        Action<MacApplicationEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserDataDirectory);
        ArgumentNullException.ThrowIfNull(migrate);
        ArgumentNullException.ThrowIfNull(emit);

        if (args.Count != 3
            || !string.Equals(args[0], "modules", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(args[1], "import", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(args[2]))
        {
            emit(CreateEvent(
                "module-import-command-rejected",
                "模块导入命令格式无效。请使用 modules import <legacy-data-directory>。"));
            return InvalidArgumentsExitCode;
        }

        var result = migrate(args[2], targetUserDataDirectory);
        if (result.SkippedReason is not null)
        {
            emit(CreateEvent("module-import-skipped", result.SkippedReason));
            return SkippedExitCode;
        }

        if (result.Failures.Count > 0)
        {
            emit(CreateEvent(
                "module-import-failed",
                $"旧模块导入失败 {result.Failures.Count} 项，未覆盖现有模块。"));
            return FailedExitCode;
        }

        emit(CreateEvent(
            "module-imported",
            result.AlreadyCompleted
                ? "该旧数据源已完成导入，本次无需处理。"
                : $"旧模块导入完成：复制 {result.CopiedFiles.Count}，保留 {result.PreservedFiles.Count}。"));
        return CompletedExitCode;
    }

    private static MacApplicationEvent CreateEvent(string stage, string message) =>
        new(DateTimeOffset.UtcNow, stage, message);
}
