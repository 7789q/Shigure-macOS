using System.Text.Json.Nodes;

namespace Shigure;

public sealed class StateBuilder : IRuntimeStateBuilder
{
    private readonly ConfigService _config;

    public StateBuilder(ConfigService config)
    {
        _config = config;
    }

    public GameState Build(
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData,
        IReadOnlyDictionary<int, int>? healAbsorbData = null)
    {
        var classId = rowData.TryGetValue(2, out var cid) ? cid : 0;
        var specId = rowData.TryGetValue(3, out var sid) ? sid : 0;
        var stateConfig = _config.BuildStateConfig(classId, specId);
        var result = new Dictionary<string, object?>();
        healAbsorbData ??= new Dictionary<int, int>();
        HealAbsorbDiagnosticSnapshot? healAbsorbDiagnostic = null;

        foreach (var (key, node) in stateConfig)
        {
            if (key is "group" or "spells" or "auras" || node is not JsonObject field || !field.ContainsKey("step"))
            {
                continue;
            }

            result[key] = ConvertRawValue(ResolveRaw(field, rowData, barData), JsonHelpers.GetString(JsonHelpers.Get(field, "type")));
        }

        if (JsonHelpers.Get(stateConfig, "spells") is JsonObject spellsConfig)
        {
            result["spells"] = BuildFieldMap(spellsConfig, rowData, barData);
        }

        if (JsonHelpers.Get(stateConfig, "auras") is JsonObject aurasConfig)
        {
            result["auras"] = BuildFieldMap(aurasConfig, rowData, barData);
        }

        if (JsonHelpers.Get(stateConfig, "group") is JsonObject groupConfig)
        {
            var (group, positiveAbsorbs) = BuildGroup(groupConfig, rowData, barData, healAbsorbData);
            result["group"] = group;
            healAbsorbDiagnostic = new HealAbsorbDiagnosticSnapshot(
                healAbsorbData.Count,
                positiveAbsorbs);
        }

        return new GameState(result, healAbsorbDiagnostic);
    }

    private static Dictionary<string, object?> BuildFieldMap(
        JsonObject fieldsConfig,
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData)
    {
        var values = new Dictionary<string, object?>();
        foreach (var (fieldName, node) in fieldsConfig)
        {
            if (node is not JsonObject field || !field.ContainsKey("step"))
            {
                continue;
            }

            values[fieldName] = ConvertRawValue(ResolveRaw(field, rowData, barData), JsonHelpers.GetString(JsonHelpers.Get(field, "type")));
        }

        return values;
    }

    private static (
        Dictionary<string, IReadOnlyDictionary<string, object?>> Group,
        IReadOnlyList<HealAbsorbUnitDiagnostic> PositiveAbsorbs) BuildGroup(
        JsonObject groupConfig,
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData,
        IReadOnlyDictionary<int, int> healAbsorbData)
    {
        var start = JsonHelpers.GetInt(JsonHelpers.Get(groupConfig, "start")) ?? 26;
        var numParams = JsonHelpers.GetInt(JsonHelpers.Get(groupConfig, "num")) ?? 5;
        var group = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
        var positiveAbsorbs = new List<HealAbsorbUnitDiagnostic>();

        for (var i = 1; i <= 30; i++)
        {
            var baseStep = start + (i - 1) * numParams;
            var sub = new Dictionary<string, object?>();
            foreach (var (fieldName, node) in groupConfig)
            {
                if (fieldName is "start" or "num" || node is not JsonObject field || !field.ContainsKey("step"))
                {
                    continue;
                }

                int? raw;
                var stepNode = JsonHelpers.Get(field, "step");
                if (JsonHelpers.GetString(stepNode) == "bar")
                {
                    raw = ResolveRaw(field, rowData, barData);
                }
                else
                {
                    var relStep = JsonHelpers.GetInt(stepNode);
                    raw = relStep is null
                        ? null
                        : rowData.TryGetValue(baseStep + relStep.Value, out var rawValue) ? rawValue : null;
                }

                sub[fieldName] = ConvertRawValue(raw, JsonHelpers.GetString(JsonHelpers.Get(field, "type")));
            }

            // 治疗吸收是独立治疗信号，不折算或覆盖单位的真实生命值。
            var absorb = healAbsorbData.TryGetValue(i, out var absorbValue) ? absorbValue : 0;
            sub["治疗吸收"] = absorb;
            if (sub.TryGetValue("生命值", out var healthObj) && healthObj is int rawHealth)
            {
                if (absorb > 0)
                {
                    positiveAbsorbs.Add(new HealAbsorbUnitDiagnostic(
                        i,
                        rawHealth,
                        absorb,
                        rawHealth));
                }
            }

            group[i.ToString()] = sub;
        }

        return (group, positiveAbsorbs);
    }

    private static int? ResolveRaw(JsonObject field, IReadOnlyDictionary<int, int> rowData, IReadOnlyDictionary<int, int> barData)
    {
        var stepNode = JsonHelpers.Get(field, "step");
        if (JsonHelpers.GetString(stepNode) == "bar")
        {
            var barIndex = JsonHelpers.GetInt(JsonHelpers.Get(field, "bar"));
            return barIndex is not null && barData.TryGetValue(barIndex.Value, out var barValue) ? barValue : null;
        }

        var step = JsonHelpers.GetInt(stepNode);
        return step is not null && rowData.TryGetValue(step.Value, out var value) ? value : null;
    }

    private static object ConvertRawValue(int? raw, string? type)
    {
        return type switch
        {
            "bool" => raw.GetValueOrDefault() != 0,
            "string" => raw?.ToString() ?? string.Empty,
            _ => raw.GetValueOrDefault()
        };
    }
}
