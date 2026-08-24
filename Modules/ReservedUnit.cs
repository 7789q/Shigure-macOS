using System.Globalization;

namespace Shigure;

/// <summary>
/// keymap 中团队槽位 1-30 之外的保留单位，以及模块编辑器使用的中文显示名称。
/// </summary>
public static class ReservedUnit
{
    public const int None = 0;
    public const int Player = 31;
    public const int Target = 32;
    public const int Focus = 33;
    public const int Cursor = 34;
    public const int Mouseover = 35;

    public static string ToDisplayText(int unit)
    {
        return unit switch
        {
            None => "无目标",
            Player => "玩家",
            Target => "目标",
            Focus => "焦点",
            Cursor => "地面",
            Mouseover => "鼠标",
            _ => unit.ToString(CultureInfo.InvariantCulture)
        };
    }

    public static int? ParseDisplayText(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        return value switch
        {
            "无目标" => None,
            "玩家" => Player,
            "目标" => Target,
            "焦点" => Focus,
            "地面" => Cursor,
            "鼠标" => Mouseover,
            _ => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unit)
                ? unit
                : null
        };
    }
}

/// <summary>宏条件在 keymap、模块和界面中统一使用原始标识。</summary>
public static class MacroConditionText
{
    private const int LegacyChannelingUnit = 36;
    private const int LegacyNoChannelingUnit = 37;
    public const string Channeling = "channeling";
    public const string NoChanneling = "nochanneling";

    /// <summary>兼容旧版误把引导条件写入 unit=36/37 的 keymap 与模块。</summary>
    public static (int Unit, string Condition) NormalizeLegacyUnit(int unit, string? condition)
    {
        var normalizedCondition = Normalize(condition);
        return unit switch
        {
            LegacyChannelingUnit => (ReservedUnit.None,
                normalizedCondition.Length == 0 ? Channeling : normalizedCondition),
            LegacyNoChannelingUnit => (ReservedUnit.None,
                normalizedCondition.Length == 0 ? NoChanneling : normalizedCondition),
            _ => (unit, normalizedCondition)
        };
    }

    public static string Normalize(string? text)
    {
        var parts = (text ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.ToLowerInvariant() switch
            {
                // 只兼容读取旧版中文值；新数据及界面统一保留 WoW 宏条件名称。
                "channeling" or "引导中" => Channeling,
                "nochanneling" or "非引导" => NoChanneling,
                _ => part
            });
        return string.Join(", ", parts);
    }

    public static string ToDisplayText(string? text)
    {
        return Normalize(text);
    }

    public static string ParseDisplayText(string? text)
    {
        return Normalize(text);
    }

}
