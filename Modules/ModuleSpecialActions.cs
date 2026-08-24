using System.Globalization;

namespace Shigure;

internal static class ModuleSpecialActions
{
    public const string PauseSpell = "暂停";
    public const string InsertSpellState = "插入法术";
    public const string FailedSpell = "自动插入法术";
    public const string OneKeySpell = "一键法术";

    public static bool IsPauseSpell(string? spell)
    {
        return string.Equals(spell?.Trim(), PauseSpell, StringComparison.Ordinal);
    }

    public static bool IsFailedSpell(string? spell)
    {
        return string.Equals(spell?.Trim(), FailedSpell, StringComparison.Ordinal);
    }

    /// <summary>旧模块动作中的“插入法术”迁移为新的特殊动作名，条件字段不在此处改写。</summary>
    public static string NormalizeSpellAction(string? spell)
    {
        var normalized = spell?.Trim() ?? string.Empty;
        return string.Equals(normalized, InsertSpellState, StringComparison.Ordinal)
            ? FailedSpell
            : normalized;
    }

    public static bool IsOneKeySpell(string? spell)
    {
        return string.Equals(spell?.Trim(), OneKeySpell, StringComparison.Ordinal);
    }

    public static string? GetFailedSpell(GameState state, IReadOnlyDictionary<int, string>? failedSpellMap)
    {
        var failedSpellId = state.GetInt(InsertSpellState);
        if (failedSpellMap is null || !failedSpellMap.TryGetValue(failedSpellId, out var spellName))
        {
            return null;
        }

        return state.Spells.TryGetValue(spellName, out var cooldown)
            && IsZero(cooldown)
                ? spellName
                : null;
    }

    public static string? GetOneKeySpell(GameState state, IReadOnlyDictionary<int, string>? oneKeySpellMap)
    {
        var oneKeyAssist = state.GetInt("一键辅助");
        return oneKeySpellMap is not null && oneKeySpellMap.TryGetValue(oneKeyAssist, out var spellName)
            ? spellName
            : null;
    }

    private static bool IsZero(object? value)
    {
        return value switch
        {
            int i => i == 0,
            long l => l == 0,
            float f => Math.Abs(f) < float.Epsilon,
            double d => Math.Abs(d) < double.Epsilon,
            decimal m => m == 0,
            bool b => !b,
            string s => TryParseZero(s, out var isZero) && isZero,
            _ => false
        };
    }

    private static bool TryParseZero(string text, out bool isZero)
    {
        isZero = false;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            return false;
        }

        isZero = Math.Abs(parsed) < double.Epsilon;
        return true;
    }
}
