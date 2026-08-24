namespace Shigure.MacApp;

public static class MacLauncherBoundCommand
{
    public const int LauncherUnavailableExitCode = 1;

    public static async Task<int> RunAsync(
        Func<int> execute,
        MacLauncherParentMonitor? monitor,
        Action<MacApplicationEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(emit);

        if (monitor is null)
        {
            return execute();
        }

        using var monitorCancellation = new CancellationTokenSource();
        var commandTask = Task.Run(execute);
        var monitorTask = monitor.WaitForParentExitAsync(monitorCancellation.Token);
        await Task.WhenAny(commandTask, monitorTask).ConfigureAwait(false);

        if (commandTask.IsCompleted)
        {
            monitorCancellation.Cancel();
            await IgnoreMonitorCancellationAsync(monitorTask).ConfigureAwait(false);
            return await commandTask.ConfigureAwait(false);
        }

        ObserveFailure(commandTask);
        try
        {
            await monitorTask.ConfigureAwait(false);
            emit(CreateEvent(
                "launcher-parent-exited",
                "AppKit 外壳已退出，正在终止一次性命令。"));
        }
        catch (Exception exception)
        {
            emit(CreateEvent(
                "launcher-parent-monitor-failed",
                $"外壳进程监视失败：{exception.GetType().Name}。"));
        }

        return LauncherUnavailableExitCode;
    }

    private static async Task IgnoreMonitorCancellationAsync(Task monitorTask)
    {
        try
        {
            await monitorTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void ObserveFailure(Task commandTask) =>
        _ = commandTask.ContinueWith(
            task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    private static MacApplicationEvent CreateEvent(string stage, string message) =>
        new(DateTimeOffset.UtcNow, stage, message);
}
