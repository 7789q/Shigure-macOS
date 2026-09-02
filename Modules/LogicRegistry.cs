namespace Shigure;

public interface IClassLogic
{
    LogicDecision Run(GameState state, string? specName);
}

public sealed class LogicRegistry : IRuntimeLogic, IActionSuppressionAwareRuntimeLogic
{
    private readonly Dictionary<int, IClassLogic> _logicByClass;
    private readonly IClassLogic _defaultLogic;
    private readonly IKeymapResolver _keymap;
    private readonly ModuleStore _moduleStore;
    private readonly string? _selectedModuleId;
    private readonly ModuleDerivedStateTracker _derivedStateTracker;

    public LogicRegistry(
        IKeymapResolver keymap,
        ModuleStore moduleStore,
        string? selectedModuleId,
        IEnumerable<KeyValuePair<int, IClassLogic>>? classLogics = null,
        TimeProvider? timeProvider = null)
    {
        _keymap = keymap;
        _moduleStore = moduleStore;
        _selectedModuleId = string.IsNullOrWhiteSpace(selectedModuleId) ? null : selectedModuleId.Trim();
        _derivedStateTracker = new ModuleDerivedStateTracker(timeProvider ?? TimeProvider.System);
        _defaultLogic = new DefaultClassLogic(keymap);
        _logicByClass = classLogics?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? new();
    }

    public LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic) => Evaluate(classId, specId, specName, state, runLogic, EmptySuppressedActions);

    public LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic,
        IReadOnlySet<LogicActionKey> suppressedActions)
    {
        _keymap.SelectForClass(classId, specId);
        var module = FindModule(classId, specId, state);
        if (module is not null)
        {
            ModuleLogic.ResolveDynamicFields(module, state);
            _derivedStateTracker.Apply(module, state, runLogic);
            return new LogicEvaluation(
                module.Name,
                runLogic ? ModuleLogic.Run(module, state, _keymap, suppressedActions) : null);
        }

        _derivedStateTracker.Reset();

        if (!runLogic)
        {
            return new LogicEvaluation(null, null);
        }

        if (classId is not null && _logicByClass.TryGetValue(classId.Value, out var logic))
        {
            return new LogicEvaluation(null, logic.Run(state, specName));
        }

        return new LogicEvaluation(null, _defaultLogic.Run(state, specName));
    }

    private static readonly IReadOnlySet<LogicActionKey> EmptySuppressedActions = new HashSet<LogicActionKey>();

    private ModuleDefinition? FindModule(int? classId, int? specId, GameState state)
    {
        return _moduleStore.FindSelectedOrBestMatch(
            _selectedModuleId,
            classId,
            specId,
            state.GetInt("队伍类型"),
            state.GetInt("英雄天赋"));
    }
}

public sealed class DefaultClassLogic : IClassLogic
{
    private readonly IKeymapResolver _keymap;

    public DefaultClassLogic(IKeymapResolver keymap)
    {
        _keymap = keymap;
    }

    public LogicDecision Run(GameState state, string? specName)
    {
        var oneKeyAssist = state.GetInt("一键辅助");
        if (oneKeyAssist == 10)
        {
            var hotkey = _keymap.GetHotkey(0, "一键辅助");
            if (!string.IsNullOrWhiteSpace(hotkey))
            {
                return new LogicDecision(hotkey, "施放 一键辅助", EmptyInfo);
            }
        }

        return new LogicDecision(null, "C# 职业逻辑尚未迁移", EmptyInfo);
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyInfo = new Dictionary<string, object?>();
}
