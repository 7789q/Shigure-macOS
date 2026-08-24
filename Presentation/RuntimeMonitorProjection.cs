namespace Shigure.Presentation;

public sealed record RuntimeDisplayRow(string First, string Second, string? Third = null);

public sealed record RuntimeMonitorView(
    IReadOnlyList<RuntimeDisplayRow> State,
    IReadOnlyList<RuntimeDisplayRow> Auras,
    IReadOnlyList<RuntimeDisplayRow> DynamicValues,
    IReadOnlyList<RuntimeDisplayRow> Spells,
    IReadOnlyList<RuntimeDisplayRow> Party,
    IReadOnlyList<RuntimeDisplayRow> Logic);

public static class RuntimeMonitorProjection
{
    public static RuntimeMonitorView Create(RenderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new RuntimeMonitorView(
            BuildStateRows(snapshot),
            BuildAuraRows(snapshot),
            BuildDynamicRows(snapshot),
            BuildSpellRows(snapshot),
            BuildPartyRows(snapshot),
            BuildLogicRows(snapshot));
    }

    public static string FormatValue(object? value) => value switch
    {
        null => "-",
        bool flag => flag ? "是" : "否",
        _ => value.ToString() ?? "-"
    };

    private static IReadOnlyList<RuntimeDisplayRow> BuildStateRows(RenderSnapshot snapshot)
    {
        if (snapshot.State is null)
        {
            return [new("-", "状态", "等待游戏状态")];
        }

        var rows = new List<RuntimeDisplayRow>();
        if (!string.IsNullOrWhiteSpace(snapshot.ModuleName))
        {
            rows.Add(new("1", "匹配模块", snapshot.ModuleName));
        }

        foreach (var (key, value) in snapshot.State.Values)
        {
            if (key is "spells" or "auras" or "group" || key.StartsWith('$'))
            {
                continue;
            }

            rows.Add(new((rows.Count + 1).ToString(), key, FormatValue(value)));
        }

        return rows;
    }

    private static IReadOnlyList<RuntimeDisplayRow> BuildAuraRows(RenderSnapshot snapshot)
    {
        var rows = snapshot.State?.Auras
            .Select((entry, index) => new RuntimeDisplayRow(
                (index + 1).ToString(),
                entry.Key,
                FormatValue(entry.Value)))
            .ToArray() ?? [];
        return rows.Length == 0 ? [new("-", "光环", "无数据")] : rows;
    }

    private static IReadOnlyList<RuntimeDisplayRow> BuildDynamicRows(RenderSnapshot snapshot)
    {
        if (snapshot.State is null)
        {
            return [new("-", "动态单位", "等待游戏状态")];
        }

        return snapshot.DynamicValues.Count == 0
            ? [new("-", "动态单位", "无数据")]
            : snapshot.DynamicValues
                .Select(value => new RuntimeDisplayRow(value.Kind, value.Name, value.Value))
                .ToArray();
    }

    private static IReadOnlyList<RuntimeDisplayRow> BuildSpellRows(RenderSnapshot snapshot)
    {
        var rows = snapshot.State?.Spells
            .Select((entry, index) => new RuntimeDisplayRow(
                (index + 1).ToString(),
                entry.Key,
                FormatValue(entry.Value)))
            .ToArray() ?? [];
        return rows.Length == 0 ? [new("-", "技能", "无数据")] : rows;
    }

    private static IReadOnlyList<RuntimeDisplayRow> BuildPartyRows(RenderSnapshot snapshot)
    {
        var partyCount = snapshot.State?.GetInt("队伍人数") ?? 0;
        if (snapshot.State is null || partyCount <= 0)
        {
            return [new("队伍", "无队伍数据")];
        }

        var rows = new List<RuntimeDisplayRow>(partyCount);
        for (var index = 1; index <= partyCount; index++)
        {
            var unitKey = index.ToString();
            if (!snapshot.State.Group.TryGetValue(unitKey, out var unitData))
            {
                rows.Add(new($"Unit {unitKey}", "-"));
                continue;
            }

            var summary = string.Join(
                "  ",
                unitData.Select(entry => $"{entry.Key}: {FormatValue(entry.Value)}"));
            rows.Add(new($"Unit {unitKey}", summary));
        }

        return rows;
    }

    private static IReadOnlyList<RuntimeDisplayRow> BuildLogicRows(RenderSnapshot snapshot)
    {
        return snapshot.UnitInfo.Count == 0
            ? [new("逻辑信息", "无推荐目标")]
            : snapshot.UnitInfo
                .OrderBy(entry => entry.Key)
                .Select(entry => new RuntimeDisplayRow(entry.Key, FormatValue(entry.Value)))
                .ToArray();
    }
}
