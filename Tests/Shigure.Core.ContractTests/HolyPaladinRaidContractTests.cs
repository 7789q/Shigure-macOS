using System.Text.Json.Nodes;
using System.Diagnostics;
using Shigure;
using Shigure.Platform;

internal static class HolyPaladinRaidContractTests
{
    public static void Run(string root)
    {
        const string fileName = "holy-paladin-raid-virtue-12.1.json";
        var path = Path.Combine(root, "BundledModules", fileName);
        var raid = ModuleStore.Parse(File.ReadAllBytes(path));
        var dungeonPath = Path.Combine(root, "BundledModules", "holy-paladin-virtue-12.1.json");
        var dungeon = ModuleStore.Parse(File.ReadAllBytes(dungeonPath));
        Check(raid.Id != dungeon.Id && raid.Name != dungeon.Name, "raid module has an independent identity");
        Check(raid.Enabled && raid.Match.ClassId == 2 && raid.Match.SpecId == 1
            && raid.Match.PartyType == "1-40", "raid module matches the existing raid party protocol");
        Check(raid.RecommendedTalent == dungeon.RecommendedTalent, "raid retains the Virtue talent baseline");
        Check(!raid.Rules.Any(rule => rule.Spell == "光环掌握"), "Aura Mastery has no automatic branch");
        Check(raid.Rules.Count(rule => rule.Spell == "黎明之光") == 2, "raid has group-healing Dawn and the high-mana spender fallback");

        // Keep the shared priority chain equal, including NPC routing and the
        // two trusted AOE Virtue entries. Only the agreed raid rules may differ.
        static bool IsShared(JsonNode rule) => rule["Spell"]!.GetValue<string>() is not ("黎明之光" or "光环掌握" or "圣洁鸣钟" or "清洁术")
            && !(rule["Spell"]!.GetValue<string>() == "圣光闪现" && rule["UnitName"]?.GetValue<string>() == "轻伤目标")
            && !(rule["Spell"]!.GetValue<string>() == "美德道标"
                && !rule["Condition"]!.GetValue<string>().StartsWith("AOE事件类型", StringComparison.Ordinal))
            && rule["Condition"]!.GetValue<string>() != "重伤目标 && 重伤目标血量 <= 30";
        static JsonNode NormalizeWindow(JsonNode rule)
        {
            var copy = rule.DeepClone();
            copy["Condition"] = copy["Condition"]!.GetValue<string>()
                .Replace("auras.美德道标", "团本美德窗口", StringComparison.Ordinal)
                .Replace(" && 团本美德预留 == 0", "", StringComparison.Ordinal);
            return copy;
        }
        var raidJson = JsonNode.Parse(File.ReadAllText(path))!;
        var dungeonJson = JsonNode.Parse(File.ReadAllText(dungeonPath))!;
        var sharedRaid = raidJson["Rules"]!.AsArray().OfType<JsonNode>().Where(IsShared).ToArray();
        var sharedDungeon = dungeonJson["Rules"]!.AsArray().OfType<JsonNode>().Where(IsShared).ToArray();
        Check(sharedRaid.Length == sharedDungeon.Length
            && sharedRaid.Zip(sharedDungeon).All(pair => JsonNode.DeepEquals(NormalizeWindow(pair.First), NormalizeWindow(pair.Second))),
            "all other rules retain the dungeon behavior and relative order");
        Check(JsonNode.DeepEquals(raidJson["Units"], dungeonJson["Units"]), "target selectors remain shared");

        var keymap = new KeymapService(root, ConfigService.LoadFromBaseDirectory(root));
        keymap.SelectForClass(2, 1);
        Check(!string.IsNullOrEmpty(keymap.GetHotkey(0, "光环掌握")), "manual Aura Mastery binding remains available");
        LogicDecision Evaluate(GameState state, ModuleDerivedStateTracker? tracker = null)
        {
            ModuleLogic.ResolveDynamicFields(raid, state);
            (tracker ?? new ModuleDerivedStateTracker(TimeProvider.System)).Apply(raid, state, true);
            var result = ModuleLogic.Run(raid, state, keymap);
            Check(!result.UnitInfo.ContainsKey("条件错误"), "raid conditions must parse");
            Check(!result.UnitInfo.ContainsKey("缺少绑定"), "raid actions must use existing bindings");
            return result;
        }
        string Action(GameState state, ModuleDerivedStateTracker? tracker = null) =>
            Evaluate(state, tracker).UnitInfo.GetValueOrDefault("动作技能")?.ToString() ?? "";

        Check(Action(State([100, 85], power: 5, infusion: 8)) == "荣耀圣令",
            "capped Holy Power is spent on mild injuries before Infusion Flash of Light");
        Check(Action(State([100, 85], power: 4, infusion: 8)) == "圣光闪现",
            "below the Holy Power cap mild injuries retain Infusion priority");
        foreach (var power in new[] { 3, 5 })
        {
            Check(Action(State([], power, mana: 81)) == "黎明之光",
                "mana above eighty percent uses Dawn before Shield without requiring raid damage");
            foreach (var mana in new[] { 0, 79, 80 })
            {
                Check(Action(State([], power, mana: mana)) == "正义盾击",
                    "mana at or below eighty percent retains the Shield fallback");
            }
        }
        Check(Action(State([], power: 5, mana: 81, infusion: 4)) == "黎明之光",
            "full Holy Power and expiring Infusion still select the high-mana spender");
        Check(Action(State([], power: 2, mana: 81)) != "黎明之光",
            "high mana cannot bypass Dawn's Holy Power requirement");
        Check(Action(State([], power: 0, freeCast: true, mana: 81)) == "黎明之光",
            "Divine Purpose remains usable for the high-mana Dawn fallback");
        var rangedDawn = State([], power: 5, mana: 81);
        rangedDawn.Values["目标距离"] = 20;
        Check(Action(rangedDawn) == "黎明之光", "Dawn does not inherit Shield's melee range gate");
        var noTargetDawn = State([], power: 5, mana: 81);
        noTargetDawn.Values["目标类型"] = 0;
        noTargetDawn.Values["目标距离"] = 0;
        Check(Action(noTargetDawn) == "黎明之光", "Dawn does not require an enemy target");
        var missingMana = State([], power: 5);
        missingMana.Values.Remove("法力值");
        Check(Action(missingMana) == "正义盾击", "unknown mana cannot enable the high-mana fallback");
        var idleDawn = State([], power: 5, mana: 81);
        idleDawn.Values["战斗时间"] = 0;
        Check(Action(idleDawn) != "黎明之光", "high-mana fallback is combat-only");
        foreach (var stage in new[] { 0, 1, 3, 4 })
        {
            Check(Action(State([], power: 5, stage: stage, mana: 81)) == "黎明之光",
                "high-mana Dawn uses the existing allowed spender stages");
        }
        foreach (var stage in new[] { 2, 5 })
        {
            Check(Action(State([], power: 5, stage: stage, mana: 81)) != "黎明之光",
                "high-mana fallback respects reserved AOE execution windows");
        }
        var reservedDawn = State([90, 90, 90, 90, 90], power: 5, mana: 81);
        ((Dictionary<string, object?>)reservedDawn.Spells)["美德道标"] = 1;
        Check(Action(reservedDawn) != "黎明之光", "high mana does not bypass imminent Virtue resource reservation");

        Check(Action(State([90, 90, 90, 90, 90], virtue: true)) == "黎明之光", "five ten-percent deficits trigger Dawn inside Virtue");
        Check(Action(State([90, 90, 90, 90, 90])) == "黎明之光", "Dawn also leads while Virtue is on cooldown");
        Check(Action(State([80, 80, 80])) == "黎明之光", "three twenty-percent deficits trigger Dawn");
        Check(Action(State([90, 90, 90, 90])) != "黎明之光", "four small deficits are below the raid threshold");
        Check(Action(State([81, 81, 81])) != "黎明之光", "three nineteen-percent deficits are below the raid threshold");
        Check(Action(State([100, 40])) == "荣耀圣令", "one deeply injured player receives single-target healing");
        Check(Action(State([100, 55, 75, 75, 75])) == "黎明之光", "ordinary single-target injury does not starve group healing");
        Check(Action(State([100, 25, 75, 75, 75])) == "荣耀圣令", "critical single-target rescue still precedes Dawn");
        Check(Action(State([100, 25, 75, 75, 75], layOnHandsReady: true)) == "圣疗术", "Lay on Hands still leads emergencies");
        Check(Action(State([80, 80, 80], virtue: true, tollReady: true)) == "黎明之光", "Dawn consumes available Holy Power before Toll");
        Check(Action(State([80, 80, 80], power: 2, virtue: true, tollReady: true)) == "圣洁鸣钟", "Toll fills an active Virtue window when Holy Power is insufficient");
        Check(Action(State([80, 80, 80], power: 0, freeCast: true)) == "黎明之光", "Divine Purpose permits resource-free Dawn");
        Check(Action(State([80, 80, 80], power: 2)) == "神圣震击", "insufficient Holy Power follows the existing resource generation chain");

        Check(Action(State([80, 80, 80], virtueReady: true)) == "美德道标", "strong group pressure with resources opens Virtue first");
        foreach (var power in new[] { 0, 1, 2 })
        {
            Check(Action(State([80, 80, 80], power, virtueReady: true, tollReady: true)) == "美德道标",
                "ready Toll supplies follow-up Holy Power without making Virtue wait");
        }
        Check(Action(State([80, 80, 80], power: 0, freeCast: true, virtue: true, tollReady: true)) == "圣洁鸣钟",
            "low-resource Toll is not starved by repeated free Dawns in the Virtue window");
        Check(Action(State([80, 80, 80], power: 0, virtue: true, tollReady: true)) == "圣洁鸣钟"
            && Action(State([80, 80, 80], power: 5, virtue: true)) == "黎明之光",
            "zero-power Virtue chains Toll's five Holy Power into Dawn");
        var almostReady = State([80, 80, 80]);
        ((Dictionary<string, object?>)almostReady.Spells)["美德道标"] = 1;
        Check(Action(almostReady) is not ("黎明之光" or "荣耀圣令" or "正义盾击"),
            "without Toll the last Holy Power spender cannot empty resources one second before Virtue");
        var readyTollNearVirtue = State([80, 80, 80], tollReady: true);
        ((Dictionary<string, object?>)readyTollNearVirtue.Spells)["美德道标"] = 1;
        Check(Action(readyTollNearVirtue) == "黎明之光", "available Toll removes the need to reserve existing Holy Power");
        var criticalNearVirtue = State([100, 25, 75, 75, 75]);
        ((Dictionary<string, object?>)criticalNearVirtue.Spells)["美德道标"] = 1;
        Check(Action(criticalNearVirtue) == "荣耀圣令", "resource reservation never blocks critical rescue");
        Check(Action(State([80, 80, 80], power: 2, virtueReady: true)) != "美德道标", "blood threshold relaxation preserves the three Holy Power requirement");
        Check(Action(State([80, 80, 80], power: 1, virtueReady: true)) != "美德道标", "reactive Virtue still requires follow-up resources");
        Check(Action(State([80, 80, 80], power: 0, freeCast: true, virtueReady: true)) == "美德道标", "free follow-up healing is enough to open reactive Virtue");
        Check(Action(State([90, 90, 90, 90, 90], virtueReady: true)) == "美德道标", "five ten-percent deficits can now open Virtue");
        Check(Action(State([85, 85, 85], virtueReady: true)) == "美德道标", "three fifteen-percent deficits can now open Virtue");
        Check(Action(State([86, 86, 86], virtueReady: true)) != "美德道标", "three fourteen-percent deficits remain below Virtue threshold");
        Check(Action(State([90, 90, 90, 90], virtueReady: true)) != "美德道标", "four ten-percent deficits remain below Virtue threshold");
        Check(Action(State([50, 50], virtueReady: true)) != "美德道标", "two deeply injured players cannot bypass the unchanged Virtue target count");
        Check(Action(State([95, 95, 95, 95, 95], virtueReady: true)) != "美德道标", "five-target Virtue branch retains its ten-percent deficit threshold");
        Check(Action(State([100, 40], virtueReady: true)) != "美德道标", "single-target damage still cannot open reactive Virtue");
        Check(Action(State([], virtueReady: true)) != "美德道标", "healthy raid cannot open reactive Virtue");

        Check(Action(State([85, 85], power: 2, virtue: true, tollReady: true)) == "圣洁鸣钟", "Toll now accepts two targets and thirty total deficit");
        Check(Action(State([90, 80], power: 2, virtue: true, tollReady: true)) == "圣洁鸣钟", "ten-percent per-target boundary is inclusive for Toll");
        Check(Action(State([86, 85], power: 2, virtue: true, tollReady: true)) != "圣洁鸣钟", "Toll rejects twenty-nine total deficit");
        Check(Action(State([91, 79], power: 2, virtue: true, tollReady: true)) != "圣洁鸣钟", "Toll requires two targets each missing at least ten percent");
        Check(Action(State([100, 40], power: 2, virtue: true, tollReady: true)) != "圣洁鸣钟", "single-target damage does not spend Toll");
        Check(Action(State([85, 85], power: 2, tollReady: true)) != "圣洁鸣钟", "Toll still waits for an active Virtue window");
        Check(Action(State([85, 85], power: 4, virtue: true, tollReady: true)) != "圣洁鸣钟", "Toll retains its Holy Power overflow guard");
        Check(Action(State([85, 85], power: 2, virtue: true)) != "圣洁鸣钟", "Toll respects its own cooldown");
        Check(Action(State([], power: 2, virtue: true, tollReady: true)) != "圣洁鸣钟", "healthy Virtue window does not spend Toll");
        var otherMembersCovered = State([85, 85], power: 2, virtue: true, tollReady: true);
        ((Dictionary<string, object?>)otherMembersCovered.Auras)["美德道标"] = 0;
        Check(Action(otherMembersCovered) == "圣洁鸣钟", "Toll recognizes actual group Virtue coverage even when the player has no aura");
        var coveredWithStaleCooldown = State([80, 80, 80], virtue: true, virtueReady: true);
        ((Dictionary<string, object?>)coveredWithStaleCooldown.Auras)["美德道标"] = 0;
        Check(Action(coveredWithStaleCooldown) != "美德道标", "existing group coverage prevents repeat Virtue when cooldown feedback is temporarily zero");

        var idleDispel = State([]);
        var cleanseRule = raid.Rules.Single(rule => rule.Spell == "清洁术");
        Check(!cleanseRule.Enabled, "raid update preserves the user's disabled automatic Cleanse setting");
        cleanseRule.Enabled = true;
        idleDispel.Values["战斗时间"] = 0;
        ((Dictionary<string, object?>)idleDispel.Group["16"])["驱散"] = 4;
        Check(Action(idleDispel) != "清洁术", "a persistent dispel flag cannot cause raid Cleanse spam out of combat");
        var combatDispel = State([]);
        ((Dictionary<string, object?>)combatDispel.Group["16"])["驱散"] = 1;
        Check(Action(combatDispel) == "清洁术", "actionable dispels remain available during combat");
        cleanseRule.Enabled = false;
        Check(Action(State([], power: 0, virtueReady: true, aoeType: 1, stage: 2)) == "美德道标", "trusted ordinary AOE timing keeps its independent entry");
        Check(Action(State([], power: 0, virtueReady: true, aoeType: 2, stage: 3)) == "美德道标", "trusted absorb timing keeps its independent entry");
        Check(Action(State([80, 80, 80], aoeType: 2, stage: 5)) != "黎明之光", "Dawn respects the last GCD reserve window");

        var transitionTracker = new ModuleDerivedStateTracker(TimeProvider.System);
        Check(Action(State([80, 80, 80]), transitionTracker) == "黎明之光", "group pressure starts Dawn immediately");
        Check(Action(State([100, 60], virtueReady: true), transitionTracker) == "荣耀圣令", "recovery immediately returns to single-target healing without a held group cast");
        Check(Action(State([]), transitionTracker) != "黎明之光", "healthy raid does not consume a held Dawn decision");
        var unavailable = State([80, 80, 80]);
        ((Dictionary<string, object?>)unavailable.Group["3"])["可治疗"] = false;
        Check(Action(unavailable) != "黎明之光", "unhealable members do not satisfy the raid threshold");
        var dead = State([80, 80, 0]);
        Check(Action(dead) != "黎明之光", "dead members do not satisfy the raid threshold");
        var npc = State([80, 80, 80]);
        npc.Values["目标类型"] = 152;
        npc.Values["目标生命值"] = 50;
        var npcDecision = Evaluate(npc);
        Check(npcDecision.UnitInfo.GetValueOrDefault("动作技能")?.ToString() == "荣耀圣令"
            && Convert.ToInt32(npcDecision.UnitInfo["动作单位槽位"]) == ReservedUnit.Target,
            "existing friendly NPC routing remains exclusive");
        foreach (var power in Enumerable.Range(0, 6))
        foreach (var virtue in new[] { false, true })
        {
            Check(Action(State(Enumerable.Repeat(55, 20).ToArray(), power, virtue: virtue)) != "光环掌握",
                "available Aura Mastery remains manual throughout high-pressure states");
        }

        var temporary = Path.Combine(Path.GetTempPath(), $"shigure-raid-module-{Guid.NewGuid():N}");
        try
        {
            var installation = new BundledModuleInstaller().Install(Path.Combine(root, "BundledModules"), temporary);
            Check(installation.Failures.Count == 0 && File.Exists(Path.Combine(temporary, fileName)), "new module is installed by the existing bundle loader");
            var store = new ModuleStore(temporary);
            foreach (var raidSlot in new[] { 1, 20, 40 })
            {
                Check(store.FindSelectedOrBestMatch(null, 2, 1, raidSlot, 0)?.Id == raid.Id, "raid-specific match wins in automatic mode");
            }
            Check(store.FindSelectedOrBestMatch(null, 2, 1, 46, 0)?.Id == dungeon.Id, "party mode keeps the existing dungeon module");
            Check(store.FindSelectedOrBestMatch(null, 2, 1, 0, 0)?.Id == dungeon.Id, "solo mode keeps the existing module");
            Check(store.FindSelectedOrBestMatch(dungeon.Id, 2, 1, 1, 0)?.Id == dungeon.Id, "explicit module selection is respected");
            Check(store.FindSelectedOrBestMatch(raid.Id, 2, 1, 46, 0)?.Id == dungeon.Id, "raid module cannot run on a party protocol");
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }

        var replayInfo = new ProcessStartInfo("/usr/bin/env")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        replayInfo.ArgumentList.Add("luajit");
        replayInfo.ArgumentList.Add(Path.Combine(root, "Tests", "Shigure.Core.ContractTests", "Fixtures", "raid-dispel-filter-replay.lua"));
        replayInfo.ArgumentList.Add(Path.Combine(root, "Fuyutsui", "core", "block.lua"));
        using var replay = Process.Start(replayInfo)!;
        var replayOutput = replay.StandardOutput.ReadToEnd() + replay.StandardError.ReadToEnd();
        replay.WaitForExit();
        Check(replay.ExitCode == 0, replayOutput);

        var healthReplayInfo = new ProcessStartInfo("/usr/bin/env")
        {
            WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        healthReplayInfo.ArgumentList.Add("luajit");
        healthReplayInfo.ArgumentList.Add(Path.Combine(root, "Tests", "Shigure.Core.ContractTests", "Fixtures", "raid-health-refresh-replay.lua"));
        healthReplayInfo.ArgumentList.Add(root);
        using var healthReplay = Process.Start(healthReplayInfo)!;
        var healthOutput = healthReplay.StandardOutput.ReadToEnd() + healthReplay.StandardError.ReadToEnd();
        healthReplay.WaitForExit();
        Check(healthReplay.ExitCode == 0, healthOutput);
        VerifyEmergencyFallback(raid, keymap, inconsistentSelf: false).GetAwaiter().GetResult();
        VerifyEmergencyFallback(raid, keymap, inconsistentSelf: true).GetAwaiter().GetResult();
    }

    private static async Task VerifyEmergencyFallback(ModuleDefinition module, IKeymapResolver keymap, bool inconsistentSelf)
    {
        var folder = Path.Combine(Path.GetTempPath(), $"shigure-raid-fallback-{Guid.NewGuid():N}");
        try
        {
            var store = new ModuleStore(folder);
            store.Save(module);
            var sent = new List<string>();
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var runtime = new ShigureRuntime(
                new AppOptions("A", SendMode.Switch, module.Id, TimeSpan.FromMilliseconds(25), TimeSpan.FromSeconds(5)),
                new ValidRuntimeScanner(), new RaidFallbackStateBuilder(inconsistentSelf), new FakeTargetKeyOutput(),
                new PressedTriggerInput(), new LogicRegistry(keymap, store, module.Id), TimeProvider.System);
            runtime.SnapshotUpdated += snapshot =>
            {
                if (!snapshot.CurrentStep.Contains(": 施放 ", StringComparison.Ordinal)
                    || snapshot.UnitInfo.GetValueOrDefault("发送结果")?.ToString() != "已投递到 WoW 进程") return;
                var spell = snapshot.UnitInfo.GetValueOrDefault("动作技能")?.ToString() ?? "";
                sent.Add(spell);
                if (inconsistentSelf || spell == "圣疗术") cancellation.Cancel();
            };
            try { await runtime.RunAsync(cancellation.Token); }
            catch (OperationCanceledException) { }
            Check(sent.FirstOrDefault() == "荣耀圣令", "an unconfirmed or rejected emergency does not stall the raid's next eligible heal");
            if (!inconsistentSelf)
            {
                Check(sent.Contains("圣疗术"), "evaluating the fallback must not reset the original emergency's consecutive-frame confirmation");
            }
            else
            {
                Check(!sent.Contains("圣疗术"), "fallback never bypasses the independent self-health guard");
            }
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class RaidFallbackStateBuilder(bool inconsistentSelf) : IRuntimeStateBuilder
    {
        public GameState Build(IReadOnlyDictionary<int, int> rowData, IReadOnlyDictionary<int, int> barData,
            IReadOnlyDictionary<int, int>? healAbsorbData = null)
        {
            var state = State(inconsistentSelf ? [100, 80, 80, 80] : [20, 80, 80, 80], layOnHandsReady: true);
            state.Values["队伍类型"] = 20;
            state.Values["生命值"] = 100;
            if (inconsistentSelf) ((Dictionary<string, object?>)state.Group["20"])["生命值"] = 20;
            state.Values["有效性"] = 1;
            state.Values["DiGua桥接就绪"] = true;
            state.Values["宏绑定状态"] = 1;
            state.Values["宏绑定数量"] = 89;
            state.Values["公共冷却剩余"] = 0;
            return state;
        }
    }

    private static GameState State(int[] health, int power = 3, bool virtue = false,
        bool virtueReady = false, bool freeCast = false, bool tollReady = false,
        bool layOnHandsReady = false, int aoeType = 0, int stage = 0, int mana = 80, int infusion = 0)
    {
        var members = new Dictionary<string, IReadOnlyDictionary<string, object?>>();
        var burst = 0;
        var spread = 0;
        for (var index = 0; index < 20; index++)
        {
            var life = index < health.Length ? health[index] : 100;
            var deficit = life > 0 ? 100 - life : 0;
            if (deficit >= 15) burst += deficit;
            if (deficit + 5 >= 15) spread++;
            members[(index + 1).ToString()] = new Dictionary<string, object?>
            {
                ["生命值"] = life, ["职责"] = 5, ["可治疗"] = life > 0,
                ["治疗吸收"] = 0, ["驱散"] = 0, ["自律"] = 0,
                ["预期需求"] = deficit + 5, ["爆发需求"] = deficit, ["持续需求"] = deficit,
                ["美德道标"] = virtue && index < 5 ? 8 : 0
            };
        }
        return new GameState(new Dictionary<string, object?>
        {
            ["职业"] = 2, ["专精"] = 1, ["队伍类型"] = 1,
            ["生命值"] = health.Length > 0 ? health[0] : 100,
            ["法力值"] = mana, ["施法技能"] = 0,
            ["神圣能量"] = power, ["战斗时间"] = 100, ["移动"] = false, ["引导"] = 0,
            ["目标类型"] = 91, ["目标距离"] = 3, ["目标生命值"] = 100, ["目标正面"] = 2,
            ["AOE事件类型"] = aoeType, ["AOE事件阶段"] = stage,
            ["多人爆发需求"] = Math.Min(255, burst), ["多人持续需求"] = Math.Min(255, burst), ["预计治疗人数"] = spread,
            ["美德主目标"] = 1, ["美德覆盖人数"] = virtue ? 5 : 0, ["美德转移需求"] = virtue ? 20 : 0,
            ["spells"] = new Dictionary<string, object?>
            {
                ["圣疗术"] = layOnHandsReady ? 0 : 600, ["圣盾术"] = 300, ["牺牲祝福"] = 120,
                ["美德道标"] = virtueReady ? 0 : 10, ["圣洁鸣钟"] = tollReady ? 0 : 30,
                ["光环掌握"] = 0, ["神圣震击"] = 0, ["神圣震击层数"] = 2,
                ["审判"] = 10, ["荣耀圣令"] = 0, ["黎明之光"] = 0, ["清洁术"] = 0
            },
            ["auras"] = new Dictionary<string, object?>
            {
                ["美德道标"] = virtue ? 8 : 0, ["神圣意志"] = freeCast ? 8 : 0,
                ["圣光灌注"] = infusion, ["圣光灌注层数"] = infusion > 0 ? 1 : 0,
                ["神性之手"] = 0, ["复仇之怒"] = 0, ["圣盾术"] = 0
            },
            ["group"] = members
        });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
