namespace Shigure;

public sealed record LogicDecision(
    string? Hotkey,
    string Step,
    IReadOnlyDictionary<string, object?> UnitInfo,
    string? ModuleName = null,
    int DelayMs = 0,
    string? RateLimitKey = null,
    int LogicDelayMs = 0,
    IReadOnlyList<string>? HotkeySequence = null)
{
    public IReadOnlyList<string> ResolveHotkeySequence() =>
        HotkeySequence is { Count: > 0 }
            ? HotkeySequence
            : string.IsNullOrWhiteSpace(Hotkey) ? [] : [Hotkey];
}

public interface IRuntimeLogic
{
    LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic);
}

public sealed record LogicEvaluation(string? ModuleName, LogicDecision? Decision);
