using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using static Shigure.LuaLiteParser;

namespace Shigure;

/// <summary>
/// 将 Fuyutsui core/classmacros.lua 的 ClassMacros 展开为 keymap/*.json
/// （对齐 core/macro.lua CreateMacro 的槽位与热键池）。
/// </summary>
public static partial class FuyutsuiKeymapConverter
{
    public const int DynamicMacroSlotCount = 30;

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] Modifiers =
    [
        "CTRL", "ALT", "SHIFT",
        "ALT-CTRL", "ALT-SHIFT", "CTRL-SHIFT",
        "ALT-CTRL-SHIFT"
    ];

    private static readonly string[] Keys =
    [
        "NUMPAD1", "NUMPAD2", "NUMPAD3", "NUMPAD4", "NUMPAD5",
        "NUMPAD6", "NUMPAD7", "NUMPAD8", "NUMPAD9", "NUMPAD0",
        "NUMPADDECIMAL", "NUMPADPLUS", "NUMPADMINUS", "NUMPADMULTIPLY", "NUMPADDIVIDE",
        "F1", "F2", "F3", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        ",", ".", "/", ";", "'", "[", "]", "\\",
        "7", "8", "9", "0", "="
    ];

    private static readonly string[] MacroKind = BuildMacroKind();

    public static int MacroSlotCapacity => Modifiers.Length * Keys.Length;

    public static int CalculateRequiredSlots(
        int dynamicCount,
        int staticCount,
        int specialCount,
        int keyOffset = 0,
        string? routingMode = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dynamicCount);
        ArgumentOutOfRangeException.ThrowIfNegative(staticCount);
        ArgumentOutOfRangeException.ThrowIfNegative(specialCount);
        ArgumentOutOfRangeException.ThrowIfNegative(keyOffset);
        var dynamicSlots = UsesSelectorTargetRouting(routingMode)
            ? dynamicCount + (dynamicCount > 0 ? DynamicMacroSlotCount : 0)
            : dynamicCount * DynamicMacroSlotCount;
        return checked(keyOffset + dynamicSlots + staticCount + specialCount);
    }

    private static readonly Dictionary<string, int> ClassFileToId = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WARRIOR"] = 1,
        ["PALADIN"] = 2,
        ["HUNTER"] = 3,
        ["ROGUE"] = 4,
        ["PRIEST"] = 5,
        ["DEATHKNIGHT"] = 6,
        ["SHAMAN"] = 7,
        ["MAGE"] = 8,
        ["WARLOCK"] = 9,
        ["MONK"] = 10,
        ["DRUID"] = 11,
        ["DEMONHUNTER"] = 12,
        ["EVOKER"] = 13
    };

    public sealed record UpdateResult(
        string ClassMacrosPath,
        IReadOnlyList<string> UpdatedFiles,
        IReadOnlyList<string> Warnings);

    public sealed record MacroCapacityIssue(
        string ClassFile,
        int? SpecIndex,
        int RequiredSlots,
        int Capacity);

    public static IReadOnlyList<MacroCapacityIssue> ValidateCapacity(ClassMacrosStore.MacrosDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var issues = new List<MacroCapacityIssue>();
        foreach (var (classFile, macros) in document.Classes)
        {
            AddCapacityIssue(classFile, null, macros.DynamicCommon.Count, macros, issues);
            if (!macros.UsesSpecDynamicSpells)
            {
                continue;
            }

            foreach (var (specIndex, spells) in macros.DynamicBySpec)
            {
                AddCapacityIssue(
                    classFile,
                    specIndex,
                    macros.DynamicCommon.Count + spells.Count,
                    macros,
                    issues);
            }
        }

        return issues;
    }

    private static void AddCapacityIssue(
        string classFile,
        int? specIndex,
        int dynamicCount,
        ClassMacrosStore.ClassMacros macros,
        ICollection<MacroCapacityIssue> issues)
    {
        var required = CalculateRequiredSlots(
            dynamicCount,
            macros.StaticSpells.Count,
            macros.SpecialSpells.Count,
            macros.KeyOffset,
            macros.RoutingMode);
        if (required > MacroSlotCapacity)
        {
            issues.Add(new MacroCapacityIssue(classFile, specIndex, required, MacroSlotCapacity));
        }
    }

    public static UpdateResult UpdateFromClassMacros(string classMacrosPath, string keymapDirectory)
    {
        if (!File.Exists(classMacrosPath))
        {
            throw new FileNotFoundException($"找不到 classmacros.lua: {classMacrosPath}", classMacrosPath);
        }

        Directory.CreateDirectory(keymapDirectory);
        var lua = File.ReadAllText(classMacrosPath, Encoding.UTF8);
        var classMacros = ExtractAssignedTable(lua, "Fuyutsui.ClassMacros")
            ?? throw new InvalidDataException("classmacros.lua 中未找到 Fuyutsui.ClassMacros");

        var updated = new List<string>();
        var warnings = new List<string>();

        foreach (var (classFile, classId) in ClassFileToId)
        {
            if (classMacros.GetTable(classFile) is not { } classTable)
            {
                warnings.Add($"跳过 {classFile}: ClassMacros 中无此职业表");
                continue;
            }

            var fileName = ClassNames.GetConfigFileName(classId).ToLowerInvariant() + ".json";
            var jsonPath = Path.Combine(keymapDirectory, fileName);
            var existing = LoadExistingSpellNames(jsonPath);

            var (root, classWarnings) = CompileClassKeymap(classTable, existing, classFile, classId);
            warnings.AddRange(classWarnings);

            File.WriteAllText(jsonPath, root.ToJsonString(WriteOptions) + Environment.NewLine, Encoding.UTF8);
            updated.Add(jsonPath);
        }

        if (updated.Count == 0)
        {
            throw new InvalidOperationException("未成功转换任何职业 keymap。");
        }

        return new UpdateResult(classMacrosPath, updated, warnings);
    }

    private static (JsonObject Root, List<string> Warnings) CompileClassKeymap(
        TableValue classTable,
        ExistingSpellNames existingSpellNames,
        string classFile,
        int classId)
    {
        var warnings = new List<string>();
        var dynamicTable = classTable.GetTable("dynamicSpells");
        var staticSpells = ReadArrayEntries(classTable.GetTable("staticSpells"));
        var specialSpells = ReadArrayEntries(classTable.GetTable("specialSpells"));
        var keyOffset = ReadKeyOffset(classTable, classFile);
        var routingMode = ClassMacrosStore.NormalizeRoutingMode(classTable.GetString("routingMode"));

        if (!IsSpecializedDynamicFormat(dynamicTable))
        {
            var dynamicSpells = ReadArrayStrings(dynamicTable);
            return (
                CompileSlotMap(
                    dynamicSpells,
                    staticSpells,
                    specialSpells,
                    keyOffset,
                    existingSpellNames,
                    null,
                    classFile,
                    warnings,
                    routingMode),
                warnings);
        }

        var commonSpells = ReadArrayStrings(dynamicTable?.GetTable("common"));
        var root = CompileSlotMap(
            commonSpells,
            staticSpells,
            specialSpells,
            keyOffset,
            existingSpellNames,
            null,
            $"{classFile}[兼容回退]",
            warnings,
            routingMode);
        var specRoot = new JsonObject();
        var specs = ClassNames.GetSpecs(classId);
        var knownSpecIds = specs.Select(spec => spec.Id).ToHashSet();
        foreach (var unknownSpecId in GetDynamicSpecIndexes(dynamicTable).Where(id => !knownSpecIds.Contains(id)))
        {
            warnings.Add($"{classFile}[专精 {unknownSpecId}]: ClassNames 未登记，未生成此专精映射");
        }

        foreach (var spec in specs)
        {
            var dynamicSpells = new List<string>(commonSpells);
            dynamicSpells.AddRange(ReadArrayStrings(GetIndexedTable(dynamicTable, spec.Id)));
            specRoot[spec.Id.ToString()] = CompileSlotMap(
                dynamicSpells,
                staticSpells,
                specialSpells,
                keyOffset,
                existingSpellNames,
                spec.Id,
                $"{classFile}[专精 {spec.Id} {spec.Name}]",
                warnings,
                routingMode);
        }

        root["专精"] = specRoot;
        return (root, warnings);
    }

    private static JsonObject CompileSlotMap(
        IReadOnlyList<string> dynamicSpells,
        IReadOnlyList<MacroEntry> staticSpells,
        IReadOnlyList<MacroEntry> specialSpells,
        int keyOffset,
        ExistingSpellNames existingSpellNames,
        int? specId,
        string warningContext,
        List<string> warnings,
        string? routingMode)
    {
        if (UsesSelectorTargetRouting(routingMode))
        {
            return CompileSelectorTargetMap(
                dynamicSpells,
                staticSpells,
                specialSpells,
                keyOffset,
                existingSpellNames,
                specId,
                warningContext,
                warnings);
        }

        var dynamicSlots = dynamicSpells.Count * DynamicMacroSlotCount;
        var requiredSlots = (long)keyOffset + dynamicSlots + staticSpells.Count + specialSpells.Count;
        if (requiredSlots > MacroKind.Length)
        {
            warnings.Add(
                $"{warningContext}: 槽位容量溢出，需要 {requiredSlots} 个，最多 {MacroKind.Length} 个；" +
                $"末尾 {requiredSlots - MacroKind.Length} 个槽位不会写入 keymap");
        }

        var root = new JsonObject();
        for (var i = 1; i <= MacroKind.Length; i++)
        {
            var hotkey = MacroKind[i - 1];
            var unit = 0;
            var spell = string.Empty;
            var macroCondition = string.Empty;

            var localSlot = i - keyOffset;
            if (localSlot <= 0)
            {
                // 职业级保留槽位不创建宏，但仍保持全局热键池索引一致。
            }
            else if (localSlot <= dynamicSlots)
            {
                var groupIndex = (localSlot - 1) / DynamicMacroSlotCount;
                var raidIdx = ((localSlot - 1) % DynamicMacroSlotCount) + 1;
                if (groupIndex < dynamicSpells.Count
                    && !string.IsNullOrWhiteSpace(dynamicSpells[groupIndex]))
                {
                    spell = dynamicSpells[groupIndex];
                    unit = raidIdx;
                }
            }
            else
            {
                var relativeIndex = localSlot - dynamicSlots - 1;
                MacroEntry? entry = null;
                var isStaticEntry = false;
                if (relativeIndex < staticSpells.Count)
                {
                    entry = staticSpells[relativeIndex];
                    isStaticEntry = true;
                }
                else
                {
                    var specialIndex = relativeIndex - staticSpells.Count;
                    if (specialIndex < specialSpells.Count)
                    {
                        entry = specialSpells[specialIndex];
                    }
                }

                if (entry is { Body.Length: > 0 } macroEntry)
                {
                    if (isStaticEntry)
                    {
                        var parsed = ParseStaticMacro(macroEntry.Body, macroEntry.Comment);
                        unit = parsed.Unit;
                        spell = parsed.Spell;
                        macroCondition = parsed.Condition;
                    }
                    else
                    {
                        var parsed = ParseSpecialMacro(macroEntry.Body, macroEntry.Comment);
                        unit = parsed.Unit;
                        spell = parsed.Spell;
                        macroCondition = parsed.Condition;
                    }
                }

                if (entry is { Body.Length: > 0 }
                    && IsWeakSpellName(spell)
                    && TryGetExistingSpellName(existingSpellNames, specId, i, out var preserved)
                    && !string.IsNullOrWhiteSpace(preserved)
                    && !IsWeakSpellName(preserved))
                {
                    warnings.Add($"{warningContext}[{i}]: 保留原技能名「{preserved}」（宏推导为「{spell}」）");
                    spell = preserved;
                }
            }

            root[i.ToString()] = new JsonObject
            {
                ["unit"] = unit,
                ["宏条件"] = macroCondition,
                ["技能"] = spell,
                ["热键"] = hotkey
            };
        }

        return root;
    }

    private static JsonObject CompileSelectorTargetMap(
        IReadOnlyList<string> dynamicSpells,
        IReadOnlyList<MacroEntry> staticSpells,
        IReadOnlyList<MacroEntry> specialSpells,
        int keyOffset,
        ExistingSpellNames existingSpellNames,
        int? specId,
        string warningContext,
        List<string> warnings)
    {
        var requiredSlots = CalculateRequiredSlots(
            dynamicSpells.Count,
            staticSpells.Count,
            specialSpells.Count,
            keyOffset,
            ClassMacrosStore.SelectorTargetRoutingMode);
        if (requiredSlots > MacroKind.Length)
        {
            warnings.Add(
                $"{warningContext}: 两段路由容量溢出，需要 {requiredSlots} 个，最多 {MacroKind.Length} 个；" +
                $"末尾 {requiredSlots - MacroKind.Length} 个槽位不会写入 keymap");
        }

        var root = new JsonObject
        {
            ["路由模式"] = ClassMacrosStore.SelectorTargetRoutingMode
        };
        for (var i = 1; i <= MacroKind.Length; i++)
        {
            root[i.ToString()] = CreateKeymapEntry(0, string.Empty, string.Empty, MacroKind[i - 1]);
        }

        var selectorStart = keyOffset + 1;
        var targetStart = selectorStart + dynamicSpells.Count;
        for (var spellIndex = 0; spellIndex < dynamicSpells.Count; spellIndex++)
        {
            var spell = dynamicSpells[spellIndex];
            var selectorSlot = selectorStart + spellIndex;
            if (selectorSlot > MacroKind.Length || string.IsNullOrWhiteSpace(spell))
            {
                continue;
            }

            var selectorHotkey = MacroKind[selectorSlot - 1];
            for (var unit = 1; unit <= DynamicMacroSlotCount; unit++)
            {
                var targetSlot = targetStart + unit - 1;
                if (targetSlot > MacroKind.Length)
                {
                    break;
                }

                var targetHotkey = MacroKind[targetSlot - 1];
                root[$"route-{spellIndex + 1}-{unit}"] = new JsonObject
                {
                    ["unit"] = unit,
                    ["宏条件"] = string.Empty,
                    ["技能"] = spell,
                    ["热键"] = $"{selectorHotkey} > {targetHotkey}",
                    ["按键序列"] = new JsonArray(selectorHotkey, targetHotkey)
                };
            }
        }

        var staticStart = targetStart + (dynamicSpells.Count > 0 ? DynamicMacroSlotCount : 0);
        var entries = staticSpells
            .Select(entry => (Entry: entry, IsStatic: true))
            .Concat(specialSpells.Select(entry => (Entry: entry, IsStatic: false)))
            .ToList();
        for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            var slot = staticStart + entryIndex;
            if (slot > MacroKind.Length)
            {
                break;
            }

            var (entry, isStatic) = entries[entryIndex];
            if (entry.Body.Length == 0)
            {
                continue;
            }

            var parsed = isStatic
                ? ParseStaticMacro(entry.Body, entry.Comment)
                : ParseSpecialMacro(entry.Body, entry.Comment);
            var spell = parsed.Spell;
            if (IsWeakSpellName(spell)
                && TryGetExistingSpellName(existingSpellNames, specId, slot, out var preserved)
                && !string.IsNullOrWhiteSpace(preserved)
                && !IsWeakSpellName(preserved))
            {
                warnings.Add($"{warningContext}[{slot}]: 保留原技能名「{preserved}」（宏推导为「{spell}」）");
                spell = preserved;
            }

            root[slot.ToString()] = CreateKeymapEntry(
                parsed.Unit,
                parsed.Condition,
                spell,
                MacroKind[slot - 1]);
        }

        return root;
    }

    private static JsonObject CreateKeymapEntry(int unit, string macroCondition, string spell, string hotkey) => new()
    {
        ["unit"] = unit,
        ["宏条件"] = macroCondition,
        ["技能"] = spell,
        ["热键"] = hotkey
    };

    private static bool UsesSelectorTargetRouting(string? routingMode) =>
        string.Equals(
            ClassMacrosStore.NormalizeRoutingMode(routingMode),
            ClassMacrosStore.SelectorTargetRoutingMode,
            StringComparison.OrdinalIgnoreCase);

    private static int ReadKeyOffset(TableValue classTable, string classFile)
    {
        var value = classTable.GetNumber("keyOffset") ?? 0;
        if (value < 0 || value > int.MaxValue || value != Math.Truncate(value))
        {
            throw new InvalidDataException($"{classFile}: keyOffset 必须是非负整数");
        }

        return (int)value;
    }

    private static bool IsSpecializedDynamicFormat(TableValue? dynamicTable)
    {
        if (dynamicTable is null)
        {
            return false;
        }

        return dynamicTable.GetTable("common") is not null
            || GetDynamicSpecIndexes(dynamicTable).Count > 0;
    }

    private static TableValue? GetIndexedTable(TableValue? table, int index)
    {
        return table?.GetTable((long)index) ?? table?.GetTable(index);
    }

    private static IReadOnlyList<int> GetDynamicSpecIndexes(TableValue? table)
    {
        if (table is null)
        {
            return [];
        }

        return table.Entries
            .Where(entry => entry.Value is TableValue)
            .Select(entry => entry.Key switch
            {
                long value when value is > 0 and <= int.MaxValue => (int?)value,
                int value when value > 0 => value,
                _ => null
            })
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
    }

    private static bool IsWeakSpellName(string? spell)
    {
        if (string.IsNullOrWhiteSpace(spell))
        {
            return true;
        }

        return spell.StartsWith("item:", StringComparison.OrdinalIgnoreCase);
    }

    public readonly record struct ParsedMacro(int Unit, string Spell, string Condition);

    /// <summary>
    /// 解析静态宏供 keymap 与宏列表共用。只有方括号内以 @ 开头的项属于目标。
    /// </summary>
    public static ParsedMacro ParseStaticMacro(string raw, string? comment = null)
    {
        var target = StaticTargetRegex().Match(raw);
        var unit = target.Success
            ? ResolveUnitName(target.Groups["unit"].Value)
            : ReservedUnit.None;

        return new ParsedMacro(
            unit,
            ResolveSpellName(new MacroEntry(raw, comment)),
            ResolveConditions(raw));
    }

    /// <summary>
    /// 解析特殊宏：方括号中以 @ 开头的首个单位作为 unit，技能沿用宏技能推导（castsequence 只取逗号前首项）。
    /// </summary>
    public static ParsedMacro ParseSpecialMacro(string raw, string? comment = null)
    {
        // 标准 WoW 条件允许单位出现在其它条件之后，例如 [known:123,@cursor]。
        var target = StaticTargetRegex().Match(raw);
        var unit = target.Success
            ? ResolveUnitName(target.Groups["unit"].Value)
            : ReservedUnit.None;

        var spell = ResolveSpellName(new MacroEntry(raw, comment));
        spell = SplitTopLevel(spell, ',').FirstOrDefault()?.Trim() ?? string.Empty;
        return new ParsedMacro(unit, spell, ResolveConditions(raw));
    }

    /// <summary>方括号中以 @ 开头的是目标，其余逗号分隔项作为只读条件摘要。</summary>
    private static string ResolveConditions(string raw)
    {
        var conditions = ConditionRegex().Matches(raw)
            .SelectMany(bracket => bracket.Value.Length < 2
                ? []
                : bracket.Value[1..^1]
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Where(item => !item.StartsWith('@'));
        return MacroConditionText.Normalize(string.Join(", ", conditions));
    }

    private static int ResolveUnitName(string raw)
    {
        var normalized = raw.Trim().TrimStart('@').ToLowerInvariant();
        if (normalized.StartsWith("party", StringComparison.Ordinal)
            && int.TryParse(normalized[5..], out var partyIndex)
            && partyIndex is >= 1 and <= 4)
        {
            // Fuyutsui 队伍槽位：player=1，party1..4=2..5。
            return partyIndex + 1;
        }

        if (normalized.StartsWith("raid", StringComparison.Ordinal)
            && int.TryParse(normalized[4..], out var raidIndex)
            && raidIndex is >= 1 and <= 30)
        {
            return raidIndex;
        }

        return normalized switch
        {
            // "player" => 1,
            // "玩家" or "31" => ReservedUnit.Player,
            "player" or "玩家" or "31" => ReservedUnit.Player,
            "target" or "目标" or "32" => ReservedUnit.Target,
            "focus" or "焦点" or "33" => ReservedUnit.Focus,
            "cursor" or "地面" or "34" => ReservedUnit.Cursor,
            "mouseover" or "鼠标" or "35" => ReservedUnit.Mouseover,
            _ => ReservedUnit.None
        };
    }

    /// <summary>同行 `--` 注释优先作为技能名；否则从宏文本推导。</summary>
    private static string ResolveSpellName(MacroEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Comment))
        {
            return entry.Comment.Trim();
        }

        return DeriveSpellName(entry.Body);
    }

    private readonly record struct MacroEntry(string Body, string? Comment);

    internal static string DeriveSpellName(string raw)
    {
        var text = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (StopCastingRegex().IsMatch(text))
        {
            return "停止施法";
        }

        var castSequence = CastSequenceRegex().Match(text);
        if (castSequence.Success)
        {
            var sequenceBody = castSequence.Groups[1].Value.Trim();
            sequenceBody = ResetOptionRegex().Replace(sequenceBody, string.Empty).Trim();
            foreach (var rawPart in SplitTopLevel(sequenceBody, ','))
            {
                var part = rawPart.Trim();
                if (part.Length == 0)
                {
                    continue;
                }

                if (part.Equals("x", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return StripConditions(part);
            }
        }

        // cancelaura 后再 /cast：取最后一个 /cast 段；纯物品宏保留首行 item:
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length > 0 && lines[0].StartsWith("item:", StringComparison.OrdinalIgnoreCase))
        {
            return lines[0].Trim();
        }

        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i];
            if (line.StartsWith("/cast", StringComparison.OrdinalIgnoreCase)
                && !line.StartsWith("/castsequence", StringComparison.OrdinalIgnoreCase))
            {
                text = line["/cast".Length..].TrimStart();
                break;
            }

            if (i == 0 && !line.StartsWith('/'))
            {
                text = line;
            }
        }

        if (text.StartsWith("/cast", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("/castsequence", StringComparison.OrdinalIgnoreCase))
        {
            text = text["/cast".Length..].TrimStart();
        }

        // 取 ; 分支中第一段（专精/条件分支）
        var firstBranch = text.Split(';', 2, StringSplitOptions.TrimEntries)[0];
        var spell = StripConditions(firstBranch);

        if (string.IsNullOrWhiteSpace(spell))
        {
            return string.Empty;
        }

        return spell;
    }

    /// <summary>按顶层分隔符切分，方括号内的逗号不作为技能分隔符。</summary>
    private static IEnumerable<string> SplitTopLevel(string text, char separator)
    {
        var start = 0;
        var bracketDepth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '[':
                    bracketDepth++;
                    break;
                case ']' when bracketDepth > 0:
                    bracketDepth--;
                    break;
                default:
                    if (text[i] == separator && bracketDepth == 0)
                    {
                        yield return text[start..i];
                        start = i + 1;
                    }

                    break;
            }
        }

        yield return text[start..];
    }

    private static string StripConditions(string text)
    {
        var stripped = ConditionRegex().Replace(text, string.Empty).Trim();
        return stripped;
    }

    private static List<string> ReadArrayStrings(TableValue? table)
    {
        var result = new List<string>();
        if (table is null)
        {
            return result;
        }

        foreach (var item in table.IPairs())
        {
            if (item is StringValue s)
            {
                result.Add(s.Value.Trim());
            }
            else
            {
                break;
            }
        }

        return result;
    }

    private static List<MacroEntry> ReadArrayEntries(TableValue? table)
    {
        var result = new List<MacroEntry>();
        if (table is null)
        {
            return result;
        }

        var index = 1;
        foreach (var value in table.IPairs())
        {
            if (value is not StringValue s)
            {
                break;
            }

            result.Add(new MacroEntry(s.Value, table.GetTrailingComment((long)index)));
            index++;
        }

        return result;
    }

    private sealed record ExistingSpellNames(
        IReadOnlyDictionary<int, string> Fallback,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>> BySpec)
    {
        public static readonly ExistingSpellNames Empty = new(
            new Dictionary<int, string>(),
            new Dictionary<int, IReadOnlyDictionary<int, string>>());
    }

    private static ExistingSpellNames LoadExistingSpellNames(string jsonPath)
    {
        if (!File.Exists(jsonPath))
        {
            return ExistingSpellNames.Empty;
        }

        try
        {
            if (JsonNode.Parse(File.ReadAllText(jsonPath)) is not JsonObject root)
            {
                return ExistingSpellNames.Empty;
            }

            var fallback = ReadExistingSpellNames(root);
            var bySpec = new Dictionary<int, IReadOnlyDictionary<int, string>>();
            if (JsonHelpers.Get(root, "专精") is JsonObject specRoot)
            {
                foreach (var (key, node) in specRoot)
                {
                    if (!int.TryParse(key, out var specId) || node is not JsonObject specMap)
                    {
                        continue;
                    }

                    bySpec[specId] = ReadExistingSpellNames(specMap);
                }
            }

            return new ExistingSpellNames(fallback, bySpec);
        }
        catch
        {
            // 旧 keymap 损坏时忽略，按宏全量重建。
            return ExistingSpellNames.Empty;
        }
    }

    private static IReadOnlyDictionary<int, string> ReadExistingSpellNames(JsonObject map)
    {
        var result = new Dictionary<int, string>();
        foreach (var (key, node) in map)
        {
            if (!int.TryParse(key, out var id) || node is not JsonObject entry)
            {
                continue;
            }

            var spell = JsonHelpers.GetString(JsonHelpers.Get(entry, "技能"))
                ?? JsonHelpers.GetString(JsonHelpers.Get(entry, "spell"));
            if (!string.IsNullOrWhiteSpace(spell))
            {
                result[id] = spell;
            }
        }

        return result;
    }

    private static bool TryGetExistingSpellName(
        ExistingSpellNames existingSpellNames,
        int? specId,
        int slot,
        out string spell)
    {
        if (specId is { } id
            && existingSpellNames.BySpec.TryGetValue(id, out var specNames)
            && specNames.TryGetValue(slot, out var specSpell)
            && !string.IsNullOrWhiteSpace(specSpell)
            && !IsWeakSpellName(specSpell))
        {
            spell = specSpell;
            return true;
        }

        return existingSpellNames.Fallback.TryGetValue(slot, out spell!);
    }

    private static string[] BuildMacroKind()
    {
        var list = new string[Modifiers.Length * Keys.Length];
        var i = 0;
        foreach (var modifier in Modifiers)
        {
            foreach (var key in Keys)
            {
                list[i++] = $"{modifier}-{key}";
            }
        }

        return list;
    }

    [GeneratedRegex(@"^\s*/stopcasting\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StopCastingRegex();

    [GeneratedRegex(@"^\s*/castsequence\b\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex CastSequenceRegex();

    [GeneratedRegex(@"\breset\s*=\s*\S+\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResetOptionRegex();

    [GeneratedRegex(@"\[[^\]]*\]", RegexOptions.CultureInvariant)]
    private static partial Regex ConditionRegex();

    [GeneratedRegex(@"\[[^\]]*@(?<unit>cursor|target|focus|player|mouseover|party[1-4]|raid(?:[1-9]|[12][0-9]|30))\b[^\]]*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StaticTargetRegex();

}
