using System.Text.Encodings.Web;
using System.Text.Json;

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
    private string? _lastLoggedMacroBindingPresence;
    private DateTimeOffset? _lastHealingDiagnosticAt;
    private static readonly JsonSerializerOptions DiagnosticJsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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

    public event Action<RuntimeLogEntry>? DetailedLogAdded;

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

        if ((snapshot.ClassId == 2 || snapshot.ClassId == 6)
            && snapshot.SpecId == 1
            && snapshot.State is not null)
        {
            var macroStatus = snapshot.State.GetInt("宏绑定状态");
            var macroCount = snapshot.State.GetInt("宏绑定数量");
            var hasStatusField = snapshot.State.Values.ContainsKey("宏绑定状态");
            var hasCountField = snapshot.State.Values.ContainsKey("宏绑定数量");
            var macroPresence = $"状态字段={(hasStatusField ? "有" : "无")}，数量字段={(hasCountField ? "有" : "无")}";
            if (macroStatus != _lastLoggedMacroBindingStatus
                || macroCount != _lastLoggedMacroBindingCount)
            {
                _lastLoggedMacroBindingStatus = macroStatus;
                _lastLoggedMacroBindingCount = macroCount;
                AddLog($"WoW宏绑定：{DescribeMacroBindingStatus(macroStatus)}，数量 {macroCount}");
            }
            if (!string.Equals(macroPresence, _lastLoggedMacroBindingPresence, StringComparison.Ordinal))
            {
                _lastLoggedMacroBindingPresence = macroPresence;
                AddDetailedLog($"WoW宏绑定诊断：{macroPresence}，扫描状态字段数 {snapshot.State.Values.Count}");
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
            AddDetailedLog(healAbsorbLog);
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
            AddDetailedLog(diagnosticLog);
        }

        var aoeWarningLog = _aoeWarningLogTracker.Observe(snapshot.State);
        if (aoeWarningLog is not null)
        {
            AddDetailedLog(aoeWarningLog);
        }

        if (string.IsNullOrWhiteSpace(snapshot.CurrentStep))
        {
            return;
        }

        var details = BuildStepLogDetails(snapshot);
        var stepChanged = !string.Equals(snapshot.CurrentStep, _lastLoggedStep, StringComparison.Ordinal);
        var detailsChanged = stepChanged
            || !string.Equals(details, _lastLoggedStepDetails, StringComparison.Ordinal);
        if (stepChanged)
        {
            Notify(LogAdded, new RuntimeLogEntry(_timeProvider.GetUtcNow(), $"步骤：{snapshot.CurrentStep}"));
        }
        if (detailsChanged)
        {
            _lastLoggedStep = snapshot.CurrentStep;
            _lastLoggedStepDetails = details;
            AddDetailedLog($"步骤：{snapshot.CurrentStep}{details}");
        }
        WriteHealingDiagnostic(snapshot, detailsChanged);
    }

    private void WriteHealingDiagnostic(RenderSnapshot snapshot, bool force)
    {
        var state = snapshot.State;
        if (snapshot.ClassId != 2 || snapshot.SpecId != 1 || state is null)
        {
            _lastHealingDiagnosticAt = null;
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var interval = TimeSpan.FromMilliseconds(snapshot.Enabled ? 250 : 1000);
        if (!force && _lastHealingDiagnosticAt is { } previous && now - previous < interval) return;
        _lastHealingDiagnosticAt = now;

        // Log only numeric game/protocol fields. Never serialize the entire state
        // or group dictionaries, which may acquire names or identifiers later.
        var fields = new[]
        {
            "有效性", "队伍类型", "队伍人数", "首领战", "战斗时间", "生命值", "神圣能量", "法力值", "移动",
            "施法技能", "施法(倒计时)", "施法(正计时)", "引导", "公共冷却剩余", "公共冷却时长",
            "Fuyutsui协议版本", "Fuyutsui状态心跳", "宏绑定状态", "宏绑定数量", "DiGua桥接就绪",
            "目标类型", "目标生命值", "目标距离", "目标正面", "目标死亡", "目标身份序号",
            "玩家动作序号", "玩家动作技能", "玩家动作状态",
            "AOE事件类型", "AOE事件阶段", "单目标需求", "多人爆发需求", "多人持续需求", "预计治疗人数",
            "美德覆盖人数", "美德转移需求"
        };
        var groupFields = new[] { "职责", "可治疗", "治疗吸收", "驱散", "预期需求", "爆发需求", "持续需求", "美德道标" };
        // Include unavailable slots as well; a zero eligibility bit must not hide
        // the very injury we need to diagnose. Old schemas without counts keep all.
        var capacity = state.GetInt("队伍人数") is > 0 and <= 40 ? Math.Min(30, state.GetInt("队伍人数")) : 30;
        var members = Enumerable.Range(1, capacity).Select(slot =>
        {
            var member = state.Group.GetValueOrDefault(slot.ToString());
            var values = groupFields.ToDictionary(key => key, key => member?.GetValueOrDefault(key));
            values["槽位"] = slot;
            values["协议生命"] = member?.GetValueOrDefault("生命值");
            values["规则生命"] = UnitSelector.ResolveHealth(slot.ToString(), state);
            return values;
        }).ToArray();
        var actions = Enumerable.Range(1, 4).Select(slot => new[] { "序号", "技能", "状态", "失败原因" }
            .ToDictionary(key => key, key => state.GetValue($"玩家动作事件{slot}{key}"))).ToArray();
        var diagnostic = new
        {
            版本 = 1, Core构建 = typeof(GameState).Module.ModuleVersionId,
            已开启 = snapshot.Enabled, 步骤 = snapshot.CurrentStep, 模块 = snapshot.ModuleName,
            模块版本 = snapshot.UnitInfo.GetValueOrDefault("模块版本"), 玩家槽位 = UnitSelector.ResolvePlayerSlot(state),
            状态 = fields.ToDictionary(key => key, state.GetValue), 队伍 = members,
            动态单位 = state.GetValue("$units"), 人数与缺口 = state.GetValue("$counts"),
            技能 = state.Spells, 光环 = state.Auras, 动作事件 = actions
        };
        AddDetailedLog($"奶骑状态快照：{JsonSerializer.Serialize(diagnostic, DiagnosticJsonOptions)}");
    }

    private static string BuildStepLogDetails(RenderSnapshot snapshot)
    {
        var fields = new (string Key, string Label)[]
        {
            ("动作单位", "目标"),
            ("目标生命值", "目标生命"),
            ("目标原始生命值", "目标协议生命"),
            ("模块版本", "模块版本"),
            ("目标治疗吸收", "目标吸收"),
            ("目标自律", "目标自律"),
            ("目标驱散类型", "目标驱散"),
            ("可驱散目标", "可驱散目标"),
            ("自身生命值", "自身生命"),
            ("战斗时间", "战斗时间"),
            ("生命值", "自身生命"),
            ("移动", "移动"),
            ("站定时长", "站定时长"),
            ("目标类型", "目标类型"),
            ("目标距离", "目标距离"),
            ("目标正面", "目标正面"),
            ("符文", "符文"),
            ("符文能量", "符文能量"),
            ("白骨之盾层数", "白骨之盾层数"),
            ("目标血之疫病", "目标血之疫病"),
            ("凋零充能", "凋零充能"),
            ("凋零冷却", "凋零冷却"),
            ("凋零光环", "凋零光环"),
            ("灵界打击冷却", "灵界打击冷却"),
            ("死亡抚摸冷却", "死亡抚摸冷却"),
            ("血沸充能", "血沸充能"),
            ("血沸循环心打次数", "血沸循环心打次数"),
            ("候选过滤摘要", "候选过滤摘要"),
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

        if (snapshot.State is not null
            && snapshot.ClassId == 2
            && snapshot.SpecId == 1)
        {
            var forecastFields = new (string Key, string Label)[]
            {
                ("AOE事件类型", "压力事件类型"),
                ("AOE事件阶段", "压力事件阶段"),
                ("单目标需求", "单目标需求"),
                ("多人爆发需求", "多人爆发需求"),
                ("多人持续需求", "多人持续需求"),
                ("预计治疗人数", "预计治疗人数"),
                ("美德主目标", "美德主目标"),
                ("美德覆盖人数", "美德覆盖人数"),
                ("美德转移需求", "美德转移需求"),
                ("美德覆盖溢出", "美德覆盖溢出")
            };
            foreach (var (key, label) in forecastFields)
            {
                if (snapshot.State.GetValue(key) is { } value)
                {
                    details.Add($"{label}: {RuntimeMonitorProjection.FormatValue(value)}");
                }
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
        _lastLoggedMacroBindingPresence = null;
        _lastHealingDiagnosticAt = null;
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

    private void AddLog(string message)
    {
        var entry = new RuntimeLogEntry(_timeProvider.GetUtcNow(), message);
        Notify(DetailedLogAdded, entry);
        Notify(LogAdded, entry);
    }

    private void AddDetailedLog(string message) =>
        Notify(DetailedLogAdded, new RuntimeLogEntry(_timeProvider.GetUtcNow(), message));

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
