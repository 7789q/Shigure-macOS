using System.Security.Cryptography;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Shigure;
using Shigure.MacDiagnostics;
using Shigure.MacApp;
using Shigure.Platform;
using Shigure.Platform.MacOS;
using Shigure.Presentation;

if (args.Length == 2
    && string.Equals(args[0], "--launcher-bound-command-child", StringComparison.Ordinal))
{
    return RunLauncherBoundCommandChild(args[1]);
}

var tests = new (string Name, Action Run)[]
{
    ("top row boundaries", TopRowBoundaries),
    ("top row requires start marker", TopRowRequiresStartMarker),
    ("count bars markers", CountBarsMarkers),
    ("heal absorb units", HealAbsorbUnits),
    ("unit selector without any aura contract", UnitSelectorWithoutAnyAuraContract),
    ("state builder fixture", StateBuilderFixture),
    ("module match selection", ModuleMatchSelection),
    ("module marketplace install contract", ModuleMarketplaceInstallContract),
    ("module editor persistence contract", ModuleEditorPersistenceContract),
    ("module dependency capture and import contract", ModuleDependencyCaptureAndImportContract),
    ("target identity contract", TargetIdentityContract),
    ("permission status contract", PermissionStatusContract),
    ("mac permission service contract", MacPermissionServiceContract),
    ("trigger input edge contract", TriggerInputEdgeContract),
    ("mac trigger input map", MacTriggerInputMapContract),
    ("mac trigger input lifecycle", MacTriggerInputLifecycleContract),
    ("mac screen capture contract", MacScreenCaptureContract),
    ("region pixel scanner equivalence", RegionPixelScannerEquivalence),
    ("mac runtime factory contract", MacRuntimeFactoryContract),
    ("runtime adaptive scan cadence", RuntimeAdaptiveScanCadenceContract),
    ("runtime toggle snapshot priority", RuntimeToggleSnapshotPriorityContract),
    ("runtime short trigger pulse contract", RuntimeShortTriggerPulseContract),
    ("runtime failure snapshot contract", RuntimeFailureSnapshotContract),
    ("runtime startup failure ownership contract", RuntimeStartupFailureOwnershipContract),
    ("runtime session ownership contract", RuntimeSessionOwnershipContract),
    ("mac application host lifecycle contract", MacApplicationHostLifecycleContract),
    ("mac permission command contract", MacPermissionCommandContract),
    ("mac module import command contract", MacModuleImportCommandContract),
    ("mac launcher parent monitor contract", MacLauncherParentMonitorContract),
    ("mac launcher-bound command contract", MacLauncherBoundCommandContract),
    ("mac single instance contract", MacSingleInstanceContract),
    ("hotkey parser contract", HotkeyParserContract),
    ("mac key output contract", MacKeyOutputContract),
    ("target process config", TargetProcessConfigFixture),
    ("wow addon path contract", WowAddonPathContract),
    ("fuyutsui addon sync contract", FuyutsuiAddonSyncContract),
    ("class blocks editor persistence contract", ClassBlocksEditorPersistenceContract),
    ("class macros editor persistence contract", ClassMacrosEditorPersistenceContract),
    ("project config update contract", ProjectConfigUpdateContract),
    ("fuyutsui global burst mouse contract", FuyutsuiGlobalBurstMouseContract),
    ("fuyutsui UI scale contract", FuyutsuiUiScaleContract),
    ("fuyutsui macro combat retry contract", FuyutsuiMacroCombatRetryContract),
    ("fuyutsui protocol 1.2.1.11 contract", FuyutsuiProtocolContract),
    ("mac user data path contract", MacUserDataPathContract),
    ("mac UI state persistence contract", MacUiStatePersistenceContract),
    ("runtime resource workspace contract", RuntimeResourceWorkspaceContract),
    ("legacy module migration contract", LegacyModuleMigrationContract),
    ("mac diagnostic command contract", MacDiagnosticCommandContract),
    ("ppm frame export contract", PpmFrameExportContract),
    ("mac target selection", MacTargetSelectionFixture),
    ("mac target locator cache", MacTargetLocatorCacheContract),
    ("mac target native smoke", MacTargetNativeSmoke),
    ("workspace presentation contract", WorkspacePresentationContract),
    ("runtime session controller contract", RuntimeSessionControllerContract),
    ("mac UI technical sample contract", MacUiTechnicalSampleContract),
    ("mac packaging release contract", MacPackagingReleaseContract),
    ("mac updater release contract", MacUpdaterReleaseContract),
    ("mac release staging contract", MacReleaseStagingContract),
    ("contract surface manifest", ContractSurfaceManifest)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}");
    }
}

Console.WriteLine($"Contract tests: {tests.Length - failures} passed, {failures} failed");
return failures == 0 ? 0 : 1;

static void TopRowBoundaries()
{
    var pixels = new[]
    {
        Argb(0, 0, 0),
        EncodeStep(1, 11),
        EncodeStep(255, 22),
        EncodeStep(256, 33),
        EncodeStep(510, 44),
        EncodeStep(2, 99)
    };

    var decoded = PixelProtocolDecoder.DecodeTopRow(pixels);
    Equal(1, PixelProtocolDecoder.FindTopRowStart(pixels), "top row start marker index");
    Equal(4, decoded.Count, "decoded field count");
    Equal(11, decoded[1], "step 1");
    Equal(22, decoded[255], "step 255");
    Equal(33, decoded[256], "step 256");
    Equal(44, decoded[510], "step 510");
}

static void TopRowRequiresStartMarker()
{
    var decoded = PixelProtocolDecoder.DecodeTopRow(
        new[] { EncodeStep(2, 10), EncodeStep(255, 20), EncodeStep(510, 30) });

    Equal(-1, PixelProtocolDecoder.FindTopRowStart(
        new[] { EncodeStep(2, 10), EncodeStep(255, 20), EncodeStep(510, 30) }), "missing start marker index");
    Equal(0, decoded.Count, "row without step 1 must be ignored");
}

static void CountBarsMarkers()
{
    var red = Argb(1, 0, 0);
    var redGreen = Argb(1, 1, 0);
    var white = Argb(255, 255, 255);
    var gray = Argb(200, 200, 200);
    var row = new[]
    {
        white,
        white,
        Argb(0, 6, 0),
        red,
        redGreen,
        white,
        Argb(0, 3, 0),
        gray,
        Argb(0, 99, 0)
    };

    var decoded = PixelProtocolDecoder.DecodeCountBars(row);
    Equal(2, decoded.Count, "count bars field count");
    Equal(5, decoded[1], "white segment value");
    Equal(2, decoded[2], "red-green segment value");

    var markerY = PixelProtocolDecoder.FindCountBarsMarkerY(
        new[] { Argb(2, 0, 0), Argb(1, 0, 1), red, gray });
    Equal(2, markerY, "exact red marker row");
}

static void HealAbsorbUnits()
{
    var white = Argb(255, 255, 255);
    var row = new[]
    {
        white,
        white,
        Argb(0, 8, 4),
        Argb(0, 0, 0),
        white,
        Argb(0, 1, 30),
        Argb(0, 0, 0),
        white,
        Argb(0, 9, 31)
    };
    var decoded = new Dictionary<int, int>();

    PixelProtocolDecoder.DecodeHealAbsorbRow(row, decoded);

    Equal(2, decoded.Count, "valid heal absorb units");
    Equal(7, decoded[4], "unit 4 heal absorb");
    Equal(0, decoded[30], "unit 30 lower bound value");
}

static void UnitSelectorWithoutAnyAuraContract()
{
    var group = new Dictionary<string, IReadOnlyDictionary<string, object?>>
    {
        ["1"] = new Dictionary<string, object?>
        {
            ["职责"] = 1,
            ["生命值"] = 20,
            ["治疗吸收"] = 50,
            ["光环甲"] = 1,
            ["光环乙"] = 0
        },
        ["2"] = new Dictionary<string, object?>
        {
            ["职责"] = 1,
            ["生命值"] = 30,
            ["治疗吸收"] = 80,
            ["光环甲"] = 0,
            ["光环乙"] = 0
        },
        ["3"] = new Dictionary<string, object?>
        {
            ["职责"] = 1,
            ["生命值"] = 10,
            ["治疗吸收"] = 60,
            ["光环甲"] = 0,
            ["光环乙"] = 1
        }
    };
    var state = new GameState(new Dictionary<string, object?> { ["group"] = group });
    var withoutAnyAura = new ModuleUnit
    {
        Kind = UnitSelectorKind.LowestHealthWithoutAnyAura,
        AuraNames = ["光环甲", "光环乙"]
    };
    var absorbWithoutAnyAura = new ModuleUnit
    {
        Kind = UnitSelectorKind.HighestHealingAbsorbWithoutAnyAura,
        AuraNames = ["光环甲", "光环乙"]
    };

    Equal("2", UnitSelector.Resolve(withoutAnyAura, state), "lowest health excludes units with any selected aura");
    Equal("2", UnitSelector.Resolve(absorbWithoutAnyAura, state), "highest absorb excludes units with any selected aura");
    withoutAnyAura.AuraNames = [];
    Equal(null, UnitSelector.Resolve(withoutAnyAura, state), "without-any selector requires at least one aura");
}

static void StateBuilderFixture()
{
    var fixturePath = Path.Combine(
        Path.GetTempPath(),
        $"shigure-state-builder-{Guid.NewGuid():N}.json");

    try
    {
        File.WriteAllText(
            fixturePath,
            """
            {
              "锚点": { "step": 1, "type": "int" },
              "职业": { "step": 2, "type": "int" },
              "专精": { "step": 3, "type": "int" },
              "state": {
                "资源": { "step": 4, "type": "int" },
                "开关": { "step": 5, "type": "bool" },
                "标签": { "step": 8, "type": "string" },
                "缺失数字": { "step": 9, "type": "int" },
                "动作条值": { "step": "bar", "bar": 2, "type": "int" }
              },
              "5": {
                "2": {
                  "spells": {
                    "快速治疗": { "step": 6, "type": "int" }
                  },
                  "auras": {
                    "救赎": { "step": 7, "type": "bool" }
                  },
                  "group": {
                    "start": 26,
                    "num": 3,
                    "生命值": { "step": 0, "type": "int" },
                    "职责": { "step": 1, "type": "int" },
                    "动作条状态": { "step": "bar", "bar": 3, "type": "bool" }
                  }
                }
              }
            }
            """);

        var state = new StateBuilder(new ConfigService(fixturePath)).Build(
            new Dictionary<int, int>
            {
                [1] = 233,
                [2] = 5,
                [3] = 2,
                [4] = 87,
                [5] = 0,
                [6] = 14,
                [7] = 1,
                [8] = 123,
                [26] = 80,
                [27] = 2,
                [29] = 30,
                [30] = 1
            },
            new Dictionary<int, int>
            {
                [2] = 19,
                [3] = 1
            },
            new Dictionary<int, int>
            {
                [1] = 12,
                [2] = 50
            });

        Equal(5, state.GetInt("职业"), "class id");
        Equal(2, state.GetInt("state.专精"), "state prefix lookup");
        Equal(87, state.GetInt("资源"), "integer state field");
        Equal(false, state.GetBool("开关", true), "boolean state field");
        Equal("123", state.GetValue("标签") as string, "string state field");
        Equal(0, state.GetInt("缺失数字", -1), "missing configured number defaults to zero");
        Equal(19, state.GetInt("动作条值"), "bar state field");
        Equal(14, Convert.ToInt32(state.Spells["快速治疗"]), "spell field");
        Equal(true, Convert.ToBoolean(state.Auras["救赎"]), "aura field");
        Equal(30, state.Group.Count, "fixed group slot count");
        Equal(68, Convert.ToInt32(state.Group["1"]["生命值"]), "heal absorb health adjustment");
        Equal(12, Convert.ToInt32(state.Group["1"]["治疗吸收"]), "heal absorb field");
        Equal(0, Convert.ToInt32(state.Group["2"]["生命值"]), "heal absorb health floor");
        Equal(1, Convert.ToInt32(state.Group["2"]["职责"]), "relative group step");
        Equal(true, Convert.ToBoolean(state.Group["30"]["动作条状态"]), "group bar field");
    }
    finally
    {
        File.Delete(fixturePath);
    }
}

static void ModuleMarketplaceInstallContract()
{
    const string shareId = "11111111-1111-1111-1111-111111111111";
    const string oversizedId = "22222222-2222-2222-2222-222222222222";
    var moduleJson = """
        {
          "Id": "community-frost",
          "Name": "社区冰法",
          "Author": "第一作者",
          "Version": "1.0",
          "Match": { "ClassId": 8, "SpecId": 3, "PartyType": 46 },
          "Rules": []
        }
        """;
    var listJson = $$"""
        {
          "shares": [
            {
              "id": "{{shareId}}",
              "filename": "社区冰法.json",
              "sharer": "tester",
              "author": "第一作者",
              "version": "1.0",
              "profession": "法师",
              "specialization": "冰霜",
              "description": "测试模块",
              "size": 512,
              "downloadCount": 3,
              "createdAt": "2026-08-20T00:00:00Z"
            },
            {
              "id": "not-a-guid",
              "filename": "无效.json",
              "sharer": "tester",
              "author": "tester",
              "version": "1",
              "profession": "",
              "specialization": "",
              "description": "",
              "size": 10,
              "downloadCount": 0,
              "createdAt": "2026-08-20T00:00:00Z"
            }
          ]
        }
        """;

    var handler = new RouteHttpMessageHandler(request => request.RequestUri?.AbsolutePath switch
    {
        "/api/shares" => JsonResponse(listJson),
        $"/api/shares/{shareId}/download" => JsonResponse(moduleJson),
        $"/api/shares/{oversizedId}/download" => JsonResponse(new string('x', 200 * 1024 + 1)),
        _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
    });
    var client = new ModuleMarketplaceClient(new HttpClient(handler));

    var shares = client.GetSharesAsync().GetAwaiter().GetResult();
    Equal(1, shares.Count, "invalid marketplace share id filtered");
    Equal("社区冰法.json", shares[0].Filename, "marketplace filename");
    Throws<ArgumentException>(
        () => client.DownloadAsync("../unsafe").GetAwaiter().GetResult(),
        "marketplace rejects invalid download id");
    Throws<InvalidDataException>(
        () => client.DownloadAsync(oversizedId).GetAwaiter().GetResult(),
        "marketplace rejects oversized module");

    var fixtureRoot = Path.Combine(Path.GetTempPath(), $"shigure-marketplace-{Guid.NewGuid():N}");
    try
    {
        var store = new ModuleStore(Path.Combine(fixtureRoot, "module"));
        var downloaded = client.DownloadAsync(shareId).GetAwaiter().GetResult();
        Equal("46", downloaded.Match.PartyType, "downloaded numeric party type normalized");
        var installed = store.Install(downloaded);
        Equal(true, File.Exists(installed.FilePath), "downloaded module installed");

        moduleJson = """
            {
              "Id": "community-frost-v2",
              "Name": "社区冰法",
              "Author": "第二作者",
              "Version": "2.0",
              "Match": { "ClassId": 8, "SpecId": 3, "PartyType": "46" },
              "Rules": []
            }
            """;
        var replacement = client.DownloadAsync(shareId).GetAwaiter().GetResult();
        Throws<InvalidOperationException>(
            () => store.Install(replacement),
            "same-name marketplace install requires confirmation");
        Equal("第一作者", store.GetModules().Single().Author, "rejected replacement preserves local module");

        var replaced = store.Install(replacement, replaceExisting: true);
        Equal(1, store.GetModules().Count, "confirmed replacement keeps one module");
        Equal("第二作者", store.GetModules().Single().Author, "confirmed replacement updates module");
        Equal(installed.FilePath, replaced.FilePath, "confirmed replacement reuses local path");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static HttpResponseMessage JsonResponse(string json) => new(System.Net.HttpStatusCode.OK)
{
    Content = new StringContent(json, Encoding.UTF8, "application/json")
};

static void ModuleEditorPersistenceContract()
{
    var fixtureRoot = Path.Combine(Path.GetTempPath(), $"shigure-module-editor-{Guid.NewGuid():N}");
    try
    {
        var store = new ModuleStore(Path.Combine(fixtureRoot, "module"));
        var module = ModuleDefinition.CreateDefault("编辑器测试");
        module.Author = "测试作者";
        module.RecommendedTalent = "测试天赋";
        module.Version = "1.2.3.4";
        module.Enabled = false;
        module.Match = new ModuleMatch { ClassId = 8, SpecId = 64, PartyType = "团队", HeroTalent = 31 };
        module.Units =
        [
            new ModuleUnit
            {
                Name = "最低生命",
                HealthName = "最低生命值",
                Kind = UnitSelectorKind.LowestHealthWithAnyAura,
                HealthThreshold = 70,
                RoleFilter = UnitRoleFilterKind.Exclude,
                Role = 1,
                Reverse = true,
                AuraNames = ["光环甲", "光环乙"],
                AuraCount = 2,
                DispelType = 4
            }
        ];
        module.Counts =
        [
            new ModuleCountField
            {
                Name = "低血量人数",
                Kind = CountKind.UnitsWithoutAuraBelowHealth,
                HealthThresholdField = "治疗阈值",
                AuraName = "光环甲"
            }
        ];
        module.ValueAdjustments =
        [
            new ModuleValueAdjustment
            {
                Enabled = true,
                Field = "治疗阈值",
                Condition = "战斗中 == true",
                Delta = -5,
                Formula = string.Empty
            }
        ];
        module.Rules =
        [
            new ModuleRule
            {
                Enabled = true,
                Comment = "优先治疗规则",
                Spell = "治疗术",
                UnitName = "最低生命",
                MacroCondition = MacroConditionText.ParseDisplayText("引导中"),
                Condition = "低血量人数 >= 2",
                SubConditions = ["法力值 > 20", "爆发开关 == 1"],
                DelayMs = 120,
                LogicDelayMs = 80
            },
            new ModuleRule
            {
                Enabled = false,
                Spell = "无目标动作",
                Unit = ReservedUnit.Target,
                Condition = "目标存在 == true"
            }
        ];
        module.Dependencies = new ModuleDependencySnapshot
        {
            ClassId = 8,
            SpecId = 64,
            Config = new ModuleConfigSnapshot
            {
                Spec = new ModuleSpecSnapshot { FlatStates = ["战斗中"] }
            },
            Macros = new ModuleMacrosSnapshot { DynamicCommon = ["治疗术"] }
        };

        var saved = store.Save(module);
        var originalPath = saved.FilePath!;
        Equal(true, File.Exists(originalPath), "module editor creates file");

        store.Reload();
        var loaded = store.GetModules().Single();
        Equal(false, loaded.Enabled, "module enabled state round trips");
        Equal("1-40", loaded.Match.PartyType, "module party type normalized");
        Equal(UnitSelectorKind.LowestHealthWithAnyAura, loaded.Units.Single().Kind, "module unit kind round trips");
        Equal("光环甲,光环乙", string.Join(',', loaded.Units.Single().AuraNames!), "module aura list round trips");
        Equal(CountKind.UnitsWithoutAuraBelowHealth, loaded.Counts.Single().Kind, "module count kind round trips");
        Equal(-5, loaded.ValueAdjustments.Single().Delta, "module adjustment round trips");
        Equal("优先治疗规则", loaded.Rules[0].Comment, "module rule comment round trips");
        Equal("channeling", loaded.Rules[0].MacroCondition, "shared macro condition parser");
        Equal(ReservedUnit.Target, loaded.Rules[1].Unit, "shared reserved unit parser");
        Equal(2, loaded.Rules[0].SubConditions?.Count, "module subconditions round trip");
        Equal(1, loaded.Dependencies?.Config.Spec.FlatStates.Count, "module dependency snapshot preserved");

        loaded.Name = "编辑器重命名";
        loaded.Rules.Reverse();
        var renamed = store.Save(loaded);
        Equal(false, File.Exists(originalPath), "module rename removes old file");
        Equal(true, File.Exists(renamed.FilePath), "module rename creates new file");
        Equal("无目标动作", store.GetModules().Single().Rules[0].Spell, "module rule order persists");

        var copy = renamed.Clone();
        copy.Id = ModuleStore.CreateModuleId("编辑器副本");
        copy.Name = "编辑器副本";
        copy.FilePath = null;
        var copied = store.Save(copy);
        Equal(2, store.GetModules().Count, "module copy creates independent file");

        store.Delete(copied);
        store.Delete(renamed);
        Equal(0, store.GetModules().Count, "module delete updates store");
        Equal(0, Directory.EnumerateFiles(store.ModuleDirectory, "*.json").Count(), "module delete removes files");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void ModuleDependencyCaptureAndImportContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var fixtureRoot = Path.Combine(Path.GetTempPath(), $"shigure-module-dependencies-{Guid.NewGuid():N}");
    try
    {
        var classDirectory = Path.Combine(fixtureRoot, "Fuyutsui", "class");
        var coreDirectory = Path.Combine(fixtureRoot, "Fuyutsui", "core");
        Directory.CreateDirectory(classDirectory);
        Directory.CreateDirectory(coreDirectory);
        var classPath = Path.Combine(classDirectory, "Mage.lua");
        var macrosPath = Path.Combine(coreDirectory, "classmacros.lua");
        File.Copy(Path.Combine(repositoryRoot, "Fuyutsui", "class", "Mage.lua"), classPath);
        File.Copy(Path.Combine(repositoryRoot, "Fuyutsui", "core", "classmacros.lua"), macrosPath);

        var service = new ModuleDependencyService(fixtureRoot);
        var module = ModuleDefinition.CreateDefault("依赖合同模块");
        module.Match.ClassId = 8;
        module.Match.SpecId = 1;

        Equal(null, service.Capture(module), "dependency capture succeeds for class and spec");
        Equal(8, module.Dependencies?.ClassId, "dependency capture stores class");
        Equal(1, module.Dependencies?.SpecId, "dependency capture stores spec");
        Equal(true, module.Dependencies?.Config.Spec.CategorizedStates.Count > 0, "dependency capture stores config");
        Equal(true, module.Dependencies?.Macros.StaticSpells.Count > 0, "dependency capture stores macros");

        var configDocument = ClassBlocksStore.Load(classPath);
        var stateCategory = configDocument.Specs[1].CategorizedStates
            .First(entry => entry.Value.Count > 0);
        var removedState = stateCategory.Value[0];
        stateCategory.Value.RemoveAt(0);
        var auraWithId = configDocument.Specs[1].PlayerAuras.First(aura => aura.SpellId is > 0);
        configDocument.Specs[1].PlayerAuras.Add(new ClassBlocksStore.AuraEntry
        {
            Name = "同 SpellId 的旧名称",
            SpellId = auraWithId.SpellId
        });
        ClassBlocksStore.Save(configDocument);

        var macrosDocument = ClassMacrosStore.Load(macrosPath);
        var mageMacros = macrosDocument.Classes[ClassMacrosStore.ToClassFileKey(8)];
        var removedMacro = mageMacros.DynamicCommon[0];
        mageMacros.DynamicCommon.RemoveAt(0);
        ClassMacrosStore.Save(macrosDocument);

        var imported = service.Import([module]);
        Equal(true, imported.ConfigAdded > 0, "dependency import restores missing config");
        Equal(true, imported.ConfigUpdated > 0, "dependency import compacts same-spellId config entries");
        Equal(true, imported.MacrosAdded > 0, "dependency import restores missing macro");
        Equal("依赖合同模块", imported.ChangedModules.Single(), "dependency import reports changed module");
        Equal(
            true,
            ClassBlocksStore.Load(classPath).Specs[1].CategorizedStates[stateCategory.Key].Contains(removedState),
            "dependency import persists config");
        Equal(
            1,
            ClassBlocksStore.Load(classPath).Specs[1].PlayerAuras.Count(aura => aura.SpellId == auraWithId.SpellId),
            "dependency import identifies auras by spellId instead of display name");
        Equal(
            true,
            ClassMacrosStore.Load(macrosPath).Classes[ClassMacrosStore.ToClassFileKey(8)].DynamicCommon.Contains(removedMacro),
            "dependency import persists macro");

        var secondImport = service.Import([module]);
        Equal(false, secondImport.HasChanges, "dependency import is idempotent");

        var conflicting = module.Clone();
        conflicting.Id = "dependency-conflict";
        conflicting.Name = "依赖冲突模块";
        var incomingAura = conflicting.Dependencies!.Config.Spec.PlayerAuras.First();
        incomingAura.SpellId = (incomingAura.SpellId ?? 0) + 1;

        var invalid = module.Clone();
        invalid.Id = "dependency-invalid";
        invalid.Name = "依赖无效模块";
        invalid.Dependencies!.SchemaVersion = ModuleDependencySnapshot.CurrentSchemaVersion + 1;

        var guarded = service.Import([conflicting, invalid]);
        Equal(true, guarded.HasChanges, "different spellId is imported even when the display name matches");
        Equal(true, guarded.ConfigAdded > 0, "spellId identity allows same-name distinct auras");
        Equal(false, guarded.Conflicts.Any(item => item.Contains("依赖冲突模块", StringComparison.Ordinal)),
            "same-name distinct spellIds are not reported as a conflict");
        Equal("dependency-invalid", guarded.Rejected.Single().ModuleId, "dependency import rejects invalid schema per module");

        var unmatched = ModuleDefinition.CreateDefault("无匹配模块");
        unmatched.Dependencies = module.Dependencies?.Clone();
        var warning = service.Capture(unmatched);
        Equal(true, !string.IsNullOrWhiteSpace(warning), "dependency capture warns without class and spec");
        Equal(null, unmatched.Dependencies, "dependency capture clears stale snapshot without class and spec");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void ModuleMatchSelection()
{
    Equal("1-40", ModuleMatch.NormalizePartyTypeValue("团队"), "raid party normalization");
    Equal("1-40", ModuleMatch.NormalizePartyTypeValue("5"), "party size normalization");
    Equal("1-40", ModuleMatch.NormalizePartyTypeValue("40-1"), "reversed range normalization");
    Equal("46", ModuleMatch.NormalizePartyTypeValue("队伍"), "party normalization");
    Equal(
        false,
        JsonSerializer.Serialize(new ModuleMatch { ClassId = 5 }).Contains("Specificity", StringComparison.Ordinal),
        "computed specificity is not persisted");

    var candidates = new[]
    {
        new MatchCandidate("fallback", "Fallback", new ModuleMatch()),
        new MatchCandidate("class", "Class", new ModuleMatch { ClassId = 5 }),
        new MatchCandidate("beta", "Beta", new ModuleMatch { ClassId = 5, SpecId = 2 }),
        new MatchCandidate("alpha", "Alpha", new ModuleMatch { ClassId = 5, SpecId = 2 }),
        new MatchCandidate(
            "raid",
            "Raid",
            new ModuleMatch { ClassId = 5, SpecId = 2, PartyType = "团队" }),
        new MatchCandidate("other", "Other", new ModuleMatch { ClassId = 8, SpecId = 2 })
    };

    MatchCandidate? Select(string? selectedId, int partyType) =>
        ModuleMatchSelector.FindSelectedOrBestMatch(
            candidates,
            selectedId,
            candidate => candidate.Id,
            candidate => candidate.Name,
            candidate => candidate.Match.Specificity,
            candidate => candidate.Match.Matches(5, 2, partyType, null));

    Equal("raid", Select(null, 3)?.Id, "highest specificity match");
    Equal("alpha", Select("ALPHA", 3)?.Id, "selected matching id wins case-insensitively");
    Equal("raid", Select("other", 3)?.Id, "selected non-match falls back to best match");
    Equal("alpha", Select(null, 46)?.Id, "name breaks equal-specificity tie");
}

static void TargetIdentityContract()
{
    var identity = new TargetIdentity(TargetPlatforms.Windows, 100, 200);
    var moved = new TargetWindow(identity, "C:/Game/Wow.exe", new TargetBounds(50, 60, 1920, 1080));
    var resized = new TargetWindow(identity, "C:/Game/Wow.exe", new TargetBounds(80, 90, 2560, 1440));

    Equal(true, identity.IsValid, "valid target identity");
    Equal(moved.Identity, resized.Identity, "bounds do not change target identity");
    Equal(
        false,
        identity == new TargetIdentity(TargetPlatforms.Windows, 101, 200),
        "process change changes identity");
    Equal(
        false,
        identity == new TargetIdentity(TargetPlatforms.Windows, 100, 201),
        "window change changes identity");
    Equal(
        false,
        identity == new TargetIdentity(TargetPlatforms.MacOS, 100, 200),
        "platform change changes identity");
    Equal(false, default(TargetIdentity).IsValid, "default target identity is invalid");
    Equal(false, new TargetBounds(0, 0, 0, 100).IsValid, "zero width bounds are invalid");
}

static void PermissionStatusContract()
{
    var initiallyGranted = new PlatformPermissionSession(screenCaptureGrantedAtStartup: true);
    var ready = initiallyGranted.Assess(screenCaptureGranted: true, accessibilityGranted: true);
    Equal(true, ready.IsReady, "both startup permissions are ready");
    Equal(false, ready.ScreenCapture.RestartRequired, "startup screen permission needs no restart");

    var initiallyMissing = new PlatformPermissionSession(screenCaptureGrantedAtStartup: false);
    var missing = initiallyMissing.Assess(screenCaptureGranted: false, accessibilityGranted: false);
    Equal(false, missing.IsReady, "missing permissions are not ready");
    Equal(
        PlatformPermissionRequestOutcome.UserActionRequired,
        PlatformPermissionSession.ClassifyRequest(false, missing.ScreenCapture),
        "first or denied screen request needs user action");

    var grantedDuringSession = initiallyMissing.Assess(screenCaptureGranted: true, accessibilityGranted: true);
    Equal(true, grantedDuringSession.ScreenCapture.RestartRequired, "new screen grant requires restart");
    Equal(false, grantedDuringSession.ScreenCapture.IsReady, "new screen grant is not ready before restart");
    Equal(
        PlatformPermissionRequestOutcome.RestartRequired,
        PlatformPermissionSession.ClassifyRequest(false, grantedDuringSession.ScreenCapture),
        "new screen grant reports restart");
    Equal(
        PlatformPermissionRequestOutcome.Granted,
        PlatformPermissionSession.ClassifyRequest(false, grantedDuringSession.Accessibility),
        "new accessibility grant is immediately ready");
    Equal(
        PlatformPermissionRequestOutcome.AlreadyGranted,
        PlatformPermissionSession.ClassifyRequest(true, ready.Accessibility),
        "existing grant is stable");
}

static void MacPermissionServiceContract()
{
    var nativeApi = new FakeMacPermissionNativeApi();
    var service = new MacPermissionService(nativeApi);

    var initial = service.Check();
    Equal(false, initial.IsReady, "initial fake permissions are missing");
    Equal(0, nativeApi.ScreenRequestCount, "check does not request screen permission");
    Equal(0, nativeApi.AccessibilityRequestCount, "check does not request accessibility permission");

    var screenResult = service.Request(PlatformPermissionKind.ScreenCapture);
    Equal(1, nativeApi.ScreenRequestCount, "explicit screen request reaches native API");
    Equal(PlatformPermissionRequestOutcome.RestartRequired, screenResult.Outcome, "screen grant needs restart");

    var accessibilityResult = service.Request(PlatformPermissionKind.Accessibility);
    Equal(1, nativeApi.AccessibilityRequestCount, "explicit accessibility request reaches native API");
    Equal(PlatformPermissionRequestOutcome.Granted, accessibilityResult.Outcome, "accessibility grant is ready");

    var repeated = service.Request(PlatformPermissionKind.Accessibility);
    Equal(1, nativeApi.AccessibilityRequestCount, "granted permission is not requested again");
    Equal(PlatformPermissionRequestOutcome.AlreadyGranted, repeated.Outcome, "granted permission reports existing grant");

    var deniedNativeApi = new FakeMacPermissionNativeApi(grantScreenOnRequest: false);
    var deniedResult = new MacPermissionService(deniedNativeApi)
        .Request(PlatformPermissionKind.ScreenCapture);
    Equal(1, deniedNativeApi.ScreenRequestCount, "denied screen request reaches native API once");
    Equal(
        PlatformPermissionRequestOutcome.UserActionRequired,
        deniedResult.Outcome,
        "denied screen request keeps deterministic user action feedback");

    var alreadyGrantedNativeApi = new FakeMacPermissionNativeApi(
        screenCaptureGranted: true,
        accessibilityGranted: true);
    var alreadyGrantedService = new MacPermissionService(alreadyGrantedNativeApi);
    var alreadyGrantedResult = alreadyGrantedService.Request(PlatformPermissionKind.ScreenCapture);
    Equal(0, alreadyGrantedNativeApi.ScreenRequestCount, "startup grant does not call request API");
    Equal(
        PlatformPermissionRequestOutcome.AlreadyGranted,
        alreadyGrantedResult.Outcome,
        "startup grant reports already granted");
}

static void TriggerInputEdgeContract()
{
    var tracker = new TriggerInputEdgeTracker();
    Equal(new TriggerInputEdges(false, false, false), tracker.ObserveState(false), "idle state has no edge");
    Equal(new TriggerInputEdges(true, true, false), tracker.ObserveState(true), "press has one rising edge");
    Equal(new TriggerInputEdges(true, false, false), tracker.ObserveState(true), "held state does not repeat rising");
    Equal(new TriggerInputEdges(false, false, true), tracker.ObserveState(false), "release has one falling edge");
    Equal(new TriggerInputEdges(false, false, false), tracker.ObserveState(false), "released state does not repeat falling");
    Equal(new TriggerInputEdges(false, true, false), TriggerInputEdgeTracker.ObservePulse(true), "pulse is one rising edge");
    Equal(new TriggerInputEdges(false, false, false), TriggerInputEdgeTracker.ObservePulse(false), "missing pulse has no edge");

    Equal(false, TriggerModePolicy.IsSingleShot(SendMode.Switch, isPulseTrigger: false), "switch key is persistent");
    Equal(false, TriggerModePolicy.IsSingleShot(SendMode.Switch, isPulseTrigger: true), "switch pulse toggles state");
    Equal(true, TriggerModePolicy.IsSingleShot(SendMode.Click, isPulseTrigger: false), "click key is single shot");
    Equal(true, TriggerModePolicy.IsSingleShot(SendMode.Click, isPulseTrigger: true), "click pulse is single shot");
    Equal(false, TriggerModePolicy.IsSingleShot(SendMode.Hold, isPulseTrigger: false), "hold key follows pressed state");
    Equal(true, TriggerModePolicy.IsSingleShot(SendMode.Hold, isPulseTrigger: true), "hold pulse is one round");
}

static void MacTriggerInputMapContract()
{
    Equal(
        new TriggerInputBinding(TriggerInputKind.Keyboard, 0),
        MacTriggerInputMap.Resolve("A"),
        "mac ANSI key mapping");
    Equal(
        new TriggerInputBinding(TriggerInputKind.Keyboard, 111),
        MacTriggerInputMap.Resolve("f12"),
        "mac function key mapping");
    Equal(
        new TriggerInputBinding(TriggerInputKind.MouseButton, 4),
        MacTriggerInputMap.Resolve("XBUTTON2"),
        "mac mouse side button mapping");
    Equal(
        new TriggerInputBinding(TriggerInputKind.MouseButton, 2),
        MacTriggerInputMap.Resolve("MIDDLE"),
        "mac middle mouse button mapping");
    Equal(
        new TriggerInputBinding(TriggerInputKind.Pulse, MacTriggerInputMap.WheelUpCode),
        MacTriggerInputMap.Resolve("WHEELUP"),
        "mac wheel-up pulse mapping");
    Equal(
        new TriggerInputBinding(TriggerInputKind.Pulse, MacTriggerInputMap.WheelDownCode),
        MacTriggerInputMap.Resolve("鼠标中键下滚"),
        "mac wheel-down pulse alias");
    Equal(null, MacTriggerInputMap.Resolve("ALT"), "unsupported mac alt trigger");
    Equal(null, MacTriggerInputMap.Resolve("unknown"), "unknown mac trigger");

    var counter = new WheelPulseCounter(gestureGapTicks: 100);
    counter.RecordWheelUp(900);
    counter.RecordWheelDown(1_000);
    counter.RecordWheelDown(1_050);
    counter.RecordWheelDown(1_150);
    Equal(true, counter.ConsumePulse(MacTriggerInputMap.WheelUpCode), "wheel-up pulse is independent");
    Equal(true, counter.ConsumePulse(MacTriggerInputMap.WheelDownCode), "first wheel-down gesture pulse");
    Equal(true, counter.ConsumePulse(MacTriggerInputMap.WheelDownCode), "second wheel-down gesture pulse after gap");
    Equal(false, counter.ConsumePulse(MacTriggerInputMap.WheelDownCode), "wheel pulses are consumed once");

    var keyPulse = new TriggerInputBinding(TriggerInputKind.Keyboard, 0);
    var latch = new TriggerPulseLatch();
    latch.Record(keyPulse);
    latch.Record(keyPulse);
    Equal(true, latch.Consume(keyPulse), "key press pulse is consumed once");
    Equal(false, latch.Consume(keyPulse), "pending key press pulses are coalesced");

    var triggerSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Platforms",
        "Shigure.Platform.Mac",
        "MacTriggerInput.cs"));
    Equal(true, triggerSource.Contains("EventKeyDown", StringComparison.Ordinal),
        "trigger tap listens for keyboard presses");
    Equal(true, triggerSource.Contains("EventOtherMouseDown", StringComparison.Ordinal),
        "trigger tap listens for other mouse presses");
    Equal(true, triggerSource.Contains("EventScrollWheel", StringComparison.Ordinal),
        "trigger tap retains wheel events");
    Equal(true, triggerSource.Contains("KeyboardEventAutorepeat", StringComparison.Ordinal),
        "trigger tap filters keyboard autorepeat");
}

static void MacTriggerInputLifecycleContract()
{
    var stateApi = new FakeMacTriggerStateApi();
    var sources = new List<FakeMacTriggerPulseSource>();
    IMacTriggerPulseSource CreateSource()
    {
        var source = new FakeMacTriggerPulseSource();
        sources.Add(source);
        return source;
    }

    var input = new MacTriggerInput(stateApi, CreateSource);
    var key = input.Resolve("A") ?? throw new InvalidOperationException("mac key was not resolved");
    stateApi.PressedKeyCode = checked((ushort)key.Code);
    Equal(true, input.IsPressed(key), "mac keyboard state uses mapped key code");
    Equal(1, sources.Count, "keyboard trigger lazily creates one event tap");
    sources[0].PressPulses.Add(key);
    Equal(true, input.ConsumePulse(key), "keyboard press pulse is consumed");
    Equal(false, input.ConsumePulse(key), "keyboard press pulse does not repeat");

    var mouse = input.Resolve("MOUSE4") ?? throw new InvalidOperationException("mac mouse button was not resolved");
    stateApi.PressedMouseButton = checked((uint)mouse.Code);
    Equal(true, input.IsPressed(mouse), "mac mouse state uses mapped button");
    Equal(1, sources.Count, "mouse trigger reuses the event tap");
    sources[0].PressPulses.Add(mouse);
    Equal(true, input.ConsumePulse(mouse), "mouse press pulse is consumed");

    var wheel = input.Resolve("WHEELDOWN") ?? throw new InvalidOperationException("mac wheel was not resolved");
    Equal(1, sources.Count, "wheel trigger reuses the event tap");
    Equal(wheel, input.Resolve("MOUSEWHEELDOWN"), "wheel aliases reuse binding");
    Equal(1, sources.Count, "wheel alias does not create another event tap");
    sources[0].Pulses = 1;
    Equal(true, input.ConsumePulse(wheel), "wheel pulse is consumed");
    Equal(false, input.ConsumePulse(wheel), "wheel pulse does not repeat");

    var wheelUp = input.Resolve("WHEELUP") ?? throw new InvalidOperationException("mac wheel-up was not resolved");
    sources[0].UpPulses = 1;
    Equal(true, input.ConsumePulse(wheelUp), "wheel-up pulse is consumed independently");

    input.Dispose();
    input.Dispose();
    Equal(1, sources[0].DisposeCount, "event tap is disposed once");
    Equal(false, input.ConsumePulse(wheel), "disposed input does not expose pulses");
    Equal(null, input.Resolve("A"), "disposed input does not resolve triggers");

    using var rebuilt = new MacTriggerInput(stateApi, CreateSource);
    Equal(wheel, rebuilt.Resolve("WHEELDOWN"), "new input rebuilds wheel trigger");
    Equal(2, sources.Count, "new input owns a new event tap");
}

static void HotkeyParserContract()
{
    var binding = HotkeyParser.Parse("CONTROL-MENU-CTRL-SHIFT-F12")
        ?? throw new InvalidOperationException("hotkey was not parsed");

    Equal("F12", binding.MainKey, "hotkey main key");
    Equal(
        "Control,Alt,Shift",
        string.Join(',', binding.Modifiers),
        "hotkey modifier aliases and deduplication");
    Equal(null, HotkeyParser.Parse("   "), "blank hotkey is rejected");
}

static void MacScreenCaptureContract()
{
    var captureSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Platforms",
        "Shigure.Platform.Mac",
        "MacScreenCapturer.cs"));
    var scannerSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Runtime",
        "RegionPixelScanner.cs"));
    var nativeStreamSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Packaging",
        "macOS",
        "ShigureCapture.swift"));
    Equal(true, captureSource.Contains("this(permissionService, new MacCoreGraphicsCaptureBackend())", StringComparison.Ordinal), "default target capture uses the proven Core Graphics path");
    Equal(true, nativeStreamSource.Contains("SCStream", StringComparison.Ordinal), "native capture uses ScreenCaptureKit streaming");
    Equal(true, nativeStreamSource.Contains("configuration.sourceRect = localRect", StringComparison.Ordinal), "native stream is cropped to the protocol band");
    Equal(true, nativeStreamSource.Contains("y: y - displayBounds.origin.y", StringComparison.Ordinal), "native stream keeps ScreenCaptureKit's top-origin display coordinate");
    Equal(false, nativeStreamSource.Contains("displayBounds.height - topOffset - height", StringComparison.Ordinal), "native stream does not invert the global Y coordinate");
    Equal(true, nativeStreamSource.Contains("content.windows.first(where: { $0.windowID == windowID })", StringComparison.Ordinal), "native stream resolves the exact target window");
    Equal(true, nativeStreamSource.Contains("SCContentFilter(display: display, including: [window])", StringComparison.Ordinal), "native stream excludes windows that cover the target");
    Equal(true, nativeStreamSource.Contains("onScreenWindowsOnly: false", StringComparison.Ordinal), "native stream keeps an occluded target available");
    Equal(true, nativeStreamSource.Contains("configuration.queueDepth = 2", StringComparison.Ordinal), "native stream bounds queued surfaces");
    Equal(true, nativeStreamSource.Contains("SCFrameStatus(rawValue: statusValue) == .complete", StringComparison.Ordinal), "native stream ignores incomplete frames");
    Equal(true, captureSource.Contains("CGDisplayCreateImageForRect", StringComparison.Ordinal), "target capture uses a real display subregion");
    Equal(true, captureSource.Contains("CGWindowListCreateImage", StringComparison.Ordinal), "target capture retains the compatibility fallback");
    Equal(false, scannerSource.Contains("physicalPixels.ToArray()", StringComparison.Ordinal), "protocol scanner does not copy the captured band");
    Equal(true, scannerSource.Contains("protocolPixels = frame.ArgbPixels", StringComparison.Ordinal), "protocol scanner reuses captured read-only memory");

    var region = new TargetBounds(-1600, 120, 2, 1);
    var bytes = new byte[]
    {
        3, 2, 1, 255, 30, 20, 10, 128, 0, 0, 0, 0, 255, 255, 255, 255, 99, 99, 99, 99,
        0, 0, 1, 255, 0, 1, 0, 255, 1, 0, 0, 255, 51, 34, 17, 255, 88, 88, 88, 88
    };
    var backend = new FakeMacScreenCaptureBackend(new MacNativeFrame(4, 2, 20, bytes));
    var permissions = new FakePlatformPermissionService(
        accessibilityReady: true,
        screenCaptureReady: true);
    var result = new MacScreenCapturer(permissions, backend).Capture(region);

    Equal(true, result.Succeeded, "mac region capture succeeds");
    Equal(region, backend.LastRegion, "global secondary-display coordinates are preserved");
    Equal(1, backend.CaptureCount, "one backend region capture");
    Equal(2.0, result.Frame!.ScaleX, "retina horizontal scale");
    Equal(2.0, result.Frame.ScaleY, "retina vertical scale");
    Equal(CapturedPixelFormat.Argb32, result.Frame.PixelFormat, "captured pixel format");
    Equal(CapturedColorSpace.Srgb, result.Frame.ColorSpace, "captured color space");
    Equal(
        "FF010203,800A141E,00000000,FFFFFFFF,FF010000,FF000100,FF000001,FF112233",
        string.Join(',', result.Frame.ArgbPixels.ToArray().Select(pixel => $"{unchecked((uint)pixel):X8}")),
        "bgra stride is compacted into exact argb pixels");

    var targetBackend = new FakeMacScreenCaptureBackend(new MacNativeFrame(4, 2, 20, bytes));
    var targetIdentity = new TargetIdentity(TargetPlatforms.MacOS, 77, 7310);
    var targetResult = new MacScreenCapturer(permissions, targetBackend).Capture(targetIdentity, region);
    Equal(true, targetResult.Succeeded, "mac target-window capture succeeds");
    Equal((uint)7310, targetBackend.LastWindowId, "target window id reaches the native backend");

    var invalidTarget = new MacScreenCapturer(permissions, targetBackend).Capture(
        new TargetIdentity(TargetPlatforms.Windows, 77, 7310),
        region);
    Equal(ScreenCaptureFailureKind.InvalidRegion, invalidTarget.FailureKind, "non-mac target is rejected");

    var fractional = new MacScreenCapturer(
        permissions,
        new FakeMacScreenCaptureBackend(new MacNativeFrame(3, 1, 12, new byte[12]))).Capture(region);
    Equal(1.5, fractional.Frame!.ScaleX, "non-integer horizontal scale");
    Equal(1.0, fractional.Frame.ScaleY, "independent vertical scale");

    var invalidPermissions = new FakePlatformPermissionService(
        accessibilityReady: true,
        screenCaptureReady: true);
    var invalidBackend = new FakeMacScreenCaptureBackend(null);
    var invalid = new MacScreenCapturer(invalidPermissions, invalidBackend).Capture(
        new TargetBounds(0, 0, 0, 1));
    Equal(ScreenCaptureFailureKind.InvalidRegion, invalid.FailureKind, "invalid region failure kind");
    Equal(0, invalidPermissions.CheckCount, "invalid region does not check permission");
    Equal(0, invalidBackend.CaptureCount, "invalid region does not call backend");

    var deniedBackend = new FakeMacScreenCaptureBackend(null);
    var denied = new MacScreenCapturer(
        new FakePlatformPermissionService(accessibilityReady: true, screenCaptureReady: false),
        deniedBackend).Capture(region);
    Equal(ScreenCaptureFailureKind.PermissionDenied, denied.FailureKind, "screen permission failure kind");
    Equal(0, deniedBackend.CaptureCount, "permission failure does not capture");

    var unavailable = new MacScreenCapturer(
        permissions,
        new FakeMacScreenCaptureBackend(null)).Capture(region);
    Equal(ScreenCaptureFailureKind.CaptureUnavailable, unavailable.FailureKind, "missing image failure kind");

    var malformed = new MacScreenCapturer(
        permissions,
        new FakeMacScreenCaptureBackend(new MacNativeFrame(2, 1, 7, new byte[8]))).Capture(region);
    Equal(ScreenCaptureFailureKind.InvalidPixelBuffer, malformed.FailureKind, "invalid stride failure kind");

    var nativeApi = new FakeMacScreenCaptureNativeApi
    {
        PixelWidth = 2,
        PixelHeight = 1,
        DrawBytes = [3, 2, 1, 255, 30, 20, 10, 255]
    };
    var nativeFrame = new MacCoreGraphicsCaptureBackend(nativeApi).Capture(region);
    Equal(true, nativeFrame is not null, "native backend returns frame");
    Equal(region, nativeApi.LastRegion, "native backend keeps global coordinates");
    Equal(null, nativeApi.LastWindowId, "plain region capture has no target window id");
    Equal(null, nativeFrame!.BgraBytes, "production capture avoids an intermediate BGRA byte buffer");
    Equal(true, nativeFrame.PackedArgbPixels is not null, "production capture returns packed ARGB pixels");

    Equal(true, BgraPixelConverter.TryConvert(nativeFrame, out var nativePixels), "packed native pixels are accepted");
    Equal(true, ReferenceEquals(nativeFrame.PackedArgbPixels, nativePixels), "packed native pixels are reused without copying");
    Equal(
        "FF010203,FF0A141E",
        string.Join(',', nativePixels.Select(pixel => $"{unchecked((uint)pixel):X8}")),
        "native BGRA memory layout is exposed as exact ARGB values");

    Equal(
        "context:33,color:22,image:11",
        string.Join(',', nativeApi.Releases),
        "native capture resources are released in dependency order");

    var streamApi = new FakeMacStreamCaptureApi(4, 2, 20, bytes);
    var compatibilityBackend = new FakeMacScreenCaptureBackend(null);
    using (var streamBackend = new MacStreamCaptureBackend(streamApi, compatibilityBackend))
    {
        var streamFrame = streamBackend.Capture(region, 7310);
        Equal(true, streamFrame is not null, "persistent stream returns its latest frame");
        Equal(region, streamApi.LastRegion, "persistent stream uses the exact protocol band");
        Equal((uint)7310, streamApi.LastWindowId, "persistent stream filters the exact target window");
        Equal(1, streamApi.StartCount, "persistent stream starts once");
        _ = streamBackend.Capture(region, 7310);
        Equal(1, streamApi.StartCount, "unchanged protocol band reuses the stream");
        _ = streamBackend.Capture(region, 7311);
        Equal(2, streamApi.StartCount, "changed target window restarts the stream");
        Equal((uint)7311, streamApi.LastWindowId, "restarted stream follows the new target window");
        _ = streamBackend.Capture(region);
        Equal(1, compatibilityBackend.CaptureCount, "untargeted diagnostics retain compatibility capture");
    }
    Equal(1, streamApi.DestroyCount, "persistent stream is released with the runtime");

    var failedStreamApi = new FakeMacStreamCaptureApi(4, 2, 20, bytes) { StartResult = -1 };
    var fallbackBackend = new FakeMacScreenCaptureBackend(new MacNativeFrame(4, 2, 20, bytes));
    using (var failedStreamBackend = new MacStreamCaptureBackend(failedStreamApi, fallbackBackend))
    {
        Equal(true, failedStreamBackend.Capture(region, 7310) is not null,
            "stream startup failure falls back to compatibility capture");
        Equal(1, fallbackBackend.CaptureCount, "stream startup failure captures once through fallback");
    }

    var targetNativeApi = new FakeMacScreenCaptureNativeApi();
    _ = new MacCoreGraphicsCaptureBackend(targetNativeApi).Capture(region, 7310);
    Equal((uint)7310, targetNativeApi.LastWindowId, "native backend forwards the target window id");

    var contextFailureApi = new FakeMacScreenCaptureNativeApi { Context = 0 };
    var contextFailure = new MacCoreGraphicsCaptureBackend(contextFailureApi).Capture(region);
    Equal(null, contextFailure, "bitmap context failure returns no frame");
    Equal(
        "color:22,image:11",
        string.Join(',', contextFailureApi.Releases),
        "context failure releases color space and image");
}

static void RegionPixelScannerEquivalence()
{
    const int width = 520;
    const int height = 20;
    const int markerY = 1;
    var logicalPixels = Enumerable.Repeat(Argb(0, 0, 0), width * height).ToArray();

    logicalPixels[0] = EncodeStep(1, 11);
    logicalPixels[1] = EncodeStep(255, 22);
    logicalPixels[2] = EncodeStep(256, 33);
    logicalPixels[3] = EncodeStep(510, 44);

    var markerOffset = markerY * width;
    logicalPixels[markerOffset] = Argb(1, 0, 0);
    logicalPixels[markerOffset + 1] = Argb(1, 1, 0);
    logicalPixels[markerOffset + 2] = Argb(255, 255, 255);
    logicalPixels[markerOffset + 3] = Argb(0, 6, 0);
    logicalPixels[markerOffset + 4] = Argb(255, 255, 255);
    logicalPixels[markerOffset + 5] = Argb(0, 4, 0);
    logicalPixels[markerOffset + 6] = Argb(200, 200, 200);

    logicalPixels[(markerY + 1) * width] = Argb(255, 255, 255);
    logicalPixels[(markerY + 1) * width + 1] = Argb(0, 11, 1);
    logicalPixels[(markerY + 6) * width] = Argb(255, 255, 255);
    logicalPixels[(markerY + 6) * width + 1] = Argb(0, 21, 30);

    var bounds = new TargetBounds(-1440, 80, width, height);
    var windowsTarget = new TargetWindow(
        new TargetIdentity(TargetPlatforms.Windows, 55, 7001),
        "C:\\World of Warcraft\\Wow.exe",
        bounds);
    var macTarget = new TargetWindow(
        new TargetIdentity(TargetPlatforms.MacOS, 77, 9001),
        "/Applications/World of Warcraft.app",
        bounds);
    var windowsFrame = new FakeScaledRegionCapturer(bounds, logicalPixels, 1, 1);
    var macFrame = new FakeScaledRegionCapturer(bounds, logicalPixels, 1.5, 2)
    {
        FlipVertically = true
    };

    var windowsResult = new RegionPixelScanner(
        new FakeTargetWindowLocator(windowsTarget),
        windowsFrame).ScanScreenData();
    var macLocator = new FakeTargetWindowLocator(macTarget);
    var macResult = new RegionPixelScanner(macLocator, macFrame).ScanScreenData();

    Equal(
        DictionaryText(windowsResult.RowData!),
        DictionaryText(macResult.RowData!),
        "physical protocol pixels are equivalent at 1x and mixed display scale");
    Equal("1:11,255:22,256:33,510:44", DictionaryText(macResult.RowData!), "top row values");
    Equal(DictionaryText(windowsResult.BarData), DictionaryText(macResult.BarData), "count bars are equivalent");
    Equal("1:5,2:3", DictionaryText(macResult.BarData), "count bars values");
    Equal(
        DictionaryText(windowsResult.HealAbsorbData),
        DictionaryText(macResult.HealAbsorbData),
        "heal absorb rows are equivalent");
    Equal("1:10,30:20", DictionaryText(macResult.HealAbsorbData), "first and sixth heal rows");
    Equal(macTarget, macResult.Target, "scan result keeps target identity and bounds");
    Equal(null, macResult.FailureReason, "complete frame has no scan warning");
    Equal(1, macFrame.Regions.Count, "scanner captures one narrow protocol band");
    Equal(1, macFrame.Targets.Count, "scanner uses the target-aware capture path for the protocol band");
    Equal(
        true,
        macFrame.Targets.All(target => target == macTarget.Identity),
        "protocol band capture keeps the located target identity");
    Equal(new TargetBounds(-1440, 80, width, 15), macFrame.Regions[0], "protocol band region follows addon row heights");

    var missingMarkerPixels = logicalPixels.ToArray();
    Array.Fill(missingMarkerPixels, Argb(0, 0, 0), markerOffset, width);
    var missingMarkerResult = new RegionPixelScanner(
        macLocator,
        new FakeScaledRegionCapturer(bounds, missingMarkerPixels, 1, 1)).ScanScreenData();
    Equal(
        true,
        missingMarkerResult.FailureReason?.Contains(
            "状态字段 4，CountBars 0，治疗吸收 0",
            StringComparison.Ordinal) == true,
        "scan warning includes decoded field counts");

    var failedCapture = new FakeScaledRegionCapturer(bounds, logicalPixels, 1.5, 2)
    {
        FailCaptureAt = 1
    };
    var failedResult = new RegionPixelScanner(macLocator, failedCapture).ScanScreenData();
    Equal(null, failedResult.RowData, "partial capture does not publish partial row state");
    Equal(macTarget, failedResult.Target, "capture failure keeps target for diagnosis");
    Equal(
        true,
        failedResult.FailureReason?.StartsWith("顶部协议窄带捕获失败:", StringComparison.Ordinal) == true,
        "capture failure identifies the protocol band");

    var downscaledResult = new RegionPixelScanner(
        macLocator,
        new FakeScaledRegionCapturer(bounds, logicalPixels, 0.5, 1)).ScanScreenData();
    Equal(null, downscaledResult.RowData, "lossy sub-1x frame is rejected");
    Equal(
        true,
        downscaledResult.FailureReason?.Contains("捕获帧", StringComparison.Ordinal) == true,
        "lossy frame reports an invalid capture frame");

    var missingCapturer = new FakeScaledRegionCapturer(bounds, logicalPixels, 1, 1);
    var missingResult = new RegionPixelScanner(
        new FakeTargetWindowLocator(null),
        missingCapturer).ScanScreenData();
    Equal(0, missingCapturer.Regions.Count, "missing target does not capture");
    Equal(
        true,
        missingResult.FailureReason?.Contains("wow_process.txt", StringComparison.Ordinal) == true,
        "missing target reports configured process source");
}

static void MacRuntimeFactoryContract()
{
    var factorySource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Platforms",
        "Shigure.Platform.Mac",
        "MacRuntimeFactory.cs"));
    Equal(true, factorySource.Contains("new MacCoreGraphicsCaptureBackend()", StringComparison.Ordinal),
        "production runtime preserves physical protocol pixels with narrow-band capture");
    Equal(false, factorySource.Contains("new MacStreamCaptureBackend()", StringComparison.Ordinal),
        "production runtime does not use ScreenCaptureKit's downsampled stream");

    var stateBuilders = new List<FakeRuntimeStateBuilder>();
    var logics = new List<FakeRuntimeLogic>();
    var locators = new List<FakeTargetWindowLocator>();
    var permissions = new List<FakePlatformPermissionService>();
    var capturers = new List<AlwaysFailRegionCapturer>();
    var outputs = new List<FakeTargetKeyOutput>();
    var triggers = new List<TrackingTriggerInput>();

    var factory = new MacRuntimeFactory(
        () => Add(stateBuilders, new FakeRuntimeStateBuilder()),
        _ => Add(logics, new FakeRuntimeLogic()),
        () => Add(locators, new FakeTargetWindowLocator(null)),
        () => Add(permissions, new FakePlatformPermissionService(accessibilityReady: true)),
        permissionService =>
        {
            Equal(permissions[^1], permissionService, "capturer shares the session permission service");
            return Add(capturers, new AlwaysFailRegionCapturer());
        },
        (targetLocator, permissionService) =>
        {
            Equal(locators[^1], targetLocator, "key output shares the session target locator");
            Equal(permissions[^1], permissionService, "key output shares the session permission service");
            return Add(outputs, new FakeTargetKeyOutput());
        },
        () => Add(triggers, new TrackingTriggerInput()),
        TimeProvider.System);

    var options = new AppOptions(
        "A",
        SendMode.Switch,
        null,
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100));
    var first = factory.Create(options);
    var second = factory.Create(options);

    Equal(2, stateBuilders.Count, "state builder is created per session");
    Equal(2, logics.Count, "logic is created per session");
    Equal(2, locators.Count, "target locator is created per session");
    Equal(2, permissions.Count, "permission session is created per runtime session");
    Equal(2, capturers.Count, "screen capturer is created per session");
    Equal(2, outputs.Count, "key output is created per session");
    Equal(2, triggers.Count, "trigger input is created per session");

    first.Dispose();
    first.Dispose();
    second.Dispose();
    second.Dispose();
    Equal("1,1", string.Join(',', triggers.Select(trigger => trigger.DisposeCount)), "runtime disposal is idempotent");
}

static void RuntimeToggleSnapshotPriorityContract()
{
    AssertTogglePublishedBeforeBlockingScan(
        new PressedTriggerInput(),
        "toggle state is published before a blocking scan");
}

static void RuntimeShortTriggerPulseContract()
{
    AssertTogglePublishedBeforeBlockingScan(
        new PulseOnlyTriggerInput(),
        "short trigger pulse is published before a blocking scan");
}

static void AssertTogglePublishedBeforeBlockingScan(ITriggerInput trigger, string message)
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var scanner = new BlockingRuntimeScanner();
    var snapshots = new System.Collections.Concurrent.ConcurrentQueue<RenderSnapshot>();
    var runtime = new ShigureRuntime(
        new AppOptions(
            "A",
            SendMode.Switch,
            null,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(5)),
        scanner,
        new FakeRuntimeStateBuilder(),
        new FakeTargetKeyOutput(),
        trigger,
        new FakeRuntimeLogic(),
        TimeProvider.System);
    runtime.SnapshotUpdated += snapshots.Enqueue;

    var runTask = Task.Run(() => runtime.RunAsync(cancellation.Token));
    try
    {
        if (!scanner.Entered.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("runtime scanner did not block");
        }

        Equal(true, snapshots.Any(snapshot => snapshot.Enabled), message);
    }
    finally
    {
        cancellation.Cancel();
        scanner.Release.Set();
        try
        {
            runTask.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
        }
    }
}

static void RuntimeAdaptiveScanCadenceContract()
{
    var configured = TimeSpan.FromMilliseconds(100);
    Equal(
        configured,
        RuntimeScanCadence.Resolve(configured, enabled: true, scanUnavailable: false),
        "active valid scans keep the configured cadence");
    Equal(
        TimeSpan.FromMilliseconds(200),
        RuntimeScanCadence.Resolve(configured, enabled: false, scanUnavailable: false),
        "disabled scans use the idle cadence");
    Equal(
        TimeSpan.FromMilliseconds(500),
        RuntimeScanCadence.Resolve(configured, enabled: true, scanUnavailable: true),
        "unavailable active scans back off");
    Equal(
        TimeSpan.FromMilliseconds(500),
        RuntimeScanCadence.Resolve(configured, enabled: false, scanUnavailable: true),
        "unavailable idle scans use the failure cadence");
    Equal(
        TimeSpan.FromMilliseconds(750),
        RuntimeScanCadence.Resolve(TimeSpan.FromMilliseconds(750), enabled: true, scanUnavailable: true),
        "adaptive cadence never runs faster than the configured interval");
}

static void RuntimeSessionOwnershipContract()
{
    RuntimeSessionOwnershipContractAsync().GetAwaiter().GetResult();
}

static void MacApplicationHostLifecycleContract()
{
    MacApplicationHostLifecycleContractAsync().GetAwaiter().GetResult();
}

static void RuntimeStartupFailureOwnershipContract()
{
    var trigger = new TrackingTriggerInput();
    var runtime = new ShigureRuntime(
        new AppOptions(
            "A",
            SendMode.Switch,
            null,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100)),
        new EmptyRuntimeScanner(),
        new FakeRuntimeStateBuilder(),
        new FakeTargetKeyOutput(),
        trigger,
        new FakeRuntimeLogic(),
        TimeProvider.System);
    runtime.SnapshotUpdated += _ => throw new InvalidOperationException("simulated subscriber failure");

    Throws<InvalidOperationException>(
        () => runtime.RunAsync().GetAwaiter().GetResult(),
        "startup snapshot failure is propagated");
    Equal(1, trigger.DisposeCount, "startup snapshot failure releases trigger input");
}

static void RuntimeFailureSnapshotContract()
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var trigger = new TrackingTriggerInput();
    var runtime = new ShigureRuntime(
        new AppOptions(
            "A",
            SendMode.Switch,
            null,
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100)),
        new EmptyRuntimeScanner(),
        new FakeRuntimeStateBuilder(),
        new FakeTargetKeyOutput(),
        trigger,
        new FakeRuntimeLogic(),
        TimeProvider.System);
    RenderSnapshot? failureSnapshot = null;
    runtime.SnapshotUpdated += snapshot =>
    {
        if (snapshot.ScanFailureReason is not null)
        {
            failureSnapshot = snapshot;
            cancellation.Cancel();
        }
    };

    Throws<OperationCanceledException>(
        () => runtime.RunAsync(cancellation.Token).GetAwaiter().GetResult(),
        "cancellation after failure snapshot is propagated");
    Equal("simulated empty scan", failureSnapshot?.ScanFailureReason, "scan failure reaches snapshot");
    Equal(1, trigger.DisposeCount, "failure snapshot session releases trigger input");
}

static async Task RuntimeSessionOwnershipContractAsync()
{
    var factory = new BlockingRuntimeFactory();
    await using var coordinator = new RuntimeSessionCoordinator(factory);
    var options = new AppOptions(
        "A",
        SendMode.Switch,
        null,
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100));

    var staleStart = Task.Run(() => coordinator.StartAsync(options, requestVersion: 1));
    if (!factory.FirstCreateEntered.Wait(TimeSpan.FromSeconds(5)))
    {
        throw new TimeoutException("first runtime factory call did not block");
    }

    var currentStart = coordinator.StartAsync(options, requestVersion: 2);
    factory.ReleaseFirstCreate.Set();
    await staleStart;
    await currentStart;

    Equal(2, factory.Triggers.Count, "stale request is replaced by a new session");
    Equal(1, factory.Triggers[0].DisposeCount, "never-started stale runtime releases trigger input");
    Equal(0, factory.Triggers[1].DisposeCount, "current runtime remains owned by coordinator");
    Equal(true, coordinator.HasSession, "replacement session is installed");

    await coordinator.RestartAsync(options, requestVersion: 3);
    Equal(3, factory.Triggers.Count, "restart creates a new runtime session");
    Equal(1, factory.Triggers[1].DisposeCount, "restart releases the previous session");
    Equal(0, factory.Triggers[2].DisposeCount, "restarted session remains owned by coordinator");

    await coordinator.StopAsync();
    Equal(1, factory.Triggers[2].DisposeCount, "stopped session releases trigger input");
    Equal(false, coordinator.HasSession, "stop clears current session");
}

static async Task MacApplicationHostLifecycleContractAsync()
{
    var factory = new HostRuntimeFactory();
    var coordinator = new RuntimeSessionCoordinator(factory);
    await using var host = new MacApplicationHost(coordinator);
    var events = new System.Collections.Concurrent.ConcurrentQueue<MacApplicationEvent>();
    host.EventEmitted += events.Enqueue;
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var options = new AppOptions(
        "A",
        SendMode.Switch,
        null,
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100));

    var runTask = host.RunAsync(options, cancellation.Token);
    if (!SpinWait.SpinUntil(
            () => events.Any(item => item.Stage == "runtime-started"),
            TimeSpan.FromSeconds(5)))
    {
        throw new TimeoutException("Mac application host did not start a runtime session");
    }

    cancellation.Cancel();
    await runTask;

    Equal(1, factory.Triggers.Count, "host creates one runtime session");
    Equal(1, factory.Triggers[0].DisposeCount, "host cancellation releases trigger input once");
    Equal(false, coordinator.HasSession, "host cancellation clears coordinator session");
    Equal(true, events.Any(item => item.Stage == "host-stopping"), "host emits stopping event");
    Equal(true, events.Any(item => item.Stage == "host-stopped"), "host emits stopped event");
}

static void MacSingleInstanceContract()
{
    var name = $"com.arasaka.shigure.contract.{Guid.NewGuid():N}";
    using var first = SingleInstanceLease.TryAcquire(name)
        ?? throw new InvalidOperationException("first single-instance lease was not acquired");
    using var duplicate = SingleInstanceLease.TryAcquire(name);
    Equal(null, duplicate, "duplicate single-instance lease is rejected");

    first.Dispose();
    using var reacquired = SingleInstanceLease.TryAcquire(name);
    Equal(true, reacquired is not null, "single-instance lease can be reacquired after disposal");
    Throws<ArgumentException>(
        () => SingleInstanceLease.TryAcquire(" "),
        "blank single-instance name is rejected");
}

static void MacLauncherParentMonitorContract()
{
    Equal(
        null,
        MacLauncherParentMonitor.FromEnvironment(
            _ => null,
            () => 42,
            TimeSpan.FromMilliseconds(1)),
        "standalone MacApp does not monitor an unspecified launcher");
    Throws<InvalidOperationException>(
        () => MacLauncherParentMonitor.FromEnvironment(
            _ => "invalid",
            () => 42,
            TimeSpan.FromMilliseconds(1)),
        "invalid launcher PID is rejected");

    var parentChecks = 0;
    var monitor = MacLauncherParentMonitor.FromEnvironment(
        _ => "42",
        () => Interlocked.Increment(ref parentChecks) < 2 ? 42 : 1,
        TimeSpan.FromMilliseconds(1))
        ?? throw new InvalidOperationException("configured parent monitor was not created");
    monitor.WaitForParentExitAsync(CancellationToken.None).GetAwaiter().GetResult();
    Equal(true, parentChecks >= 2, "parent PID change completes the monitor");

    using var cancellation = new CancellationTokenSource();
    var canceledMonitor = MacLauncherParentMonitor.FromEnvironment(
        _ => "42",
        () => 42,
        TimeSpan.FromMilliseconds(1))
        ?? throw new InvalidOperationException("cancelable parent monitor was not created");
    cancellation.Cancel();
    try
    {
        canceledMonitor.WaitForParentExitAsync(cancellation.Token).GetAwaiter().GetResult();
        throw new InvalidOperationException("canceled parent monitor completed successfully");
    }
    catch (OperationCanceledException)
    {
    }

    var launcherSource = File.ReadAllText(Path.Combine(
        FindRepositoryRoot(),
        "Packaging",
        "macOS",
        "ShigureLauncher.swift"));
    Equal(
        true,
        launcherSource.Contains(
            MacLauncherParentMonitor.ParentProcessIdEnvironmentVariable,
            StringComparison.Ordinal),
        "Swift launcher and MacApp use the same parent PID environment variable");
}

static void MacLauncherBoundCommandContract()
{
    var standaloneCalls = 0;
    var standaloneResult = MacLauncherBoundCommand.RunAsync(
        () =>
        {
            standaloneCalls++;
            return MacModuleImportCommand.SkippedExitCode;
        },
        monitor: null,
        _ => { }).GetAwaiter().GetResult();
    Equal(MacModuleImportCommand.SkippedExitCode, standaloneResult, "standalone command keeps its exit code");
    Equal(1, standaloneCalls, "standalone command runs exactly once");

    var commandFirstMonitor = MacLauncherParentMonitor.FromEnvironment(
        _ => "42",
        () => 42,
        TimeSpan.FromMilliseconds(1))
        ?? throw new InvalidOperationException("command-first monitor was not created");
    var commandFirstEvents = new List<MacApplicationEvent>();
    var commandFirstResult = MacLauncherBoundCommand.RunAsync(
        () => MacPermissionCommand.RestartRequiredExitCode,
        commandFirstMonitor,
        commandFirstEvents.Add).GetAwaiter().GetResult();
    Equal(MacPermissionCommand.RestartRequiredExitCode, commandFirstResult, "completed command keeps its exit code");
    Equal(0, commandFirstEvents.Count, "completed command does not report parent loss");

    using var commandStarted = new ManualResetEventSlim(false);
    using var releaseCommand = new ManualResetEventSlim(false);
    var parentFirstMonitor = MacLauncherParentMonitor.FromEnvironment(
        _ => "42",
        () => commandStarted.IsSet ? 1 : 42,
        TimeSpan.FromMilliseconds(1))
        ?? throw new InvalidOperationException("parent-first monitor was not created");
    var parentFirstEvents = new List<MacApplicationEvent>();
    try
    {
        var parentFirstResult = MacLauncherBoundCommand.RunAsync(
            () =>
            {
                commandStarted.Set();
                releaseCommand.Wait();
                return MacPermissionCommand.ReadyExitCode;
            },
            parentFirstMonitor,
            parentFirstEvents.Add).GetAwaiter().GetResult();
        Equal(
            MacLauncherBoundCommand.LauncherUnavailableExitCode,
            parentFirstResult,
            "parent loss ends the launcher-bound command");
        Equal("launcher-parent-exited", parentFirstEvents.Single().Stage, "parent loss event");
    }
    finally
    {
        releaseCommand.Set();
    }

    using var failedCommandStarted = new ManualResetEventSlim(false);
    using var releaseFailedCommand = new ManualResetEventSlim(false);
    var failedMonitor = MacLauncherParentMonitor.FromEnvironment(
        _ => "42",
        () => failedCommandStarted.IsSet
            ? throw new InvalidOperationException("monitor failure")
            : 42,
        TimeSpan.FromMilliseconds(1))
        ?? throw new InvalidOperationException("failed monitor was not created");
    var failedMonitorEvents = new List<MacApplicationEvent>();
    try
    {
        var failedMonitorResult = MacLauncherBoundCommand.RunAsync(
            () =>
            {
                failedCommandStarted.Set();
                releaseFailedCommand.Wait();
                return MacPermissionCommand.ReadyExitCode;
            },
            failedMonitor,
            failedMonitorEvents.Add).GetAwaiter().GetResult();
        Equal(
            MacLauncherBoundCommand.LauncherUnavailableExitCode,
            failedMonitorResult,
            "monitor failure ends the launcher-bound command");
        Equal("launcher-parent-monitor-failed", failedMonitorEvents.Single().Stage, "monitor failure event");
    }
    finally
    {
        releaseFailedCommand.Set();
    }

    if (OperatingSystem.IsMacOS())
    {
        MacLauncherBoundCommandProcessContract();
    }
}

static void MacLauncherBoundCommandProcessContract()
{
    var fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        $"shigure-launcher-bound-{Guid.NewGuid():N}");
    var readyPath = Path.Combine(fixtureRoot, "child-ready.txt");
    var executablePath = Path.Combine(AppContext.BaseDirectory, "Shigure.Core.ContractTests");
    Directory.CreateDirectory(fixtureRoot);
    Process? launcher = null;
    var childProcessId = 0;

    try
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Contract test apphost was not found.", executablePath);
        }

        var startInfo = new ProcessStartInfo("/bin/zsh")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(
            "export SHIGURE_LAUNCHER_PID=$$; \"$1\" --launcher-bound-command-child \"$2\" & child_pid=$!; wait \"$child_pid\"");
        startInfo.ArgumentList.Add("shigure-launcher-bound-test");
        startInfo.ArgumentList.Add(executablePath);
        startInfo.ArgumentList.Add(readyPath);
        launcher = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the launcher test process.");

        if (!SpinWait.SpinUntil(
                () => File.Exists(readyPath)
                    && int.TryParse(File.ReadAllText(readyPath), out childProcessId),
                TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Launcher-bound child did not become ready.");
        }

        Equal(true, IsProcessRunning(childProcessId), "launcher-bound child starts under the test launcher");
        launcher.Kill();
        launcher.WaitForExit();
        Equal(
            true,
            SpinWait.SpinUntil(() => !IsProcessRunning(childProcessId), TimeSpan.FromSeconds(5)),
            "launcher-bound child exits after its launcher is killed");
    }
    finally
    {
        if (launcher is { HasExited: false })
        {
            launcher.Kill();
            launcher.WaitForExit();
        }

        launcher?.Dispose();
        if (childProcessId > 0 && IsProcessRunning(childProcessId))
        {
            using var child = Process.GetProcessById(childProcessId);
            child.Kill();
            child.WaitForExit();
        }

        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static int RunLauncherBoundCommandChild(string readyPath)
{
    var monitor = MacLauncherParentMonitor.FromEnvironment()
        ?? throw new InvalidOperationException("Launcher PID is required for the child contract.");
    return MacLauncherBoundCommand.RunAsync(
        () =>
        {
            File.WriteAllText(readyPath, Environment.ProcessId.ToString());
            Thread.Sleep(Timeout.Infinite);
            return MacLauncherBoundCommand.LauncherUnavailableExitCode;
        },
        monitor,
        _ => { }).GetAwaiter().GetResult();
}

static bool IsProcessRunning(int processId)
{
    try
    {
        using var process = Process.GetProcessById(processId);
        return !process.HasExited;
    }
    catch (ArgumentException)
    {
        return false;
    }
}

static void MacPermissionCommandContract()
{
    Equal(false, MacPermissionCommand.IsCommand([]), "empty arguments are not a permission command");
    Equal(
        false,
        MacPermissionCommand.IsCommand(["--toggle", "A"]),
        "runtime arguments are not a permission command");

    var invalidService = new RecordingPermissionService(
        PlatformPermissionRequestOutcome.Granted);
    var invalidEvents = new List<MacApplicationEvent>();
    var invalidExit = MacPermissionCommand.Execute(
        ["permission", "request", "unknown"],
        invalidService,
        invalidEvents.Add);
    Equal(MacPermissionCommand.InvalidArgumentsExitCode, invalidExit, "invalid permission exit code");
    Equal(0, invalidService.RequestCount, "invalid permission does not call platform request");
    Equal("permission-command-rejected", invalidEvents.Single().Stage, "invalid permission event");

    var screenService = new RecordingPermissionService(
        PlatformPermissionRequestOutcome.RestartRequired);
    var screenEvents = new List<MacApplicationEvent>();
    var screenExit = MacPermissionCommand.Execute(
        ["permission", "request", "screen-capture"],
        screenService,
        screenEvents.Add);
    Equal(MacPermissionCommand.RestartRequiredExitCode, screenExit, "screen restart exit code");
    Equal(PlatformPermissionKind.ScreenCapture, screenService.LastRequested, "screen permission kind");
    Equal("permission-requested", screenEvents.Single().Stage, "screen permission event");

    var accessibilityService = new RecordingPermissionService(
        PlatformPermissionRequestOutcome.UserActionRequired);
    var accessibilityExit = MacPermissionCommand.Execute(
        ["permission", "request", "accessibility"],
        accessibilityService,
        _ => { });
    Equal(
        MacPermissionCommand.UserActionRequiredExitCode,
        accessibilityExit,
        "accessibility user-action exit code");
    Equal(
        PlatformPermissionKind.Accessibility,
        accessibilityService.LastRequested,
        "accessibility permission kind");

    var readyService = new RecordingPermissionService(
        PlatformPermissionRequestOutcome.AlreadyGranted);
    Equal(
        MacPermissionCommand.ReadyExitCode,
        MacPermissionCommand.Execute(
            ["PERMISSION", "REQUEST", "ACCESSIBILITY"],
            readyService,
            _ => { }),
        "permission command is case-insensitive and reports ready");

    var newlyGrantedService = new RecordingPermissionService(
        PlatformPermissionRequestOutcome.Granted);
    Equal(
        MacPermissionCommand.ReadyExitCode,
        MacPermissionCommand.Execute(
            ["permission", "request", "accessibility"],
            newlyGrantedService,
            _ => { }),
        "newly granted accessibility permission reports ready");
}

static void MacModuleImportCommandContract()
{
    Equal(false, MacModuleImportCommand.IsCommand([]), "empty arguments are not an import command");
    Equal(
        false,
        MacModuleImportCommand.IsCommand(["--toggle", "A"]),
        "runtime arguments are not an import command");

    var migrationCalls = 0;
    var invalidEvents = new List<MacApplicationEvent>();
    var invalidExit = MacModuleImportCommand.Execute(
        ["modules", "unknown", "/legacy"],
        "/target",
        (_, _) =>
        {
            migrationCalls++;
            throw new InvalidOperationException("invalid command must not migrate");
        },
        invalidEvents.Add);
    Equal(MacModuleImportCommand.InvalidArgumentsExitCode, invalidExit, "invalid import exit code");
    Equal(0, migrationCalls, "invalid import does not call migration");
    Equal("module-import-command-rejected", invalidEvents.Single().Stage, "invalid import event");

    string? actualSource = null;
    string? actualTarget = null;
    var successEvents = new List<MacApplicationEvent>();
    var successExit = MacModuleImportCommand.Execute(
        ["MODULES", "IMPORT", "/legacy"],
        "/target",
        (source, target) =>
        {
            actualSource = source;
            actualTarget = target;
            return new LegacyModuleMigrationResult(
                source,
                target,
                "/target/migration/legacy-modules-v1.json",
                ["one.json", "nested/two.json"],
                ["existing.json"],
                [],
                AlreadyCompleted: false,
                SkippedReason: null);
        },
        successEvents.Add);
    Equal(MacModuleImportCommand.CompletedExitCode, successExit, "successful import exit code");
    Equal("/legacy", actualSource, "import source argument");
    Equal("/target", actualTarget, "import target argument");
    Equal("module-imported", successEvents.Single().Stage, "successful import event");
    Equal(true, successEvents.Single().Message.Contains("复制 2", StringComparison.Ordinal), "import copied count");
    Equal(true, successEvents.Single().Message.Contains("保留 1", StringComparison.Ordinal), "import preserved count");

    var skippedExit = MacModuleImportCommand.Execute(
        ["modules", "import", "/legacy"],
        "/target",
        (source, target) => LegacyModuleMigrationResult.Skipped(
            source,
            target,
            "/target/migration/legacy-modules-v1.json",
            "旧数据目录中没有 module 目录，已跳过迁移。"),
        _ => { });
    Equal(MacModuleImportCommand.SkippedExitCode, skippedExit, "skipped import exit code");

    var failureEvents = new List<MacApplicationEvent>();
    var failedExit = MacModuleImportCommand.Execute(
        ["modules", "import", "/legacy"],
        "/target",
        (source, target) => new LegacyModuleMigrationResult(
            source,
            target,
            "/target/migration/legacy-modules-v1.json",
            [],
            [],
            [new LegacyModuleMigrationFailure(
                "broken.json",
                LegacyModuleMigrationFailureKind.InvalidSourceFile,
                "invalid")],
            AlreadyCompleted: false,
            SkippedReason: null),
        failureEvents.Add);
    Equal(MacModuleImportCommand.FailedExitCode, failedExit, "failed import exit code");
    Equal("module-import-failed", failureEvents.Single().Stage, "failed import event");
    Equal(false, failureEvents.Single().Message.Contains("/legacy", StringComparison.Ordinal), "import event hides source path");
}

static void MacKeyOutputContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var keyOutputSource = File.ReadAllText(Path.Combine(
        repositoryRoot,
        "Platforms",
        "Shigure.Platform.Mac",
        "MacKeySender.cs"));
    Equal(true, keyOutputSource.Contains("CGEventPost(MacKeyOutputInterop.EventTapHid", StringComparison.Ordinal),
        "mac key output enters the HID event stream");
    Equal(false, keyOutputSource.Contains("CGEventPostToPid", StringComparison.Ordinal),
        "mac key output avoids the target text-input pipeline");

    var identity = new TargetIdentity(TargetPlatforms.MacOS, 77, 9001);
    var target = new TargetWindow(identity, "/Applications/World of Warcraft.app", new TargetBounds(0, 0, 100, 100));
    var locator = new FakeTargetWindowLocator(target);
    var permissions = new FakePlatformPermissionService(accessibilityReady: true);
    var eventApi = new FakeMacKeyEventApi();
    var frontmost = new FakeMacFrontmostApplicationProvider(identity.ProcessId);
    var sender = new MacKeySender(locator, permissions, eventApi, frontmost);

    var result = sender.Send("CTRL-ALT-SHIFT-F12", identity);
    Equal(true, result.Succeeded, "mac key output succeeds");
    Equal(KeySendFailureKind.None, result.FailureKind, "successful output has no failure kind");
    Equal(
        "111:D,111:U",
        string.Join(',', eventApi.Events.Select(item => $"{item.KeyCode}:{(item.KeyDown ? "D" : "U")}")),
        "mac output avoids standalone modifier taps");
    Equal(
        "917504,917504",
        string.Join(',', eventApi.Events.Select(item => item.Flags)),
        "mac main-key events carry all requested modifier flags");
    Equal(
        string.Join(',', eventApi.Events.Select(item => item.Reference)),
        string.Join(',', eventApi.Posts),
        "all mac events enter HID only after frontmost-target verification");
    Equal(3, eventApi.Released.Count, "two events and source are released");
    Equal(eventApi.Source, eventApi.Released[^1], "event source is released last");
    Equal(1, permissions.CheckCount, "send checks accessibility once");

    var unknownApi = new FakeMacKeyEventApi();
    var unknown = new MacKeySender(locator, permissions, unknownApi, frontmost).Send("CTRL-NOT_A_KEY", identity);
    Equal(KeySendFailureKind.UnknownKey, unknown.FailureKind, "unknown key failure kind");
    Equal(0, unknownApi.Posts.Count, "unknown key sends nothing");

    var missingApi = new FakeMacKeyEventApi();
    var missing = new MacKeySender(
        new FakeTargetWindowLocator(null),
        permissions,
        missingApi,
        frontmost).Send("A", identity);
    Equal(KeySendFailureKind.TargetUnavailable, missing.FailureKind, "missing target failure kind");
    Equal(0, missingApi.Posts.Count, "missing target sends nothing");

    var switchedApi = new FakeMacKeyEventApi();
    var switched = new MacKeySender(locator, permissions, switchedApi, frontmost).Send(
        "A",
        identity with { WindowId = 9002 });
    Equal(KeySendFailureKind.TargetChanged, switched.FailureKind, "switched target failure kind");
    Equal(0, switchedApi.Posts.Count, "switched target sends nothing");

    var backgroundApi = new FakeMacKeyEventApi();
    var background = new MacKeySender(
        locator,
        permissions,
        backgroundApi,
        new FakeMacFrontmostApplicationProvider(1234)).Send("A", identity);
    Equal(KeySendFailureKind.TargetChanged, background.FailureKind, "background target failure kind");
    Equal(0, backgroundApi.Posts.Count, "background target sends nothing");

    var deniedApi = new FakeMacKeyEventApi();
    var denied = new MacKeySender(
        locator,
        new FakePlatformPermissionService(accessibilityReady: false),
        deniedApi,
        frontmost).Send("A", identity);
    Equal(KeySendFailureKind.PermissionDenied, denied.FailureKind, "permission failure kind");
    Equal(0, deniedApi.Posts.Count, "permission failure sends nothing");

    var missingSourceApi = new FakeMacKeyEventApi { Source = 0 };
    var missingSource = new MacKeySender(locator, permissions, missingSourceApi, frontmost).Send("A", identity);
    Equal(KeySendFailureKind.NativeFailure, missingSource.FailureKind, "missing event source failure kind");
    Equal(0, missingSourceApi.Posts.Count, "missing event source sends nothing");

    var partialApi = new FakeMacKeyEventApi { FailCreationAt = 2 };
    var partial = new MacKeySender(locator, permissions, partialApi, frontmost).Send("CTRL-SHIFT-A", identity);
    Equal(KeySendFailureKind.NativeFailure, partial.FailureKind, "partial sequence failure kind");
    Equal(0, partialApi.Posts.Count, "partial sequence is never posted");
    Equal(2, partialApi.Released.Count, "partial event and source are released");
}

static void TargetProcessConfigFixture()
{
    var fixturePath = Path.Combine(
        Path.GetTempPath(),
        $"shigure-process-config-{Guid.NewGuid():N}.txt");

    try
    {
        File.WriteAllText(
            fixturePath,
            "# comment\nWow.exe\n wow \n; disabled\nWorld of Warcraft\n\n");
        var config = new TargetProcessConfig(fixturePath);
        var names = config.ReadProcessNames();

        Equal(2, names.Count, "normalized process name count");
        Equal("Wow", names[0], "exe suffix normalization");
        Equal("World of Warcraft", names[1], "mac process name");
        Equal("Wow、World of Warcraft", config.DescribeConfiguredProcesses(), "process description");
        Equal(true, ReferenceEquals(names, config.ReadProcessNames()), "unchanged process config reuses parsed names");

        File.WriteAllText(fixturePath, "Wow.exe\nWorld of Warcraft\nWowClassic.exe\n");
        File.SetLastWriteTimeUtc(fixturePath, DateTime.UtcNow.AddSeconds(2));
        var updatedNames = config.ReadProcessNames();
        Equal(3, updatedNames.Count, "changed process config invalidates parsed names");
        Equal("WowClassic", updatedNames[2], "updated process name normalization");
    }
    finally
    {
        File.Delete(fixturePath);
    }
}

static void WowAddonPathContract()
{
    var fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        $"shigure-addon-path-{Guid.NewGuid():N}");

    try
    {
        var flavorRoot = Path.Combine(fixtureRoot, "_retail_");
        var macExecutable = Path.Combine(
            flavorRoot,
            "World of Warcraft.app",
            "Contents",
            "MacOS",
            "World of Warcraft");
        var expectedMacAddOns = Path.Combine(flavorRoot, "Interface", "AddOns");

        var windowsExecutable = Path.Combine(fixtureRoot, "windows", "Wow.exe");
        Equal(
            Path.Combine(fixtureRoot, "windows", "Interface", "AddOns"),
            WowAddonLocator.ResolveAddOnsDirectory(windowsExecutable),
            "non-app fallback remains executable-relative");

        Directory.CreateDirectory(Path.Combine(fixtureRoot, "Interface"));
        Equal(
            expectedMacAddOns,
            WowAddonLocator.ResolveAddOnsDirectory(macExecutable),
            "mac app bundle fallback does not cross the flavor root");

        Directory.CreateDirectory(Path.Combine(flavorRoot, "Interface"));
        Equal(
            expectedMacAddOns,
            WowAddonLocator.ResolveAddOnsDirectory(macExecutable),
            "existing mac Interface directory is discovered");

        var locator = new FakeTargetWindowLocator(new TargetWindow(
            new TargetIdentity(TargetPlatforms.MacOS, 12, 34),
            macExecutable,
            null));
        Equal(
            expectedMacAddOns,
            WowAddonLocator.FindAddOnsDirectory(locator),
            "target locator process path drives addon location");

        Equal(
            null,
            WowAddonLocator.FindAddOnsDirectory(new FakeTargetWindowLocator(null)),
            "missing target has no addon directory");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void FuyutsuiUiScaleContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var quickButton = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "quickbutton.lua"));
    var pixelBlocks = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "block.lua"));
    var scaleConstant = quickButton.IndexOf("local PANEL_SCALE = GetDefaultScale()", StringComparison.Ordinal);
    var frameCreation = quickButton.IndexOf("local f = CreateFrame(\"Frame\", nil, UIParent)", StringComparison.Ordinal);
    var ignoreParentScale = quickButton.IndexOf("f:SetIgnoreParentScale(true)", StringComparison.Ordinal);
    var defaultScale = quickButton.IndexOf("f:SetScale(PANEL_SCALE)", StringComparison.Ordinal);
    var frameSize = quickButton.IndexOf("f:SetSize(PANEL_WIDTH", StringComparison.Ordinal);
    var legacyScale = quickButton.IndexOf("local savedScale = c.quickButtonScale or 1", StringComparison.Ordinal);
    var positionScale = quickButton.IndexOf("local positionScale = savedScale / PANEL_SCALE", StringComparison.Ordinal);
    var migratedX = quickButton.IndexOf("c.quickButtonX = c.quickButtonX * positionScale", StringComparison.Ordinal);
    var clampFunction = quickButton.IndexOf("local function ClampQuickButtonPosition", StringComparison.Ordinal);
    var clampCall = quickButton.IndexOf("ClampQuickButtonPosition(f, c, p)", StringComparison.Ordinal);
    var savedScale = quickButton.IndexOf("c.quickButtonScale = PANEL_SCALE", StringComparison.Ordinal);

    Equal(true, scaleConstant >= 0, "quick panel has one default scale baseline");
    Equal(true, frameCreation >= 0, "quick panel remains attached to UIParent");
    Equal(true, ignoreParentScale > frameCreation, "quick panel ignores UIParent scale");
    Equal(true, defaultScale > ignoreParentScale, "quick panel uses the default scale baseline");
    Equal(true, frameSize > defaultScale, "scale independence is applied before panel sizing");
    Equal(true, legacyScale > frameSize, "legacy positions default to their original scale");
    Equal(true, positionScale > legacyScale, "saved positions are converted to the fixed scale");
    Equal(true, migratedX > positionScale, "position conversion is applied before anchoring");
    Equal(true, clampFunction >= 0, "quick panel has an explicit screen bounds clamp");
    Equal(true, clampCall > migratedX, "screen bounds are clamped after position conversion");
    Equal(true, savedScale >= 0, "saved positions record their scale baseline");

    Equal(true, pixelBlocks.Contains("local screenWidth = GetScreenWidth()", StringComparison.Ordinal),
        "pixel protocol keeps its known-decodable screen-width layout");
    Equal(true, pixelBlocks.Contains("local BLOCK_HEIGHT = 1", StringComparison.Ordinal),
        "scanner main row height stays aligned with the addon");
    Equal(true, pixelBlocks.Contains("local BAR_HEIGHT = 2", StringComparison.Ordinal),
        "scanner count and heal row heights stay aligned with the addon");
    Equal(false, pixelBlocks.Contains("FuyutsuiPixelRoot", StringComparison.Ordinal),
        "pixel protocol does not introduce an extra scaled root");
    Equal(false, pixelBlocks.Contains("pixelRoot", StringComparison.Ordinal),
        "all protocol frames remain on their original UIParent path");
    Equal(true, pixelBlocks.Contains("FuyutsuiColorBars\", UIParent", StringComparison.Ordinal),
        "main color bars use the known-decodable UIParent layout");
    Equal(true, pixelBlocks.Contains("FuyutsuiCountBars\", UIParent", StringComparison.Ordinal),
        "count bars use the known-decodable UIParent layout");
    Equal(true, pixelBlocks.Contains("FuyutsuiHealAbsorbBars\", UIParent", StringComparison.Ordinal),
        "heal absorb bars use the known-decodable UIParent layout");
    Equal(true, pixelBlocks.Contains("button:SetPoint(\"TOPLEFT\", UIParent", StringComparison.Ordinal),
        "aura pixels share the original UIParent anchor path");
    Equal(true, pixelBlocks.Contains("CreateFrame(\"AuraContainer\", frameName, UIParent", StringComparison.Ordinal),
        "aura duration containers share the original UIParent parent");
}

static void FuyutsuiGlobalBurstMouseContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var commands = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "commands.lua"));
    var quickButton = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "quickbutton.lua"));
    var events = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "events.lua"));
    var core = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "core.lua"));

    Equal(true, commands.Contains("function Fuyutsui:ToggleBurst()", StringComparison.Ordinal),
        "burst toggle has one addon-owned entry point");
    Equal(true, commands.Contains("SetBurstTime(c, math.huge, 1", StringComparison.Ordinal),
        "burst toggle stays enabled until the next toggle");
    Equal(true, commands.Contains("SetBurstTime(c, -1, 0", StringComparison.Ordinal),
        "burst toggle can disable the shared state");
    Equal(true, commands.Contains("command == \"cd toggle\"", StringComparison.Ordinal),
        "slash command keeps the shared toggle target");
    Equal(true, core.Contains("self:RegisterEvent(\"GLOBAL_MOUSE_UP\")", StringComparison.Ordinal),
        "addon registers one interface-wide mouse release event");
    Equal(true, events.Contains("function Fuyutsui:GLOBAL_MOUSE_UP(_, button)", StringComparison.Ordinal),
        "global mouse release has an addon-owned handler");
    Equal(true, events.Contains("if button == \"MiddleButton\" then", StringComparison.Ordinal),
        "global handler filters to the middle mouse button");
    Equal(true, events.Contains("self:ToggleBurst()", StringComparison.Ordinal),
        "global handler invokes the shared burst toggle");
    Equal(false, quickButton.Contains("SetOverrideBindingClick(", StringComparison.Ordinal),
        "global burst no longer depends on a binding consumed by UI frames");
    Equal(false, quickButton.Contains("SecureActionButtonTemplate", StringComparison.Ordinal),
        "global burst no longer creates a protected click button");
    Equal(false, quickButton.Contains("Fuyutsui:ToggleBurst()", StringComparison.Ordinal),
        "quick panel does not add a second middle-click toggle");
    Equal(false, events.Contains("burstBindingPending", StringComparison.Ordinal),
        "global mouse event does not require combat-lockdown retry state");
}

static void FuyutsuiProtocolContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var curves = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "curves.lua"));
    var main = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "main.lua"));
    var stateBlocks = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "stateblocks.lua"));

    Equal(true, curves.Contains("CreateColorCurve(25.5, 255)", StringComparison.Ordinal),
        "cast protocol encodes one second as ten units");
    Equal(true, ClassStateCatalog.IsKnown(ClassStateCatalog.CategoryState, "施法(正计时)"),
        "state catalog exposes elapsed cast time");
    Equal(true, ClassStateCatalog.IsKnown(ClassStateCatalog.CategoryState, "施法(倒计时)"),
        "state catalog exposes remaining cast time");
    Equal(false, ClassStateCatalog.IsKnown(ClassStateCatalog.CategoryState, "施法"),
        "legacy cast state is no longer selectable");
    Equal(true, ClassStateCatalog.TopCategories.Contains(ClassStateCatalog.CategoryMouseover),
        "state catalog exposes mouseover category");
    Equal(true, ClassStateCatalog.TopCategories.Contains(ClassStateCatalog.CategoryBoss5),
        "state catalog exposes every boss category");
    Equal(true, main.Contains("\"鼠标\"", StringComparison.Ordinal)
        && main.Contains("\"首领5\"", StringComparison.Ordinal),
        "addon block loader includes mouseover and boss categories");
    Equal(true, stateBlocks.Contains("[\"施法(正计时)\"]", StringComparison.Ordinal)
        && stateBlocks.Contains("[\"施法(倒计时)\"]", StringComparison.Ordinal),
        "addon runtime registers both cast directions");
}

static void ClassBlocksEditorPersistenceContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var fixtureRoot = Path.Combine(Path.GetTempPath(), $"shigure-class-editor-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(fixtureRoot);
        var sourcePath = Path.Combine(repositoryRoot, "Fuyutsui", "class", "Mage.lua");
        var fixturePath = Path.Combine(fixtureRoot, "Mage.lua");
        File.Copy(sourcePath, fixturePath);

        var document = ClassBlocksStore.Load(fixturePath);
        Equal(true, document.IsModernFormat, "class editor fixture uses modern format");
        var spec = document.Specs.OrderBy(item => item.Key).First().Value;
        spec.CategorizedStates[ClassStateCatalog.CategoryConfig].Add("契约测试开关");
        spec.PlayerAuras.Add(new ClassBlocksStore.AuraEntry
        {
            Name = "契约测试光环",
            SpellId = 123456789
        });
        ClassBlocksStore.Save(document);

        var reloaded = ClassBlocksStore.Load(fixturePath);
        var reloadedSpec = reloaded.Specs.OrderBy(item => item.Key).First().Value;
        Equal(
            true,
            reloadedSpec.CategorizedStates[ClassStateCatalog.CategoryConfig].Contains("契约测试开关"),
            "class editor persists categorized states");
        Equal(
            true,
            reloadedSpec.PlayerAuras.Any(aura => aura.Name == "契约测试光环" && aura.SpellId == 123456789),
            "class editor persists aura fields");
        Equal(true, File.ReadAllText(fixturePath).Contains("Fuyutsui.spellsList", StringComparison.Ordinal), "class editor preserves spellsList table");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void ClassMacrosEditorPersistenceContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var fixtureRoot = Path.Combine(Path.GetTempPath(), $"shigure-macro-editor-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(fixtureRoot);
        var sourcePath = Path.Combine(repositoryRoot, "Fuyutsui", "core", "classmacros.lua");
        var fixturePath = Path.Combine(fixtureRoot, "classmacros.lua");
        File.Copy(sourcePath, fixturePath);

        var document = ClassMacrosStore.Load(fixturePath);
        Equal(13, document.Classes.Count, "macro editor loads every class table");
        Equal(39, document.Classes["DEATHKNIGHT"].KeyOffset, "macro editor loads class key offset");
        var warrior = document.Classes[ClassMacrosStore.ToClassFileKey(1)];
        warrior.UsesSpecDynamicSpells = true;
        warrior.DynamicBySpec[1] = ["契约动态法术"];
        warrior.StaticSpells.Add(new ClassMacrosStore.ArrayEntry { Text = string.Empty });
        warrior.SpecialSpells.Add(new ClassMacrosStore.ArrayEntry
        {
            Text = "/cast [known:123,@cursor]契约特殊法术",
            Comment = "契约技能"
        });

        Equal(0, FuyutsuiKeymapConverter.ValidateCapacity(document).Count, "valid macro edits fit shared capacity");
        ClassMacrosStore.Save(document);

        var reloaded = ClassMacrosStore.Load(fixturePath);
        var reloadedWarrior = reloaded.Classes[ClassMacrosStore.ToClassFileKey(1)];
        Equal(39, reloaded.Classes["DEATHKNIGHT"].KeyOffset, "macro editor preserves class key offset");
        Equal(true, reloadedWarrior.UsesSpecDynamicSpells, "macro editor persists spec dynamic format");
        Equal("契约动态法术", reloadedWarrior.DynamicBySpec[1][0], "macro editor persists spec dynamic spell");
        Equal(string.Empty, reloadedWarrior.StaticSpells[^1].Text, "macro editor preserves empty slot");
        Equal("契约技能", reloadedWarrior.SpecialSpells[^1].Comment, "macro editor persists macro comment");
        Equal(true, reloaded.SourceText.Contains("Fuyutsui.MacroBodies", StringComparison.Ordinal), "macro editor preserves content outside ClassMacros");

        var parsed = FuyutsuiKeymapConverter.ParseSpecialMacro(
            reloadedWarrior.SpecialSpells[^1].Text,
            reloadedWarrior.SpecialSpells[^1].Comment);
        Equal(ReservedUnit.Cursor, parsed.Unit, "macro editor uses shared unit mapping");
        Equal("known:123", parsed.Condition, "macro editor uses shared condition parsing");
        Equal("契约技能", parsed.Spell, "macro editor uses shared comment spell name");
        Equal(67, FuyutsuiKeymapConverter.CalculateRequiredSlots(2, 3, 4), "macro slot calculation");

        reloadedWarrior.DynamicCommon.Clear();
        reloadedWarrior.DynamicCommon.AddRange(Enumerable.Repeat("超限法术", 10));
        var capacityIssue = FuyutsuiKeymapConverter.ValidateCapacity(reloaded)
            .Single(issue => issue.ClassFile == "WARRIOR" && issue.SpecIndex is null);
        Equal(true, capacityIssue.RequiredSlots > capacityIssue.Capacity, "macro capacity overflow is rejected before save");

        reloadedWarrior.DynamicCommon.Clear();
        var keymapDirectory = Path.Combine(fixtureRoot, "keymap");
        var update = FuyutsuiKeymapConverter.UpdateFromClassMacros(fixturePath, keymapDirectory);
        Equal(13, update.UpdatedFiles.Count, "saved macro document generates every class keymap");
        var warriorKeymapPath = Path.Combine(keymapDirectory, "warrior.json");
        Equal(true, File.Exists(warriorKeymapPath), "saved macro document generates selected class keymap");
        var warriorKeymap = JsonNode.Parse(File.ReadAllText(warriorKeymapPath))
            ?? throw new InvalidDataException("generated warrior keymap is empty");
        var specOne = warriorKeymap["专精"]?["1"]
            ?? throw new InvalidDataException("generated warrior keymap is missing spec 1");
        Equal("契约动态法术", specOne["1"]?["技能"]?.GetValue<string>(), "dynamic macro starts at first slot");
        Equal(1, specOne["1"]?["unit"]?.GetValue<int>() ?? -1, "dynamic macro starts at raid unit 1");
        Equal("契约动态法术", specOne["30"]?["技能"]?.GetValue<string>(), "dynamic macro fills shared slot count");
        Equal(30, specOne["30"]?["unit"]?.GetValue<int>() ?? -1, "dynamic macro ends at raid unit 30");

        var deathKnightKeymap = JsonNode.Parse(File.ReadAllText(Path.Combine(keymapDirectory, "deathknight.json")))
            ?? throw new InvalidDataException("generated death knight keymap is empty");
        Equal(string.Empty, deathKnightKeymap["1"]?["技能"]?.GetValue<string>(), "death knight reserves CTRL slot block");
        Equal("亡者复生", deathKnightKeymap["40"]?["技能"]?.GetValue<string>(), "death knight starts macros after reserved slots");
        Equal("ALT-NUMPAD1", deathKnightKeymap["40"]?["热键"]?.GetValue<string>(), "death knight uses ALT hotkeys on macOS");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void ProjectConfigUpdateContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var fixtureRoot = Path.Combine(Path.GetTempPath(), $"shigure-config-update-{Guid.NewGuid():N}");
    try
    {
        Directory.CreateDirectory(Path.Combine(fixtureRoot, "config"));
        var sourceRoot = Path.Combine(repositoryRoot, "Fuyutsui");
        var savedFile = Path.Combine(sourceRoot, "class", "Mage.lua");
        var sourceHashBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(savedFile)));
        var addonSync = new FuyutsuiAddonSyncService(sourceRoot, new FakeTargetWindowLocator(null));
        var service = new ProjectConfigUpdateService(fixtureRoot, addonSync);

        var result = service.Update(savedFile);

        Equal(Path.Combine(sourceRoot, "class"), service.ClassDirectory, "config update exposes addon class authority");
        Equal(13, result.Config.UpdatedFiles.Count, "config update compiles every class file");
        Equal(13, result.Keymap?.UpdatedFiles.Count ?? 0, "config update compiles every class keymap");
        Equal(false, result.AddonSync.TargetFound, "missing game skips deployment without failing local update");
        Equal(true, File.Exists(Path.Combine(fixtureRoot, "config", "Mage.json")), "config update writes runtime workspace config");
        Equal(true, File.Exists(Path.Combine(fixtureRoot, "keymap", "mage.json")), "config update writes runtime workspace keymap");
        foreach (var generatedPath in result.Config.UpdatedFiles)
        {
            var checkedInPath = Path.Combine(repositoryRoot, "config", Path.GetFileName(generatedPath));
            var generated = JsonNode.Parse(File.ReadAllText(generatedPath));
            var checkedIn = JsonNode.Parse(File.ReadAllText(checkedInPath));
            Equal(true, JsonNode.DeepEquals(generated, checkedIn),
                $"checked-in config is reproducible: {Path.GetFileName(generatedPath)}");
        }
        var mageConfig = JsonNode.Parse(File.ReadAllText(Path.Combine(fixtureRoot, "config", "Mage.json")))
            ?? throw new InvalidDataException("generated mage config is empty");
        Equal("状态", mageConfig["1"]?["施法(正计时)"]?["category"]?.GetValue<string>(),
            "generated state records its source category");
        Equal("鼠标", mageConfig["1"]?["鼠标类型"]?["category"]?.GetValue<string>(),
            "generated config includes mouseover state metadata");
        Equal(
            sourceHashBefore,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(savedFile))),
            "config update does not rewrite addon authority");

        Parallel.For(0, 4, _ => service.Update(savedFile));
        Equal(true, File.Exists(Path.Combine(fixtureRoot, "config", "Mage.json")), "concurrent config updates are serialized");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void FuyutsuiAddonSyncContract()
{
    var fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        $"shigure-addon-sync-{Guid.NewGuid():N}");

    try
    {
        var sourceRoot = Path.Combine(fixtureRoot, "source", "Fuyutsui");
        var sourceCore = Path.Combine(sourceRoot, "core");
        Directory.CreateDirectory(sourceCore);
        var tocPath = Path.Combine(sourceRoot, "Fuyutsui.toc");
        var nestedRelativePath = Path.Combine("core", "state.lua");
        var nestedSourcePath = Path.Combine(sourceRoot, nestedRelativePath);
        File.WriteAllText(tocPath, "version-one");
        File.WriteAllText(nestedSourcePath, "state-one");

        var flavorRoot = Path.Combine(fixtureRoot, "game", "_retail_");
        Directory.CreateDirectory(Path.Combine(flavorRoot, "Interface"));
        var processPath = Path.Combine(
            flavorRoot,
            "World of Warcraft.app",
            "Contents",
            "MacOS",
            "World of Warcraft");
        var locator = new FakeTargetWindowLocator(new TargetWindow(
            new TargetIdentity(TargetPlatforms.MacOS, 56, 78),
            processPath,
            null));
        var service = new FuyutsuiAddonSyncService(sourceRoot, locator);
        var targetRoot = Path.Combine(flavorRoot, "Interface", "AddOns", "Fuyutsui");

        var first = service.SynchronizeAll();
        Equal(true, first.CompletedSuccessfully, "first deployment succeeds without addon directory");
        Equal(targetRoot, first.TargetRoot, "first deployment target root");
        Equal(2, first.CopiedFiles.Count, "first deployment copies all files");
        Equal("state-one", File.ReadAllText(Path.Combine(targetRoot, nestedRelativePath)), "nested file copied");

        var same = service.SynchronizeAll();
        Equal(true, same.CompletedSuccessfully, "same-version deployment succeeds");
        Equal(0, same.CopiedFiles.Count, "same-version deployment copies nothing");
        Equal(2, same.SkippedFiles.Count, "same-version deployment skips all files");

        File.WriteAllText(nestedSourcePath, "state-two");
        var extraTargetPath = Path.Combine(targetRoot, "user-extra.lua");
        File.WriteAllText(extraTargetPath, "preserve-me");
        var partial = service.SynchronizeAll();
        Equal(true, partial.CompletedSuccessfully, "partial update succeeds");
        Equal(true, partial.CopiedFiles.Contains(nestedRelativePath), "changed nested file is copied");
        Equal(true, partial.SkippedFiles.Contains("Fuyutsui.toc"), "unchanged file is skipped");
        Equal(true, File.Exists(extraTargetPath), "target-only file is preserved");

        File.WriteAllText(tocPath, "version-two");
        var single = service.SynchronizeFile(tocPath);
        Equal(1, single.CopiedFiles.Count, "single-file update copies one file");
        Equal("version-two", File.ReadAllText(Path.Combine(targetRoot, "Fuyutsui.toc")), "single-file target updated");

        var outsideSourcePath = Path.Combine(fixtureRoot, "outside.lua");
        File.WriteAllText(outsideSourcePath, "outside");
        Throws<InvalidOperationException>(
            () => service.SynchronizeFile(outsideSourcePath),
            "single-file update rejects paths outside source root");

        File.WriteAllText(nestedSourcePath, "state-blocked");
        var deniedService = new FuyutsuiAddonSyncService(
            sourceRoot,
            locator,
            static (_, _, _) => throw new UnauthorizedAccessException("simulated target denial"));
        var denied = deniedService.SynchronizeFile(nestedSourcePath);
        Equal(false, denied.CompletedSuccessfully, "permission failure is reported");
        Equal(1, denied.Failures.Count, "permission failure count");
        Equal(nestedRelativePath, denied.Failures[0].RelativePath, "permission failure relative path");
        Equal("state-two", File.ReadAllText(Path.Combine(targetRoot, nestedRelativePath)), "permission failure leaves target unchanged");

        var missingTarget = new FuyutsuiAddonSyncService(
            sourceRoot,
            new FakeTargetWindowLocator(null)).SynchronizeAll();
        Equal(false, missingTarget.TargetFound, "missing target is a non-throwing skip");
        Equal(true, !string.IsNullOrWhiteSpace(missingTarget.SkippedReason), "missing target explains skip");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void MacUserDataPathContract()
{
    var applicationSupport = Path.Combine(
        Path.GetTempPath(),
        "shigure-application-support-fixture");
    var expectedRoot = Path.Combine(Path.GetFullPath(applicationSupport), "Shigure");

    Equal(
        expectedRoot,
        MacUserDataPaths.ResolveUserDataDirectory(applicationSupport),
        "Mac application support root");
    Equal(
        Path.Combine(expectedRoot, "module"),
        UserDataLayout.ResolveModuleDirectory(expectedRoot),
        "shared module directory");
    Equal(
        Path.Combine(expectedRoot, "cache"),
        UserDataLayout.ResolveCacheDirectory(expectedRoot),
        "shared cache directory");
    Equal(
        Path.Combine(expectedRoot, "logs"),
        UserDataLayout.ResolveLogsDirectory(expectedRoot),
        "shared logs directory");
    Equal(
        Path.Combine(expectedRoot, "migration"),
        UserDataLayout.ResolveMigrationDirectory(expectedRoot),
        "shared migration directory");
    Equal(
        Path.Combine(expectedRoot, "runtime"),
        UserDataLayout.ResolveRuntimeDirectory(expectedRoot),
        "shared runtime resource directory");
    Throws<ArgumentException>(
        () => MacUserDataPaths.ResolveUserDataDirectory(" "),
        "empty application support root is rejected");
}

static void MacUiStatePersistenceContract()
{
    var fixtureRoot = Path.Combine(Path.GetTempPath(), $"shigure-mac-ui-state-{Guid.NewGuid():N}");
    try
    {
        var userDataRoot = Path.Combine(fixtureRoot, "Application Support", "Shigure");
        var store = new MacUiStateStore(userDataRoot);
        Equal(
            Path.Combine(userDataRoot, "cache", "mac-ui-state-v1.json"),
            store.FilePath,
            "Mac UI state uses its own cache file");
        Equal(false, File.Exists(Path.Combine(userDataRoot, "cache", "window-state.json")), "Windows UI cache is untouched");

        var missing = store.Load();
        Equal(null, missing.Warning, "missing Mac UI state is not an error");
        Equal("General", missing.State.SelectedPage, "missing Mac UI state uses default page");
        Equal("XBUTTON2", missing.State.TriggerKey, "missing Mac UI state uses default trigger key");
        Equal(SendMode.Switch, missing.State.SendMode, "missing Mac UI state uses default send mode");

        var state = new MacUiState
        {
            MainWindowBounds = new MacUiBounds { X = 120, Y = 80, Width = 1180, Height = 760 },
            SelectedPage = "Status",
            OverlayLayout = MacOverlayLayout.Vertical,
            TriggerKey = "WHEELUP",
            SendMode = SendMode.Click,
            HorizontalOverlayBounds = new MacUiBounds { X = 40, Y = 60, Width = 540, Height = 64 },
            VerticalOverlayBounds = new MacUiBounds { X = 90, Y = 100, Width = 240, Height = 190 },
            ColumnWidths = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["status.state.0"] = 72,
                ["status.state.1"] = 220,
                ["invalid"] = double.NaN
            }
        };
        Equal(null, store.TrySave(state), "valid Mac UI state saves");
        Equal(true, File.Exists(store.FilePath), "Mac UI state file exists");
        Equal(0, Directory.EnumerateFiles(Path.GetDirectoryName(store.FilePath)!, "*.tmp").Count(), "Mac UI state leaves no atomic temp file");

        var loaded = store.Load();
        Equal(null, loaded.Warning, "saved Mac UI state loads without warning");
        Equal("Status", loaded.State.SelectedPage, "selected page round trip");
        Equal(MacOverlayLayout.Vertical, loaded.State.OverlayLayout, "overlay layout round trip");
        Equal("WHEELUP", loaded.State.TriggerKey, "trigger key round trip");
        Equal(SendMode.Click, loaded.State.SendMode, "send mode round trip");
        Equal(1180D, loaded.State.MainWindowBounds?.Width, "main window width round trip");
        Equal(240D, loaded.State.VerticalOverlayBounds?.Width, "vertical overlay width round trip");
        Equal(2, loaded.State.ColumnWidths.Count, "invalid column width is discarded before save");
        Equal(220D, loaded.State.ColumnWidths["status.state.1"], "column width round trip");

        const string damaged = "{ this is not json }";
        File.WriteAllText(store.FilePath, damaged);
        var damagedLoad = store.Load();
        Equal(true, !string.IsNullOrWhiteSpace(damagedLoad.Warning), "damaged Mac UI state reports warning");
        Equal("General", damagedLoad.State.SelectedPage, "damaged Mac UI state falls back to defaults");
        Equal(damaged, File.ReadAllText(store.FilePath), "damaged Mac UI state is not overwritten during load");

        const string unknownVersion = "{\"schemaVersion\":99,\"selectedPage\":\"Logs\"}";
        File.WriteAllText(store.FilePath, unknownVersion);
        var unknownLoad = store.Load();
        Equal(true, !string.IsNullOrWhiteSpace(unknownLoad.Warning), "unknown Mac UI schema reports warning");
        Equal("General", unknownLoad.State.SelectedPage, "unknown Mac UI schema falls back to defaults");
        Equal(unknownVersion, File.ReadAllText(store.FilePath), "unknown Mac UI schema is not overwritten during load");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void RuntimeResourceWorkspaceContract()
{
    var fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        $"shigure-runtime-resources-{Guid.NewGuid():N}");

    try
    {
        var repositoryRoot = FindRepositoryRoot();
        var sourceRoot = Path.Combine(fixtureRoot, "bundle");
        var userDataRoot = Path.Combine(fixtureRoot, "application-support", "Shigure");
        var sourceLua = Path.Combine(sourceRoot, "Fuyutsui", "core", "state.lua");
        var sourceTexture = Path.Combine(sourceRoot, "Fuyutsui", "media", "icon.blp");
        var sourceClass = Path.Combine(sourceRoot, "Fuyutsui", "class", "Mage.lua");
        var sourceConfig = Path.Combine(sourceRoot, "config", "common.json");
        var sourceKeymap = Path.Combine(sourceRoot, "keymap", "base.json");
        var sourceProcess = Path.Combine(sourceRoot, "wow_process.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceLua)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceTexture)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceClass)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceConfig)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceKeymap)!);
        File.WriteAllText(sourceLua, "source-v1");
        File.WriteAllBytes(sourceTexture, [0, 1, 2, 255]);
        File.Copy(Path.Combine(repositoryRoot, "Fuyutsui", "class", "Mage.lua"), sourceClass);
        File.WriteAllText(sourceConfig, "{\"version\":1}");
        File.WriteAllText(sourceKeymap, "{\"key\":1}");
        File.WriteAllText(sourceProcess, "Wow");

        var service = new RuntimeResourceWorkspaceService();
        var first = service.Initialize(sourceRoot, userDataRoot);
        Equal(6, first.CreatedFiles.Count, "workspace first initialization creates every source");
        Equal(0, first.UpdatedFiles.Count, "workspace first initialization has no updates");
        Equal(0, first.ConflictingFiles.Count, "workspace first initialization has no conflicts");
        Equal(
            UserDataLayout.ResolveRuntimeDirectory(userDataRoot),
            first.WorkspaceDirectory,
            "workspace uses shared runtime directory");
        Equal(true, File.Exists(first.ManifestPath), "workspace manifest is committed");
        Equal(
            true,
            File.ReadAllBytes(Path.Combine(first.WorkspaceDirectory, "Fuyutsui", "media", "icon.blp"))
                .SequenceEqual(new byte[] { 0, 1, 2, 255 }),
            "workspace copies binary resources exactly");

        var second = service.Initialize(sourceRoot, userDataRoot);
        Equal(6, second.SkippedFiles.Count, "workspace unchanged files are skipped");

        var targetLua = Path.Combine(first.WorkspaceDirectory, "Fuyutsui", "core", "state.lua");
        var targetConfig = Path.Combine(first.WorkspaceDirectory, "config", "common.json");
        var targetClass = Path.Combine(first.WorkspaceDirectory, "Fuyutsui", "class", "Mage.lua");
        var targetOldKeymap = Path.Combine(first.WorkspaceDirectory, "keymap", "base.json");
        File.WriteAllText(targetLua, "user-change");
        File.WriteAllText(
            targetClass,
            File.ReadAllText(targetClass).Replace("\"施法(倒计时)\"", "\"施法\"", StringComparison.Ordinal));
        File.WriteAllText(sourceLua, "source-v2");
        File.WriteAllText(sourceConfig, "{\"version\":2}");
        File.Delete(sourceKeymap);
        var sourceNewKeymap = Path.Combine(sourceRoot, "keymap", "new.json");
        File.WriteAllText(sourceNewKeymap, "{\"key\":2}");

        var upgraded = service.Initialize(sourceRoot, userDataRoot);
        Equal(true, upgraded.UpdatedFiles.Contains("config/common.json"), "unchanged target receives source update");
        Equal(true, upgraded.CreatedFiles.Contains("keymap/new.json"), "new source file is created");
        Equal(true, upgraded.ConflictingFiles.Contains("Fuyutsui/core/state.lua"), "user edit is reported as conflict");
        Equal(true, upgraded.ConflictingFiles.Contains("Fuyutsui/class/Mage.lua"), "custom class is reported as preserved conflict");
        Equal(true, upgraded.ProtocolConflictingFiles.Contains("Fuyutsui/core/state.lua"), "core conflict blocks mixed protocol runtime");
        Equal(false, upgraded.ProtocolConflictingFiles.Contains("Fuyutsui/class/Mage.lua"), "migratable class customization does not block runtime");
        Equal(true, upgraded.MigratedFiles.Contains("Fuyutsui/class/Mage.lua"), "legacy cast field is structurally migrated");
        Equal(true, upgraded.MigratedFiles.Contains("config/Mage.json"), "legacy cast migration regenerates derived config");
        Equal(
            false,
            ClassBlocksStore.Load(targetClass).Specs.Values.Any(spec =>
                spec.FlatStates.Contains("施法", StringComparer.Ordinal)
                || spec.CategorizedStates.Values.Any(states => states.Contains("施法", StringComparer.Ordinal))),
            "legacy cast state is removed from every class spec");
        Equal("user-change", File.ReadAllText(targetLua), "user edit is preserved");
        Equal("{\"version\":2}", File.ReadAllText(targetConfig), "managed target is updated");
        Equal(true, File.Exists(targetOldKeymap), "removed source does not delete target");

        var targetProcess = Path.Combine(first.WorkspaceDirectory, "wow_process.txt");
        File.Delete(targetProcess);
        var restored = service.Initialize(sourceRoot, userDataRoot);
        Equal(true, restored.CreatedFiles.Contains("wow_process.txt"), "missing target is restored");
        Equal("Wow", File.ReadAllText(targetProcess), "restored target matches source");

        File.WriteAllText(first.ManifestPath, "not-json");
        File.WriteAllText(sourceConfig, "{\"version\":3}");
        Throws<InvalidDataException>(
            () => service.Initialize(sourceRoot, userDataRoot),
            "damaged workspace manifest fails closed");
        Equal("{\"version\":2}", File.ReadAllText(targetConfig), "manifest failure writes no resources");

        var macAppProgram = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Apps",
            "Shigure.MacApp",
            "Program.cs"));
        var macUiComposition = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Apps",
            "Shigure.MacUI",
            "MacUiComposition.cs"));
        Equal(true, macAppProgram.Contains("RuntimeResourceWorkspaceService", StringComparison.Ordinal), "MacApp initializes runtime workspace");
        Equal(true, macAppProgram.Contains("workspace.WorkspaceDirectory", StringComparison.Ordinal), "MacApp runs from runtime workspace");
        Equal(true, macAppProgram.Contains("workspace.ProtocolConflictingFiles", StringComparison.Ordinal), "MacApp blocks mixed plugin protocols");
        Equal(true, macUiComposition.Contains("RuntimeResourceWorkspaceService", StringComparison.Ordinal), "MacUI initializes runtime workspace");
        Equal(true, macUiComposition.Contains("workspace.WorkspaceDirectory", StringComparison.Ordinal), "MacUI runs from runtime workspace");
        Equal(true, macUiComposition.Contains("workspace.ProtocolConflictingFiles", StringComparison.Ordinal), "MacUI blocks mixed plugin protocols");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void LegacyModuleMigrationContract()
{
    var fixtureRoot = Path.Combine(
        Path.GetTempPath(),
        $"shigure-module-migration-{Guid.NewGuid():N}");

    try
    {
        var service = new LegacyModuleMigrationService();
        var sourceRoot = Path.Combine(fixtureRoot, "legacy");
        var sourceModules = Path.Combine(sourceRoot, "module");
        var targetRoot = Path.Combine(fixtureRoot, "application-support", "Shigure");
        var targetModules = UserDataLayout.ResolveModuleDirectory(targetRoot);
        Directory.CreateDirectory(Path.Combine(sourceModules, "nested"));
        Directory.CreateDirectory(targetModules);
        File.WriteAllText(Path.Combine(sourceModules, "first.json"), "{\"name\":\"first\"}");
        File.WriteAllText(
            Path.Combine(sourceModules, "nested", "second.json"),
            "{\"name\":\"second\"}");
        File.WriteAllText(Path.Combine(sourceModules, "existing.json"), "{\"source\":true}");
        var existingTargetPath = Path.Combine(targetModules, "existing.json");
        File.WriteAllText(existingTargetPath, "{\"target\":true}");

        var first = service.Migrate(sourceRoot, targetRoot);
        Equal(true, first.CompletedSuccessfully, "first module migration succeeds");
        Equal(2, first.CopiedFiles.Count, "first migration copies missing modules");
        Equal(1, first.PreservedFiles.Count, "first migration preserves existing target");
        Equal("{\"target\":true}", File.ReadAllText(existingTargetPath), "existing target is unchanged");
        Equal(true, File.Exists(Path.Combine(targetModules, "first.json")), "root module copied");
        Equal(
            true,
            File.Exists(Path.Combine(targetModules, "nested", "second.json")),
            "nested module copied");
        Equal(true, File.Exists(first.MarkerPath), "rollback marker created");
        Equal(true, File.Exists(Path.Combine(sourceModules, "first.json")), "legacy source is retained");
        Equal(
            false,
            Directory.EnumerateFiles(targetRoot, "*.tmp", SearchOption.AllDirectories).Any(),
            "atomic migration leaves no temp files");

        using (var marker = JsonDocument.Parse(File.ReadAllText(first.MarkerPath)))
        {
            Equal(true, marker.RootElement.GetProperty("completed").GetBoolean(), "marker completed state");
            Equal(
                2,
                marker.RootElement.GetProperty("createdFiles").GetArrayLength(),
                "marker records only migration-created modules");
            Equal(
                0,
                marker.RootElement.GetProperty("pendingFiles").GetArrayLength(),
                "completed marker has no pending modules");
        }

        var repeated = service.Migrate(sourceRoot, targetRoot);
        Equal(true, repeated.CompletedSuccessfully, "repeated migration succeeds");
        Equal(true, repeated.AlreadyCompleted, "repeated migration is a completed no-op");
        Equal(0, repeated.CopiedFiles.Count, "repeated migration copies nothing");

        var damagedSourceRoot = Path.Combine(fixtureRoot, "damaged-source");
        var damagedSourceModules = Path.Combine(damagedSourceRoot, "module");
        var protectedTargetRoot = Path.Combine(fixtureRoot, "protected-target");
        var protectedTargetModules = UserDataLayout.ResolveModuleDirectory(protectedTargetRoot);
        Directory.CreateDirectory(damagedSourceModules);
        Directory.CreateDirectory(protectedTargetModules);
        File.WriteAllText(Path.Combine(damagedSourceModules, "protected.json"), "not-json");
        File.WriteAllText(Path.Combine(damagedSourceModules, "other.json"), "{\"valid\":true}");
        var protectedTargetPath = Path.Combine(protectedTargetModules, "protected.json");
        File.WriteAllText(protectedTargetPath, "{\"keep\":true}");

        var damagedSource = service.Migrate(damagedSourceRoot, protectedTargetRoot);
        Equal(false, damagedSource.CompletedSuccessfully, "damaged source fails migration");
        Equal(
            LegacyModuleMigrationFailureKind.InvalidSourceFile,
            damagedSource.Failures.Single().Kind,
            "damaged source failure kind");
        Equal("{\"keep\":true}", File.ReadAllText(protectedTargetPath), "valid target survives damaged source");
        Equal(
            false,
            File.Exists(Path.Combine(protectedTargetModules, "other.json")),
            "preflight failure prevents partial copies");

        var damagedTargetSource = Path.Combine(fixtureRoot, "damaged-target-source");
        var damagedTargetSourceModules = Path.Combine(damagedTargetSource, "module");
        var damagedTargetRoot = Path.Combine(fixtureRoot, "damaged-target");
        var damagedTargetModules = UserDataLayout.ResolveModuleDirectory(damagedTargetRoot);
        Directory.CreateDirectory(damagedTargetSourceModules);
        Directory.CreateDirectory(damagedTargetModules);
        File.WriteAllText(Path.Combine(damagedTargetSourceModules, "conflict.json"), "{\"valid\":true}");
        var damagedTargetPath = Path.Combine(damagedTargetModules, "conflict.json");
        File.WriteAllText(damagedTargetPath, "broken-target");

        var damagedTarget = service.Migrate(damagedTargetSource, damagedTargetRoot);
        Equal(false, damagedTarget.CompletedSuccessfully, "damaged target fails closed");
        Equal(
            LegacyModuleMigrationFailureKind.InvalidTargetFile,
            damagedTarget.Failures.Single().Kind,
            "damaged target failure kind");
        Equal("broken-target", File.ReadAllText(damagedTargetPath), "damaged target is not overwritten");

        var otherSourceRoot = Path.Combine(fixtureRoot, "other-legacy");
        Directory.CreateDirectory(Path.Combine(otherSourceRoot, "module"));
        File.WriteAllText(Path.Combine(otherSourceRoot, "module", "other.json"), "{\"other\":true}");
        var sourceMismatch = service.Migrate(otherSourceRoot, targetRoot);
        Equal(false, sourceMismatch.CompletedSuccessfully, "marker source mismatch fails closed");
        Equal(
            LegacyModuleMigrationFailureKind.InvalidMarker,
            sourceMismatch.Failures.Single().Kind,
            "marker source mismatch failure kind");

        var unsafeMarkerSource = Path.Combine(fixtureRoot, "unsafe-marker-source");
        Directory.CreateDirectory(Path.Combine(unsafeMarkerSource, "module"));
        File.WriteAllText(
            Path.Combine(unsafeMarkerSource, "module", "safe.json"),
            "{\"safe\":true}");
        var unsafeMarkerTarget = Path.Combine(fixtureRoot, "unsafe-marker-target");
        var unsafeMarkerPath = Path.Combine(
            UserDataLayout.ResolveMigrationDirectory(unsafeMarkerTarget),
            LegacyModuleMigrationService.MarkerFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(unsafeMarkerPath)!);
        File.WriteAllText(
            unsafeMarkerPath,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["formatVersion"] = LegacyModuleMigrationService.MarkerFormatVersion,
                ["sourceDataDirectory"] = Path.GetFullPath(unsafeMarkerSource),
                ["completed"] = false,
                ["createdFiles"] = Array.Empty<string>(),
                ["pendingFiles"] = new[] { Path.Combine("nested", "..", "..", "escape.json") }
            }));

        var unsafeMarker = service.Migrate(unsafeMarkerSource, unsafeMarkerTarget);
        Equal(false, unsafeMarker.CompletedSuccessfully, "unsafe marker path fails closed");
        Equal(
            LegacyModuleMigrationFailureKind.InvalidMarker,
            unsafeMarker.Failures.Single().Kind,
            "unsafe marker failure kind");
        Equal(false, File.Exists(Path.Combine(fixtureRoot, "escape.json")), "unsafe marker cannot escape target root");

        var resumeSourceRoot = Path.Combine(fixtureRoot, "resume-source");
        var resumeSourceModules = Path.Combine(resumeSourceRoot, "module");
        var resumeTargetRoot = Path.Combine(fixtureRoot, "resume-target");
        var resumeTargetModules = UserDataLayout.ResolveModuleDirectory(resumeTargetRoot);
        Directory.CreateDirectory(resumeSourceModules);
        Directory.CreateDirectory(resumeTargetModules);
        File.WriteAllText(Path.Combine(resumeSourceModules, "resume.json"), "{\"resume\":true}");
        File.WriteAllText(Path.Combine(resumeTargetModules, "resume.json"), "{\"resume\":true}");
        var resumeMarkerPath = Path.Combine(
            UserDataLayout.ResolveMigrationDirectory(resumeTargetRoot),
            LegacyModuleMigrationService.MarkerFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(resumeMarkerPath)!);
        File.WriteAllText(
            resumeMarkerPath,
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["formatVersion"] = LegacyModuleMigrationService.MarkerFormatVersion,
                ["sourceDataDirectory"] = Path.GetFullPath(resumeSourceRoot),
                ["completed"] = false,
                ["createdFiles"] = Array.Empty<string>(),
                ["pendingFiles"] = new[] { "resume.json" }
            }));

        var resumed = service.Migrate(resumeSourceRoot, resumeTargetRoot);
        Equal(true, resumed.CompletedSuccessfully, "pending journal resumes safely");
        using var resumedMarker = JsonDocument.Parse(File.ReadAllText(resumeMarkerPath));
        Equal(true, resumedMarker.RootElement.GetProperty("completed").GetBoolean(), "resumed marker completes");
        Equal(
            "resume.json",
            resumedMarker.RootElement.GetProperty("createdFiles")[0].GetString(),
            "resumed pending file becomes created file");

        var missingSource = service.Migrate(Path.Combine(fixtureRoot, "missing"), Path.Combine(fixtureRoot, "unused"));
        Equal(true, !string.IsNullOrWhiteSpace(missingSource.SkippedReason), "missing legacy module directory is skipped");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void MacDiagnosticCommandContract()
{
    var target = new TargetWindow(
        new TargetIdentity(TargetPlatforms.MacOS, 42, 84),
        "/Applications/World of Warcraft/_retail_/World of Warcraft.app/Contents/MacOS/World of Warcraft",
        new TargetBounds(10, 20, 2, 1));
    var permissions = new PlatformPermissionSnapshot(
        new PlatformPermissionStatus(
            PlatformPermissionKind.ScreenCapture,
            PlatformPermissionState.Granted,
            RestartRequired: false),
        new PlatformPermissionStatus(
            PlatformPermissionKind.Accessibility,
            PlatformPermissionState.Granted,
            RestartRequired: false));
    var frame = new CapturedRegion(
        2,
        1,
        1,
        1,
        CapturedPixelFormat.Argb32,
        CapturedColorSpace.Srgb,
        new[] { Argb(1, 2, 3), Argb(4, 5, 6) });

    var statusEnvironment = new FakeMacDiagnosticEnvironment(target, permissions, frame);
    var statusOutput = new StringWriter();
    var statusError = new StringWriter();
    Equal(
        0,
        MacDiagnosticCommand.Run([], statusEnvironment, statusOutput, statusError),
        "default status exit code");
    Equal(1, statusEnvironment.LocateCount, "default status target checks");
    Equal(1, statusEnvironment.PermissionCount, "default status permission checks");
    Equal(1, statusEnvironment.AddOnPathCount, "default status addon path checks");
    Equal(0, statusEnvironment.CaptureCount, "default status captures");
    Equal(0, statusEnvironment.DecodeCount, "default status decodes");
    Equal(0, statusEnvironment.SendCount, "default status sends");
    Equal(0, statusEnvironment.ExportCount, "default status exports");

    var decodeEnvironment = new FakeMacDiagnosticEnvironment(target, permissions, frame);
    var decodeOutput = new StringWriter();
    Equal(
        0,
        MacDiagnosticCommand.Run(
            ["decode"],
            decodeEnvironment,
            decodeOutput,
            new StringWriter()),
        "decode metadata exit code");
    Equal(1, decodeEnvironment.DecodeCount, "explicit decode count");
    Equal(
        true,
        decodeOutput.ToString().Contains(
            "定位=1.000 ms, 捕获=2.000 ms, 解码=3.000 ms, 总计=6.000 ms",
            StringComparison.Ordinal),
        "decode outputs segmented scan timing");

    var captureEnvironment = new FakeMacDiagnosticEnvironment(target, permissions, frame);
    var captureOutput = new StringWriter();
    Equal(
        0,
        MacDiagnosticCommand.Run(
            ["capture"],
            captureEnvironment,
            captureOutput,
            new StringWriter()),
        "capture metadata exit code");
    Equal(1, captureEnvironment.CaptureCount, "explicit capture count");
    Equal(0, captureEnvironment.ExportCount, "capture without export writes no file");
    Equal(true, captureOutput.ToString().Contains("画面未保存", StringComparison.Ordinal), "capture no-save output");

    var exportEnvironment = new FakeMacDiagnosticEnvironment(target, permissions, frame);
    var exportOutput = new StringWriter();
    var exportPath = Path.Combine(Path.GetTempPath(), "shigure-sensitive-frame.ppm");
    exportEnvironment.BeforeExport = path =>
    {
        var currentOutput = exportOutput.ToString();
        Equal(true, currentOutput.Contains("敏感性警告", StringComparison.Ordinal), "warning before export");
        Equal(true, currentOutput.Contains(Path.GetFullPath(path), StringComparison.Ordinal), "path before export");
    };
    Equal(
        0,
        MacDiagnosticCommand.Run(
            ["capture", "--export", exportPath],
            exportEnvironment,
            exportOutput,
            new StringWriter()),
        "explicit export exit code");
    Equal(1, exportEnvironment.ExportCount, "explicit export count");

    var dryRunEnvironment = new FakeMacDiagnosticEnvironment(target, permissions, frame);
    var dryRunOutput = new StringWriter();
    Equal(
        0,
        MacDiagnosticCommand.Run(
            ["send", "--hotkey", "CTRL-1"],
            dryRunEnvironment,
            dryRunOutput,
            new StringWriter()),
        "send dry-run exit code");
    Equal(0, dryRunEnvironment.SendCount, "dry-run sends no event");
    Equal(true, dryRunOutput.ToString().Contains("dry-run", StringComparison.Ordinal), "dry-run output");

    Equal(
        0,
        MacDiagnosticCommand.Run(
            ["send", "--execute", "--hotkey", "CTRL-1"],
            dryRunEnvironment,
            new StringWriter(),
            new StringWriter()),
        "explicit send exit code");
    Equal(1, dryRunEnvironment.SendCount, "explicit send count");
    Equal(target.Identity, dryRunEnvironment.LastExpectedTarget, "explicit send stable target");

    var invalidEnvironment = new FakeMacDiagnosticEnvironment(target, permissions, frame);
    Equal(
        2,
        MacDiagnosticCommand.Run(
            ["capture", "--unknown"],
            invalidEnvironment,
            new StringWriter(),
            new StringWriter()),
        "invalid command exit code");
    Equal(0, invalidEnvironment.TotalOperationCount, "invalid command has no environment operations");
    Equal(
        2,
        MacDiagnosticCommand.Run(
            ["send", "--hotkey", "--execute"],
            invalidEnvironment,
            new StringWriter(),
            new StringWriter()),
        "missing hotkey value exit code");
    Equal(0, invalidEnvironment.TotalOperationCount, "missing hotkey value has no environment operations");
}

static void PpmFrameExportContract()
{
    var directory = Path.Combine(Path.GetTempPath(), $"shigure-ppm-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "frame.ppm");
        var frame = new CapturedRegion(
            2,
            1,
            1,
            1,
            CapturedPixelFormat.Argb32,
            CapturedColorSpace.Srgb,
            new[] { Argb(1, 2, 3), Argb(254, 253, 252) });

        PpmFrameExporter.Write(frame, path);
        var bytes = File.ReadAllBytes(path);
        var header = Encoding.ASCII.GetBytes("P6\n2 1\n255\n");
        Equal(header.Length + 6, bytes.Length, "ppm byte count");
        Equal(true, bytes.AsSpan(0, header.Length).SequenceEqual(header), "ppm header");
        Equal(true, bytes.AsSpan(header.Length).SequenceEqual(new byte[] { 1, 2, 3, 254, 253, 252 }), "ppm RGB bytes");
        Throws<IOException>(() => PpmFrameExporter.Write(frame, path), "ppm export must not overwrite");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void MacTargetSelectionFixture()
{
    var windows = new[]
    {
        new MacWindowDescriptor(10, 100, 1, new TargetBounds(0, 0, 100, 100)),
        new MacWindowDescriptor(11, 999, 0, new TargetBounds(0, 0, 100, 100)),
        new MacWindowDescriptor(12, 100, 0, new TargetBounds(10, 20, 1280, 720)),
        new MacWindowDescriptor(13, 100, 0, new TargetBounds(30, 40, 1920, 1080))
    };

    var selected = MacTargetSelection.FindFrontmost(windows, new HashSet<int> { 100 });
    Equal(12L, selected?.WindowId, "first eligible CGWindow in z order");
    Equal(
        null,
        MacTargetSelection.FindFrontmost(windows, new HashSet<int> { 200 }),
        "no candidate process match");
}

static void MacTargetLocatorCacheContract()
{
    if (!OperatingSystem.IsMacOS())
    {
        return;
    }

    var timeProvider = new ManualTimeProvider();
    var windowReads = 0;
    IReadOnlyList<MacWindowDescriptor> windows =
    [
        new MacWindowDescriptor(1001, 77, 0, new TargetBounds(10, 20, 1920, 1080))
    ];
    var locator = new MacTargetWindowLocator(
        () => new HashSet<int> { 77 },
        () =>
        {
            windowReads++;
            return windows;
        },
        processId => $"/Applications/WoW-{processId}.app",
        () => "World of Warcraft",
        timeProvider,
        TimeSpan.FromMilliseconds(200));

    var first = locator.FindFrontmostTarget();
    var cached = locator.FindFrontmostTarget();
    Equal(1001L, first?.Identity.WindowId, "initial target window");
    Equal(first, cached, "target snapshot is reused within the cache window");
    Equal(1, windowReads, "cached target avoids repeated CGWindow enumeration");

    timeProvider.Advance(TimeSpan.FromMilliseconds(201));
    windows =
    [
        new MacWindowDescriptor(1002, 77, 0, new TargetBounds(30, 40, 1920, 1080))
    ];
    var expired = locator.FindFrontmostTarget();
    Equal(1002L, expired?.Identity.WindowId, "expired target cache refreshes identity");
    Equal(2, windowReads, "expired target cache enumerates windows");

    windows =
    [
        new MacWindowDescriptor(1003, 77, 0, new TargetBounds(50, 60, 1920, 1080))
    ];
    var refreshed = ((IMacFreshTargetWindowLocator)locator).FindFrontmostTargetFresh();
    Equal(1003L, refreshed?.Identity.WindowId, "explicit refresh bypasses a live cache entry");
    Equal(3, windowReads, "explicit refresh enumerates windows");

    windows =
    [
        new MacWindowDescriptor(1004, 77, 0, new TargetBounds(70, 80, 1920, 1080))
    ];
    var output = new FakeMacKeyEventApi();
    var send = new MacKeySender(
        locator,
        new FakePlatformPermissionService(accessibilityReady: true),
        output,
        new FakeMacFrontmostApplicationProvider(77)).Send("A", refreshed!.Identity);
    Equal(KeySendFailureKind.TargetChanged, send.FailureKind, "send revalidation bypasses target cache");
    Equal(0, output.Posts.Count, "changed target sends no event");
    Equal(4, windowReads, "send performs a fresh window enumeration");
}

static void MacTargetNativeSmoke()
{
    if (!OperatingSystem.IsMacOS())
    {
        return;
    }

    var processPath = MacProcessPathResolver.TryResolve(Environment.ProcessId);
    Equal(true, !string.IsNullOrWhiteSpace(processPath), "current mac process path");
    Equal(true, File.Exists(processPath), "current mac process path exists");

    var windows = MacWindowCatalog.ReadOnScreenWindows();
    Equal(
        true,
        windows.All(window => window.WindowId > 0 && window.OwnerProcessId > 0),
        "CGWindow entries have stable ids and owner pids");

    var permissions = new MacPermissionService().Check();
    Equal(PlatformPermissionKind.ScreenCapture, permissions.ScreenCapture.Kind, "screen permission kind");
    Equal(PlatformPermissionKind.Accessibility, permissions.Accessibility.Kind, "accessibility permission kind");

    using var triggerInput = new MacTriggerInput();
    var triggerKey = triggerInput.Resolve("F12")
        ?? throw new InvalidOperationException("native mac trigger key was not resolved");
    _ = triggerInput.IsPressed(triggerKey);

    var target = new MacTargetWindowLocator(FindRepositoryRoot()).FindFrontmostTarget();
    if (target is not null)
    {
        Equal(true, target.Identity.IsValid, "located mac target identity");
        Equal(true, target.Bounds?.IsValid == true, "located mac target bounds");
        Equal(true, !string.IsNullOrWhiteSpace(target.ProcessPath), "located mac target process path");
    }
}

static void WorkspacePresentationContract()
{
    Equal(9, WorkspacePageCatalog.All.Count, "workspace page count");
    Equal(
        "General,Config,Macros,Modules,Status,Party,Logic,Logs,About",
        string.Join(',', WorkspacePageCatalog.All.Select(item => item.Page)),
        "workspace page order");
    Equal(
        "常用,编辑,编辑,编辑,监控,监控,监控,监控,系统",
        string.Join(',', WorkspacePageCatalog.All.Select(item => item.Group)),
        "workspace page groups");
    Equal(
        false,
        typeof(WorkspacePageCatalog).Assembly.GetReferencedAssemblies().Any(
            assembly => assembly.Name is "System.Windows.Forms" or "Avalonia"),
        "presentation assembly UI framework independence");

    var empty = RuntimeMonitorProjection.Create(new RenderSnapshot(
        false,
        null,
        null,
        null,
        null,
        null,
        null,
        "等待扫描",
        new Dictionary<string, object?>(),
        [],
        "simulated no frame"));
    Equal(new RuntimeDisplayRow("-", "状态", "等待游戏状态"), empty.State.Single(), "empty state row");
    Equal(new RuntimeDisplayRow("-", "光环", "无数据"), empty.Auras.Single(), "empty aura row");
    Equal(new RuntimeDisplayRow("-", "动态单位", "等待游戏状态"), empty.DynamicValues.Single(), "empty dynamic row");
    Equal(new RuntimeDisplayRow("-", "技能", "无数据"), empty.Spells.Single(), "empty spell row");
    Equal(new RuntimeDisplayRow("队伍", "无队伍数据"), empty.Party.Single(), "empty party row");
    Equal(new RuntimeDisplayRow("逻辑信息", "无推荐目标"), empty.Logic.Single(), "empty logic row");

    IReadOnlyDictionary<string, object?> spells = new Dictionary<string, object?>
    {
        ["寒冰箭"] = true
    };
    IReadOnlyDictionary<string, object?> auras = new Dictionary<string, object?>
    {
        ["冰冷智慧"] = 2
    };
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> group =
        new Dictionary<string, IReadOnlyDictionary<string, object?>>
        {
            ["1"] = new Dictionary<string, object?>
            {
                ["生命值"] = 83,
                ["可驱散"] = true
            }
        };
    var state = new GameState(new Dictionary<string, object?>
    {
        ["职业"] = "法师",
        ["已开启"] = true,
        ["队伍人数"] = 2,
        ["spells"] = spells,
        ["auras"] = auras,
        ["group"] = group,
        ["$internal"] = "hidden"
    });
    var populated = RuntimeMonitorProjection.Create(new RenderSnapshot(
        true,
        "法师",
        "冰霜",
        8,
        64,
        "冰法",
        state,
        "寒冰箭",
        new Dictionary<string, object?>
        {
            ["目标"] = "Unit 1",
            ["可施放"] = false
        },
        [new DynamicValueSnapshot("单位", "最低生命", "Unit 1")],
        null));

    Equal(new RuntimeDisplayRow("1", "匹配模块", "冰法"), populated.State[0], "matched module row");
    Equal(false, populated.State.Any(row => row.Second is "spells" or "auras" or "group" or "$internal"), "reserved state rows hidden");
    Equal("是", populated.State.Single(row => row.Second == "已开启").Third, "boolean state formatting");
    Equal(new RuntimeDisplayRow("1", "冰冷智慧", "2"), populated.Auras.Single(), "aura projection");
    Equal(new RuntimeDisplayRow("单位", "最低生命", "Unit 1"), populated.DynamicValues.Single(), "dynamic projection");
    Equal(new RuntimeDisplayRow("1", "寒冰箭", "是"), populated.Spells.Single(), "spell projection");
    Equal(new RuntimeDisplayRow("Unit 1", "生命值: 83  可驱散: 是"), populated.Party[0], "party summary");
    Equal(new RuntimeDisplayRow("Unit 2", "-"), populated.Party[1], "missing party unit");
    Equal("可施放,目标", string.Join(',', populated.Logic.Select(row => row.First)), "logic row ordering");
    Equal("否", populated.Logic[0].Second, "logic boolean formatting");
}

static void RuntimeSessionControllerContract()
{
    var factory = new HostRuntimeFactory();
    var coordinator = new RuntimeSessionCoordinator(factory);
    var leases = new List<TrackingRuntimeLease>();
    var controller = new RuntimeSessionController(
        coordinator,
        runtimeLeaseFactory: () => Add(leases, new TrackingRuntimeLease()));
    var statuses = new System.Collections.Concurrent.ConcurrentQueue<RuntimeSessionStatus>();
    var snapshots = new System.Collections.Concurrent.ConcurrentQueue<RenderSnapshot>();
    var logs = new System.Collections.Concurrent.ConcurrentQueue<RuntimeLogEntry>();
    controller.StatusChanged += _ => throw new InvalidOperationException("simulated observer failure");
    controller.StatusChanged += statuses.Enqueue;
    controller.SnapshotUpdated += snapshots.Enqueue;
    controller.LogAdded += logs.Enqueue;

    var initial = new AppOptions(
        "F12",
        SendMode.Switch,
        null,
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(25));
    controller.StartAsync(initial).GetAwaiter().GetResult();
    Equal(RuntimeSessionState.Running, controller.Status.State, "controller running state");
    Equal(1, leases.Count, "controller start acquires runtime lease");
    Equal(true, SpinWait.SpinUntil(() => !snapshots.IsEmpty, TimeSpan.FromSeconds(5)), "controller initial snapshot");

    controller.ToggleEnabled();
    Equal(
        true,
        SpinWait.SpinUntil(
            () => snapshots.Any(snapshot => snapshot.Enabled),
            TimeSpan.FromSeconds(5)),
        "controller enabled snapshot");
    Equal(true, logs.Any(entry => entry.Message == "逻辑已开启"), "controller enabled log");

    var restarted = initial with { Mode = SendMode.Click, ModuleId = "module-2" };
    controller.RestartAsync(restarted).GetAwaiter().GetResult();
    Equal(RuntimeSessionState.Running, controller.Status.State, "controller restarted state");
    Equal(restarted, controller.Status.Options, "controller restarted options");
    Equal(2, factory.Triggers.Count, "controller restart creates new runtime");
    Equal(1, leases.Count, "controller restart retains runtime lease");
    Equal(0, leases[0].DisposeCount, "controller restart keeps runtime lease active");
    Equal(1, factory.Triggers[0].DisposeCount, "controller restart disposes old runtime");

    controller.StopAsync().GetAwaiter().GetResult();
    Equal(RuntimeSessionState.Stopped, controller.Status.State, "controller stopped state");
    Equal(1, factory.Triggers[1].DisposeCount, "controller stop disposes current runtime");
    Equal(1, leases[0].DisposeCount, "controller stop releases runtime lease");
    Equal(true, statuses.Any(status => status.State == RuntimeSessionState.Starting), "controller starting status");
    Equal(true, statuses.Any(status => status.State == RuntimeSessionState.Stopping), "controller stopping status");

    controller.DisposeAsync().AsTask().GetAwaiter().GetResult();
    controller.DisposeAsync().AsTask().GetAwaiter().GetResult();

    var cancellationFactory = new BlockingRuntimeFactory();
    var cancellationCoordinator = new RuntimeSessionCoordinator(cancellationFactory);
    var cancellationLeases = new List<TrackingRuntimeLease>();
    var cancellationController = new RuntimeSessionController(
        cancellationCoordinator,
        runtimeLeaseFactory: () => Add(cancellationLeases, new TrackingRuntimeLease()));
    var blockedStart = Task.Run(
        () => cancellationCoordinator.StartAsync(initial, requestVersion: 0));

    try
    {
        Equal(
            true,
            cancellationFactory.FirstCreateEntered.Wait(TimeSpan.FromSeconds(5)),
            "controller cancellation fixture enters blocked runtime creation");

        using var cancellation = new CancellationTokenSource();
        var canceledStart = cancellationController.StartAsync(initial, cancellation.Token);
        Equal(1, cancellationLeases.Count, "controller canceled start acquires runtime lease");
        cancellation.Cancel();
        Throws<OperationCanceledException>(
            () => canceledStart.GetAwaiter().GetResult(),
            "controller start cancellation is propagated");

        cancellationFactory.ReleaseFirstCreate.Set();
        blockedStart.GetAwaiter().GetResult();
        Equal(1, cancellationLeases[0].DisposeCount, "controller canceled start releases runtime lease");
        Equal(RuntimeSessionState.Stopped, cancellationController.Status.State, "controller canceled start restores stopped state");
    }
    finally
    {
        cancellationFactory.ReleaseFirstCreate.Set();
        try
        {
            blockedStart.GetAwaiter().GetResult();
        }
        finally
        {
            cancellationController.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    var immediateFactory = new ImmediateStopRuntimeFactory();
    var immediateCoordinator = new RuntimeSessionCoordinator(immediateFactory);
    var immediateLeases = new List<TrackingRuntimeLease>();
    var immediateController = new RuntimeSessionController(
        immediateCoordinator,
        runtimeLeaseFactory: () => Add(immediateLeases, new TrackingRuntimeLease()));
    long stoppedSessionId = 0;
    long currentSessionIdAtStop = 0;
    immediateCoordinator.RuntimeStopped += sessionId =>
    {
        Volatile.Write(ref stoppedSessionId, sessionId);
        Volatile.Write(ref currentSessionIdAtStop, immediateCoordinator.CurrentSessionId.GetValueOrDefault());
    };

    immediateController.StartAsync(initial).GetAwaiter().GetResult();
    Equal(
        true,
        SpinWait.SpinUntil(
            () => immediateController.Status.State == RuntimeSessionState.Stopped
                && Volatile.Read(ref stoppedSessionId) != 0,
            TimeSpan.FromSeconds(5)),
        "controller observes an immediately stopped runtime");
    Equal(
        Volatile.Read(ref stoppedSessionId),
        Volatile.Read(ref currentSessionIdAtStop),
        "immediate stop event belongs to installed session");
    Equal(1, immediateLeases[0].DisposeCount, "immediate stop releases runtime lease");
    Equal(1, immediateFactory.Triggers[0].DisposeCount, "immediate stop releases trigger input");
    immediateController.DisposeAsync().AsTask().GetAwaiter().GetResult();
}

static void MacUiTechnicalSampleContract()
{
    var sampleRoot = Path.Combine(FindRepositoryRoot(), "Apps", "Shigure.MacUI");
    var project = XDocument.Load(Path.Combine(sampleRoot, "Shigure.MacUI.csproj"));
    var packages = project.Descendants("PackageReference").ToDictionary(
        element => element.Attribute("Include")?.Value
            ?? throw new InvalidDataException("Mac UI package reference is missing Include"),
        element => element.Attribute("Version")?.Value
            ?? throw new InvalidDataException("Mac UI package reference is missing Version"),
        StringComparer.Ordinal);

    Equal(3, packages.Count, "Mac UI package count");
    Equal("12.1.1", packages["Avalonia.Desktop"], "Avalonia desktop version");
    Equal("12.1.1", packages["Avalonia.Themes.Fluent"], "Avalonia theme version");
    Equal("12.1.1", packages["Avalonia.Controls.DataGrid"], "Avalonia data grid version");

    var projectReferences = project.Descendants("ProjectReference")
        .Select(element => (element.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/'))
        .Order(StringComparer.Ordinal)
        .ToArray();
    Equal(
        "../../Core/Shigure.Core.csproj,../../Presentation/Shigure.Presentation.csproj,../Shigure.MacApp/Shigure.MacApp.csproj",
        string.Join(',', projectReferences),
        "Mac UI project references");

    var sourceText = string.Join(
        '\n',
        Directory.EnumerateFiles(sampleRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path) is ".cs" or ".axaml")
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));
    var mainWindowText = File.ReadAllText(Path.Combine(sampleRoot, "MainWindow.axaml.cs"));
    var windowInteractionText = File.ReadAllText(Path.Combine(sampleRoot, "MacWindowInteraction.cs"));
    var compositionText = File.ReadAllText(Path.Combine(sampleRoot, "MacUiComposition.cs"));
    var programText = File.ReadAllText(Path.Combine(sampleRoot, "Program.cs"));
    var appText = File.ReadAllText(Path.Combine(sampleRoot, "App.axaml.cs"));
    var packagingRoot = Path.Combine(FindRepositoryRoot(), "Packaging", "macOS");
    var buildScriptText = File.ReadAllText(Path.Combine(packagingRoot, "build-app.sh"));
    var infoPlistText = File.ReadAllText(Path.Combine(packagingRoot, "Info.plist"));
    var coreStateStoreText = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Core", "MacUiStateStore.cs"));
    Equal(true, sourceText.Contains("IPlatformPermissionService", StringComparison.Ordinal), "Mac UI receives the shared permission contract");
    Equal(true, compositionText.Contains("new MacPermissionService()", StringComparison.Ordinal), "Mac UI composition owns the Mac permission service");
    Equal(true, compositionText.Contains("Resources\",\n            \"runtime-baseline", StringComparison.Ordinal), "Mac UI resolves packaged version resources outside Contents/MacOS");
    Equal(true, compositionText.Contains("CreateAddonSync(workspace.WorkspaceDirectory)", StringComparison.Ordinal),
        "Mac UI creates addon deployment from the runtime workspace");
    Equal(true, compositionText.Contains("addonSync.SynchronizeAll()", StringComparison.Ordinal),
        "Mac UI deploys the runtime addon at startup");
    Equal(true, compositionText.Contains("workspace.ProtocolConflictingFiles", StringComparison.Ordinal)
        && compositionText.Contains("FuyutsuiAddonSyncResult.Skipped", StringComparison.Ordinal),
        "Mac UI skips addon deployment when protocol files conflict");
    Equal(true, mainWindowText.Contains("游戏插件已同步", StringComparison.Ordinal),
        "Mac UI reports startup addon deployment");
    Equal(true, mainWindowText.Contains("RunButton.IsEnabled = false", StringComparison.Ordinal),
        "Mac UI disables runtime controls when protocol files conflict");
    Equal(false, mainWindowText.Contains("new MacPermissionService()", StringComparison.Ordinal), "Mac UI controls do not construct native permission services");
    Equal(true, mainWindowText.Contains("_permissions.Check()", StringComparison.Ordinal), "Mac UI exposes side-effect-free permission checks");
    Equal(true, mainWindowText.Contains("_permissions.Request(permission)", StringComparison.Ordinal), "Mac UI permission prompts require an explicit button path");
    Equal(true, mainWindowText.Contains("_permissionRequestGate.WaitAsync(0)", StringComparison.Ordinal), "Mac UI serializes explicit permission requests");
    Equal(true, mainWindowText.Contains("SetPermissionCommandsEnabled", StringComparison.Ordinal), "Mac UI disables permission controls while a request is active");
    Equal(true, appText.Contains("Shigure.MacUI.Application", StringComparison.Ordinal), "Mac UI owns an application-level single-instance lease");
    Equal(true, programText.Contains("args[0], \"--help\"", StringComparison.Ordinal), "Mac UI exposes a side-effect-free bundle smoke command");
    Equal(true, buildScriptText.Contains("Apps/Shigure.MacUI/Shigure.MacUI.csproj", StringComparison.Ordinal), "production packaging publishes Mac UI");
    Equal(true, buildScriptText.Contains("SHIGURE_RUNTIME_IDENTIFIER", StringComparison.Ordinal), "production packaging selects an explicit Mac RID");
    Equal(true, buildScriptText.Contains("runtime-baseline", StringComparison.Ordinal), "production packaging isolates version resources from executable code");
    Equal(false, buildScriptText.Contains("ShigureLauncher.swift", StringComparison.Ordinal), "production packaging no longer builds the AppKit launcher");
    Equal(true, buildScriptText.Contains("ShigureCapture.swift", StringComparison.Ordinal), "production packaging builds the native narrow-band stream bridge");
    Equal(true, infoPlistText.Contains("<string>Shigure.MacUI</string>", StringComparison.Ordinal), "bundle executable is the complete Mac UI");
    Equal(false, sourceText.Contains("MacScreenCapturer", StringComparison.Ordinal), "Mac UI excludes screen capture");
    Equal(false, sourceText.Contains("MacTriggerInput", StringComparison.Ordinal), "Mac UI excludes native input hooks");
    Equal(false, sourceText.Contains("MacKeySender", StringComparison.Ordinal), "Mac UI excludes native key output");
    Equal(true, sourceText.Contains("ConfigEditorView", StringComparison.Ordinal), "Mac UI provides the real class configuration editor");
    Equal(true, sourceText.Contains("ClassBlocksStore", StringComparison.Ordinal), "Mac UI edits shared ClassBlocks models");
    Equal(true, sourceText.Contains("ProjectConfigUpdateService", StringComparison.Ordinal), "Mac UI uses the shared save and deployment workflow");
    Equal(true, sourceText.Contains("nameof(RuleRow.Comment)", StringComparison.Ordinal)
        && sourceText.Contains("Comment = model.Comment", StringComparison.Ordinal),
        "Mac module editor round trips rule comments");
    Equal(false, sourceText.Contains("配置编辑样例", StringComparison.Ordinal), "Mac UI no longer exposes the in-memory config sample");
    Equal(true, sourceText.Contains("MacroEditorView", StringComparison.Ordinal), "Mac UI provides the real class macro editor");
    Equal(true, sourceText.Contains("ClassMacrosStore", StringComparison.Ordinal), "Mac UI edits shared ClassMacros models");
    Equal(true, sourceText.Contains("FuyutsuiKeymapConverter.ValidateCapacity", StringComparison.Ordinal), "Mac UI validates macro capacity through the shared converter");
    Equal(true, mainWindowText.Contains(
            "TimeSpan.FromMilliseconds(100),\n        TimeSpan.FromMilliseconds(250));",
            StringComparison.Ordinal),
        "Mac UI keeps logic at 100ms while throttling monitor rendering to 250ms");
    Equal(true, sourceText.Contains("CanUserSortColumns = false", StringComparison.Ordinal), "Mac UI preserves macro slot order by disabling grid sorting");
    Equal(false, sourceText.Contains("宏编辑样例", StringComparison.Ordinal), "Mac UI no longer exposes the in-memory macro sample");
    Equal(true, sourceText.Contains("_macroEditor.ConfirmDiscardBeforeExitAsync", StringComparison.Ordinal), "Mac UI confirms before discarding macro edits on exit");
    Equal(true, sourceText.Contains("ConfirmDiscardBeforeExitAsync", StringComparison.Ordinal), "Mac UI confirms before discarding config edits on exit");
    Equal(true, sourceText.Contains("await _pendingSave", StringComparison.Ordinal), "Mac UI waits for an active config save before exit");
    Equal(
        true,
        sourceText.Contains("_classId = option.ClassId;\n        _spec = null;\n        _specId = null;", StringComparison.Ordinal),
        "Mac UI resets the selected spec when switching class documents");
    Equal(true, sourceText.Contains("MacApplicationRuntimeFactory", StringComparison.Ordinal), "Mac UI reuses Mac application runtime composition");
    Equal(true, sourceText.Contains("RuntimeSessionController", StringComparison.Ordinal), "Mac UI consumes shared runtime controller");
    Equal(true, sourceText.Contains("SingleInstanceLease.TryAcquire", StringComparison.Ordinal), "Mac UI shares the runtime single-instance lease");
    Equal(
        true,
        sourceText.Contains("Interlocked.Exchange(ref _applicationLease, null)?.Dispose()", StringComparison.Ordinal),
        "Mac UI application lease release is idempotent across repeated exit callbacks");
    Equal(false, sourceText.Contains("_applicationLease.Dispose()", StringComparison.Ordinal),
        "Mac UI repeated exit callbacks never dereference a cleared application lease");
    Equal(true, sourceText.Contains("_runtime.StartAsync", StringComparison.Ordinal), "Mac UI starts the real runtime controller");
    Equal(true, sourceText.Contains("_runtime.RestartAsync", StringComparison.Ordinal), "Mac UI restarts after setting changes");
    Equal(true, sourceText.Contains("_runtime.StopAsync", StringComparison.Ordinal), "Mac UI stops the real runtime controller");
    Equal(true, sourceText.Contains("RuntimeMonitorProjection.Create(snapshot)", StringComparison.Ordinal), "Mac UI projects live snapshots");
    Equal(true, mainWindowText.Contains("_pendingRuntimeSnapshot = snapshot", StringComparison.Ordinal),
        "Mac UI coalesces live snapshots instead of queuing every frame");
    Equal(true, mainWindowText.Contains("RowsMatch(target, rows)", StringComparison.Ordinal),
        "Mac UI skips unchanged monitor collection rebuilds");
    Equal(false, sourceText.Contains("_simulatedRunning", StringComparison.Ordinal), "Mac UI has no simulated runtime state");
    Equal(false, sourceText.Contains("CreateSampleSnapshot", StringComparison.Ordinal), "Mac UI has no fixed runtime snapshot");
    Equal(true, sourceText.Contains("_store.Save(", StringComparison.Ordinal), "Mac UI module editor persists through store");
    Equal(true, sourceText.Contains("_store.Delete(", StringComparison.Ordinal), "Mac UI module editor deletes through store");
    Equal(true, sourceText.Contains("PromptModuleNameAsync", StringComparison.Ordinal), "Mac module creation uses a cancellable text-input dialog");
    Equal(true, sourceText.Contains("AutomationProperties.SetName(input, \"新模块名称\")", StringComparison.Ordinal), "Mac module-name dialog exposes an accessible IME input");
    Equal(true, sourceText.Contains("_baseline = Fingerprint(BuildModule(setVersion: false))", StringComparison.Ordinal), "Mac module dirty tracking uses the normalized editor projection");
    Equal(true, sourceText.Contains("ModuleDependencyService", StringComparison.Ordinal), "Mac UI composes the shared dependency service");
    Equal(true, sourceText.Contains("_captureDependencies(module)", StringComparison.Ordinal), "Mac UI captures dependencies before module save");
    Equal(true, sourceText.Contains("导入全部模块依赖", StringComparison.Ordinal), "Mac UI exposes dependency import");
    Equal(true, sourceText.Contains("HasUnsavedChanges == true", StringComparison.Ordinal), "Mac UI guards dependency import from editor drafts");
    Equal(true, sourceText.Contains("_moduleEditor?.HasUnsavedChanges == true", StringComparison.Ordinal), "Mac UI protects module drafts from external refresh");
    Equal(true, sourceText.Contains("ReloadFromAddonAsync", StringComparison.Ordinal), "Mac UI reloads editors after dependency import");
    Equal(true, sourceText.Contains("_moduleEditor.ConfirmDiscardBeforeExitAsync", StringComparison.Ordinal), "Mac UI confirms before discarding module edits on exit");
    Equal(false, sourceText.Contains("当前为只读依赖摘要", StringComparison.Ordinal), "Mac UI no longer labels dependencies as read-only only");
    Equal(false, sourceText.Contains("模块编辑样例 · 内存已保存", StringComparison.Ordinal), "Mac UI has no simulated module save");
    Equal(true, sourceText.Contains("PrepareForShutdownAsync", StringComparison.Ordinal), "Mac UI explicit quit bypasses hide-on-close");
    Equal(true, sourceText.Contains("await _runtime.DisposeAsync()", StringComparison.Ordinal), "Mac UI explicit quit waits for runtime disposal");
    Equal(true, sourceText.Contains("app.RequestQuitAsync()", StringComparison.Ordinal), "Mac window close enters the explicit quit path");
    Equal(false, sourceText.Contains("e.Cancel = true;\n                Hide();", StringComparison.Ordinal), "Mac window close no longer hides the process");
    Equal(true, sourceText.Contains("IsMiddleButtonPressed", StringComparison.Ordinal), "Mac UI captures the middle mouse button");
    Equal(true, sourceText.Contains("PointerWheelChangedEvent", StringComparison.Ordinal), "Mac UI captures wheel triggers at window scope");
    Equal(true, sourceText.Contains("_uiState.TriggerKey = _triggerKey", StringComparison.Ordinal), "Mac UI persists trigger changes");
    Equal(true, sourceText.Contains("_uiState.SendMode = _sendMode", StringComparison.Ordinal), "Mac UI persists send mode changes");
    Equal(true, sourceText.Contains("Interval = TimeSpan.FromSeconds(1)", StringComparison.Ordinal), "Mac logic status toast lasts one second");
    Equal(true, sourceText.Contains("ShowLogicToast(snapshot.Enabled)", StringComparison.Ordinal), "Mac UI shows logic status on actual state changes");
    Equal(true, sourceText.Contains("ShowActivated = false", StringComparison.Ordinal), "Mac logic status toast does not steal focus");
    Equal(true, windowInteractionText.Contains("setIgnoresMouseEvents:", StringComparison.Ordinal), "Mac logic status toast uses native click-through");
    Equal(true, sourceText.Contains("MacUiStateStore", StringComparison.Ordinal), "Mac UI composes the versioned state store");
    Equal(true, coreStateStoreText.Contains("mac-ui-state-v1.json", StringComparison.Ordinal), "Mac UI state uses a Mac-specific cache file");
    Equal(true, sourceText.Contains("WindowState != WindowState.Normal", StringComparison.Ordinal), "Mac UI does not persist full-screen bounds");
    Equal(true, sourceText.Contains("IsScreenFilling(this)", StringComparison.Ordinal), "Mac UI rejects native full-screen geometry reported as normal");
    Equal(true, sourceText.Contains("TimeSpan.FromMilliseconds(500)", StringComparison.Ordinal), "Mac UI captures stable window bounds after transitions");
    Equal(true, sourceText.Contains("PointToScreen", StringComparison.Ordinal), "Mac overlay maps pointer movement to screen coordinates");
    Equal(true, sourceText.Contains("e.Pointer.Capture(control)", StringComparison.Ordinal), "Mac overlay captures pointer during dragging and resizing");
    Equal(true, sourceText.Contains("StartOverlayDrag", StringComparison.Ordinal), "Mac overlay exposes a dedicated drag operation");
    Equal(true, sourceText.Contains("_overlayDragPointerStart = control.PointToScreen", StringComparison.Ordinal), "Mac overlay drag starts in screen coordinates");
    Equal(true, sourceText.Contains("_overlayDragPointerCurrent = control.PointToScreen", StringComparison.Ordinal), "Mac overlay drag retains the last screen-coordinate move");
    Equal(true, sourceText.Contains("e.Pointer.Capture(null)", StringComparison.Ordinal), "Mac overlay releases pointer capture before moving");
    Equal(true, sourceText.Contains("_overlay.RenderScaling", StringComparison.Ordinal), "Mac overlay converts resize deltas through display scaling");
    Equal(true, sourceText.Contains("MacOverlayLayout.Vertical", StringComparison.Ordinal), "Mac overlay exposes vertical layout");
    Equal(true, sourceText.Contains("TrackColumnGrid", StringComparison.Ordinal), "Mac UI persists monitor column widths");
    Equal(true, sourceText.Contains("ModuleMarketplaceClient.WebsiteUrl", StringComparison.Ordinal), "Mac UI reuses the shared module website URL");
    Equal(true, sourceText.Contains("LaunchUriAsync", StringComparison.Ordinal), "Mac UI opens the module website through the platform launcher");
    Equal(true, sourceText.Contains("LaunchDirectoryInfoAsync", StringComparison.Ordinal), "Mac UI opens user directories through the platform launcher");
    Equal(true, sourceText.Contains("打开模块目录", StringComparison.Ordinal), "Mac UI exposes the module directory command");
    Equal(true, sourceText.Contains("打开配置目录", StringComparison.Ordinal), "Mac UI exposes the config directory command");
    Equal(false, sourceText.Contains("finally\n        {\n            desktop.Shutdown();", StringComparison.Ordinal), "Mac UI canceling quit cannot fall through a finally shutdown");
    Equal(true, sourceText.Contains("Gesture=\"Meta+Q\"", StringComparison.Ordinal), "Mac UI exposes Command-Q quit");
}

static void MacPackagingReleaseContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var packagingRoot = Path.Combine(repositoryRoot, "Packaging", "macOS");
    var buildScriptText = File.ReadAllText(Path.Combine(packagingRoot, "build-app.sh"));
    var localSigningScriptText = File.ReadAllText(Path.Combine(packagingRoot, "ensure-local-signing-identity.sh"));
    var notarizeScriptText = File.ReadAllText(Path.Combine(packagingRoot, "notarize-app.sh"));
    var infoPlistText = File.ReadAllText(Path.Combine(packagingRoot, "Info.plist"));
    var entitlements = XDocument.Load(Path.Combine(packagingRoot, "Shigure.entitlements"));
    var entitlementKeys = entitlements.Descendants("key")
        .Select(element => element.Value)
        .ToArray();
    var versionProperties = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"))
        .Descendants("PropertyGroup")
        .Elements()
        .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);
    var projectVersionOverrides = new[]
        {
            "Apps/Shigure.MacApp/Shigure.MacApp.csproj",
            "Apps/Shigure.MacUI/Shigure.MacUI.csproj"
        }
        .SelectMany(path => XDocument.Load(Path.Combine(repositoryRoot, path)).Descendants())
        .Where(element => element.Name.LocalName is "Version" or "AssemblyVersion" or "FileVersion" or "InformationalVersion")
        .Select(element => element.Name.LocalName)
        .ToArray();
    var macPayloadSigningStart = buildScriptText.LastIndexOf("while IFS= read -r -d '' nested_code", StringComparison.Ordinal);
    var macPayloadSigningEnd = buildScriptText.IndexOf(
        "done < <(find \"$macos_path\" -type f",
        macPayloadSigningStart,
        StringComparison.Ordinal);
    var macPayloadSigningBlock = buildScriptText[macPayloadSigningStart..macPayloadSigningEnd];

    Equal("1", versionProperties["ShigureVersionMajor"], "version authority defines the major component");
    Equal("2", versionProperties["ShigureVersionMinor"], "version authority defines the minor component");
    Equal("1", versionProperties["ShigureVersionPatch"], "version authority defines the patch component");
    Equal("7", versionProperties["ShigureBuildNumber"], "version authority defines the global build number");
    Equal("$(ShigureVersionMajor).$(ShigureVersionMinor).$(ShigureVersionPatch)", versionProperties["ShigureMarketingVersion"], "marketing version is derived from components");
    Equal("$(ShigureMarketingVersion).$(ShigureBuildNumber)", versionProperties["ShigureVersion"], ".NET version is derived from marketing and build versions");
    Equal("$(ShigureVersionMajor).$(ShigureVersionMinor).$(ShigureBuildNumber)", versionProperties["ShigureBundleVersion"], "Apple bundle version is derived from the global build number");
    Equal("$(ShigureVersion)", versionProperties["Version"], "MSBuild Version consumes the shared version");
    Equal("$(ShigureVersion)", versionProperties["AssemblyVersion"], "assembly version consumes the shared version");
    Equal("$(ShigureVersion)", versionProperties["FileVersion"], "file version consumes the shared version");
    Equal("$(ShigureVersion)", versionProperties["InformationalVersion"], "informational version consumes the shared version");
    Equal(0, projectVersionOverrides.Length, $"projects do not override shared version properties: {string.Join(',', projectVersionOverrides)}");
    Equal(true, infoPlistText.Contains("$(ShigureMarketingVersion)", StringComparison.Ordinal), "Info.plist keeps a marketing-version placeholder");
    Equal(true, infoPlistText.Contains("$(ShigureBundleVersion)", StringComparison.Ordinal), "Info.plist keeps a bundle-version placeholder");
    Equal(false, infoPlistText.Contains("1.2.1", StringComparison.Ordinal), "Info.plist contains no copied version literal");
    Equal(true, buildScriptText.Contains("-getProperty:ShigureMarketingVersion", StringComparison.Ordinal), "bundle build reads the evaluated marketing version");
    Equal(true, buildScriptText.Contains("-getProperty:ShigureBundleVersion", StringComparison.Ordinal), "bundle build reads the evaluated Apple bundle version");
    Equal(true, buildScriptText.Contains("plutil -replace CFBundleShortVersionString", StringComparison.Ordinal), "bundle build writes the marketing version structurally");
    Equal(true, buildScriptText.Contains("plutil -replace CFBundleVersion", StringComparison.Ordinal), "bundle build writes the Apple bundle version structurally");
    Equal(true, buildScriptText.Contains("plutil -extract CFBundleShortVersionString", StringComparison.Ordinal), "bundle build reads back the marketing version");
    Equal(true, buildScriptText.Contains("plutil -extract CFBundleVersion", StringComparison.Ordinal), "bundle build reads back the Apple bundle version");
    Equal(
        "com.apple.security.cs.allow-jit",
        string.Join(',', entitlementKeys),
        "release entitlements contain only the .NET JIT requirement");
    Equal(true, buildScriptText.Contains("--options runtime", StringComparison.Ordinal), "persistent signing enables Hardened Runtime");
    Equal(true, buildScriptText.Contains("Shigure.entitlements", StringComparison.Ordinal), "persistent signing embeds release entitlements");
    Equal(true, buildScriptText.Contains("find \"$macos_path\" -type f", StringComparison.Ordinal), "persistent signing enumerates nested code");
    Equal(false, macPayloadSigningBlock.Contains("continue", StringComparison.Ordinal), "persistent signing does not skip managed payload files");
    Equal(false, buildScriptText.Contains("--deep --sign \"$codesign_identity\"", StringComparison.Ordinal), "persistent signing does not use deprecated deep signing");
    Equal(true, buildScriptText.Contains("signed_entitlements", StringComparison.Ordinal), "persistent signing reads back embedded entitlements");
    Equal(true, buildScriptText.Contains("ensure-local-signing-identity.sh", StringComparison.Ordinal), "ordinary builds resolve the stable local signing identity");
    Equal(true, buildScriptText.Contains("using_local_signing_identity\" == true", StringComparison.Ordinal), "local self-signed builds use a separate signing branch");
    Equal(true, buildScriptText.Contains("sign_executable_code true", StringComparison.Ordinal), "application executables use the explicit signing branch");
    Equal(true, buildScriptText.Contains("using_local_signing_identity\" == false", StringComparison.Ordinal), "Hardened Runtime validation remains mandatory for Apple identities");
    Equal(true, buildScriptText.Contains("SHIGURE_CODESIGN_IDENTITY=-", StringComparison.Ordinal), "ad-hoc signing requires an explicit opt-in");
    Equal(true, buildScriptText.Contains("designated_requirement", StringComparison.Ordinal), "persistent signing reads back the designated requirement");
    Equal(true, buildScriptText.Contains("*\"cdhash\"*", StringComparison.Ordinal), "persistent signing rejects version-bound designated requirements");
    Equal(true, localSigningScriptText.Contains("extendedKeyUsage=codeSigning", StringComparison.Ordinal), "local identity is restricted to code signing");
    Equal(true, localSigningScriptText.Contains("security add-trusted-cert -r trustRoot -p codeSign", StringComparison.Ordinal), "local code-signing trust is explicit");
    Equal(true, localSigningScriptText.Contains("-x \\", StringComparison.Ordinal), "local private key is imported as non-extractable");
    Equal(true, localSigningScriptText.Contains("-T /usr/bin/codesign", StringComparison.Ordinal), "local private-key access is limited to codesign");
    Equal(true, notarizeScriptText.Contains("SHIGURE_NOTARYTOOL_PROFILE", StringComparison.Ordinal), "notarization requires an explicit keychain profile");
    Equal(true, notarizeScriptText.Contains("Authority=Developer ID Application:", StringComparison.Ordinal), "notarization rejects non-distribution identities");
    Equal(true, notarizeScriptText.Contains("notarytool submit", StringComparison.Ordinal), "notarization submits through notarytool");
    Equal(true, notarizeScriptText.Contains("--keychain-profile", StringComparison.Ordinal), "notarization reads credentials from Keychain");
    Equal(true, notarizeScriptText.Contains("--wait", StringComparison.Ordinal), "notarization waits for a final service result");
    Equal(true, notarizeScriptText.Contains("notarytool log", StringComparison.Ordinal), "notarization preserves the service log");
    Equal(true, notarizeScriptText.Contains("plutil -extract issues", StringComparison.Ordinal), "notarization rejects service log issues");
    Equal(true, notarizeScriptText.Contains("stapler staple", StringComparison.Ordinal), "notarization staples the accepted ticket");
    Equal(true, notarizeScriptText.Contains("stapler validate", StringComparison.Ordinal), "notarization validates the stapled ticket");
    Equal(true, notarizeScriptText.Contains("spctl --assess", StringComparison.Ordinal), "notarization performs a local Gatekeeper assessment");
    Equal(false, notarizeScriptText.Contains("--password", StringComparison.Ordinal), "notarization never accepts a plaintext password argument");
}

static void MacUpdaterReleaseContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var packagingRoot = Path.Combine(repositoryRoot, "Packaging", "macOS");
    var metadata = JsonNode.Parse(File.ReadAllText(Path.Combine(packagingRoot, "Sparkle.json")))
        ?? throw new InvalidDataException("Sparkle metadata is empty");
    var buildScriptText = File.ReadAllText(Path.Combine(packagingRoot, "build-app.sh"));
    var fetchScriptText = File.ReadAllText(Path.Combine(packagingRoot, "fetch-sparkle.sh"));
    var appcastScriptText = File.ReadAllText(Path.Combine(packagingRoot, "generate-appcast.sh"));
    var appText = File.ReadAllText(Path.Combine(repositoryRoot, "Apps", "Shigure.MacUI", "App.axaml.cs"));
    var appMenuText = File.ReadAllText(Path.Combine(repositoryRoot, "Apps", "Shigure.MacUI", "App.axaml"));
    var updateControllerText = File.ReadAllText(Path.Combine(repositoryRoot, "Apps", "Shigure.MacUI", "SparkleUpdateController.cs"));
    var versionProperties = XDocument.Load(Path.Combine(repositoryRoot, "Directory.Build.props"))
        .Descendants("PropertyGroup")
        .Elements()
        .ToDictionary(element => element.Name.LocalName, element => element.Value, StringComparer.Ordinal);

    Equal(1, metadata["schemaVersion"]!.GetValue<int>(), "Sparkle metadata schema version");
    Equal("2.9.6", metadata["version"]!.GetValue<string>(), "Sparkle version is pinned");
    Equal(
        "https://github.com/sparkle-project/Sparkle/releases/download/2.9.6/Sparkle-2.9.6.tar.xz",
        metadata["archiveUrl"]!.GetValue<string>(),
        "Sparkle archive uses the official release asset");
    Equal(
        "52bf9e88cdd972fc0c81501377a880e90d47031bd8ca5462488f843e2609e192",
        metadata["archiveSha256"]!.GetValue<string>(),
        "Sparkle archive SHA-256 is pinned");
    Equal(0, metadata["maximumDeltas"]!.GetValue<int>(), "initial updater release disables deltas");
    Equal("1.2.7", versionProperties["ShigureMinimumUpdateBundleVersion"], "minimum updater version policy is defined once");

    Equal(true, fetchScriptText.Contains("curl --fail --location --retry 3", StringComparison.Ordinal), "Sparkle fetch fails closed");
    Equal(true, fetchScriptText.Contains("archiveSha256", StringComparison.Ordinal), "Sparkle fetch verifies the pinned digest");
    Equal(true, buildScriptText.Contains("SHIGURE_SPARKLE_ARCHIVE", StringComparison.Ordinal), "bundle requires an explicit Sparkle archive");
    Equal(true, buildScriptText.Contains("SHIGURE_SPARKLE_FEED_URL", StringComparison.Ordinal), "bundle requires an explicit architecture feed");
    Equal(true, buildScriptText.Contains("SHIGURE_SPARKLE_PUBLIC_ED_KEY", StringComparison.Ordinal), "bundle requires an explicit public EdDSA key");
    Equal(true, buildScriptText.Contains("$sparkle_feed_url\" != https://*", StringComparison.Ordinal), "bundle rejects non-HTTPS feeds");
    Equal(true, buildScriptText.Contains("openssl base64 -d -A", StringComparison.Ordinal), "bundle validates the public key bytes");
    Equal(true, buildScriptText.Contains("SURequireSignedFeed -bool true", StringComparison.Ordinal), "bundle requires signed feeds");
    Equal(true, buildScriptText.Contains("SUVerifyUpdateBeforeExtraction -bool true", StringComparison.Ordinal), "bundle verifies updates before extraction");
    Equal(true, buildScriptText.Contains("SUEnableAutomaticChecks -bool false", StringComparison.Ordinal), "bundle disables automatic checks by default");
    Equal(true, buildScriptText.Contains("SUAutomaticallyUpdate -bool false", StringComparison.Ordinal), "bundle disables automatic installation");
    Equal(true, buildScriptText.Contains("SUAllowsAutomaticUpdates -bool false", StringComparison.Ordinal), "bundle disallows enabling automatic installation");
    Equal(true, buildScriptText.Contains("ThirdPartyNotices/Sparkle-LICENSE", StringComparison.Ordinal), "bundle retains the Sparkle license");
    Equal(true, buildScriptText.Contains("find \"$frameworks_path/Sparkle.framework\" -type d -name '*.xpc'", StringComparison.Ordinal), "persistent signing signs Sparkle XPC services explicitly");
    Equal(true, buildScriptText.Contains("find \"$frameworks_path/Sparkle.framework\" -type d -name '*.app'", StringComparison.Ordinal), "persistent signing signs Sparkle helper apps explicitly");
    Equal(true, buildScriptText.Contains("\"$frameworks_path/Sparkle.framework\"", StringComparison.Ordinal), "persistent signing signs the Sparkle framework");

    Equal(true, appMenuText.Contains("Header=\"检查更新…\"", StringComparison.Ordinal), "Mac UI exposes manual update checks");
    Equal(true, appText.Contains("SparkleUpdateController.TryCreate", StringComparison.Ordinal), "Mac UI initializes the updater controller");
    Equal(true, appText.Contains("_updateController.CheckForUpdates()", StringComparison.Ordinal), "Mac UI only checks from the explicit command");
    Equal(true, updateControllerText.Contains("SPUStandardUpdaterController", StringComparison.Ordinal), "Mac UI uses Sparkle's standard updater controller");
    Equal(true, updateControllerText.Contains("checkForUpdates:", StringComparison.Ordinal), "Mac UI invokes Sparkle's user-initiated check API");
    Equal(false, updateControllerText.Contains("checkForUpdatesInBackground", StringComparison.Ordinal), "Mac UI never forces a background check");

    Equal(true, appcastScriptText.Contains("--account \"$keychain_account\"", StringComparison.Ordinal), "appcast signing reads the key from Keychain");
    Equal(false, appcastScriptText.Contains("--ed-key-file", StringComparison.Ordinal), "appcast script rejects private-key files");
    Equal(false, appcastScriptText.Contains("PRIVATE_KEY", StringComparison.Ordinal), "appcast script has no private-key environment variable");
    Equal(true, appcastScriptText.Contains("--minimum-update-version \"$minimum_update_version\"", StringComparison.Ordinal), "appcast consumes the shared minimum version");
    Equal(true, appcastScriptText.Contains("minimum_order > bundle_order", StringComparison.Ordinal), "appcast rejects a minimum version above the current bundle");
    Equal(true, appcastScriptText.Contains("--maximum-deltas \"$maximum_deltas\"", StringComparison.Ordinal), "appcast consumes the no-delta policy");
    Equal(true, appcastScriptText.Contains("SHIGURE_SPARKLE_DOWNLOAD_URL_PREFIX", StringComparison.Ordinal), "appcast requires an explicit download URL prefix");
}

static void MacReleaseStagingContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var scriptPath = Path.Combine(repositoryRoot, "Packaging", "macOS", "prepare-release.sh");
    var scriptText = File.ReadAllText(scriptPath);

    Equal(true, scriptText.Contains("git -C \"$repository_root\" rev-parse --verify HEAD", StringComparison.Ordinal), "release staging requires a real Git HEAD");
    Equal(true, scriptText.Contains("if ! worktree_status=", StringComparison.Ordinal), "release staging fails closed when Git status cannot be read");
    Equal(true, scriptText.Contains("status --porcelain=v1 --untracked-files=all", StringComparison.Ordinal), "release staging requires a fully clean worktree");
    Equal(true, scriptText.Contains("expected_tag=\"v$marketing_version\"", StringComparison.Ordinal), "release tag derives from the shared marketing version");
    Equal(true, scriptText.Contains("rev-list -n 1 \"refs/tags/$release_tag\"", StringComparison.Ordinal), "release tag must resolve to the current commit");
    Equal(true, scriptText.Contains("HEAD^{tree}", StringComparison.Ordinal), "release provenance records the source tree");
    Equal(true, scriptText.Contains("baselineSha", StringComparison.Ordinal), "release provenance distinguishes the upstream baseline commit");
    Equal(true, scriptText.Contains("baselineTreeSha", StringComparison.Ordinal), "release provenance distinguishes the upstream baseline tree");

    Equal(true, scriptText.Contains("Authority=Developer ID Application:", StringComparison.Ordinal), "release staging requires Developer ID Application");
    Equal(true, scriptText.Contains("codesign --verify --deep --strict", StringComparison.Ordinal), "release staging recursively verifies signatures");
    Equal(true, scriptText.Contains("xcrun stapler validate", StringComparison.Ordinal), "release staging requires a stapled ticket");
    Equal(true, scriptText.Contains("spctl --assess --type execute", StringComparison.Ordinal), "release staging requires Gatekeeper acceptance");
    Equal(true, scriptText.Contains("Mach-O 64-bit executable arm64", StringComparison.Ordinal), "release staging verifies the arm64 apphost");
    Equal(true, scriptText.Contains("Mach-O 64-bit executable x86_64", StringComparison.Ordinal), "release staging verifies the x64 apphost");
    Equal(true, scriptText.Contains("com.apple.security.cs.allow-jit", StringComparison.Ordinal), "release staging verifies the .NET JIT entitlement");
    Equal(true, scriptText.Contains("framework_team_identifier\" != \"$team_identifier", StringComparison.Ordinal), "release staging requires Sparkle and the app to share one signing team");
    Equal(true, scriptText.Contains("arm64_team\" != \"$x64_team", StringComparison.Ordinal), "release staging requires one signing team");

    Equal(true, scriptText.Contains("SURequireSignedFeed", StringComparison.Ordinal), "release staging requires signed Sparkle feeds");
    Equal(true, scriptText.Contains("SUVerifyUpdateBeforeExtraction", StringComparison.Ordinal), "release staging requires verification before extraction");
    Equal(true, scriptText.Contains("arm64_public_key_sha256\" != \"$x64_public_key_sha256", StringComparison.Ordinal), "release staging requires one EdDSA public key");
    Equal(true, scriptText.Contains("arm64_feed\" == \"$x64_feed", StringComparison.Ordinal), "release staging rejects a shared architecture feed");
    Equal(true, scriptText.Contains("status\" != \"Accepted", StringComparison.Ordinal), "release staging requires accepted notarization logs");
    Equal(true, scriptText.Contains("issues\" != \"[]", StringComparison.Ordinal), "release staging rejects notarization issues");

    Equal(true, scriptText.Contains("release-provenance.json", StringComparison.Ordinal), "release staging emits provenance JSON");
    Equal(true, scriptText.Contains("SHA256SUMS", StringComparison.Ordinal), "release staging emits checksums");
    Equal(true, scriptText.Contains("shasum -a 256 -c SHA256SUMS", StringComparison.Ordinal), "release staging verifies its checksum file");
    Equal(true, scriptText.Contains("ditto -c -k --sequesterRsrc --keepParent", StringComparison.Ordinal), "release staging archives complete app bundles");
    Equal(true, scriptText.Contains("mv -n \"$staging_directory\" \"$output_directory\"", StringComparison.Ordinal), "release staging publishes locally without overwrite");
    Equal(false, scriptText.Contains("gh release", StringComparison.Ordinal), "release staging never creates a GitHub Release");
    Equal(false, scriptText.Contains("GITHUB_TOKEN", StringComparison.Ordinal), "release staging never reads a GitHub token");
    Equal(false, scriptText.Contains("curl ", StringComparison.Ordinal), "release staging has no network upload path");
}

static void ContractSurfaceManifest()
{
    var repositoryRoot = FindRepositoryRoot();
    var manifestPath = Path.Combine(
        repositoryRoot,
        "Tests",
        "Shigure.Core.ContractTests",
        "ContractSurfaceManifest.json");
    var manifest = JsonSerializer.Deserialize<ContractSurfaceManifestModel>(
        File.ReadAllText(manifestPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidDataException("contract surface manifest is empty");

    Equal(1, manifest.SchemaVersion, "contract surface manifest schema version");

    var knownCategories = new HashSet<string>(StringComparer.Ordinal)
    {
        "shared-contract",
        "configuration-schema",
        "fuyutsui-authority",
        "fuyutsui-generation",
        "macos-platform"
    };
    var expectedPaths = new HashSet<string>(StringComparer.Ordinal);
    var errors = new List<string>();

    foreach (var (path, expected) in manifest.Files)
    {
        var normalizedPath = path.Replace('\\', '/');
        if (!IsSafeRelativePath(normalizedPath))
        {
            errors.Add($"unsafe path: {path}");
            continue;
        }

        if (!expectedPaths.Add(normalizedPath))
        {
            errors.Add($"duplicate path: {normalizedPath}");
            continue;
        }

        if (!knownCategories.Contains(expected.Category))
        {
            errors.Add($"unknown category for {normalizedPath}: {expected.Category}");
        }

        if (expected.Sha256.Length != 64 || expected.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            errors.Add($"invalid SHA-256 for {normalizedPath}: {expected.Sha256}");
            continue;
        }

        var fullPath = Path.Combine(repositoryRoot, normalizedPath);
        if (!File.Exists(fullPath))
        {
            errors.Add($"missing file: {normalizedPath}");
            continue;
        }

        using var stream = File.OpenRead(fullPath);
        var actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!actualHash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"changed file: {normalizedPath} (actual {actualHash})");
        }
    }

    foreach (var coveredRoot in manifest.CoveredRoots)
    {
        var normalizedRoot = coveredRoot.Replace('\\', '/').TrimEnd('/');
        if (!IsSafeRelativePath(normalizedRoot))
        {
            errors.Add($"unsafe covered root: {coveredRoot}");
            continue;
        }

        var fullRoot = Path.Combine(repositoryRoot, normalizedRoot);
        if (!Directory.Exists(fullRoot))
        {
            errors.Add($"missing covered root: {normalizedRoot}");
            continue;
        }

        foreach (var fullPath in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/');
            if (relativePath.Split('/').Any(part => part is "bin" or "obj"))
            {
                continue;
            }

            if (!expectedPaths.Contains(relativePath))
            {
                errors.Add($"unlisted file under {normalizedRoot}: {relativePath}");
            }
        }
    }

    if (errors.Count > 0)
    {
        throw new InvalidOperationException(
            "contract surface drift detected:\n - " + string.Join("\n - ", errors) +
            "\nReview the change, then update ContractSurfaceManifest.json deliberately.");
    }
}

static void FuyutsuiMacroCombatRetryContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var macroText = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "macro.lua"));
    var mainText = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "main.lua"));
    var eventsText = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "events.lua"));

    Equal(true, macroText.Contains("if InCombatLockdown() then\n        return false", StringComparison.Ordinal),
        "macro creation reports combat lockdown");
    Equal(true, macroText.Contains("return true\nend", StringComparison.Ordinal),
        "macro creation reports success");
    Equal(true, mainText.Contains("self.macrosPending = not created", StringComparison.Ordinal),
        "failed macro creation is retained for retry");
    Equal(true, macroText.Contains("local i = 1 + (keyOffset or 0)", StringComparison.Ordinal),
        "macro creation applies the class key offset");
    Equal(true, mainText.Contains(
            "self:CreateMacro(dynamicSpells, m.staticSpells, m.specialSpells, m.keyOffset)",
            StringComparison.Ordinal),
        "player macro loading forwards the class key offset");
    Equal(true, eventsText.Contains(
            "if self.macrosPending then\n        C_Timer.After(0, function()\n            if self.macrosPending and not InCombatLockdown() then\n                self:LoadPlayerMacros()",
            StringComparison.Ordinal),
        "leaving combat schedules a safe pending macro retry");
}

static string FindRepositoryRoot()
{
    foreach (var startPath in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Shigure.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
    }

    throw new DirectoryNotFoundException("cannot locate repository root containing Shigure.slnx");
}

static bool IsSafeRelativePath(string path)
{
    return !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && path.Split('/', StringSplitOptions.RemoveEmptyEntries).All(part => part is not "." and not "..");
}

static int EncodeStep(int step, int value)
{
    var red = step <= 255 ? 0 : 1;
    var green = step <= 255 ? step : step - 255;
    return Argb(red, green, value);
}

static int Argb(int red, int green, int blue) =>
    unchecked((int)0xFF000000) | (red << 16) | (green << 8) | blue;

static string DictionaryText(IReadOnlyDictionary<int, int> values) =>
    string.Join(',', values.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}"));

static T Add<T>(ICollection<T> items, T item)
{
    items.Add(item);
    return item;
}

static void Equal<T>(T expected, T actual, string description)
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        throw new InvalidOperationException($"{description}: expected {expected}, actual {actual}");
    }
}

static void Throws<TException>(Action action, string description)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"{description}: expected {typeof(TException).Name}");
}

sealed record ContractSurfaceManifestModel(
    int SchemaVersion,
    string[] CoveredRoots,
    Dictionary<string, ContractSurfaceFile> Files);

sealed record ContractSurfaceFile(string Category, string Sha256);

sealed record MatchCandidate(string Id, string Name, ModuleMatch Match);

sealed class FakeMacDiagnosticEnvironment : IMacDiagnosticEnvironment
{
    private readonly TargetWindow? _target;
    private readonly PlatformPermissionSnapshot _permissions;
    private readonly CapturedRegion _frame;

    public FakeMacDiagnosticEnvironment(
        TargetWindow? target,
        PlatformPermissionSnapshot permissions,
        CapturedRegion frame)
    {
        _target = target;
        _permissions = permissions;
        _frame = frame;
    }

    public int LocateCount { get; private set; }
    public int PermissionCount { get; private set; }
    public int CaptureCount { get; private set; }
    public int DecodeCount { get; private set; }
    public int SendCount { get; private set; }
    public int AddOnPathCount { get; private set; }
    public int ExportCount { get; private set; }
    public TargetIdentity? LastExpectedTarget { get; private set; }
    public Action<string>? BeforeExport { get; set; }

    public int TotalOperationCount =>
        LocateCount + PermissionCount + CaptureCount + DecodeCount + SendCount + AddOnPathCount + ExportCount;

    public TargetWindow? LocateTarget()
    {
        LocateCount++;
        return _target;
    }

    public PlatformPermissionSnapshot CheckPermissions()
    {
        PermissionCount++;
        return _permissions;
    }

    public ScreenCaptureResult Capture(TargetBounds bounds)
    {
        CaptureCount++;
        return ScreenCaptureResult.Success(_frame);
    }

    public ScreenScanResult Decode()
    {
        DecodeCount++;
        return new ScreenScanResult(
            new Dictionary<int, int> { [1] = 1 },
            new Dictionary<int, int>(),
            new Dictionary<int, int>(),
            null)
        {
            Target = _target,
            Timing = new ScreenScanTiming(
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromMilliseconds(2),
                TimeSpan.FromMilliseconds(3))
        };
    }

    public KeySendResult Send(string hotkey, TargetIdentity expectedTarget)
    {
        SendCount++;
        LastExpectedTarget = expectedTarget;
        return KeySendResult.Success;
    }

    public string? ResolveAddOnsDirectory(TargetWindow? target)
    {
        AddOnPathCount++;
        return target is null ? null : "/Applications/World of Warcraft/_retail_/Interface/AddOns";
    }

    public void ExportPpm(CapturedRegion frame, string path)
    {
        BeforeExport?.Invoke(path);
        ExportCount++;
    }
}

sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(_timestamp);

    public void Advance(TimeSpan duration) => _timestamp = checked(_timestamp + duration.Ticks);
}

sealed class FakeMacPermissionNativeApi : IMacPermissionNativeApi
{
    private readonly bool _grantScreenOnRequest;
    private readonly bool _grantAccessibilityOnRequest;

    public FakeMacPermissionNativeApi(
        bool screenCaptureGranted = false,
        bool accessibilityGranted = false,
        bool grantScreenOnRequest = true,
        bool grantAccessibilityOnRequest = true)
    {
        ScreenCaptureGranted = screenCaptureGranted;
        AccessibilityGranted = accessibilityGranted;
        _grantScreenOnRequest = grantScreenOnRequest;
        _grantAccessibilityOnRequest = grantAccessibilityOnRequest;
    }

    public bool ScreenCaptureGranted { get; private set; }
    public bool AccessibilityGranted { get; private set; }
    public int ScreenRequestCount { get; private set; }
    public int AccessibilityRequestCount { get; private set; }

    public bool HasScreenCaptureAccess() => ScreenCaptureGranted;

    public bool HasAccessibilityAccess() => AccessibilityGranted;

    public bool RequestScreenCaptureAccess()
    {
        ScreenRequestCount++;
        ScreenCaptureGranted = _grantScreenOnRequest;
        return ScreenCaptureGranted;
    }

    public bool RequestAccessibilityAccess()
    {
        AccessibilityRequestCount++;
        AccessibilityGranted = _grantAccessibilityOnRequest;
        return AccessibilityGranted;
    }
}

sealed class FakeMacTriggerStateApi : IMacTriggerStateApi
{
    public ushort? PressedKeyCode { get; set; }
    public uint? PressedMouseButton { get; set; }

    public bool IsKeyPressed(ushort keyCode) => PressedKeyCode == keyCode;

    public bool IsMouseButtonPressed(uint button) => PressedMouseButton == button;
}

sealed class FakeMacTriggerPulseSource : IMacTriggerPulseSource
{
    public HashSet<TriggerInputBinding> PressPulses { get; } = [];
    public int Pulses { get; set; }
    public int UpPulses { get; set; }
    public int DisposeCount { get; private set; }

    public bool ConsumePulse(TriggerInputBinding input)
    {
        if (!input.IsPulse)
        {
            return PressPulses.Remove(input);
        }

        if (input.Code == MacTriggerInputMap.WheelUpCode)
        {
            if (UpPulses <= 0)
            {
                return false;
            }

            UpPulses--;
            return true;
        }

        if (Pulses <= 0)
        {
            return false;
        }

        Pulses--;
        return true;
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}

sealed class FakeTargetWindowLocator : ITargetWindowLocator
{
    public FakeTargetWindowLocator(TargetWindow? target)
    {
        Target = target;
    }

    public TargetWindow? Target { get; set; }

    public TargetWindow? FindFrontmostTarget() => Target;

    public string DescribeConfiguredProcesses() => "World of Warcraft";
}

sealed class RecordingPermissionService : IPlatformPermissionService
{
    private readonly PlatformPermissionRequestOutcome _outcome;

    public RecordingPermissionService(PlatformPermissionRequestOutcome outcome)
    {
        _outcome = outcome;
    }

    public int RequestCount { get; private set; }

    public PlatformPermissionKind? LastRequested { get; private set; }

    public PlatformPermissionSnapshot Check() => throw new InvalidOperationException(
        "permission command must not use the read-only check path");

    public PlatformPermissionRequestResult Request(PlatformPermissionKind permission)
    {
        RequestCount++;
        LastRequested = permission;
        var granted = _outcome != PlatformPermissionRequestOutcome.UserActionRequired;
        return new PlatformPermissionRequestResult(
            new PlatformPermissionStatus(
                permission,
                granted ? PlatformPermissionState.Granted : PlatformPermissionState.NotGranted,
                _outcome == PlatformPermissionRequestOutcome.RestartRequired),
            _outcome);
    }
}

sealed class FakePlatformPermissionService : IPlatformPermissionService
{
    private readonly bool _accessibilityReady;
    private readonly bool _screenCaptureReady;

    public FakePlatformPermissionService(
        bool accessibilityReady,
        bool screenCaptureReady = true)
    {
        _accessibilityReady = accessibilityReady;
        _screenCaptureReady = screenCaptureReady;
    }

    public int CheckCount { get; private set; }

    public PlatformPermissionSnapshot Check()
    {
        CheckCount++;
        return new PlatformPermissionSnapshot(
            new PlatformPermissionStatus(
                PlatformPermissionKind.ScreenCapture,
                _screenCaptureReady ? PlatformPermissionState.Granted : PlatformPermissionState.NotGranted,
                RestartRequired: false),
            new PlatformPermissionStatus(
                PlatformPermissionKind.Accessibility,
                _accessibilityReady ? PlatformPermissionState.Granted : PlatformPermissionState.NotGranted,
                RestartRequired: false));
    }

    public PlatformPermissionRequestResult Request(PlatformPermissionKind permission) =>
        throw new InvalidOperationException("key output must not request permissions");
}

sealed class FakeMacKeyEventApi : IMacKeyEventApi
{
    private int _creationCount;

    public nint Source { get; set; } = 10;
    public int? FailCreationAt { get; set; }
    public List<FakeMacKeyEvent> Events { get; } = [];
    public List<nint> Posts { get; } = [];
    public List<nint> Released { get; } = [];

    public nint CreateSource() => Source;

    public nint CreateKeyboardEvent(nint source, ushort keyCode, bool keyDown)
    {
        _creationCount++;
        if (_creationCount == FailCreationAt)
        {
            return 0;
        }

        var item = new FakeMacKeyEvent(100 + _creationCount, keyCode, keyDown);
        Events.Add(item);
        return item.Reference;
    }

    public void SetFlags(nint eventRef, ulong flags)
    {
        Events.Single(item => item.Reference == eventRef).Flags = flags;
    }

    public void Post(nint eventRef)
    {
        Posts.Add(eventRef);
    }

    public void Release(nint value)
    {
        Released.Add(value);
    }
}

sealed class FakeMacFrontmostApplicationProvider(int? processId) : IMacFrontmostApplicationProvider
{
    public int? GetProcessId() => processId;
}

sealed class FakeMacKeyEvent(nint reference, ushort keyCode, bool keyDown)
{
    public nint Reference { get; } = reference;
    public ushort KeyCode { get; } = keyCode;
    public bool KeyDown { get; } = keyDown;
    public ulong Flags { get; set; }
}

sealed class FakeMacScreenCaptureBackend : IMacScreenCaptureBackend
{
    private readonly MacNativeFrame? _frame;

    public FakeMacScreenCaptureBackend(MacNativeFrame? frame)
    {
        _frame = frame;
    }

    public int CaptureCount { get; private set; }
    public TargetBounds? LastRegion { get; private set; }
    public uint? LastWindowId { get; private set; }

    public MacNativeFrame? Capture(TargetBounds region, uint? windowId = null)
    {
        CaptureCount++;
        LastRegion = region;
        LastWindowId = windowId;
        return _frame;
    }
}

sealed class FakeMacStreamCaptureApi(
    int width,
    int height,
    int bytesPerRow,
    byte[] bytes) : IMacStreamCaptureApi
{
    public TargetBounds? LastRegion { get; private set; }
    public uint? LastWindowId { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }
    public int DestroyCount { get; private set; }
    public int StartResult { get; set; }

    public nint Create() => 71;

    public int Start(nint handle, uint windowId, TargetBounds region)
    {
        LastRegion = region;
        LastWindowId = windowId;
        StartCount++;
        return StartResult;
    }

    public int GetLatestSize(nint handle, out int frameWidth, out int frameHeight, out int frameBytesPerRow)
    {
        frameWidth = width;
        frameHeight = height;
        frameBytesPerRow = bytesPerRow;
        return bytes.Length;
    }

    public int CopyLatest(nint handle, nint destination, int capacity)
    {
        if (capacity < bytes.Length)
        {
            return 0;
        }

        Marshal.Copy(bytes, 0, destination, bytes.Length);
        return bytes.Length;
    }

    public void Stop(nint handle) => StopCount++;

    public void Destroy(nint handle) => DestroyCount++;
}

sealed class FakeScaledRegionCapturer : ITargetWindowRegionCapturer
{
    private readonly TargetBounds _sourceBounds;
    private readonly int[] _sourcePixels;
    private readonly double _scaleX;
    private readonly double _scaleY;

    public FakeScaledRegionCapturer(
        TargetBounds sourceBounds,
        int[] sourcePixels,
        double scaleX,
        double scaleY)
    {
        _sourceBounds = sourceBounds;
        _sourcePixels = sourcePixels;
        _scaleX = scaleX;
        _scaleY = scaleY;
    }

    public int? FailCaptureAt { get; init; }
    public bool FlipVertically { get; init; }
    public List<TargetBounds> Regions { get; } = [];
    public List<TargetIdentity> Targets { get; } = [];

    public ScreenCaptureResult Capture(TargetIdentity target, TargetBounds region)
    {
        Targets.Add(target);
        return Capture(region);
    }

    public ScreenCaptureResult Capture(TargetBounds region)
    {
        Regions.Add(region);
        if (Regions.Count == FailCaptureAt)
        {
            return ScreenCaptureResult.Failure(
                ScreenCaptureFailureKind.CaptureUnavailable,
                "simulated capture failure");
        }

        var localX = region.X - _sourceBounds.X;
        var localY = region.Y - _sourceBounds.Y;
        if (localX < 0 || localY < 0
            || localX + region.Width > _sourceBounds.Width
            || localY + region.Height > _sourceBounds.Height)
        {
            return ScreenCaptureResult.Failure(
                ScreenCaptureFailureKind.InvalidRegion,
                "region is outside the source frame");
        }

        var pixelWidth = Math.Max(1, (int)Math.Round(region.Width * _scaleX));
        var pixelHeight = Math.Max(1, (int)Math.Round(region.Height * _scaleY));
        var actualScaleX = (double)pixelWidth / region.Width;
        var actualScaleY = (double)pixelHeight / region.Height;
        var pixels = Enumerable.Repeat(unchecked((int)0xFF000000), pixelWidth * pixelHeight).ToArray();
        for (var y = 0; y < pixelHeight; y++)
        {
            var sourceY = localY + y;
            if (sourceY >= _sourceBounds.Height)
            {
                break;
            }

            for (var x = 0; x < pixelWidth; x++)
            {
                var sourceX = localX + x;
                if (sourceX >= _sourceBounds.Width)
                {
                    break;
                }

                var destinationY = FlipVertically ? pixelHeight - 1 - y : y;
                pixels[destinationY * pixelWidth + x] =
                    _sourcePixels[sourceY * _sourceBounds.Width + sourceX];
            }
        }

        return ScreenCaptureResult.Success(new CapturedRegion(
            pixelWidth,
            pixelHeight,
            actualScaleX,
            actualScaleY,
            CapturedPixelFormat.Argb32,
            CapturedColorSpace.Srgb,
            pixels));
    }
}

sealed class RouteHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken) =>
        Task.FromResult(route(request));
}

sealed class AlwaysFailRegionCapturer : IScreenRegionCapturer
{
    public ScreenCaptureResult Capture(TargetBounds region) =>
        ScreenCaptureResult.Failure(ScreenCaptureFailureKind.CaptureUnavailable, "simulated unavailable frame");
}

sealed class FakeRuntimeStateBuilder : IRuntimeStateBuilder
{
    public GameState Build(
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData,
        IReadOnlyDictionary<int, int>? healAbsorbData = null) =>
        new(new Dictionary<string, object?>());
}

sealed class FakeRuntimeLogic : IRuntimeLogic
{
    public LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic) =>
        new(null, null);
}

sealed class FakeTargetKeyOutput : ITargetKeyOutput
{
    public KeySendResult Send(string hotkey, TargetIdentity? expectedTarget) =>
        KeySendResult.Success;
}

sealed class TrackingTriggerInput : ITriggerInput
{
    private bool _disposed;

    public int DisposeCount { get; private set; }

    public TriggerInputBinding? Resolve(string triggerName) =>
        _disposed ? null : new TriggerInputBinding(TriggerInputKind.Keyboard, 0);

    public bool IsPressed(TriggerInputBinding input) => false;

    public bool ConsumePulse(TriggerInputBinding input) => false;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeCount++;
    }
}

sealed class PressedTriggerInput : ITriggerInput
{
    public TriggerInputBinding? Resolve(string triggerName) =>
        new(TriggerInputKind.Keyboard, 0);

    public bool IsPressed(TriggerInputBinding input) => true;

    public bool ConsumePulse(TriggerInputBinding input) => false;

    public void Dispose()
    {
    }
}

sealed class PulseOnlyTriggerInput : ITriggerInput
{
    private int _pending = 1;

    public TriggerInputBinding? Resolve(string triggerName) =>
        new(TriggerInputKind.Keyboard, 0);

    public bool IsPressed(TriggerInputBinding input) => false;

    public bool ConsumePulse(TriggerInputBinding input) =>
        Interlocked.Exchange(ref _pending, 0) != 0;

    public void Dispose()
    {
    }
}

sealed class BlockingRuntimeScanner : IRuntimeScreenScanner
{
    public ManualResetEventSlim Entered { get; } = new(false);
    public ManualResetEventSlim Release { get; } = new(false);

    public ScreenScanResult ScanScreenData()
    {
        Entered.Set();
        if (!Release.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("blocking runtime scanner was not released");
        }

        return new ScreenScanResult(
            null,
            new Dictionary<int, int>(),
            new Dictionary<int, int>(),
            "simulated blocking scan");
    }
}

sealed class TrackingRuntimeLease : IDisposable
{
    public int DisposeCount { get; private set; }

    public void Dispose() => DisposeCount++;
}

sealed class BlockingRuntimeFactory : IShigureRuntimeFactory
{
    private int _createCount;

    public ManualResetEventSlim FirstCreateEntered { get; } = new(false);
    public ManualResetEventSlim ReleaseFirstCreate { get; } = new(false);
    public List<TrackingTriggerInput> Triggers { get; } = [];

    public ShigureRuntime Create(AppOptions options)
    {
        var trigger = new TrackingTriggerInput();
        Triggers.Add(trigger);
        var runtime = new ShigureRuntime(
            options,
            new EmptyRuntimeScanner(),
            new FakeRuntimeStateBuilder(),
            new FakeTargetKeyOutput(),
            trigger,
            new FakeRuntimeLogic(),
            TimeProvider.System);

        if (Interlocked.Increment(ref _createCount) == 1)
        {
            FirstCreateEntered.Set();
            if (!ReleaseFirstCreate.Wait(TimeSpan.FromSeconds(5)))
            {
                runtime.Dispose();
                throw new TimeoutException("stale runtime factory was not released");
            }
        }

        return runtime;
    }
}

sealed class HostRuntimeFactory : IShigureRuntimeFactory
{
    public List<TrackingTriggerInput> Triggers { get; } = [];

    public ShigureRuntime Create(AppOptions options)
    {
        var trigger = new TrackingTriggerInput();
        Triggers.Add(trigger);
        return new ShigureRuntime(
            options,
            new EmptyRuntimeScanner(),
            new FakeRuntimeStateBuilder(),
            new FakeTargetKeyOutput(),
            trigger,
            new FakeRuntimeLogic(),
            TimeProvider.System);
    }
}

sealed class ImmediateStopRuntimeFactory : IShigureRuntimeFactory
{
    public List<UnresolvedTriggerInput> Triggers { get; } = [];

    public ShigureRuntime Create(AppOptions options)
    {
        var trigger = new UnresolvedTriggerInput();
        Triggers.Add(trigger);
        return new ShigureRuntime(
            options,
            new EmptyRuntimeScanner(),
            new FakeRuntimeStateBuilder(),
            new FakeTargetKeyOutput(),
            trigger,
            new FakeRuntimeLogic(),
            TimeProvider.System);
    }
}

sealed class UnresolvedTriggerInput : ITriggerInput
{
    public int DisposeCount { get; private set; }

    public TriggerInputBinding? Resolve(string triggerName) => null;

    public bool IsPressed(TriggerInputBinding input) => false;

    public bool ConsumePulse(TriggerInputBinding input) => false;

    public void Dispose() => DisposeCount++;
}

sealed class EmptyRuntimeScanner : IRuntimeScreenScanner
{
    public ScreenScanResult ScanScreenData() =>
        new(
            null,
            new Dictionary<int, int>(),
            new Dictionary<int, int>(),
            "simulated empty scan");
}

sealed class FakeMacScreenCaptureNativeApi : IMacScreenCaptureNativeApi
{
    public nint Image { get; set; } = 11;
    public nint ColorSpace { get; set; } = 22;
    public nint Context { get; set; } = 33;
    public nuint PixelWidth { get; set; } = 2;
    public nuint PixelHeight { get; set; } = 1;
    public byte[] DrawBytes { get; set; } = [0, 0, 0, 255, 0, 0, 0, 255];
    public TargetBounds? LastRegion { get; private set; }
    public uint? LastWindowId { get; private set; }
    public List<string> Releases { get; } = [];

    public nint CreateImage(TargetBounds region, uint? windowId)
    {
        LastRegion = region;
        LastWindowId = windowId;
        return Image;
    }

    public nuint GetImageWidth(nint image) => PixelWidth;

    public nuint GetImageHeight(nint image) => PixelHeight;

    public nint CreateSrgbColorSpace() => ColorSpace;

    public nint CreateBitmapContext(
        nint data,
        int width,
        int height,
        int bytesPerRow,
        nint colorSpace)
    {
        LastBuffer = data;
        return Context;
    }

    public void DrawImage(nint context, nint image, int width, int height)
    {
        Marshal.Copy(DrawBytes, 0, LastBuffer, DrawBytes.Length);
    }

    private nint LastBuffer { get; set; }

    public void ReleaseContext(nint context) => Releases.Add($"context:{context}");

    public void ReleaseColorSpace(nint colorSpace) => Releases.Add($"color:{colorSpace}");

    public void ReleaseImage(nint image) => Releases.Add($"image:{image}");
}
