namespace Shigure;

/// <summary>
/// 把 <see cref="ModuleUnit"/> / <see cref="ModuleCountField"/> 定义在当前 group 状态下解析为
/// 单位槽位或数量。逻辑忠实移植自旧 Python 项目 utils.py:
/// - 只考虑职责 != 0 的可用单位，玩家槽位由独立玩家状态校正;
/// - 生命值 0 视为死亡跳过;
/// - 阈值表示只考虑 0 &lt; 生命值 &lt; 阈值;
/// - 按 "1".."30" 升序遍历, 保证首/末语义稳定。
/// </summary>
public static class UnitSelector
{
    private const int DefaultThreshold = 100;

    public static int ResolvePlayerSlot(GameState state)
    {
        var groupType = state.GetInt("队伍类型");
        return groupType == 46 ? 1 : groupType is >= 1 and <= 30 ? groupType : 1;
    }

    /// <summary>解析动态单位为 group 槽位("1".."30"), 无匹配返回 null。</summary>
    public static string? Resolve(ModuleUnit unit, GameState state)
    {
        var group = ReconcilePlayerHealth(state);
        var threshold = ResolveThreshold(
            unit.HealthThreshold,
            unit.HealthThresholdField,
            state,
            UsesZeroDefaultThreshold(unit.Kind) ? 0 : DefaultThreshold);
        var aura = FirstAura(unit.AuraNames);

        return unit.Kind switch
        {
            UnitSelectorKind.LowestHealth => LowestHealth(
                group,
                threshold,
                data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role)),
            UnitSelectorKind.LowestHealthWithAnyAura => unit.AuraNames is { Count: > 0 } names
                ? LowestHealth(
                    group,
                    threshold,
                    data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role) && HasAnyAura(data, names))
                : null,
            UnitSelectorKind.LowestHealthWithoutAnyAura => unit.AuraNames is { Count: > 0 } names
                ? LowestHealth(
                    group,
                    threshold,
                    data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role) && !HasAnyAura(data, names))
                : null,
            UnitSelectorKind.LowestHealthWithoutAura => aura is null
                ? null
                : LowestHealth(
                    group,
                    threshold,
                    data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role) && !HasAura(data, aura)),
            UnitSelectorKind.LowestHealthWithAura => aura is null
                ? null
                : LowestHealth(
                    group,
                    threshold,
                    data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role) && HasAura(data, aura)),
            UnitSelectorKind.LowestHealthWithAuraCount => aura is null || unit.AuraCount is null
                ? null
                : LowestHealth(
                    group,
                    threshold,
                    data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role)
                        && AuraEquals(data, aura, unit.AuraCount.Value)),
            UnitSelectorKind.UnitWithRole => unit.Role is null
                ? null
                : UnitWithRole(group, unit.Role.Value, unit.Reverse, _ => true),
            UnitSelectorKind.UnitWithRoleWithoutAura => unit.Role is null || aura is null
                ? null
                : UnitWithRole(group, unit.Role.Value, unit.Reverse, data => !HasAura(data, aura)),
            UnitSelectorKind.UnitWithAura => aura is null ? null : UnitWithAura(group, aura),
            UnitSelectorKind.UnitWithDispelType => unit.DispelType is null
                ? null
                : UnitWithDispelType(group, unit.DispelType.Value),
            UnitSelectorKind.UnitWithAnyDispelType => UnitWithAnyDispelType(group),
            UnitSelectorKind.HighestHealingAbsorb => HighestHealingAbsorb(group, threshold, _ => true),
            UnitSelectorKind.HighestHealingAbsorbWithAnyAura => unit.AuraNames is { Count: > 0 } names
                ? HighestHealingAbsorb(group, threshold, data => HasAnyAura(data, names))
                : null,
            UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura => unit.AuraNames is { Count: > 0 } names
                ? HighestHealingAbsorb(group, threshold, data => !HasAnyAura(data, names))
                : null,
            UnitSelectorKind.HighestHealingAbsorbWithoutAura => aura is null
                ? null
                : HighestHealingAbsorb(group, threshold, data => !HasAura(data, aura)),
            UnitSelectorKind.HighestHealingAbsorbWithAura => aura is null
                ? null
                : HighestHealingAbsorb(group, threshold, data => HasAura(data, aura)),
            UnitSelectorKind.HighestHealingAbsorbWithAuraCount => aura is null || unit.AuraCount is null
                ? null
                : HighestHealingAbsorb(group, threshold, data => AuraEquals(data, aura, unit.AuraCount.Value)),
            UnitSelectorKind.HighestHealingDeficit => HighestHealingDeficit(
                group,
                threshold,
                data => MatchesRoleFilter(data, unit.RoleFilter, unit.Role)),
            _ => null
        };
    }

    /// <summary>解析数量字段为整数。</summary>
    public static int Resolve(ModuleCountField count, GameState state)
    {
        var group = ReconcilePlayerHealth(state);
        var threshold = ResolveThreshold(
            count.HealthThreshold,
            count.HealthThresholdField,
            state,
            UsesZeroDefaultThreshold(count.Kind) ? 0 : DefaultThreshold);

        return count.Kind switch
        {
            CountKind.UnitsBelowHealth => CountUnits(group, data => BelowThreshold(data, threshold)),
            CountKind.UnitsWithoutAuraBelowHealth => count.AuraName is null
                ? 0
                : CountUnits(group, data => !HasAura(data, count.AuraName) && BelowThreshold(data, threshold)),
            CountKind.UnitsWithAura => count.AuraName is null
                ? 0
                : CountUnits(group, data => HasAura(data, count.AuraName)),
            CountKind.UnitsWithAuraBelowHealth => count.AuraName is null
                ? 0
                : CountUnits(
                    group,
                    data => HasAura(data, count.AuraName)
                        && BelowThreshold(data, threshold)),
            CountKind.UnitsAboveHealingAbsorb => CountUnits(
                group,
                data => AboveHealingAbsorbThreshold(data, threshold)),
            CountKind.UnitsWithoutAuraAboveHealingAbsorb => count.AuraName is null
                ? 0
                : CountUnits(
                    group,
                    data => !HasAura(data, count.AuraName)
                        && AboveHealingAbsorbThreshold(data, threshold)),
            CountKind.UnitsWithAuraAboveHealingAbsorb => count.AuraName is null
                ? 0
                : CountUnits(
                    group,
                    data => HasAura(data, count.AuraName)
                        && AboveHealingAbsorbThreshold(data, threshold)),
            CountKind.UnitsAboveHealingDeficit => CountUnits(
                group,
                data => TryHealingDeficit(data, out var deficit) && deficit > threshold),
            CountKind.UnitsAtOrAboveHealingDeficit => CountUnits(
                group,
                data => TryHealingLoad(data, out var load) && load >= threshold),
            CountKind.TotalHealingDeficit => SumHealingLoad(group),
            CountKind.TotalHealthDeficit => SumHealthDeficit(group),
            _ => 0
        };
    }

    public static int? ResolveHealth(string? slot, GameState state)
    {
        if (string.IsNullOrWhiteSpace(slot)
            || !ReconcilePlayerHealth(state).TryGetValue(slot, out var data)
            || !TryInt(GetField(data, "生命值"), out var health))
        {
            return null;
        }

        return health;
    }

    /// <summary>取可用且 0 &lt; 生命值 &lt; 阈值并满足 predicate 的最低血量单位。</summary>
    private static string? LowestHealth(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        int threshold,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        string? lowestUnit = null;
        var lowestPct = threshold;
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data) || !RoleNotZero(data) || !predicate(data))
            {
                continue;
            }

            if (!TryInt(GetField(data, "生命值"), out var pct))
            {
                continue;
            }

            if (pct > 0 && pct < threshold && pct < lowestPct)
            {
                lowestUnit = key;
                lowestPct = pct;
            }
        }

        return lowestUnit;
    }

    /// <summary>按职责取首个(reverse=false)或逆序首个(reverse=true)且满足 predicate 的单位。</summary>
    private static string? UnitWithRole(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        int role,
        bool reverse,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        string? first = null;
        string? last = null;
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data) || !RoleNotZero(data))
            {
                continue;
            }

            if (!TryInt(GetField(data, "职责"), out var r) || r != role || !predicate(data))
            {
                continue;
            }

            first ??= key;
            last = key;
        }

        return reverse ? last : first;
    }

    /// <summary>取拥有某光环(数值 &gt; 0)且持续时间最长的单位。</summary>
    private static string? UnitWithAura(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        string auraName)
    {
        string? bestUnit = null;
        var bestDuration = 0;
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data) || !RoleNotZero(data))
            {
                continue;
            }

            if (!TryInt(GetField(data, auraName), out var duration) || duration <= 0)
            {
                continue;
            }

            if (bestUnit is null || duration > bestDuration)
            {
                bestUnit = key;
                bestDuration = duration;
            }
        }

        return bestUnit;
    }

    /// <summary>取拥有指定驱散类型的首个单位。</summary>
    private static string? UnitWithDispelType(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        int dispelType)
    {
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data) || !RoleNotZero(data))
            {
                continue;
            }

            if (TryInt(GetField(data, "驱散"), out var val) && val == dispelType)
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>取拥有任意已解码可驱散类型的首个单位。</summary>
    private static string? UnitWithAnyDispelType(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group)
    {
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data) || !RoleNotZero(data))
            {
                continue;
            }

            if (TryInt(GetField(data, "驱散"), out var val) && val > 0)
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>
    /// 在可用且满足 predicate 的单位里，取治疗吸收 &gt; 阈值的最高单位；
    /// 没有符合阈值的单位时返回 null。
    /// </summary>
    private static string? HighestHealingAbsorb(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        int threshold,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        string? bestUnit = null;
        var highestAbsorb = 0;
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data)
                || !RoleNotZero(data)
                || !predicate(data))
            {
                continue;
            }

            if (TryInt(GetField(data, "治疗吸收"), out var absorb)
                && absorb > 0
                && absorb > threshold
                && absorb > highestAbsorb)
            {
                bestUnit = key;
                highestAbsorb = absorb;
            }
        }

        return bestUnit;
    }

    private static string? HighestHealingDeficit(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        int threshold,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        string? bestUnit = null;
        var highestDeficit = threshold;
        for (var i = 1; i <= 30; i++)
        {
            var key = i.ToString();
            if (!group.TryGetValue(key, out var data)
                || !RoleNotZero(data)
                || !predicate(data)
                || !TryHealingDeficit(data, out var deficit)
                || deficit <= highestDeficit)
            {
                continue;
            }

            bestUnit = key;
            highestDeficit = deficit;
        }

        return bestUnit;
    }

    /// <summary>统计可用且满足 predicate 的单位数量。</summary>
    private static int CountUnits(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group,
        Func<IReadOnlyDictionary<string, object?>, bool> predicate)
    {
        var count = 0;
        for (var i = 1; i <= 30; i++)
        {
            if (group.TryGetValue(i.ToString(), out var data) && RoleNotZero(data) && predicate(data))
            {
                count++;
            }
        }

        return count;
    }

    private static bool BelowThreshold(IReadOnlyDictionary<string, object?> data, int threshold)
    {
        return TryInt(GetField(data, "生命值"), out var pct) && pct > 0 && pct < threshold;
    }

    private static bool AboveHealingAbsorbThreshold(
        IReadOnlyDictionary<string, object?> data,
        int threshold)
    {
        return TryInt(GetField(data, "治疗吸收"), out var absorb)
            && absorb > threshold;
    }

    private static bool TryHealingDeficit(
        IReadOnlyDictionary<string, object?> data,
        out int deficit)
    {
        deficit = 0;
        if (!TryInt(GetField(data, "生命值"), out var health) || health <= 0)
        {
            return false;
        }

        var absorb = TryInt(GetField(data, "治疗吸收"), out var value) ? Math.Max(0, value) : 0;
        deficit = Math.Max(0, 100 - health) + absorb;
        return true;
    }

    private static bool TryHealingLoad(
        IReadOnlyDictionary<string, object?> data,
        out int load)
    {
        if (!TryHealingDeficit(data, out var deficit))
        {
            load = 0;
            return false;
        }

        load = Math.Min(100, deficit);
        return true;
    }

    private static int SumHealingLoad(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group)
    {
        var total = 0;
        for (var i = 1; i <= 30; i++)
        {
            if (group.TryGetValue(i.ToString(), out var data)
                && RoleNotZero(data)
                && TryHealingLoad(data, out var load))
            {
                total += load;
            }
        }

        return total;
    }

    private static int SumHealthDeficit(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group)
    {
        var total = 0;
        for (var i = 1; i <= 30; i++)
        {
            if (group.TryGetValue(i.ToString(), out var data)
                && RoleNotZero(data)
                && TryInt(GetField(data, "生命值"), out var health)
                && health > 0)
            {
                total += Math.Max(0, 100 - health);
            }
        }

        return total;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> ReconcilePlayerHealth(
        GameState state)
    {
        var playerHealth = state.GetInt("生命值");
        var playerForbearance = state.GetInt("自律");
        var playerSlot = ResolvePlayerSlot(state);
        var key = playerSlot.ToString();

        Dictionary<string, IReadOnlyDictionary<string, object?>>? reconciled = null;
        foreach (var (slot, data) in state.Group)
        {
            if (slot == key || !data.ContainsKey("自律"))
            {
                continue;
            }

            reconciled ??= state.Group.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var sanitized = data.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            sanitized.Remove("自律");
            reconciled[slot] = sanitized;
        }

        if (!state.Group.TryGetValue(key, out var playerData))
        {
            return reconciled ?? state.Group;
        }

        var correctedPlayer = (Dictionary<string, object?>?)null;
        if (playerHealth > 0
            && TryInt(GetField(playerData, "生命值"), out var groupHealth))
        {
            var correctedHealth = groupHealth <= 0 ? playerHealth : Math.Min(groupHealth, playerHealth);
            var playerRoleAvailable = !TryInt(GetField(playerData, "职责"), out var playerRole) || playerRole != 0;
            if (correctedHealth != groupHealth || !playerRoleAvailable)
            {
                correctedPlayer = playerData.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
                correctedPlayer["生命值"] = correctedHealth;
                if (!playerRoleAvailable)
                {
                    correctedPlayer["职责"] = 5;
                }
            }
        }

        if (playerData.ContainsKey("自律") || playerForbearance > 0)
        {
            correctedPlayer ??= playerData.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            if (playerForbearance > 0)
            {
                correctedPlayer["自律"] = playerForbearance;
            }
            else
            {
                correctedPlayer.Remove("自律");
            }
        }

        if (correctedPlayer is not null)
        {
            reconciled ??= state.Group.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            reconciled[key] = correctedPlayer;
        }

        return reconciled ?? state.Group;
    }

    private static int ResolveThreshold(
        int? fixedValue,
        string? fieldName,
        GameState state,
        int defaultValue = DefaultThreshold)
    {
        return !string.IsNullOrWhiteSpace(fieldName)
            && ModuleConditionEvaluator.TryResolveInt(state, fieldName, out var dynamicValue)
                ? dynamicValue
                : fixedValue ?? defaultValue;
    }

    private static bool UsesZeroDefaultThreshold(UnitSelectorKind kind)
        => kind is UnitSelectorKind.HighestHealingAbsorb
            or UnitSelectorKind.HighestHealingAbsorbWithAnyAura
            or UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura
            or UnitSelectorKind.HighestHealingAbsorbWithoutAura
            or UnitSelectorKind.HighestHealingAbsorbWithAura
            or UnitSelectorKind.HighestHealingAbsorbWithAuraCount
            or UnitSelectorKind.HighestHealingDeficit;

    private static bool UsesZeroDefaultThreshold(CountKind kind)
        => kind is CountKind.UnitsAboveHealingAbsorb
            or CountKind.UnitsWithoutAuraAboveHealingAbsorb
            or CountKind.UnitsWithAuraAboveHealingAbsorb
            or CountKind.UnitsAboveHealingDeficit
            or CountKind.UnitsAtOrAboveHealingDeficit
            or CountKind.TotalHealingDeficit
            or CountKind.TotalHealthDeficit;

    private static bool AuraEquals(IReadOnlyDictionary<string, object?> data, string auraName, int target)
    {
        return TryInt(GetField(data, auraName), out var val) && val == target;
    }

    private static bool MatchesRoleFilter(
        IReadOnlyDictionary<string, object?> data,
        UnitRoleFilterKind? filter,
        int? role)
    {
        if (filter is null)
        {
            return true;
        }

        if (role is null || !TryInt(GetField(data, "职责"), out var actualRole))
        {
            return false;
        }

        return filter == UnitRoleFilterKind.Include
            ? actualRole == role.Value
            : actualRole != role.Value;
    }

    private static bool RoleNotZero(IReadOnlyDictionary<string, object?> data)
    {
        var role = GetField(data, "职责");
        return role is null || !TryInt(role, out var value) || value != 0;
    }

    private static bool HasAura(IReadOnlyDictionary<string, object?> data, string auraName)
    {
        return TryInt(GetField(data, auraName), out var n) && n != 0;
    }

    private static bool HasAnyAura(IReadOnlyDictionary<string, object?> data, IEnumerable<string> auraNames)
    {
        foreach (var name in auraNames)
        {
            if (HasAura(data, name))
            {
                return true;
            }
        }

        return false;
    }

    private static string? FirstAura(List<string>? auraNames)
    {
        return auraNames is { Count: > 0 } ? auraNames[0] : null;
    }

    private static object? GetField(IReadOnlyDictionary<string, object?> data, string field)
    {
        return data.TryGetValue(field, out var value) ? value : null;
    }

    // 模仿 Python int() 的 try/except: null 或无法解析返回 false, 调用侧据此跳过。
    private static bool TryInt(object? value, out int result)
    {
        switch (value)
        {
            case int i:
                result = i;
                return true;
            case long l:
                result = (int)l;
                return true;
            case bool b:
                result = b ? 1 : 0;
                return true;
            case string s when int.TryParse(s, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }
}
