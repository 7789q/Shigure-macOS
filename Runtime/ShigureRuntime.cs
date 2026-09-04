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
    private readonly EmergencyActionGuard _emergencyActionGuard = new();
    private readonly HealAbsorbStabilizer _healAbsorbStabilizer = new();
    private readonly CooldownConfirmationTracker _cooldownConfirmationTracker = new();
    private readonly ActionFailureBackoff _actionFailureBackoff = new();

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
    private readonly Dictionary<string, DateTimeOffset> _ruleRateLimitedUntil = new(StringComparer.Ordinal);
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
            _ruleRateLimitedUntil.Clear();
            _logicPausedUntil = DateTimeOffset.MinValue;
            _emergencyActionGuard.Reset();
            _cooldownConfirmationTracker.Reset();
            _actionFailureBackoff.Reset();
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
                var pulse = _triggerInput.ConsumePulse(trigger.Value);
                var edges = isPulseTrigger
                    ? TriggerInputEdgeTracker.ObservePulse(pulse)
                    : edgeTracker.ObserveState(_triggerInput.IsPressed(trigger.Value) || pulse);
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
                        _ruleRateLimitedUntil.Clear();
                        _logicPausedUntil = DateTimeOffset.MinValue;
                        _cooldownConfirmationTracker.Reset();
                        _actionFailureBackoff.Reset();
                        _currentStep = "按住结束";
                        PublishSnapshot();
                        lastRenderAt = now;
                    }
                }

                var scanInterval = RuntimeScanCadence.Resolve(
                    _options.LogicInterval,
                    _enabled,
                    _scanUnavailable,
                    _cooldownConfirmationTracker.HasPending);
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
                    _ruleRateLimitedUntil.Clear();
                    _logicPausedUntil = DateTimeOffset.MinValue;
                    _cooldownConfirmationTracker.Reset();
                    _actionFailureBackoff.Reset();
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
            _emergencyActionGuard.Reset();
            _healAbsorbStabilizer.Reset();
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

        var previousState = _state;
        var healAbsorb = _healAbsorbStabilizer.Observe(scan.HealAbsorbData);
        _state = _stateBuilder.Build(scan.RowData, scan.BarData, healAbsorb.Values);
        if (AoeAbsorbStageGuard.EnteredReserveStage(previousState, _state))
        {
            // A queued filler from before the reserve window must not delay
            // Virtue after the absorb cast's post-cast delay expires.
            _cooldownConfirmationTracker.ResetOrdinaryGcd();
        }
        _classId = _state.GetInt("职业");
        _specId = _state.GetInt("专精");
        (_className, _specName) = ClassNames.GetClassAndSpecName(_classId, _specId);
        var validityCode = _state.GetInt("有效性");
        if (validityCode != 1)
        {
            _healAbsorbStabilizer.Reset();
            _moduleName = null;
            _scanFailureReason = DescribeInvalidState(validityCode);
            _currentStep =
                $"等待游戏状态（状态字段 {scan.RowData.Count}，CountBars {scan.BarData.Count}，" +
                $"治疗吸收 {scan.HealAbsorbData.Count}，职业 {_classId?.ToString() ?? "-"}，" +
                $"专精 {_specId?.ToString() ?? "-"}，{_scanFailureReason}）";
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        if (_classId == 2 && _specId == 1 && !_state.GetBool("DiGua桥接就绪"))
        {
            _cooldownConfirmationTracker.Reset();
            _moduleName = null;
            _currentStep = "等待 DiGua 桥接就绪（请确认插件已加载、时间轴 API 可用，并在插件更新后执行 /reload）";
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        var macroBindingStatus = _state.GetInt("宏绑定状态");
        var macroBindingCount = _state.GetInt("宏绑定数量");
        var hasMacroBindingState = _state.Values.ContainsKey("宏绑定状态")
            && _state.Values.ContainsKey("宏绑定数量");
        var requiresMacroBindingState = (_classId == 2 || _classId == 6) && _specId == 1;
        if (requiresMacroBindingState
            && (!hasMacroBindingState || macroBindingStatus != 1 || macroBindingCount <= 0))
        {
            // /reload clears secure override bindings before the addon rebuilds them.
            // Never let a valid screen state trigger input during that gap.
            _ruleRateLimitedUntil.Clear();
            _logicPausedUntil = DateTimeOffset.MinValue;
            _emergencyActionGuard.Reset();
            _cooldownConfirmationTracker.Reset();
            _actionFailureBackoff.Reset();
            _moduleName = null;
            _unitInfo = new Dictionary<string, object?>();
            _currentStep = !hasMacroBindingState
                ? $"等待 WoW 宏绑定诊断字段（字段缺失，状态 {macroBindingStatus}，数量 {macroBindingCount}）"
                : macroBindingStatus == 2
                    ? $"等待 WoW 宏绑定脱离战斗后重建（状态 {macroBindingStatus}，数量 {macroBindingCount}）"
                    : $"等待 WoW 宏绑定就绪（状态 {macroBindingStatus}，数量 {macroBindingCount}）";
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var confirmationUpdates = _cooldownConfirmationTracker.Observe(_state, now);
        PublishCooldownConfirmationUpdates(confirmationUpdates, now);
        if (healAbsorb.HasPendingPositive && !HasUrgentHealingNeed(_state))
        {
            _currentStep = "等待治疗吸收连续帧确认";
            _unitInfo = new Dictionary<string, object?>();
            return;
        }

        var suppressedActions = _actionFailureBackoff.GetSuppressed(now);
        var rateLimitedRuleKeys = GetRateLimitedRuleKeys(now);
        var evaluation = _logic is IRateLimitAwareRuntimeLogic rateLimitAware
            ? rateLimitAware.Evaluate(
                _classId,
                _specId,
                _specName,
                _state,
                _enabled,
                suppressedActions,
                rateLimitedRuleKeys)
            : _logic is IActionSuppressionAwareRuntimeLogic suppressionAware
                ? suppressionAware.Evaluate(_classId, _specId, _specName, _state, _enabled, suppressedActions)
                : _logic.Evaluate(_classId, _specId, _specName, _state, _enabled);
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

        var emergencyCheck = _emergencyActionGuard.Observe(decision, _state);
        if (!emergencyCheck.Allowed)
        {
            var guardedInfo = _unitInfo.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);
            guardedInfo["安全确认"] = emergencyCheck.Reason ?? "未通过";
            guardedInfo["确认帧"] = $"{emergencyCheck.ConsecutiveFrames}/2";
            _unitInfo = guardedInfo;
            _currentStep = $"{decision.Step}（已拦截）";
            return;
        }

        if (_clickPending)
        {
            if (_clickPending
                && !string.IsNullOrWhiteSpace(decision.Hotkey))
            {
                TrySendDecision(decision, scan.Target?.Identity);
            }

            _enabled = false;
            _clickPending = false;
            return;
        }

        if (!string.IsNullOrWhiteSpace(decision.Hotkey))
        {
            TrySendDecision(decision, scan.Target?.Identity);
        }
    }

    private void TrySendDecision(LogicDecision decision, TargetIdentity? targetIdentity)
    {
        var sendAttemptAt = _timeProvider.GetUtcNow();
        if (ShouldSuppressStaleHealing(decision))
        {
            var guardedInfo = _unitInfo.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);
            guardedInfo["发送拦截"] = "目标当前已满血且无治疗吸收，跳过过期治疗快照";
            guardedInfo["发送拦截原因"] = "重新读取的目标状态与决策快照不一致";
            _unitInfo = guardedInfo;
            _currentStep = $"跳过过期治疗决策：{decision.CooldownConfirmationSpell ?? "动作"}";
            return;
        }

        if (!IsDispatchTargetValid(decision))
        {
            var guardedInfo = _unitInfo.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);
            guardedInfo["发送拦截"] = "当前目标不满足审判施放条件";
            guardedInfo["发送拦截原因"] = "目标类型或距离在发送前重新校验失败";
            _unitInfo = guardedInfo;
            _currentStep = "跳过无效目标的审判";
            return;
        }

        var isAoeVirtueExecution = IsAoeVirtueExecution(decision);
        if (AoeAbsorbStageGuard.ShouldBlock(_state, decision))
        {
            var guardedInfo = _unitInfo.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);
            guardedInfo["发送拦截"] = "治疗吸收延时窗口禁止普通 GCD";
            guardedInfo["发送拦截原因"] = "阶段 5 只保留紧急治疗、驱散和友方 NPC 治疗";
            _unitInfo = guardedInfo;
            _currentStep = $"阶段 5 保留美德窗口：{decision.CooldownConfirmationSpell ?? "动作"}";
            return;
        }
        if (_state is not null
            && _state.GetInt("施法技能") > 0
            && !decision.AllowCastPreemption)
        {
            var guardedInfo = _unitInfo.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);
            guardedInfo["当前施法技能"] = _state.GetInt("施法技能");
            guardedInfo["动作技能"] = decision.UnitInfo.TryGetValue("动作技能", out var actionSpell)
                ? actionSpell
                : decision.CooldownConfirmationSpell ?? "-";
            guardedInfo["动作单位槽位"] = ReadInt(decision.UnitInfo, "动作单位槽位");
            guardedInfo["动作意图"] = decision.Intent.ToString();
            guardedInfo["允许抢占读条"] = "否";
            guardedInfo["过滤原因"] = "活跃读条保护且动作未获抢占授权";
            guardedInfo["重试时机"] = "下一次约 300 ms 请求周期或当前读条结束";
            _unitInfo = guardedInfo;
            _currentStep = $"等待当前施法完成：{decision.CooldownConfirmationSpell ?? "动作"}";
            return;
        }
        var hadPendingConfirmation = _cooldownConfirmationTracker.HasPending;
        var allowsPreemption = decision.IsEmergency
            || isAoeVirtueExecution
            || decision.IsHealing;
        var queueWindowCentiseconds = IsBloodDeathKnight()
            ? CooldownConfirmationTracker.BloodDeathKnightQueueWindowCentiseconds
            : CooldownConfirmationTracker.QueueWindowCentiseconds;
        var postConfirmationHold = IsBloodDeathKnight()
            ? CooldownConfirmationTracker.BloodDeathKnightPostConfirmationHold
            : CooldownConfirmationTracker.PostConfirmationHold;
        if (!_cooldownConfirmationTracker.CanAttempt(
                decision,
                _state,
                sendAttemptAt,
                allowsPreemption,
                out var pendingSpell,
                queueWindowCentiseconds,
                postConfirmationHold))
        {
            var info = _unitInfo.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);
            info["等待技能"] = pendingSpell ?? "状态回写";
            var gcdBlocked = _cooldownConfirmationTracker.IsGlobalCooldownBlocked(
                decision,
                _state,
                sendAttemptAt,
                queueWindowCentiseconds);
            info["重试时机"] = gcdBlocked
                ? $"公共冷却剩余不高于 {queueWindowCentiseconds * 10} ms，并通过本地 GCD 保护"
                : $"下一次动作请求周期约 {CooldownConfirmationTracker.RetryCadence.TotalMilliseconds:F0} ms，或等待状态确认";
            _unitInfo = info;
            _currentStep = gcdBlocked
                ? $"等待 GCD 队列窗口：{pendingSpell ?? decision.CooldownConfirmationSpell ?? "动作"}"
                : hadPendingConfirmation
                ? $"等待技能确认：{pendingSpell ?? "状态回写"}"
                : $"等待动作请求周期：{pendingSpell ?? decision.CooldownConfirmationSpell ?? "动作"}";
            if (isAoeVirtueExecution)
            {
                info["AOE执行窗口"] = "治疗吸收阶段 3";
                info["AOE窗口保护"] = "美德未确认前保留阶段 3，等待下一次可发送检查";
                _currentStep += "（治疗吸收阶段 3，保留美德窗口）";
                _unitInfo = info;
            }
            return;
        }

        if (CanSend(decision, sendAttemptAt))
        {
            SendAndPauseLogic(decision, targetIdentity, queueWindowCentiseconds);
        }
    }

    private bool IsBloodDeathKnight() => _classId == 6 && _specId == 1;

    private bool ShouldSuppressStaleHealing(LogicDecision decision)
    {
        if (_state is null
            || !decision.UnitInfo.TryGetValue("动作技能", out var actionSpellValue)
            || actionSpellValue is null
            || !decision.UnitInfo.TryGetValue("动作单位槽位", out var unitValue))
        {
            return false;
        }

        var spell = actionSpellValue.ToString();
        if (spell is "暂停" or "审判" or "正义盾击" or "神圣震击" or "清洁术" or "美德道标")
        {
            return false;
        }

        // Out-of-combat Infusion conversion deliberately casts on a full-health
        // player. The live rule uses Flash of Light; keep the legacy Holy Light
        // rule number as a compatibility fallback for older local modules.
        var isInfusionConversion = decision.UnitInfo.TryGetValue("命中条件", out var conditionValue)
            && conditionValue?.ToString()?.Contains("战斗时间 == 0", StringComparison.Ordinal) == true
            && conditionValue.ToString()?.Contains("auras.圣光灌注层数 > 0", StringComparison.Ordinal) == true;
        var isLegacyInfusionConversion = spell == "圣光术"
            && decision.UnitInfo.TryGetValue("规则编号", out var ruleValue)
            && Convert.ToInt32(ruleValue) == 35;
        if ((spell == "圣光闪现" || spell == "圣光术")
            && (isInfusionConversion || isLegacyInfusionConversion))
        {
            return false;
        }

        var unit = Convert.ToInt32(unitValue);
        if (unit <= 0 || !_state.Group.TryGetValue(unit.ToString(), out var member))
        {
            return false;
        }

        var health = member.TryGetValue("生命值", out var healthValue)
            ? Convert.ToInt32(healthValue)
            : 0;
        var absorb = member.TryGetValue("治疗吸收", out var absorbValue)
            ? Convert.ToInt32(absorbValue)
            : 0;
        return health >= 100 && absorb <= 0;
    }

    private bool IsDispatchTargetValid(LogicDecision decision)
    {
        if (!decision.UnitInfo.TryGetValue("动作技能", out var actionSpell)
            || !string.Equals(actionSpell?.ToString(), "审判", StringComparison.Ordinal))
        {
            return true;
        }

        if (!decision.UnitInfo.ContainsKey("目标类型")
            || !decision.UnitInfo.ContainsKey("目标距离"))
        {
            return true;
        }

        var targetType = ReadInt(decision.UnitInfo, "目标类型");
        var distance = ReadInt(decision.UnitInfo, "目标距离");
        return targetType != 0 && distance > 0 && distance <= 28;
    }

    private static int ReadInt(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is not null ? Convert.ToInt32(value) : 0;

    private bool IsAoeVirtueExecution(LogicDecision decision) =>
        _state is not null
        && decision.UnitInfo.TryGetValue("动作技能", out var actionSpell)
        && string.Equals(actionSpell?.ToString(), "美德道标", StringComparison.Ordinal)
        && _state.GetInt("AOE事件类型") == 2
        && _state.GetInt("AOE事件阶段") == 3;

    private void SendAndPauseLogic(
        LogicDecision decision,
        TargetIdentity? targetIdentity,
        int queueWindowCentiseconds = CooldownConfirmationTracker.QueueWindowCentiseconds)
    {
        var hotkeySequence = decision.ResolveHotkeySequence();
        var sendResult = _keySender.SendSequence(hotkeySequence, targetIdentity);
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
        sentInfo["发送结果说明"] = "按键投递成功不等于技能已施放，等待 WoW 施法事件";
        sentInfo["发送序列"] = string.Join(" > ", hotkeySequence);
        var confirmationSpell = decision.CooldownConfirmationSpell;
        if (string.IsNullOrWhiteSpace(confirmationSpell)
            && decision.UnitInfo.TryGetValue("动作技能", out var actionSpellValue))
        {
            confirmationSpell = actionSpellValue?.ToString();
        }
        if (!string.IsNullOrWhiteSpace(confirmationSpell))
        {
            sentInfo["技能确认"] = $"等待 {confirmationSpell} 状态回写";
            if (!string.IsNullOrWhiteSpace(decision.CooldownConfirmationStateField))
            {
                sentInfo["确认状态字段"] = decision.CooldownConfirmationStateField;
                sentInfo["确认初始值"] = decision.CooldownConfirmationInitialValue;
            }
            if (decision.PlayerActionCode.HasValue)
            {
                sentInfo["期待动作技能码"] = decision.PlayerActionCode.Value;
            }
        }
        _unitInfo = sentInfo;
        _cooldownConfirmationTracker.RecordSent(
            decision,
            sentAt,
            _state,
            allowTargetReplacement: true,
            queueWindowCentiseconds: queueWindowCentiseconds);
        RecordSent(decision, sentAt);
        if (decision.LogicDelayMs > 0)
        {
            _logicPausedUntil = sentAt.AddMilliseconds(decision.LogicDelayMs);
        }

        PublishSnapshot();
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
        return !_ruleRateLimitedUntil.TryGetValue(key, out var rateLimitedUntil)
            || now >= rateLimitedUntil;
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
        _ruleRateLimitedUntil[key] = now.AddMilliseconds(decision.DelayMs);
    }

    private IReadOnlySet<string> GetRateLimitedRuleKeys(DateTimeOffset now)
    {
        var expiredKeys = _ruleRateLimitedUntil
            .Where(entry => entry.Value <= now)
            .Select(entry => entry.Key)
            .ToArray();
        foreach (var key in expiredKeys)
        {
            _ruleRateLimitedUntil.Remove(key);
        }

        return _ruleRateLimitedUntil.Keys.ToHashSet(StringComparer.Ordinal);
    }

    private void PublishCooldownConfirmationUpdates(
        IReadOnlyList<CooldownConfirmationUpdate> updates,
        DateTimeOffset now)
    {
        foreach (var update in updates)
        {
            var backedOff = _actionFailureBackoff.Observe(update, now);
            var ambiguousTarget = !update.Confirmed && update.Actions.Count > 1;
            var observedActionStatus = update.ObservedActionStatus;
            _currentStep = update.Confirmed
                ? $"技能确认：{update.Spell} 已释放"
                : backedOff
                    ? $"技能确认：{update.Spell} 连续未生效，临时让出优先级"
                    : ambiguousTarget
                        ? $"技能确认：{update.Spell} 状态未变化，目标归因不确定，允许重试"
                        : $"技能确认：{update.Spell} 状态未变化，允许重试";
            var info = new Dictionary<string, object?>
            {
                ["技能确认"] = update.Confirmed ? "释放成功" : "确认超时",
                ["确认耗时"] = $"{Math.Max(0, (long)(now - update.SentAt).TotalMilliseconds)} ms",
                ["技能冷却"] = update.Cooldown,
                ["动作目标"] = string.Join(",", update.Actions.Select(action => action.Unit).Order()),
                ["玩家动作序号"] = update.ObservedActionSerial,
                ["玩家动作技能"] = update.ObservedActionCode,
                ["玩家动作状态"] = observedActionStatus,
                ["玩家动作状态说明"] = observedActionStatus switch
                {
                    1 => "开始",
                    2 => "成功",
                    3 => "中断",
                    4 => "失败",
                    _ => "无"
                },
                ["期待动作技能码"] = update.ExpectedPlayerActionCode ?? 0,
                ["公共冷却剩余"] = update.CooldownRemaining,
                ["确认状态字段"] = update.StateField ?? "-",
                ["确认初始值"] = update.InitialValue ?? 0,
                ["确认当前值"] = update.ObservedValue ?? 0
            };
            if (!string.IsNullOrWhiteSpace(update.ConfirmationSource))
            {
                info["确认来源"] = update.ConfirmationSource;
            }
            else if (update.UsedGenericPlayerAction)
            {
                info["确认来源"] = "受保护的玩家施法事件";
            }
            else if (update.UsedDelayedActionAcknowledgement)
            {
                info["确认来源"] = "共享资源已变化（动作回写滞后）";
            }
            if (ambiguousTarget)
            {
                info["失败归因"] = "同一确认窗口尝试了多个目标，不对单个目标累计失败";
            }
            else if (!update.Confirmed && observedActionStatus == 4)
            {
                info["失败诊断"] = InferObservableFailure(update);
            }
            if (string.Equals(update.Spell, "心灵冰冻", StringComparison.Ordinal)
                && _state is not null)
            {
                info["打断诊断"] = DescribeInterruptState(_state, update.Actions);
            }
            if (backedOff)
            {
                info["失败退让"] = $"{ActionFailureBackoff.BackoffDuration.TotalSeconds:F0} 秒内跳过同技能同目标";
            }
            if (!string.IsNullOrWhiteSpace(update.StateField))
            {
                info["确认状态字段"] = update.StateField;
                info["确认初始值"] = update.InitialValue;
                info["确认当前值"] = update.ObservedValue;
            }
            if ((string.Equals(update.Spell, "圣光闪现", StringComparison.Ordinal)
                 || string.Equals(update.Spell, "圣光术", StringComparison.Ordinal))
                && string.Equals(update.StateField, "auras.圣光灌注层数", StringComparison.Ordinal))
            {
                var conversionMessage = update.Confirmed
                    ? "灌注层数已下降，转换为圣能的施法已确认"
                    : "读条事件已收到，但灌注层数未下降，转换未确认";
                info["灌注转换确认"] = conversionMessage;
                _currentStep += $"（{conversionMessage}）";
            }
            _unitInfo = info;
            PublishSnapshot();
        }
    }

    private string InferObservableFailure(CooldownConfirmationUpdate update)
    {
        var state = _state;
        if (state is null)
        {
            return "WoW 已报告失败，但确认帧没有可用状态";
        }

        if (string.Equals(update.Spell, "心灵冰冻", StringComparison.Ordinal))
        {
            return DescribeInterruptState(state, update.Actions);
        }

        if (string.Equals(update.Spell, "正义盾击", StringComparison.Ordinal))
        {
            var targetType = state.GetInt("目标类型");
            var distance = state.GetInt("目标距离");
            var inFront = state.GetInt("目标正面");
            if (targetType <= 0 || targetType >= 100)
            {
                return $"当前目标不是可攻击目标（目标类型 {targetType}）；WoW 原始错误文本未提供";
            }

            if (distance <= 0 || distance > 5)
            {
                return $"当前目标距离不可用或超过 5 码（{distance}）；WoW 原始错误文本未提供";
            }

            if (inFront <= 0)
            {
                return "当前目标正面未确认（值为 0）；WoW 原始错误文本未提供";
            }

            if (inFront == 2)
            {
                return "当前场景无法由 API 预判目标正面（值为 2）；已交给 WoW 施法结果确认";
            }
        }

        var gcd = state.GetInt("公共冷却剩余");
        return gcd > 0
            ? $"WoW 已报告技能失败；当时公共冷却仍剩约 {gcd} cs，仅作为时序诊断信息"
            : "WoW 已报告技能失败，但未提供可区分的客户端错误文本";
    }

    internal static string DescribeInterruptState(
        GameState state,
        IReadOnlySet<LogicActionKey> actions)
    {
        var attemptedUnit = actions.Count == 1 ? actions.Single().Unit : ReservedUnit.None;
        var attemptedLabel = attemptedUnit switch
        {
            ReservedUnit.Target => "目标",
            ReservedUnit.Focus => "焦点",
            _ => actions.Count == 0 ? "未知单位" : "多个单位"
        };
        var targetInterruptible = state.GetInt("目标施法可打断");
        var targetRemaining = state.GetInt("目标施法(倒计时)");
        var focusInterruptible = state.GetInt("焦点施法可打断");
        var focusRemaining = state.GetInt("焦点施法(倒计时)");

        if (attemptedUnit == ReservedUnit.Target)
        {
            var targetType = state.GetInt("目标类型");
            var targetHealth = state.GetInt("目标生命值");
            var targetDistance = state.GetInt("目标距离");
            if (targetType <= 0 || targetHealth <= 0)
            {
                return $"打断目标={attemptedLabel}；目标已消失或死亡（类型={targetType}，生命={targetHealth}），当前读条={targetRemaining} cs，可打断={targetInterruptible}";
            }

            if (targetInterruptible <= 0 || targetRemaining <= 0)
            {
                return $"打断目标={attemptedLabel}；目标当前无可打断读条（剩余={targetRemaining} cs，可打断={targetInterruptible}），可能读条已结束或目标已切换";
            }

            if (targetRemaining > 5)
            {
                return $"打断目标={attemptedLabel}；目标仍在读条但已离开最后 0.5 秒窗口（剩余={targetRemaining} cs，可打断={targetInterruptible}），可能存在发送延迟或目标切换";
            }

            if (targetDistance <= 0 || targetDistance > 5)
            {
                return $"打断目标={attemptedLabel}；目标仍在最后 0.5 秒且可打断，但距离={targetDistance} 码，可能超出心灵冰冻距离";
            }

            return $"打断目标={attemptedLabel}；目标仍在最后 0.5 秒且可打断（剩余={targetRemaining} cs，距离={targetDistance} 码），WoW 返回失败，可能是免疫或时序竞争";
        }

        if (attemptedUnit == ReservedUnit.Focus)
        {
            if (focusInterruptible <= 0 || focusRemaining <= 0)
            {
                return $"打断目标={attemptedLabel}；焦点当前无可打断读条（剩余={focusRemaining} cs，可打断={focusInterruptible}），可能读条已结束或焦点已切换";
            }

            if (focusRemaining > 5)
            {
                return $"打断目标={attemptedLabel}；焦点仍在读条但已离开最后 0.5 秒窗口（剩余={focusRemaining} cs，可打断={focusInterruptible}），可能存在发送延迟或焦点切换";
            }

            return $"打断目标={attemptedLabel}；焦点仍在最后 0.5 秒且可打断（剩余={focusRemaining} cs），WoW 返回失败，可能是免疫或时序竞争";
        }

        return $"打断目标={attemptedLabel}；目标读条={targetRemaining} cs/可打断={targetInterruptible}，焦点读条={focusRemaining} cs/可打断={focusInterruptible}";
    }

    private static bool HasUrgentHealingNeed(GameState state)
    {
        if (state.GetInt("生命值") is > 0 and <= 45)
        {
            return true;
        }

        var injuredCount = 0;
        var totalHealthDeficit = 0;
        foreach (var member in state.Group.Values)
        {
            var health = member.TryGetValue("生命值", out var value) ? Convert.ToInt32(value) : 0;
            if (health <= 0)
            {
                continue;
            }

            if (health < 85)
            {
                injuredCount++;
            }

            totalHealthDeficit += Math.Max(0, 100 - health);
        }

        return injuredCount >= 2 && totalHealthDeficit >= 30;
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

    private static string DescribeInvalidState(int code) => code switch
    {
        2 => "游戏状态暂停：角色已死亡",
        3 => "游戏状态暂停：角色处于坐骑状态",
        4 => "游戏状态暂停：聊天输入框已打开",
        5 => "游戏状态暂停：角色正在饮水",
        6 => "游戏状态暂停：角色正在施放坐骑",
        _ => "色块状态尚未就绪：请等待游戏界面加载"
    };

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

internal sealed class CooldownConfirmationTracker
{
    internal static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(1);
    internal static readonly TimeSpan RetryCadence = TimeSpan.FromMilliseconds(300);
    internal static readonly TimeSpan PostConfirmationHold = TimeSpan.FromMilliseconds(1500);
    internal static readonly TimeSpan BloodDeathKnightPostConfirmationHold = TimeSpan.Zero;
    internal const int QueueWindowCentiseconds = 5;
    // Blood DK actions should enter the WoW queue with enough transport
    // margin to avoid landing on the final few centiseconds of the GCD.
    internal const int BloodDeathKnightQueueWindowCentiseconds = 20;
    private static readonly TimeSpan DefaultGlobalCooldown = TimeSpan.FromMilliseconds(1500);
    private static readonly HashSet<string> HealingSpells = new(StringComparer.Ordinal)
    {
        "圣疗术",
        "美德道标",
        "荣耀圣令",
        "圣洁鸣钟",
        "光环掌握",
        "圣光闪现",
        "圣光术",
        "黎明之光",
        "神圣震击",
        "圣盾术",
        "治疗石",
        "治疗药水",
        "银月城生命药水"
    };
    private static readonly HashSet<string> OffGlobalCooldownSpells = new(StringComparer.Ordinal)
    {
        "圣疗术",
        "牺牲祝福",
        "光环掌握",
        "亡者复生",
        "巫妖之躯",
        "心灵冰冻"
    };
    private readonly Dictionary<string, PendingCooldownConfirmation> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _recentlyConfirmed = new(StringComparer.Ordinal);
    private DateTimeOffset _globalCooldownBlockedUntil = DateTimeOffset.MinValue;

    public bool HasPending => _pending.Count > 0;

    public static bool IsOffGlobalCooldownSpell(string? spell) =>
        !string.IsNullOrWhiteSpace(spell) && OffGlobalCooldownSpells.Contains(spell);

    public static bool IsHealingSpell(string? spell) =>
        !string.IsNullOrWhiteSpace(spell) && HealingSpells.Contains(spell);

    public bool IsGlobalCooldownBlocked(
        LogicDecision decision,
        GameState? state,
        DateTimeOffset now,
        int queueWindowCentiseconds = QueueWindowCentiseconds)
    {
        var decisionSpell = ResolveConfirmationSpell(decision);
        if (decisionSpell is null || IsOffGlobalCooldownSpell(decisionSpell))
        {
            return false;
        }

        var observedGcdRemaining = state?.GetInt("公共冷却剩余") ?? 0;
        if (observedGcdRemaining > queueWindowCentiseconds)
        {
            return true;
        }

        return _globalCooldownBlockedUntil > now;
    }

    public bool CanAttempt(
        LogicDecision decision,
        GameState? state,
        DateTimeOffset now,
        bool allowPreemption,
        out string? pendingSpell,
        int queueWindowCentiseconds = QueueWindowCentiseconds,
        TimeSpan? postConfirmationHold = null)
    {
        pendingSpell = _pending.Keys.Order(StringComparer.Ordinal).FirstOrDefault();
        var decisionSpell = ResolveConfirmationSpell(decision);
        if (IsGlobalCooldownBlocked(decision, state, now, queueWindowCentiseconds))
        {
            pendingSpell = decisionSpell ?? pendingSpell;
            return false;
        }

        if (decisionSpell is not null
            && _recentlyConfirmed.TryGetValue(decisionSpell, out var confirmedAt)
            && now - confirmedAt < (postConfirmationHold ?? PostConfirmationHold))
        {
            pendingSpell = decisionSpell;
            return false;
        }

        if (_pending.Count == 0)
        {
            pendingSpell = decisionSpell;
            return true;
        }

        var blocking = _pending.Values.MinBy(item => item.Urgency)
            ?? throw new InvalidOperationException("待确认技能集合不能为空。");

        if (decisionSpell is not null
            && _pending.TryGetValue(decisionSpell, out var pending))
        {
            pendingSpell = decisionSpell;
            var urgency = ResolveUrgency(decision);
            var action = ResolveAction(decision);
            if (action == pending.LastAction)
            {
                pending.Urgency = Math.Min(pending.Urgency, urgency);
                return now - pending.LastAttemptAt >= RetryCadence;
            }

            // Keep target attribution unambiguous. A changed target is allowed
            // only after the previous target is no longer a valid healing
            // target; the caller then replaces the entire pending generation.
            return IsPendingTargetInvalid(pending.LastAction, decision, state);
        }

        pendingSpell = blocking.Spell;
        if (OffGlobalCooldownSpells.Contains(decisionSpell ?? string.Empty))
        {
            return true;
        }

        var candidateUrgency = ResolveUrgency(decision);
        if (candidateUrgency >= blocking.Urgency)
        {
            return false;
        }
        return allowPreemption || IsHealingSpell(decisionSpell);
    }

    internal bool CanAttempt(
        LogicDecision decision,
        DateTimeOffset now,
        bool allowPreemption,
        out string? pendingSpell,
        int queueWindowCentiseconds = QueueWindowCentiseconds,
        TimeSpan? postConfirmationHold = null)
    {
        var decisionSpell = ResolveConfirmationSpell(decision);
        if (decisionSpell is not null
            && _pending.TryGetValue(decisionSpell, out var pending)
            && ResolveUrgency(decision) >= pending.Urgency
            && now - pending.LastAttemptAt < RetryCadence)
        {
            pendingSpell = pending.Spell;
            return false;
        }

        return CanAttempt(
            decision,
            new GameState(new Dictionary<string, object?> { ["公共冷却剩余"] = 0 }),
            now,
            allowPreemption,
            out pendingSpell,
            queueWindowCentiseconds,
            postConfirmationHold);
    }

    public void RecordSent(
        LogicDecision decision,
        DateTimeOffset sentAt,
        GameState? state,
        bool allowTargetReplacement = false,
        int queueWindowCentiseconds = QueueWindowCentiseconds)
    {
        var decisionSpell = ResolveConfirmationSpell(decision);
        if (decisionSpell is not null && !IsOffGlobalCooldownSpell(decisionSpell))
        {
            RecordGlobalCooldownSent(sentAt, state, queueWindowCentiseconds);
        }

        if (decisionSpell is null
            || (decision.CooldownConfirmationSpell is null && decision.PlayerActionCode is null))
        {
            return;
        }

        var urgency = ResolveUrgency(decision);
        var actionSerial = state?.GetInt("玩家动作序号") ?? 0;
        var initialCooldown = state?.GetInt($"spells.{decisionSpell}") ?? 0;
        var action = ResolveAction(decision);
        if (_pending.TryGetValue(decisionSpell, out var pending)
            && pending.LastAction == action)
        {
            pending.LastAttemptAt = sentAt;
            pending.InitialActionSerial = actionSerial;
            pending.Urgency = Math.Min(pending.Urgency, urgency);
            return;
        }

        if (_pending.ContainsKey(decisionSpell) && !allowTargetReplacement)
        {
            return;
        }

        // Replace the old generation so late events cannot be attributed to
        // both actions after an explicit preemption or target failover. The
        // caller has already checked that a same-spell target change is valid.
        _pending.Clear();
        _pending.Add(decisionSpell, new PendingCooldownConfirmation(
            decisionSpell,
            sentAt,
            sentAt,
            decision.CooldownConfirmationStateField,
            decision.CooldownConfirmationInitialValue,
            decision.ConfirmationStateChange,
            decision.PlayerActionCode,
            decision.AllowResourceOnlyConfirmation,
            actionSerial,
            initialCooldown,
            urgency,
            action,
            new HashSet<LogicActionKey> { action }));
    }

    internal void RecordSent(LogicDecision decision, DateTimeOffset sentAt)
    {
        RecordSent(decision, sentAt, null);
    }

    public IReadOnlyList<CooldownConfirmationUpdate> Observe(GameState state, DateTimeOffset now)
    {
        var updates = new List<CooldownConfirmationUpdate>();
        foreach (var (spell, pending) in _pending.ToArray())
        {
            var cooldown = state.GetInt($"spells.{spell}");
            var actionSerial = state.GetInt("玩家动作序号");
            var actionCode = state.GetInt("玩家动作技能");
            var actionStatus = state.GetInt("玩家动作状态");
            var gcdRemaining = state.GetInt("公共冷却剩余");
            var matchingActionObserved = pending.PlayerActionCode.HasValue
                && actionSerial != pending.InitialActionSerial
                && actionCode == pending.PlayerActionCode.Value;
            var actionFailed = matchingActionObserved && actionStatus is 3 or 4;
            // Some off-GCD class actions are not present in the one-key spell
            // map, so WoW reports them with action code 0. Within the pending
            // confirmation window, a new unattributed failure for such an
            // action must still override a cooldown change; otherwise a
            // rejected cast is falsely confirmed as successful.
            var unattributedOffGcdFailure = pending.PlayerActionCode is null
                && IsOffGlobalCooldownSpell(spell)
                && actionSerial != pending.InitialActionSerial
                && actionCode == 0
                && actionStatus is 3 or 4;
            var definitiveActionFailure = actionFailed || unattributedOffGcdFailure;
            var observedValue = string.IsNullOrWhiteSpace(pending.StateField)
                ? (int?)null
                : state.GetInt(pending.StateField);
            var stateChanged = pending.InitialValue.HasValue
                && observedValue.HasValue
                && (pending.StateChange == ConfirmationStateChangeKind.Cleared
                    ? observedValue.Value == 0
                    : observedValue.Value < pending.InitialValue.Value);
            // A resource update can arrive before the addon publishes the matching
            // action event. If the action serial is still exactly the send snapshot
            // and the state moved in the expected direction, accept it as a delayed
            // action acknowledgement instead of retrying the successful cast.
            var delayedActionAcknowledgement = stateChanged
                && pending.PlayerActionCode.HasValue
                && actionSerial == pending.InitialActionSerial
                && actionCode != pending.PlayerActionCode.Value
                && actionStatus is 1 or 2;
            var stateChangeAccepted = stateChanged
                && (pending.AllowResourceOnlyConfirmation
                    || pending.PlayerActionCode.HasValue
                        && (matchingActionObserved || delayedActionAcknowledgement))
                && (pending.StateField != "auras.圣光灌注层数" || actionStatus == 2);
            var actionAccepted = matchingActionObserved
                && actionStatus is 1 or 2
                && (pending.StateField != "auras.圣光灌注层数"
                    || stateChanged && actionStatus == 2);
            var cooldownAdvanced = cooldown > 0
                && cooldown > pending.InitialCooldown;
            var confirmationSource = stateChangeAccepted
                ? $"状态字段变化：{pending.StateField}"
                : cooldownAdvanced
                    ? $"技能冷却/充能变化：spells.{spell}"
                    : actionAccepted
                        ? "玩家施法成功事件"
                        : null;
            if (confirmationSource is not null && delayedActionAcknowledgement)
            {
                confirmationSource += "（动作事件回写滞后）";
            }
            if (!definitiveActionFailure
                && (actionAccepted || cooldownAdvanced || stateChangeAccepted))
            {
                _pending.Remove(spell);
                _recentlyConfirmed[spell] = now;
                updates.Add(new CooldownConfirmationUpdate(
                    spell,
                    true,
                    cooldown,
                    pending.StateField,
                    pending.InitialValue,
                    observedValue,
                    pending.SentAt,
                    pending.Actions,
                    false,
                    pending.PlayerActionCode,
                    delayedActionAcknowledgement,
                    state.GetInt("玩家动作序号"),
                    state.GetInt("玩家动作技能"),
                    state.GetInt("玩家动作状态"),
                    state.GetInt("公共冷却剩余"),
                    actionFailed,
                    confirmationSource));
            }
            else if (definitiveActionFailure
                     || now - pending.SentAt >= ConfirmationTimeout(pending.Spell))
            {
                _pending.Remove(spell);
                updates.Add(new CooldownConfirmationUpdate(
                    spell,
                    false,
                    cooldown,
                    pending.StateField,
                    pending.InitialValue,
                    observedValue,
                    pending.SentAt,
                    pending.Actions,
                    false,
                    pending.PlayerActionCode,
                    false,
                    actionSerial,
                    actionCode,
                    actionStatus,
                    gcdRemaining,
                    definitiveActionFailure));
            }
        }

        return updates;
    }

    public void Reset()
    {
        _pending.Clear();
        _recentlyConfirmed.Clear();
        _globalCooldownBlockedUntil = DateTimeOffset.MinValue;
    }

    public void ResetOrdinaryGcd()
    {
        foreach (var (spell, _) in _pending.ToArray())
        {
            if (!OffGlobalCooldownSpells.Contains(spell))
            {
                _pending.Remove(spell);
            }
        }
    }

    private static TimeSpan ConfirmationTimeout(string spell)
    {
        if (spell is "圣光闪现" or "圣光术")
        {
            return TimeSpan.FromSeconds(3);
        }

        return RetryAfter;
    }

    private void RecordGlobalCooldownSent(
        DateTimeOffset sentAt,
        GameState? state,
        int queueWindowCentiseconds)
    {
        if (state is null
            || !state.Values.ContainsKey("公共冷却时长"))
        {
            return;
        }

        var gcdCentiseconds = state.GetInt("公共冷却时长");
        var gcdDuration = gcdCentiseconds > 0
            ? TimeSpan.FromMilliseconds(gcdCentiseconds * 10d)
            : DefaultGlobalCooldown;
        var queueWindow = TimeSpan.FromMilliseconds(queueWindowCentiseconds * 10d);
        _globalCooldownBlockedUntil = sentAt + gcdDuration - queueWindow;
        if (_globalCooldownBlockedUntil < sentAt)
        {
            _globalCooldownBlockedUntil = sentAt;
        }
    }

    private static int ReadInt(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) ? Convert.ToInt32(value) : 0;

    private static bool IsPendingTargetInvalid(
        LogicActionKey pendingAction,
        LogicDecision replacement,
        GameState? state)
    {
        if (state is null
            || !IsHealingSpell(pendingAction.Spell))
        {
            return false;
        }

        if (pendingAction.Unit == ReservedUnit.Target)
        {
            var targetType = state.GetInt("目标类型");
            var targetHealth = state.GetInt("目标生命值");
            return targetType != 152 || targetHealth <= 0 || targetHealth >= 100;
        }

        if (pendingAction.Unit <= 0
            || !state.Group.TryGetValue(pendingAction.Unit.ToString(), out var member))
        {
            return false;
        }

        var health = member.TryGetValue("生命值", out var healthValue)
            ? Convert.ToInt32(healthValue)
            : 0;
        var absorb = member.TryGetValue("治疗吸收", out var absorbValue)
            ? Convert.ToInt32(absorbValue)
            : 0;
        return health <= 0 || (health >= 100 && absorb <= 0);
    }

    private static LogicActionKey ResolveAction(LogicDecision decision) => new(
        ResolveConfirmationSpell(decision) ?? string.Empty,
        ReadInt(decision.UnitInfo, "动作单位槽位"));

    private static string? ResolveConfirmationSpell(LogicDecision decision)
    {
        if (!string.IsNullOrWhiteSpace(decision.CooldownConfirmationSpell))
        {
            return decision.CooldownConfirmationSpell;
        }

        return decision.UnitInfo.TryGetValue("动作技能", out var actionSpell)
            && !string.IsNullOrWhiteSpace(actionSpell?.ToString())
            ? actionSpell.ToString()
            : null;
    }

    private static int ResolveUrgency(LogicDecision decision)
    {
        if (decision.IsEmergency || EmergencyActionGuard.IsEmergency(decision))
        {
            return 0;
        }

        var spell = decision.UnitInfo.TryGetValue("动作技能", out var actionSpell)
            ? actionSpell?.ToString()
            : decision.CooldownConfirmationSpell;
        var unit = ReadInt(decision.UnitInfo, "动作单位槽位");
        var rule = ReadInt(decision.UnitInfo, "规则编号");
        if (string.Equals(spell, "清洁术", StringComparison.Ordinal))
        {
            return 50 + rule;
        }

        var isHealing = decision.IsHealing
            || !string.IsNullOrWhiteSpace(spell)
                && (HealingSpells.Contains(spell)
                    || string.Equals(spell, "神圣震击", StringComparison.Ordinal) && unit > 0);
        return (isHealing ? 100 : 1000) + rule;
    }

    private sealed class PendingCooldownConfirmation(
        string spell,
        DateTimeOffset sentAt,
        DateTimeOffset lastAttemptAt,
        string? stateField,
        int? initialValue,
        ConfirmationStateChangeKind stateChange,
        int? playerActionCode,
        bool allowResourceOnlyConfirmation,
        int initialActionSerial,
        int initialCooldown,
        int urgency,
        LogicActionKey lastAction,
        HashSet<LogicActionKey> actions)
    {
        public string Spell { get; } = spell;
        public DateTimeOffset SentAt { get; } = sentAt;
        public DateTimeOffset LastAttemptAt { get; set; } = lastAttemptAt;
        public string? StateField { get; } = stateField;
        public int? InitialValue { get; } = initialValue;
        public ConfirmationStateChangeKind StateChange { get; } = stateChange;
        public int? PlayerActionCode { get; } = playerActionCode;
        public bool AllowResourceOnlyConfirmation { get; } = allowResourceOnlyConfirmation;
        public int InitialActionSerial { get; set; } = initialActionSerial;
        public int InitialCooldown { get; } = initialCooldown;
        public int Urgency { get; set; } = urgency;
        public LogicActionKey LastAction { get; set; } = lastAction;
        public HashSet<LogicActionKey> Actions { get; } = actions;
    }
}

internal sealed record CooldownConfirmationUpdate(
    string Spell,
    bool Confirmed,
    int Cooldown,
    string? StateField,
    int? InitialValue,
    int? ObservedValue,
    DateTimeOffset SentAt,
    IReadOnlySet<LogicActionKey> Actions,
    bool UsedGenericPlayerAction = false,
    int? ExpectedPlayerActionCode = null,
    bool UsedDelayedActionAcknowledgement = false,
    int ObservedActionSerial = 0,
    int ObservedActionCode = 0,
    int ObservedActionStatus = 0,
    int CooldownRemaining = 0,
    bool DefinitiveFailure = true,
    string? ConfirmationSource = null);

internal static class AoeAbsorbStageGuard
{
    public static bool EnteredReserveStage(GameState? previous, GameState current) =>
        current.GetInt("AOE事件类型") == 2
        && current.GetInt("AOE事件阶段") == 5
        && (previous is null
            || previous.GetInt("AOE事件类型") != 2
            || previous.GetInt("AOE事件阶段") != 5);

    public static bool ShouldBlock(GameState? state, LogicDecision decision)
    {
        if (state is null
            || state.GetInt("AOE事件类型") != 2
            || state.GetInt("AOE事件阶段") != 5)
        {
            return false;
        }

        if (!decision.UnitInfo.TryGetValue("动作技能", out var actionSpellValue))
        {
            return false;
        }

        var actionSpell = actionSpellValue?.ToString();
        if (string.IsNullOrWhiteSpace(actionSpell)
            || decision.IsEmergency
            || CooldownConfirmationTracker.IsOffGlobalCooldownSpell(actionSpell)
            || ReadInt(decision.UnitInfo, "目标驱散") > 0
            || (ReadInt(decision.UnitInfo, "目标类型") == 152
                && ReadInt(decision.UnitInfo, "目标生命值") < 100))
        {
            return false;
        }

        return true;
    }

    private static int ReadInt(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) && value is not null ? Convert.ToInt32(value) : 0;
}

internal sealed class ActionFailureBackoff
{
    internal const int FailureThreshold = 2;
    internal static readonly TimeSpan InitialRetryBackoff = TimeSpan.FromMilliseconds(350);
    internal static readonly TimeSpan BackoffDuration = TimeSpan.FromSeconds(5);
    private readonly Dictionary<LogicActionKey, FailureState> _failures = new();

    public bool Observe(CooldownConfirmationUpdate update, DateTimeOffset now)
    {
        if (update.Confirmed)
        {
            foreach (var confirmedAction in update.Actions)
            {
                _failures.Remove(confirmedAction);
            }
            return false;
        }

        if (update.Actions.Count != 1)
        {
            return false;
        }

        var failedAction = update.Actions.Single();
        var failureCount = _failures.TryGetValue(failedAction, out var previous)
            ? previous.FailureCount + 1
            : 1;
        if (failureCount < FailureThreshold)
        {
            _failures[failedAction] = new FailureState(failureCount, now.Add(InitialRetryBackoff));
            return false;
        }

        _failures[failedAction] = new FailureState(0, now.Add(BackoffDuration));
        return true;
    }

    public IReadOnlySet<LogicActionKey> GetSuppressed(DateTimeOffset now)
    {
        foreach (var (action, state) in _failures.ToArray())
        {
            if (state.SuppressedUntil != DateTimeOffset.MinValue && state.SuppressedUntil <= now)
            {
                _failures.Remove(action);
            }
        }

        return _failures
            .Where(entry => entry.Value.SuppressedUntil > now)
            .Select(entry => entry.Key)
            .ToHashSet();
    }

    public void Reset() => _failures.Clear();

    private sealed record FailureState(int FailureCount, DateTimeOffset SuppressedUntil);
}

internal sealed class EmergencyActionGuard
{
    private const string LayOnHands = "圣疗术";
    private const int CriticalHealthThreshold = 30;
    private const int RequiredConsecutiveFrames = 2;
    private string? _pendingKey;
    private int _consecutiveFrames;

    public static bool IsEmergency(LogicDecision decision) =>
        decision.UnitInfo.TryGetValue("动作技能", out var actionSpell)
        && string.Equals(actionSpell?.ToString(), LayOnHands, StringComparison.Ordinal);

    public EmergencyActionCheck Observe(LogicDecision decision, GameState state)
    {
        if (!IsEmergency(decision))
        {
            Reset();
            return EmergencyActionCheck.Allow;
        }

        var unit = ReadInt(decision.UnitInfo, "动作单位槽位");
        var playerHealth = state.GetInt("生命值");
        var targetHealth = unit switch
        {
            0 => playerHealth,
            > 0 when state.Group.TryGetValue(unit.ToString(), out var member)
                && member.TryGetValue("生命值", out var health) => ConvertToInt(health),
            _ => 0
        };

        if (targetHealth is <= 0 or > CriticalHealthThreshold)
        {
            Reset();
            return new EmergencyActionCheck(
                false,
                $"目标生命值 {targetHealth}% 不在 1–{CriticalHealthThreshold}%",
                0);
        }

        if (unit == 1 && playerHealth > CriticalHealthThreshold)
        {
            Reset();
            return new EmergencyActionCheck(
                false,
                $"单位 1 显示 {targetHealth}%，但独立自身生命值为 {playerHealth}%",
                0);
        }

        var key = $"{decision.RateLimitKey}|{unit}";
        if (string.Equals(_pendingKey, key, StringComparison.Ordinal))
        {
            _consecutiveFrames++;
        }
        else
        {
            _pendingKey = key;
            _consecutiveFrames = 1;
        }

        return _consecutiveFrames >= RequiredConsecutiveFrames
            ? new EmergencyActionCheck(true, null, _consecutiveFrames)
            : new EmergencyActionCheck(
                false,
                $"等待同一目标连续 {RequiredConsecutiveFrames} 帧确认",
                _consecutiveFrames);
    }

    public void Reset()
    {
        _pendingKey = null;
        _consecutiveFrames = 0;
    }

    private static int ReadInt(IReadOnlyDictionary<string, object?> values, string key) =>
        values.TryGetValue(key, out var value) ? ConvertToInt(value) : 0;

    private static int ConvertToInt(object? value) => value switch
    {
        int number => number,
        long number => (int)number,
        string text when int.TryParse(text, out var number) => number,
        _ => 0
    };
}

internal sealed class HealAbsorbStabilizer
{
    private const int RequiredPositiveFrames = 2;
    private readonly Dictionary<int, int> _positiveStreaks = new();

    public HealAbsorbStabilizationResult Observe(IReadOnlyDictionary<int, int> rawValues)
    {
        var values = new Dictionary<int, int>(rawValues.Count);
        var presentUnits = new HashSet<int>();
        var hasPendingPositive = false;

        foreach (var (unit, rawValue) in rawValues)
        {
            presentUnits.Add(unit);
            var value = Math.Max(0, rawValue);
            if (value == 0)
            {
                _positiveStreaks.Remove(unit);
                values[unit] = 0;
                continue;
            }

            var streak = _positiveStreaks.GetValueOrDefault(unit) + 1;
            _positiveStreaks[unit] = streak;
            if (streak < RequiredPositiveFrames)
            {
                values[unit] = 0;
                hasPendingPositive = true;
                continue;
            }

            values[unit] = value;
        }

        foreach (var unit in _positiveStreaks.Keys.Where(unit => !presentUnits.Contains(unit)).ToList())
        {
            _positiveStreaks.Remove(unit);
        }

        return new HealAbsorbStabilizationResult(values, hasPendingPositive);
    }

    public void Reset() => _positiveStreaks.Clear();
}

internal sealed record HealAbsorbStabilizationResult(
    IReadOnlyDictionary<int, int> Values,
    bool HasPendingPositive);

internal sealed record EmergencyActionCheck(bool Allowed, string? Reason, int ConsecutiveFrames)
{
    public static EmergencyActionCheck Allow { get; } = new(true, null, 0);
}

internal static class RuntimeScanCadence
{
    private static readonly TimeSpan IdleMinimum = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan FailureMinimum = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ConfirmationMaximum = TimeSpan.FromMilliseconds(50);

    public static TimeSpan Resolve(
        TimeSpan configuredInterval,
        bool enabled,
        bool scanUnavailable,
        bool hasPendingConfirmation = false)
    {
        if (enabled && !scanUnavailable && hasPendingConfirmation)
        {
            return configuredInterval <= ConfirmationMaximum
                ? configuredInterval
                : ConfirmationMaximum;
        }

        var minimum = scanUnavailable
            ? FailureMinimum
            : enabled
                ? configuredInterval
                : IdleMinimum;
        return configuredInterval >= minimum ? configuredInterval : minimum;
    }
}
