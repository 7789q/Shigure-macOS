using System.Collections.Concurrent;
using Shigure.Platform;

namespace Shigure;

public sealed class ShigureRuntime : IDisposable
{
    private readonly AppOptions _options;
    private readonly IRuntimeScreenScanner _scanner;
    private readonly IRuntimeStateBuilder _stateBuilder;
    private readonly ITargetKeyOutput _keySender;
    private readonly ITriggerInput _triggerInput;
    private readonly IRuntimeLogic _logic;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentQueue<RuntimeCommand> _pendingCommands = new();

    private GameState? _state;
    private string? _className;
    private string? _specName;
    private int? _classId;
    private int? _specId;
    private string? _moduleName;
    private string? _scanFailureReason;
    private bool _scanUnavailable;
    private string _currentStep = "等待启动";
    private IReadOnlyDictionary<string, object?> _unitInfo = new Dictionary<string, object?>();
    private bool _enabled;
    private bool _clickPending;
    private readonly Dictionary<string, DateTimeOffset> _lastRuleSentAt = new(StringComparer.Ordinal);
    private DateTimeOffset _logicPausedUntil = DateTimeOffset.MinValue;
    private int _triggerInputDisposed;
    private int _scannerDisposed;

    public ShigureRuntime(
        AppOptions options,
        IRuntimeScreenScanner scanner,
        IRuntimeStateBuilder stateBuilder,
        ITargetKeyOutput keySender,
        ITriggerInput triggerInput,
        IRuntimeLogic logic,
        TimeProvider timeProvider)
    {
        _options = options;
        _scanner = scanner;
        _stateBuilder = stateBuilder;
        _keySender = keySender;
        _triggerInput = triggerInput;
        _logic = logic;
        _timeProvider = timeProvider;
    }

    public event Action<RenderSnapshot>? SnapshotUpdated;

    public AppOptions Options => _options;

    public void SetEnabled(bool enabled)
    {
        _pendingCommands.Enqueue(RuntimeCommand.SetEnabled(enabled));
    }

    public void ToggleEnabled()
    {
        _pendingCommands.Enqueue(RuntimeCommand.ToggleEnabled());
    }

    private void ApplyEnabled(bool enabled)
    {
        _enabled = enabled;
        _clickPending = false;
        if (!enabled)
        {
            _lastRuleSentAt.Clear();
            _logicPausedUntil = DateTimeOffset.MinValue;
        }

        _currentStep = enabled ? "手动开启" : "手动关闭";
        PublishSnapshot();
    }

    private void DrainPendingCommands()
    {
        while (_pendingCommands.TryDequeue(out var command))
        {
            switch (command.Kind)
            {
                case RuntimeCommandKind.SetEnabled:
                    ApplyEnabled(command.Enabled);
                    break;
                case RuntimeCommandKind.ToggleEnabled:
                    ApplyEnabled(!_enabled);
                    break;
            }
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _triggerInputDisposed) != 0, this);

        TriggerInputBinding? trigger;
        try
        {
            trigger = _triggerInput.Resolve(_options.ToggleKey);
        }
        catch
        {
            Dispose();
            throw;
        }

        if (trigger is null)
        {
            Dispose();
            _currentStep = $"无法识别触发键: {_options.ToggleKey}";
            PublishSnapshot();
            return;
        }

        var isPulseTrigger = trigger.Value.IsPulse;
        var edgeTracker = new TriggerInputEdgeTracker();
        var lastLogicAt = DateTimeOffset.MinValue;
        var lastRenderAt = DateTimeOffset.MinValue;
        var lastToggleAt = DateTimeOffset.MinValue;

        try
        {
            _currentStep = "已启动";
            PublishSnapshot();

            while (!cancellationToken.IsCancellationRequested)
            {
                DrainPendingCommands();
                var now = _timeProvider.GetUtcNow();
                var edges = isPulseTrigger
                    ? TriggerInputEdgeTracker.ObservePulse(_triggerInput.ConsumePulse(trigger.Value))
                    : edgeTracker.ObserveState(_triggerInput.IsPressed(trigger.Value));
                var rising = edges.Rising
                    && (isPulseTrigger || now - lastToggleAt >= TimeSpan.FromMilliseconds(120));

                if (rising)
                {
                    lastToggleAt = now;
                    if (TriggerModePolicy.IsSingleShot(_options.Mode, isPulseTrigger))
                    {
                        _enabled = true;
                        _clickPending = true;
                        _currentStep = isPulseTrigger ? "脉冲触发" : "单击触发";
                    }
                    else
                    {
                        HandleRisingEdge();
                    }

                    PublishSnapshot();
                    lastRenderAt = now;
                }

                if (_options.Mode == SendMode.Hold && !isPulseTrigger)
                {
                    _enabled = edges.IsPressed;
                    if (edges.Falling)
                    {
                        _lastRuleSentAt.Clear();
                        _logicPausedUntil = DateTimeOffset.MinValue;
                        _currentStep = "按住结束";
                        PublishSnapshot();
                        lastRenderAt = now;
                    }
                }

                var scanInterval = RuntimeScanCadence.Resolve(
                    _options.LogicInterval,
                    _enabled,
                    _scanUnavailable);
                if (now - lastLogicAt >= scanInterval)
                {
                    lastLogicAt = now;
                    if (now >= _logicPausedUntil)
                    {
                        TickLogic();
                    }
                }

                if (now - lastRenderAt >= _options.RenderInterval)
                {
                    lastRenderAt = now;
                    PublishSnapshot();
                }

                await Task.Delay(TimeSpan.FromMilliseconds(25), _timeProvider, cancellationToken);
            }
        }
        finally
        {
            Dispose();
            _enabled = false;
            _clickPending = false;
            _logicPausedUntil = DateTimeOffset.MinValue;
            _currentStep = "已停止";
            PublishSnapshot();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _triggerInputDisposed, 1) == 0)
        {
            _triggerInput.Dispose();
        }

        if (Interlocked.Exchange(ref _scannerDisposed, 1) == 0
            && _scanner is IDisposable disposableScanner)
        {
            disposableScanner.Dispose();
        }
    }

    private void HandleRisingEdge()
    {
        switch (_options.Mode)
        {
            case SendMode.Click:
                _enabled = true;
                _clickPending = true;
                _currentStep = "单击触发";
                break;
            case SendMode.Hold:
                _enabled = true;
                _currentStep = "按住触发";
                break;
            default:
                _enabled = !_enabled;
                _clickPending = false;
                if (!_enabled)
                {
                    _lastRuleSentAt.Clear();
                    _logicPausedUntil = DateTimeOffset.MinValue;
                }

                _currentStep = _enabled ? "逻辑开启" : "逻辑关闭";
                break;
        }
    }

    private void TickLogic()
    {
        var scan = _scanner.ScanScreenData();
        _scanFailureReason = scan.FailureReason;
        _scanUnavailable = scan.RowData is null;

        if (scan.RowData is null)
        {
            _state = null;
            _classId = null;
            _specId = null;
            _className = null;
            _specName = null;
            _moduleName = null;
            _unitInfo = new Dictionary<string, object?>();
            if (_enabled)
            {
                _currentStep = "等待游戏状态";
            }

            return;
        }

        _state = _stateBuilder.Build(scan.RowData, scan.BarData, scan.HealAbsorbData);
        _classId = _state.GetInt("职业");
        _specId = _state.GetInt("专精");
        (_className, _specName) = ClassNames.GetClassAndSpecName(_classId, _specId);
        if (!_state.GetBool("有效性"))
        {
            _moduleName = null;
            _currentStep =
                $"等待游戏状态（状态字段 {scan.RowData.Count}，CountBars {scan.BarData.Count}，" +
                $"治疗吸收 {scan.HealAbsorbData.Count}，职业 {_classId?.ToString() ?? "-"}，" +
                $"专精 {_specId?.ToString() ?? "-"}，有效性 false）";
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        var evaluation = _logic.Evaluate(_classId, _specId, _specName, _state, _enabled);
        _moduleName = evaluation.ModuleName;

        if (!_enabled)
        {
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        var decision = evaluation.Decision;
        if (decision is null)
        {
            _currentStep = "逻辑未返回决策";
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        _currentStep = decision.Step;
        _unitInfo = decision.UnitInfo;
        _moduleName = decision.ModuleName;

        if (_clickPending)
        {
            if (_clickPending
                && !string.IsNullOrWhiteSpace(decision.Hotkey))
            {
                var sendAttemptAt = _timeProvider.GetUtcNow();
                if (CanSend(decision, sendAttemptAt))
                {
                    SendAndPauseLogic(decision, scan.Target?.Identity);
                }
            }

            _enabled = false;
            _clickPending = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(decision.Hotkey))
        {
            var sendAttemptAt = _timeProvider.GetUtcNow();
            if (CanSend(decision, sendAttemptAt))
            {
                SendAndPauseLogic(decision, scan.Target?.Identity);
            }
        }
    }

    private void SendAndPauseLogic(LogicDecision decision, TargetIdentity? targetIdentity)
    {
        var sendResult = _keySender.Send(decision.Hotkey!, targetIdentity);
        if (!sendResult.Succeeded)
        {
            var info = _unitInfo.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);
            info["发送失败"] = sendResult.FailureReason ?? "未知原因";
            _unitInfo = info;
            _currentStep = $"{decision.Step}（按键发送失败）";
            return;
        }

        var sentAt = _timeProvider.GetUtcNow();
        var sentInfo = _unitInfo.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal);
        sentInfo["发送结果"] = "已投递到 WoW 进程";
        _unitInfo = sentInfo;
        RecordSent(decision, sentAt);
        if (decision.LogicDelayMs > 0)
        {
            _logicPausedUntil = sentAt.AddMilliseconds(decision.LogicDelayMs);
        }
    }

    private bool CanSend(LogicDecision decision, DateTimeOffset now)
    {
        if (decision.DelayMs <= 0)
        {
            return true;
        }

        var key = string.IsNullOrWhiteSpace(decision.RateLimitKey)
            ? decision.Hotkey ?? string.Empty
            : decision.RateLimitKey;
        if (_lastRuleSentAt.TryGetValue(key, out var lastSentAt)
            && now - lastSentAt < TimeSpan.FromMilliseconds(decision.DelayMs))
        {
            return false;
        }

        return true;
    }

    private void RecordSent(LogicDecision decision, DateTimeOffset now)
    {
        if (decision.DelayMs <= 0)
        {
            return;
        }

        var key = string.IsNullOrWhiteSpace(decision.RateLimitKey)
            ? decision.Hotkey ?? string.Empty
            : decision.RateLimitKey;
        _lastRuleSentAt[key] = now;
    }

    private void PublishSnapshot()
    {
        SnapshotUpdated?.Invoke(new RenderSnapshot(
            _enabled,
            _className,
            _specName,
            _classId,
            _specId,
            _moduleName,
            _state,
            _currentStep,
            _unitInfo,
            BuildDynamicValues(_state),
            _scanFailureReason));
    }

    private static IReadOnlyList<DynamicValueSnapshot> BuildDynamicValues(GameState? state)
    {
        if (state is null)
        {
            return [];
        }

        var values = new List<DynamicValueSnapshot>();
        if (state.Values.TryGetValue("$units", out var unitsObj)
            && unitsObj is IReadOnlyDictionary<string, string?> units)
        {
            foreach (var (name, slot) in units.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                values.Add(new DynamicValueSnapshot("单位", name, FormatUnitSlot(state, slot)));
            }
        }

        if (state.Values.TryGetValue("$unithealth", out var healthObj)
            && healthObj is IReadOnlyDictionary<string, object?> unitHealth)
        {
            foreach (var (name, value) in unitHealth.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                values.Add(new DynamicValueSnapshot("值名称", name, FormatSnapshotValue(value)));
            }
        }

        if (state.Values.TryGetValue("$counts", out var countsObj)
            && countsObj is IReadOnlyDictionary<string, int> counts)
        {
            foreach (var (name, value) in counts.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                values.Add(new DynamicValueSnapshot("数量", name, value.ToString()));
            }
        }

        if (state.Values.TryGetValue("$dynamicvalues", out var dynamicObj)
            && dynamicObj is IReadOnlyDictionary<string, object?> dynamicValues)
        {
            foreach (var (name, value) in dynamicValues.OrderBy(kv => kv.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                values.Add(new DynamicValueSnapshot("动态值", name, FormatSnapshotValue(value)));
            }
        }

        return values;
    }

    private static string FormatUnitSlot(GameState state, string? slot)
    {
        if (string.IsNullOrWhiteSpace(slot))
        {
            return "-";
        }

        if (state.Group.TryGetValue(slot, out var member)
            && member.TryGetValue("生命值", out var health))
        {
            return $"{slot} (生命值 {FormatSnapshotValue(health)})";
        }

        return slot;
    }

    private static string FormatSnapshotValue(object? value)
    {
        return value switch
        {
            null => "-",
            bool b => b ? "是" : "否",
            _ => value.ToString() ?? "-"
        };
    }

    private enum RuntimeCommandKind
    {
        SetEnabled,
        ToggleEnabled
    }

    private readonly record struct RuntimeCommand(RuntimeCommandKind Kind, bool Enabled)
    {
        public static RuntimeCommand SetEnabled(bool enabled)
            => new(RuntimeCommandKind.SetEnabled, enabled);

        public static RuntimeCommand ToggleEnabled()
            => new(RuntimeCommandKind.ToggleEnabled, false);
    }
}

internal static class RuntimeScanCadence
{
    private static readonly TimeSpan IdleMinimum = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FailureMinimum = TimeSpan.FromMilliseconds(500);

    public static TimeSpan Resolve(
        TimeSpan configuredInterval,
        bool enabled,
        bool scanUnavailable)
    {
        var minimum = scanUnavailable
            ? FailureMinimum
            : enabled
                ? configuredInterval
                : IdleMinimum;
        return configuredInterval >= minimum ? configuredInterval : minimum;
    }
}
