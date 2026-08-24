namespace Shigure.MacApp;

public sealed record MacApplicationEvent(
    DateTimeOffset Timestamp,
    string Stage,
    string Message);

public sealed class MacApplicationHost : IAsyncDisposable
{
    private readonly RuntimeSessionCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    private readonly TaskCompletionSource _runtimeStopped = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private int _runState;

    public MacApplicationHost(
        RuntimeSessionCoordinator coordinator,
        TimeProvider? timeProvider = null)
    {
        _coordinator = coordinator;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _coordinator.RuntimeFailed += OnRuntimeFailed;
        _coordinator.RuntimeStopped += OnRuntimeStopped;
    }

    public event Action<MacApplicationEvent>? EventEmitted;

    public async Task RunAsync(AppOptions options, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _runState, 1, 0) != 0)
        {
            throw new InvalidOperationException("Mac 应用宿主只能运行一次。");
        }

        Emit("host-starting", "正在启动运行时会话。");
        try
        {
            await _coordinator.StartAsync(options, requestVersion: 1).ConfigureAwait(false);
            Emit("runtime-started", "运行时会话已启动。");

            var cancellationTask = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            await Task.WhenAny(cancellationTask, _runtimeStopped.Task).ConfigureAwait(false);
        }
        finally
        {
            Emit("host-stopping", "正在停止运行时会话。");
            await _coordinator.StopAsync().ConfigureAwait(false);
            Emit("host-stopped", "运行时会话已停止。");
            Volatile.Write(ref _runState, 2);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _coordinator.RuntimeFailed -= OnRuntimeFailed;
        _coordinator.RuntimeStopped -= OnRuntimeStopped;
        await _coordinator.DisposeAsync().ConfigureAwait(false);
    }

    private void OnRuntimeFailed(long sessionId, Exception exception) =>
        Emit("runtime-failed", $"运行时会话 {sessionId} 失败：{exception.GetType().Name}。");

    private void OnRuntimeStopped(long sessionId)
    {
        Emit("runtime-stopped", $"运行时会话 {sessionId} 已结束。");
        _runtimeStopped.TrySetResult();
    }

    private void Emit(string stage, string message) =>
        EventEmitted?.Invoke(new MacApplicationEvent(
            _timeProvider.GetUtcNow(),
            stage,
            message));
}
