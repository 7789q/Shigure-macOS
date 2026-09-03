namespace Shigure.Presentation;

public enum RuntimeSessionState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public sealed record RuntimeSessionStatus(
    RuntimeSessionState State,
    string Message,
    AppOptions? Options,
    long? SessionId)
{
    public bool IsRunning => State == RuntimeSessionState.Running;

    public bool IsBusy => State is RuntimeSessionState.Starting or RuntimeSessionState.Stopping;
}

public sealed record RuntimeLogEntry(DateTimeOffset Timestamp, string Message);

public sealed class RuntimeSessionController : IAsyncDisposable
{
    private readonly RuntimeSessionCoordinator _coordinator;
    private readonly TimeProvider _timeProvider;
    private readonly Func<IDisposable?>? _runtimeLeaseFactory;
    private readonly HealAbsorbLogTracker _healAbsorbLogTracker = new();
    private readonly AoeWarningLogTracker _aoeWarningLogTracker = new();
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateSync = new();
    private RuntimeSessionStatus _status = new(
        RuntimeSessionState.Stopped,
        "运行时已停止",
        null,
        null);
    private RenderSnapshot? _lastSnapshot;
    private long _requestVersion;
    private long _replacingSessionId;
    private volatile bool _disposed;
    private IDisposable? _runtimeLease;
    private string? _lastLoggedStep;
    private string? _lastLoggedStepDetails;
    private string? _lastLoggedScanFailureReason;
    private string? _lastLoggedClass;
    private string? _lastLoggedModule;
    private bool? _lastLoggedEnabled;
    private int? _lastLoggedMacroBindingStatus;
    private int? _lastLoggedMacroBindingCount;

    public RuntimeSessionController(
        RuntimeSessionCoordinator coordinator,
        TimeProvider? timeProvider = null,
        Func<IDisposable?>? runtimeLeaseFactory = null)
    {
        _coordinator = coordinator;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _runtimeLeaseFactory = runtimeLeaseFactory;
        _coordinator.SnapshotUpdated += OnSnapshotUpdated;
        _coordinator.RuntimeFailed += OnRuntimeFailed;
        _coordinator.RuntimeStopped += OnRuntimeStopped;
    }

    public event Action<RuntimeSessionStatus>? StatusChanged;

    public event Action<RenderSnapshot>? SnapshotUpdated;

    public event Action<RuntimeLogEntry>? LogAdded;

    public RuntimeSessionStatus Status
    {
        get
        {
            lock (_stateSync)
            {
                return _status;
            }
        }
    }

    public RenderSnapshot? LastSnapshot
    {
        get
        {
            lock (_stateSync)
            {
                return _lastSnapshot;
            }
        }
    }

    public Task StartAsync(AppOptions options, CancellationToken cancellationToken = default) =>
        ChangeSessionAsync(options, restart: false, cancellationToken);

    public Task RestartAsync(AppOptions options, CancellationToken cancellationToken = default) =>
        ChangeSessionAsync(options, restart: true, cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_coordinator.HasSession)
            {
                ReleaseRuntimeLease();
                PublishStatus(RuntimeSessionState.Stopped, "运行时已停止", null, null);
                return;
            }

            Interlocked.Increment(ref _requestVersion);
            PublishStatus(
                RuntimeSessionState.Stopping,
                "正在停止运行时",
                _coordinator.CurrentOptions,
                _coordinator.CurrentSessionId);
            AddLog("正在停止运行时会话");
            await _coordinator.StopAsync(cancellationToken).ConfigureAwait(false);
            ReleaseRuntimeLease();
            PublishStatus(RuntimeSessionState.Stopped, "运行时已停止", null, null);
            AddLog("运行时会话已停止");
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref _replacingSessionId, 0);
            if (!_coordinator.IsRunning)
            {
                ReleaseRuntimeLease();
                PublishStatus(RuntimeSessionState.Stopped, "运行时已停止", null, null);
            }

            throw;
        }
        catch (Exception exception)
        {
            PublishFailure("停止", exception);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void ToggleEnabled()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_coordinator.IsRunning)
        {
            _coordinator.ToggleEnabled();
        }
    }

    public void SetEnabled(bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_coordinator.IsRunning)
        {
            _coordinator.SetEnabled(enabled);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _operationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Interlocked.Increment(ref _requestVersion);
            if (_coordinator.HasSession)
            {
                PublishStatus(
                    RuntimeSessionState.Stopping,
                    "正在停止运行时",
                    _coordinator.CurrentOptions,
                    _coordinator.CurrentSessionId);
            }

            await _coordinator.DisposeAsync().ConfigureAwait(false);
            ReleaseRuntimeLease();
            PublishStatus(RuntimeSessionState.Stopped, "运行时已停止", null, null);
            _coordinator.SnapshotUpdated -= OnSnapshotUpdated;
            _coordinator.RuntimeFailed -= OnRuntimeFailed;
            _coordinator.RuntimeStopped -= OnRuntimeStopped;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ChangeSessionAsync(
        AppOptions options,
        bool restart,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureRuntimeLease();
            var requestVersion = Interlocked.Increment(ref _requestVersion);
            var operation = restart || _coordinator.HasSession ? "重启" : "启动";
            var replacingSessionId = restart || _coordinator.HasSession
                ? _coordinator.CurrentSessionId.GetValueOrDefault()
                : 0;
            Volatile.Write(ref _replacingSessionId, replacingSessionId);
            PublishStatus(
                RuntimeSessionState.Starting,
                $"正在{operation}运行时",
                options,
                _coordinator.CurrentSessionId);
            AddLog($"正在{operation}运行时会话");

            if (restart || _coordinator.HasSession)
            {
                await _coordinator.RestartAsync(options, requestVersion, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await _coordinator.StartAsync(options, requestVersion, cancellationToken).ConfigureAwait(false);
            }

            if (requestVersion != Volatile.Read(ref _requestVersion))
            {
                return;
            }

            if (_coordinator.IsRunning)
            {
                ResetSnapshotLogState();
                PublishStatus(
                    RuntimeSessionState.Running,
                    "运行时正在运行",
                    options,
                    _coordinator.CurrentSessionId);
                AddLog(
                    $"运行时已{operation}：{options.ToggleKey} / {ModeLabel(options.Mode)} / " +
                    $"{(string.IsNullOrWhiteSpace(options.ModuleId) ? "自动模块" : "指定模块")}");
            }
            else
            {
                PublishStatus(RuntimeSessionState.Stopped, "运行时未保持运行", options, null);
            }

            Volatile.Write(ref _replacingSessionId, 0);
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref _replacingSessionId, 0);
            if (_coordinator.IsRunning)
            {
                PublishStatus(
                    RuntimeSessionState.Running,
                    "运行时正在运行",
                    _coordinator.CurrentOptions,
                    _coordinator.CurrentSessionId);
            }
            else
            {
                ReleaseRuntimeLease();
                PublishStatus(RuntimeSessionState.Stopped, "运行时已停止", null, null);
            }

            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Volatile.Write(ref _replacingSessionId, 0);
            if (!_coordinator.IsRunning)
            {
                ReleaseRuntimeLease();
            }

            PublishFailure(restart ? "重启" : "启动", exception);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void OnSnapshotUpdated(long sessionId, RenderSnapshot snapshot)
    {
        if (_disposed || _coordinator.CurrentSessionId != sessionId)
        {
            return;
        }

        lock (_stateSync)
        {
            _lastSnapshot = snapshot;
        }

        WriteSnapshotLog(snapshot);
        Notify(SnapshotUpdated, snapshot);
    }

    private void OnRuntimeFailed(long sessionId, Exception exception)
    {
        if (_disposed || _coordinator.CurrentSessionId != sessionId)
        {
            return;
        }

        PublishFailure("运行", exception);
    }

    private void OnRuntimeStopped(long sessionId)
    {
        if (_disposed
            || _coordinator.CurrentSessionId != sessionId
            || Volatile.Read(ref _replacingSessionId) == sessionId)
        {
            return;
        }

        if (Status.State != RuntimeSessionState.Faulted)
        {
            PublishStatus(RuntimeSessionState.Stopped, "运行时已停止", null, sessionId);
        }

        ReleaseRuntimeLease();
    }

    private void PublishFailure(string operation, Exception exception)
    {
        var message = $"运行时{operation}失败：{exception.GetType().Name}";
        PublishStatus(
            RuntimeSessionState.Faulted,
            message,
            _coordinator.CurrentOptions,
            _coordinator.CurrentSessionId);
        AddLog(message);
    }

    private void PublishStatus(
        RuntimeSessionState state,
        string message,
        AppOptions? options,
        long? sessionId)
    {
        var status = new RuntimeSessionStatus(state, message, options, sessionId);
        lock (_stateSync)
        {
            _status = status;
        }

        Notify(StatusChanged, status);
    }

    private void WriteSnapshotLog(RenderSnapshot snapshot)
    {
        if (!string.Equals(snapshot.ScanFailureReason, _lastLoggedScanFailureReason, StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(snapshot.ScanFailureReason))
            {
                if (!string.IsNullOrWhiteSpace(_lastLoggedScanFailureReason))
                {
                    AddLog("扫描已恢复");
                }
            }
            else
            {
                AddLog($"扫描失败：{snapshot.ScanFailureReason}");
            }

            _lastLoggedScanFailureReason = snapshot.ScanFailureReason;
        }

        var classSpec = snapshot.ClassName is null
            ? null
            : $"{snapshot.ClassName} / {snapshot.SpecName ?? "-"}";
        if (!string.IsNullOrWhiteSpace(classSpec)
            && !string.Equals(classSpec, _lastLoggedClass, StringComparison.Ordinal))
        {
            _lastLoggedClass = classSpec;
            AddLog($"识别职业：{classSpec}");
        }

        if (snapshot.ClassId == 2 && snapshot.SpecId == 1 && snapshot.State is not null)
        {
            var macroStatus = snapshot.State.GetInt("宏绑定状态");
            var macroCount = snapshot.State.GetInt("宏绑定数量");
            if (macroStatus != _lastLoggedMacroBindingStatus
                || macroCount != _lastLoggedMacroBindingCount)
            {
                _lastLoggedMacroBindingStatus = macroStatus;
                _lastLoggedMacroBindingCount = macroCount;
                AddLog($"WoW宏绑定：{DescribeMacroBindingStatus(macroStatus)}，数量 {macroCount}");
            }
        }

        if (_lastLoggedEnabled != snapshot.Enabled)
        {
            _lastLoggedEnabled = snapshot.Enabled;
            AddLog(snapshot.Enabled ? "逻辑已开启" : "逻辑已关闭");
        }

        if (!string.Equals(snapshot.ModuleName, _lastLoggedModule, StringComparison.Ordinal))
        {
            _lastLoggedModule = snapshot.ModuleName;
            if (!string.IsNullOrWhiteSpace(snapshot.ModuleName))
            {
                AddLog($"匹配模块：{snapshot.ModuleName}");
            }
        }

        var healAbsorbLog = _healAbsorbLogTracker.Observe(snapshot.State?.HealAbsorbDiagnostic);
        if (healAbsorbLog is not null)
        {
            AddLog(healAbsorbLog);
        }

        var aoeDiagnosticsReady = string.IsNullOrWhiteSpace(snapshot.ScanFailureReason)
            && snapshot.State is not null
            && snapshot.State.GetInt("有效性") == 1
            && (snapshot.ClassId != 2 || snapshot.SpecId != 1 || snapshot.State.GetBool("DiGua桥接就绪"));
        if (!aoeDiagnosticsReady)
        {
            _aoeWarningLogTracker.ResetDiagnosticBaseline();
        }
        foreach (var diagnosticLog in aoeDiagnosticsReady
                     ? _aoeWarningLogTracker.ObserveDiagnostics(snapshot.State)
                     : [])
        {
            AddLog(diagnosticLog);
        }

        var aoeWarningLog = _aoeWarningLogTracker.Observe(snapshot.State);
        if (aoeWarningLog is not null)
        {
            AddLog(aoeWarningLog);
        }

        if (string.IsNullOrWhiteSpace(snapshot.CurrentStep))
        {
            return;
        }

        var details = BuildStepLogDetails(snapshot);
        if (!string.Equals(snapshot.CurrentStep, _lastLoggedStep, StringComparison.Ordinal)
            || !string.Equals(details, _lastLoggedStepDetails, StringComparison.Ordinal))
        {
            _lastLoggedStep = snapshot.CurrentStep;
            _lastLoggedStepDetails = details;
            AddLog($"步骤：{snapshot.CurrentStep}{details}");
        }
    }

    private static string BuildStepLogDetails(RenderSnapshot snapshot)
    {
        var fields = new (string Key, string Label)[]
        {
            ("动作单位", "目标"),
            ("目标生命值", "目标生命"),
            ("目标治疗吸收", "目标吸收"),
            ("目标自律", "目标自律"),
            ("目标驱散类型", "目标驱散"),
            ("可驱散目标", "可驱散目标"),
            ("自身生命值", "自身生命"),
            ("安全确认", "安全确认"),
            ("确认帧", "确认帧"),
            ("动作按键", "按键"),
            ("动作延迟", "动作延迟"),
            ("逻辑延迟", "逻辑延迟"),
            ("规则编号", "规则编号"),
            ("优先级说明", "优先级说明"),
            ("限流键", "限流键"),
            ("正义盾击候选状态", "正义盾击候选状态"),
            ("等待技能", "等待技能"),
            ("重试时机", "重试时机"),
            ("技能确认", "技能确认"),
            ("冷却确认", "冷却确认"),
            ("确认来源", "确认来源"),
            ("确认状态字段", "确认状态字段"),
            ("确认初始值", "确认初始值"),
            ("确认当前值", "确认当前值"),
            ("确认耗时", "确认耗时"),
            ("技能冷却", "技能冷却"),
            ("玩家动作序号", "动作序号"),
            ("玩家动作技能", "动作技能码"),
            ("玩家动作状态", "动作状态码"),
            ("玩家动作状态说明", "动作状态说明"),
            ("期待动作技能码", "期待动作技能码"),
            ("公共冷却剩余", "公共冷却剩余"),
            ("发送序列", "发送序列"),
            ("发送结果", "发送结果"),
            ("发送结果说明", "发送结果说明"),
            ("发送失败", "发送失败"),
            ("缺失按键", "缺失按键"),
            ("已跳过缺失按键", "已跳过缺失按键"),
            ("已跳过确认失败动作", "已跳过确认失败动作"),
            ("发送拦截", "发送拦截"),
            ("发送拦截原因", "发送拦截原因"),
            ("失败归因", "失败归因"),
            ("失败诊断", "失败诊断"),
            ("失败退让", "失败退让"),
            ("灌注转换确认", "灌注转换确认")
        };
        var details = new List<string>();
        foreach (var (key, label) in fields)
        {
            if (snapshot.UnitInfo.TryGetValue(key, out var value))
            {
                details.Add($"{label}: {RuntimeMonitorProjection.FormatValue(value)}");
            }
        }

        return details.Count == 0 ? string.Empty : $"，{string.Join("，", details)}";
    }

    private void ResetSnapshotLogState()
    {
        _lastLoggedStep = null;
        _lastLoggedStepDetails = null;
        _lastLoggedScanFailureReason = null;
        _lastLoggedClass = null;
        _lastLoggedModule = null;
        _lastLoggedEnabled = null;
        _lastLoggedMacroBindingStatus = null;
        _lastLoggedMacroBindingCount = null;
        _healAbsorbLogTracker.Reset();
        _aoeWarningLogTracker.Reset();
    }

    private static string DescribeMacroBindingStatus(int status) => status switch
    {
        1 => "已就绪",
        2 => "战斗锁定，等待脱战重建",
        3 => "创建失败",
        _ => "未初始化"
    };

    private void EnsureRuntimeLease()
    {
        if (_runtimeLease is not null || _runtimeLeaseFactory is null)
        {
            return;
        }

        var lease = _runtimeLeaseFactory()
            ?? throw new InvalidOperationException("运行时已被另一个 Shigure 进程占用。");
        if (Interlocked.CompareExchange(ref _runtimeLease, lease, null) is not null)
        {
            lease.Dispose();
        }
    }

    private void ReleaseRuntimeLease() =>
        Interlocked.Exchange(ref _runtimeLease, null)?.Dispose();

    private void AddLog(string message) =>
        Notify(LogAdded, new RuntimeLogEntry(_timeProvider.GetUtcNow(), message));

    private static string ModeLabel(SendMode mode) => mode switch
    {
        SendMode.Click => "单击",
        SendMode.Hold => "按住",
        _ => "开关"
    };

    private static void Notify<T>(Action<T>? subscribers, T value)
    {
        if (subscribers is null)
        {
            return;
        }

        foreach (Action<T> subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber(value);
            }
            catch
            {
                // UI 观察者失败不得终止运行时线程。
            }
        }
    }
}
