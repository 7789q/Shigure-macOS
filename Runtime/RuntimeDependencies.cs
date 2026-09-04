namespace Shigure;

public sealed record LogicDecision(
    string? Hotkey,
    string Step,
    IReadOnlyDictionary<string, object?> UnitInfo,
    string? ModuleName = null,
    int DelayMs = 0,
    string? RateLimitKey = null,
    int LogicDelayMs = 0,
    IReadOnlyList<string>? HotkeySequence = null,
    string? CooldownConfirmationSpell = null,
    string? CooldownConfirmationStateField = null,
    int? CooldownConfirmationInitialValue = null,
    ConfirmationStateChangeKind ConfirmationStateChange = ConfirmationStateChangeKind.Decreased,
    int? PlayerActionCode = null,
    LogicActionIntent Intent = LogicActionIntent.Unknown,
    bool AllowCastPreemption = false,
    bool AllowResourceOnlyConfirmation = false)
{
    public IReadOnlyList<string> ResolveHotkeySequence() =>
        HotkeySequence is { Count: > 0 }
            ? HotkeySequence
            : string.IsNullOrWhiteSpace(Hotkey) ? [] : [Hotkey];

    public bool IsEmergency => Intent is LogicActionIntent.EmergencySelfDefense
        or LogicActionIntent.EmergencyPartySupport;

    public bool IsHealing => Intent is LogicActionIntent.EmergencySelfDefense
        or LogicActionIntent.EmergencyPartySupport
        or LogicActionIntent.GroupHealing
        or LogicActionIntent.DirectHealing
        or LogicActionIntent.NpcHealing;
}

public enum LogicActionIntent
{
    Unknown,
    EmergencySelfDefense,
    EmergencyPartySupport,
    GroupHealing,
    DirectHealing,
    NpcHealing,
    Dispel,
    OffGlobalCooldown,
    Offensive,
    Pause
}

internal static class CastPreemptionPolicy
{
    internal static readonly IReadOnlySet<string> AllowedSpells =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "圣盾术",
            "治疗石",
            "治疗药水",
            "银月城生命药水",
            "美德道标",
            "心灵冰冻"
        };

    internal static bool Allows(string? spell) =>
        !string.IsNullOrWhiteSpace(spell) && AllowedSpells.Contains(spell);
}

public enum ConfirmationStateChangeKind
{
    Decreased,
    Cleared
}

public readonly record struct LogicActionKey(string Spell, int Unit);

public interface IRuntimeLogic
{
    LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic);
}

public interface IActionSuppressionAwareRuntimeLogic
{
    LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic,
        IReadOnlySet<LogicActionKey> suppressedActions);
}

public interface IRateLimitAwareRuntimeLogic
{
    LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic,
        IReadOnlySet<LogicActionKey> suppressedActions,
        IReadOnlySet<string> rateLimitedRuleKeys);
}

public sealed record LogicEvaluation(string? ModuleName, LogicDecision? Decision);
