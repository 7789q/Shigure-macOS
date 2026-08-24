using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shigure;

public sealed class KeymapService : IKeymapResolver
{
    private readonly string _baseDirectory;
    private readonly ConfigService _config;
    private readonly Dictionary<(int Unit, string Spell, string MacroCondition), string> _hotkeys = new();
    private readonly Dictionary<(int Unit, string Spell), string> _fallbackHotkeys = new();
    private int? _currentClassId;
    private int? _currentSpecId;

    public KeymapService(string baseDirectory, ConfigService config)
    {
        _baseDirectory = baseDirectory;
        _config = config;
    }

    public void SelectForClass(int? classId)
    {
        SelectForClass(classId, null);
    }

    public void SelectForClass(int? classId, int? specId)
    {
        if (_currentClassId == classId && _currentSpecId == specId && _hotkeys.Count > 0)
        {
            return;
        }

        _currentClassId = classId;
        _currentSpecId = specId;
        _hotkeys.Clear();
        _fallbackHotkeys.Clear();

        var path = KeymapCatalog.ResolveKeymapFilePath(_baseDirectory, _config.GetKeymapName(classId));
        if (!File.Exists(path))
        {
            return;
        }

        var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) as JsonObject;

        if (root is null)
        {
            return;
        }

        var entries = root;
        if (specId is { } id
            && JsonHelpers.Get(root, "专精") is JsonObject specRoot
            && JsonHelpers.Get(specRoot, id.ToString()) is JsonObject specEntries)
        {
            entries = specEntries;
        }

        foreach (var (_, node) in entries)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            var rawUnit = JsonHelpers.GetInt(JsonHelpers.Get(entry, "unit")) ?? 0;
            var spell = JsonHelpers.GetString(JsonHelpers.Get(entry, "spell"))
                ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "技能"));
            var hotkey = JsonHelpers.GetString(JsonHelpers.Get(entry, "hotkey"))
                ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "热键"));
            var normalizedMacro = MacroConditionText.NormalizeLegacyUnit(
                rawUnit,
                JsonHelpers.GetString(JsonHelpers.Get(entry, "宏条件")));
            var unit = normalizedMacro.Unit;
            var macroCondition = normalizedMacro.Condition;

            if (!string.IsNullOrWhiteSpace(spell) && !string.IsNullOrWhiteSpace(hotkey))
            {
                _hotkeys[(unit, spell, macroCondition)] = hotkey;
                // 兼容未保存“宏条件”的旧模块：保留旧版按单位+技能查询时的最后一项行为。
                _fallbackHotkeys[(unit, spell)] = hotkey;
            }
        }
    }

    public string? GetHotkey(int? unit, string spell, string? macroCondition = null)
    {
        var normalizedUnit = unit.GetValueOrDefault();
        // null 表示旧模块根本没有该字段，严格沿用升级前“单位+技能”的最后一项匹配。
        if (macroCondition is null)
        {
            return _fallbackHotkeys.TryGetValue((normalizedUnit, spell), out var legacyHotkey)
                ? legacyHotkey
                : null;
        }

        var normalizedCondition = MacroConditionText.Normalize(macroCondition);
        if (_hotkeys.TryGetValue((normalizedUnit, spell, normalizedCondition), out var exactHotkey))
        {
            return exactHotkey;
        }

        return null;
    }

    public IReadOnlyDictionary<int, string> GetCurrentFailedSpells()
    {
        return _config.GetFailedSpells(_currentClassId);
    }

    public IReadOnlyDictionary<int, string> GetCurrentOneKeySpells()
    {
        return _config.GetOneKeySpells(_currentClassId);
    }
}
