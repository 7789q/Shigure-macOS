namespace Shigure.Presentation;

public sealed class AoeWarningLogTracker
{
    private static readonly (string Field, string Label)[] DiagnosticCounters =
    [
        ("AOE桥接请求数", "桥接重发请求"),
        ("AOE桥接成功数", "桥接重发成功"),
        ("AOE带技能预警数", "带 Spell ID 预警"),
        ("AOE原始读条数", "原始敌方读条"),
        ("AOE技能受保护数", "Spell ID 受保护"),
        ("AOE敌对状态受保护数", "敌对状态受保护"),
        ("AOE受保护匹配数", "受保护读条匹配"),
        ("AOE敌方读条数", "候选敌方读条"),
        ("AOE读条未采纳数", "读条未采纳"),
        ("AOE读条匹配数", "读条匹配"),
        ("AOE读条未匹配数", "读条未匹配"),
        ("AOE读条成功数", "读条成功"),
        ("AOE读条失败数", "读条失败")
    ];

    private readonly Dictionary<string, int> _lastDiagnosticCounters = new(StringComparer.Ordinal);
    private int _lastType;
    private int _lastStage;
    private bool _hasActiveObservation;
    private bool _hasDiagnosticObservation;

    public IReadOnlyList<string> ObserveDiagnostics(GameState? state)
    {
        if (state is null)
        {
            return [];
        }

        var changes = new List<string>();
        foreach (var (field, label) in DiagnosticCounters)
        {
            var current = state.GetInt(field) & 0xFF;
            var previous = _lastDiagnosticCounters.GetValueOrDefault(field);
            var delta = _hasDiagnosticObservation ? (current - previous + 256) % 256 : 0;
            _lastDiagnosticCounters[field] = current;
            if (delta > 0)
            {
                changes.Add($"{label} +{delta}");
            }
        }

        _hasDiagnosticObservation = true;
        if (changes.Count == 0)
        {
            return [];
        }

        var details = new List<string>();
        var expectedSpellId = ReadSpellId(state, "AOE预警技能");
        var observedSpellId = ReadSpellId(state, "AOE读条技能");
        if (expectedSpellId > 0)
        {
            details.Add($"预警 Spell ID {expectedSpellId}");
        }
        if (observedSpellId > 0)
        {
            details.Add($"读条 Spell ID {observedSpellId}");
        }

        var suffix = details.Count == 0 ? string.Empty : $"；{string.Join("，", details)}";
        return [$"AOE诊断：{string.Join("，", changes)}{suffix}"];
    }

    public string? Observe(GameState? state)
    {
        if (state is null)
        {
            return null;
        }

        var eventType = state.GetInt("AOE事件类型");
        var stage = state.GetInt("AOE事件阶段");
        if (eventType == 0 || stage == 0)
        {
            if (!_hasActiveObservation)
            {
                return null;
            }

            _hasActiveObservation = false;
            var completion = CompletionLabel(_lastStage);
            _lastType = 0;
            _lastStage = 0;
            return $"AOE预警：已结束{completion}";
        }

        if (_hasActiveObservation && eventType == _lastType && stage == _lastStage)
        {
            return null;
        }

        _hasActiveObservation = true;
        _lastType = eventType;
        _lastStage = stage;
        var typeLabel = eventType switch
        {
            1 => "普通AOE",
            2 => "治疗吸收",
            _ => $"类型{eventType}"
        };
        var stageLabel = stage switch
        {
            1 => "资源预留",
            2 => "释放美德",
            3 => "等待生效",
            4 => "治疗缺口已出现",
            5 => "停止非紧急GCD",
            _ => $"阶段{stage}"
        };
        var holyPower = state.GetInt("神圣能量");
        var infusion = state.GetInt("auras.圣光灌注");
        var infusionStacks = state.GetInt("auras.圣光灌注层数");
        var visibleDeficits = state.GetInt("D10AtLeast");
        var totalDeficit = state.GetInt("DTotal");
        var burstHeld = state.GetInt("群疗爆发保持") > 0 ? "是" : "否";
        var divineTollExpectedReady = state.GetInt("圣洁鸣钟预计可用") > 0 ? "是" : "否";
        return
            $"AOE预警：{typeLabel} / {stageLabel}；圣能 {holyPower}，圣光灌注 {infusionStacks} 层 / {infusion} 秒；" +
            $"明显缺口 {visibleDeficits} 人，总负荷 {totalDeficit}，爆发保持 {burstHeld}，鸣钟预计可用 {divineTollExpectedReady}";
    }

    public void Reset()
    {
        _lastType = 0;
        _lastStage = 0;
        _hasActiveObservation = false;
        _hasDiagnosticObservation = false;
        _lastDiagnosticCounters.Clear();
    }

    public void ResetDiagnosticBaseline()
    {
        _hasDiagnosticObservation = false;
        _lastDiagnosticCounters.Clear();
    }

    private static int ReadSpellId(GameState state, string prefix) =>
        state.GetInt($"{prefix}低位")
        + state.GetInt($"{prefix}中位") * 256
        + state.GetInt($"{prefix}高位") * 65536;

    private static string CompletionLabel(int previousStage) => previousStage switch
    {
        1 => "；未进入执行窗口，可能为读条未匹配、受保护值或预警取消",
        2 or 5 => "；对应读条已结束或中断",
        3 => "；治疗吸收等待窗口结束",
        _ => string.Empty
    };
}
