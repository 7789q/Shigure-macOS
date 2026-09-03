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

if (args.Length == 2
    && string.Equals(args[0], "--validate-holy-paladin-module", StringComparison.Ordinal))
{
    return ValidateHolyPaladinModule(args[1]);
}

var tests = new (string Name, Action Run)[]
{
    ("top row boundaries", TopRowBoundaries),
    ("top row requires start marker", TopRowRequiresStartMarker),
    ("count bars markers", CountBarsMarkers),
    ("heal absorb units", HealAbsorbUnits),
    ("heal absorb stabilization contract", HealAbsorbStabilizationContract),
    ("unit selector without any aura contract", UnitSelectorWithoutAnyAuraContract),
    ("unit selector excludes unavailable role contract", UnitSelectorExcludesUnavailableRoleContract),
    ("unit selector other player contract", UnitSelectorOtherPlayerContract),
    ("healing deficit selector contract", HealingDeficitSelectorContract),
    ("module derived state tracker contract", ModuleDerivedStateTrackerContract),
    ("bundled module installation contract", BundledModuleInstallationContract),
    ("bundled holy paladin module replay", BundledHolyPaladinModuleReplay),
    ("module missing binding fallback contract", ModuleMissingBindingFallbackContract),
    ("state builder fixture", StateBuilderFixture),
    ("heal absorb diagnostic log contract", HealAbsorbDiagnosticLogContract),
    ("AOE warning diagnostic log contract", AoeWarningDiagnosticLogContract),
    ("module match selection", ModuleMatchSelection),
    ("module marketplace install contract", ModuleMarketplaceInstallContract),
    ("module editor persistence contract", ModuleEditorPersistenceContract),
    ("module load failure contract", ModuleLoadFailureContract),
    ("legacy module state compatibility contract", LegacyModuleStateCompatibilityContract),
    ("module dependency capture and import contract", ModuleDependencyCaptureAndImportContract),
    ("cooldown confirmation tracker contract", CooldownConfirmationTrackerContract),
    ("AOE absorb reserve guard contract", AoeAbsorbReserveGuardContract),
    ("action failure backoff contract", ActionFailureBackoffContract),
    ("emergency action guard contract", EmergencyActionGuardContract),
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
    ("runtime cooldown confirmation contract", RuntimeCooldownConfirmationContract),
    ("runtime failure snapshot contract", RuntimeFailureSnapshotContract),
    ("runtime startup failure ownership contract", RuntimeStartupFailureOwnershipContract),
    ("runtime session ownership contract", RuntimeSessionOwnershipContract),
    ("local runtime log store contract", LocalRuntimeLogStoreContract),
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
    ("fuyutsui protocol 1.2.1.15 contract", FuyutsuiProtocolContract),
    ("DiGua bridge production Lua replay", DiGuaBridgeProductionLuaReplayContract),
    ("AOE warning state machine replay", AoeWarningStateMachineReplayContract),
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
    ("runtime UI update guard contract", RuntimeUiUpdateGuardContract),
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

static void BundledHolyPaladinModuleReplay()
{
    var modulePath = Path.Combine(
        FindRepositoryRoot(),
        "BundledModules",
        "holy-paladin-virtue-12.1.json");
    Equal(0, ValidateHolyPaladinModule(modulePath), "bundled holy paladin module replay result");
}

static void BundledModuleInstallationContract()
{
    var fixtureRoot = Path.Combine(Path.GetTempPath(), $"shigure-bundled-modules-{Guid.NewGuid():N}");
    var sourceDirectory = Path.Combine(fixtureRoot, "source");
    var targetDirectory = Path.Combine(fixtureRoot, "target");
    Directory.CreateDirectory(sourceDirectory);
    try
    {
        var sourcePath = Path.Combine(sourceDirectory, "holy-paladin.json");
        File.Copy(
            Path.Combine(FindRepositoryRoot(), "BundledModules", "holy-paladin-virtue-12.1.json"),
            sourcePath);
        var installer = new BundledModuleInstaller();

        var first = installer.Install(sourceDirectory, targetDirectory);
        Equal(1, first.InstalledModules.Count, "missing bundled module is installed");
        Equal(0, first.UpdatedModules.Count, "first bundled module install has nothing to upgrade");
        Equal(0, first.PreservedModules.Count, "first bundled module install has nothing to preserve");
        Equal(0, first.Failures.Count, "first bundled module install succeeds");

        var installedPath = Path.Combine(targetDirectory, "holy-paladin.json");
        File.AppendAllText(installedPath, Environment.NewLine);
        var locallyEdited = File.ReadAllText(installedPath);
        var second = installer.Install(sourceDirectory, targetDirectory);
        Equal(0, second.InstalledModules.Count, "existing bundled module is not reinstalled");
        Equal(0, second.UpdatedModules.Count, "locally edited bundled module is not upgraded");
        Equal(1, second.PreservedModules.Count, "existing bundled module is reported as preserved");
        Equal(locallyEdited, File.ReadAllText(installedPath), "existing local module content is not overwritten");

        var legacyTargetDirectory = Path.Combine(fixtureRoot, "legacy-target");
        Directory.CreateDirectory(legacyTargetDirectory);
        var legacyPath = Path.Combine(legacyTargetDirectory, "holy-paladin.json");
        File.Copy(
            Path.Combine(FindRepositoryRoot(), "Tests", "Shigure.Core.ContractTests", "Fixtures",
                "holy-paladin-legacy-11-rules.json"),
            legacyPath);
        var legacyContent = File.ReadAllText(legacyPath);
        var upgrade = installer.Install(sourceDirectory, legacyTargetDirectory);
        Equal(0, upgrade.InstalledModules.Count, "known legacy module is upgraded in place");
        Equal(1, upgrade.UpdatedModules.Count, "known legacy module upgrade is reported");
        Equal(0, upgrade.PreservedModules.Count, "known legacy module is not reported as preserved");
        Equal(File.ReadAllText(sourcePath), File.ReadAllText(legacyPath),
            "known legacy module receives the current bundled rules");
        var backupPath = Path.Combine(
            fixtureRoot,
            UserDataLayout.MigrationDirectoryName,
            "bundled-module-upgrades",
            "holy-paladin.591a0616e604.json");
        Equal(true, File.Exists(backupPath), "known legacy module is backed up before upgrade");
        Equal(legacyContent, File.ReadAllText(backupPath), "legacy module backup preserves the old content");

        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                "shigure-holy-paladin-virtue-12-1",
                "95ec8854e404e7de0f3820b3d49e3ce2e6a5b6042eb8872c79bddb39319b1019"),
            "the known Magic-only Cleanse module remains upgradeable");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                "shigure-holy-paladin-virtue-12-1",
                "296efdf7c9564016351ec2f2df8744259540629c0673f43e50ec65b9714959d1"),
            "the locally preserved 1.2.1.21 module is upgradeable");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                "shigure-holy-paladin-virtue-12-1",
                "06fac28138cb0bf752c35e1e269a25998d07ce071a4bb5a28243f9903429460f"),
            "the repository 1.2.1.21 module is upgradeable");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                "shigure-holy-paladin-virtue-12-1",
                "ea16efa2c7bbd04eb65ac08ef45ed5dc38a6bf0462b87738058cd5d54a29865e"),
            "the observed local 1.2.1.21 module is upgradeable");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                "shigure-holy-paladin-virtue-12-1",
                "70d96b2b330ac68d41f070d9cd16fbf705650849f20f29f4c0330842380f2854"),
            "the observed local 1.2.1.22 module with the stale frontal condition is upgradeable");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableModule(
                "shigure-holy-paladin-virtue-12-1",
                "烈日奶骑大秘境美德爆发-20260829",
                "1f2a37b5e49c62a5c744a81278d8d26310953a0447e9c920a595620e403d06c2"),
            "the exact deployed legacy holy paladin module upgrades across its ID migration");
        Equal(false, BundledModuleInstaller.IsKnownUpgradeableModule(
                "shigure-holy-paladin-virtue-12-1",
                "烈日奶骑大秘境美德爆发-20260829",
                new string('0', 64)),
            "an edited legacy holy paladin module remains preserved");
        Equal(false, BundledModuleInstaller.IsKnownUpgradeableModule(
                "shigure-holy-paladin-virtue-12-1",
                "unknown-local-module",
                "1f2a37b5e49c62a5c744a81278d8d26310953a0447e9c920a595620e403d06c2"),
            "an unknown module ID cannot borrow the legacy upgrade hash");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static int ValidateHolyPaladinModule(string path)
{
    try
    {
        var module = ModuleStore.Parse(File.ReadAllBytes(path));
        Equal("烈日奶骑大秘境-美德爆发 12.1", module.Name, "holy paladin module identity");
        Equal("1.2.1.22", module.Version, "holy paladin module version");
        Equal(5, module.Counts.Single(count => count.Name == "D5AtLeast").HealthThreshold,
            "holy paladin module tracks the five-percent light-injury count");
        Equal(CountKind.UnitsAtOrAboveHealingDeficit,
            module.Counts.Single(count => count.Name == "D10AtLeast").Kind,
            "holy paladin module preserves the ten-percent count for AOE diagnostics");
        Equal(CountKind.TotalHealingDeficit,
            module.Counts.Single(count => count.Name == "DTotal").Kind,
            "holy paladin module uses the total healing load metric");
        Equal(CountKind.TotalHealthDeficit,
            module.Counts.Single(count => count.Name == "HTotal").Kind,
            "holy paladin module separately tracks real missing health");
        var realGroupDamage = module.DerivedStates.Single(state => state.Name == "真实群伤");
        Equal(2000, realGroupDamage.HoldMs,
            "reactive Virtue remains selected across one complete GCD after real group damage briefly recovers");
        Equal("H85 >= 3 && HTotal >= 50",
            realGroupDamage.Condition,
            "real group damage requires three members below eighty-five percent and fifty total missing health");
        Equal(false, module.DerivedStates.Any(state => state.Name == "群疗爆发保持"),
            "holy paladin module does not keep a redundant burst-hold derived state");
        Equal(false, module.Rules.Any(rule => rule.Condition.Contains("群疗爆发保持", StringComparison.Ordinal)),
            "holy paladin rules re-evaluate current conditions instead of reading burst hold");
        var cleanseRule = module.Rules.Single(rule => rule.Spell == "清洁术");
        Equal("H70 == 0 && spells.清洁术 == 0", cleanseRule.Condition,
            "holy paladin Cleanse requires every available group member to be at least seventy percent");
        Equal(0, cleanseRule.DelayMs.GetValueOrDefault(),
            "holy paladin Cleanse can be pressed continuously until the game confirms its cooldown");
        Equal(13, module.Rules.IndexOf(cleanseRule) + 1,
            "holy paladin Cleanse follows the AOE burst chain and precedes ordinary healing");
        Equal(true, module.Rules.Count >= 30,
            "holy paladin module keeps the complete priority matrix instead of a scenario subset");
        Equal(41, module.Rules.Count,
            "holy paladin module includes sacrifice, consumable and split lowest-health healing branches");
        Equal(true, module.Rules.All(rule => rule.LogicDelayMs.GetValueOrDefault() == 0),
            "holy paladin rules never pause the whole logic loop after a decision");
        var requiredActions = new[]
        {
            "圣盾术", "圣疗术", "美德道标", "荣耀圣令", "圣洁鸣钟", "光环掌握", "圣光闪现", "神圣震击",
            "清洁术", "正义盾击", "黎明之光", "审判", "圣光术", "暂停"
        };
        foreach (var action in requiredActions)
        {
            Equal(true, module.Rules.Any(rule => string.Equals(rule.Spell, action, StringComparison.Ordinal)),
                $"holy paladin required action remains registered: {action}");
        }
        var flashRules = module.Rules
            .Where(rule => string.Equals(rule.Spell, "圣光闪现", StringComparison.Ordinal))
            .ToArray();
        Equal(true, flashRules
                .Where(rule => rule.Condition.Contains("重伤目标", StringComparison.Ordinal))
                .All(rule => rule.Condition.Contains("auras.圣光灌注", StringComparison.Ordinal)
                    && rule.DelayMs == 700),
            "holy paladin severe healing uses Infusion Flash of Light before enhanced Holy Light");
        Equal(false, flashRules.Any(rule => rule.Condition.Contains("轻伤目标血量 >= 93", StringComparison.Ordinal)
                && !rule.Condition.Contains("auras.圣光灌注", StringComparison.Ordinal)),
            "holy paladin minor healing has no bare Flash of Light fallback");
        Equal(true, module.Rules
                .Where(rule => string.Equals(rule.Spell, "圣光术", StringComparison.Ordinal))
                .All(rule => rule.DelayMs == 700),
            "holy paladin module rate-limits Holy Light retries without pausing emergency evaluation");
        Equal(true, module.Rules
                .Where(rule => string.Equals(rule.Spell, "圣洁鸣钟", StringComparison.Ordinal))
                .All(rule => rule.Condition.Contains("DTotal > 50", StringComparison.Ordinal)
                    && rule.Condition.Contains("D15AtLeast >= 3", StringComparison.Ordinal)
                    && rule.Condition.Contains("auras.美德道标 > 0", StringComparison.Ordinal)),
            "holy paladin Divine Toll requires a remaining real healing need inside Virtue");
        Equal(true, module.Rules
                .Where(rule => string.Equals(rule.Spell, "光环掌握", StringComparison.Ordinal))
                .All(rule => rule.Condition.Contains("D30AtLeast >= 3", StringComparison.Ordinal)
                    && rule.Condition.Contains("DTotal >= 120", StringComparison.Ordinal)
                    && rule.Condition.Contains("auras.美德道标 > 0", StringComparison.Ordinal)),
            "holy paladin Aura Mastery requires sustained group pressure inside Virtue");
        Equal(15, module.Counts.Single(count => count.Name == "D15AtLeast").HealthThreshold,
            "holy paladin module tracks fifteen-percent healing deficits");
        Equal(30, module.Counts.Single(count => count.Name == "D30AtLeast").HealthThreshold,
            "holy paladin module tracks thirty-percent healing deficits");
        Equal(95, module.Counts.Single(count => count.Name == "H95").HealthThreshold,
            "holy paladin non-Virtue Light of Dawn uses the ninety-five percent boundary");
        Equal(85, module.Counts.Single(count => count.Name == "H85").HealthThreshold,
            "holy paladin real group damage tracks the eighty-five percent health boundary");
        Equal(70, module.Units.Single(unit => unit.Name == "重伤目标").HealthThreshold,
            "holy paladin severe injury threshold is seventy percent");
        Equal(90, module.Units.Single(unit => unit.Name == "轻伤目标").HealthThreshold,
            "holy paladin light injury threshold is ninety percent");
        Equal(70, module.Counts.Single(count => count.Name == "H70").HealthThreshold,
            "holy paladin Cleanse boundary is represented by one shared count field");
        var severeWordOfGloryRules = module.Rules.Where(rule =>
            rule.Spell == "荣耀圣令" && rule.UnitName == "重伤目标").ToArray();
        Equal(1, severeWordOfGloryRules.Length,
            "holy paladin has one lowest-real-health severe Word of Glory branch");
        Equal(1, module.Rules.Count(rule => rule.Spell == "荣耀圣令" && rule.UnitName == "治疗目标"),
            "holy paladin keeps one separate healing-absorb Word of Glory branch");
        var lightWordOfGloryRule = module.Rules.Single(rule =>
            string.Equals(rule.Spell, "荣耀圣令", StringComparison.Ordinal)
            && string.Equals(rule.UnitName, "轻伤目标", StringComparison.Ordinal)
            && rule.Condition.Contains("AOE事件阶段", StringComparison.Ordinal));
        Equal("H70 == 0 && 轻伤目标 && AOE事件阶段 in (0, 4) || H70 == 0 && 轻伤目标 && AOE事件阶段 in (1, 3) && spells.圣洁鸣钟 == 0 || H70 == 0 && 轻伤目标 && AOE事件阶段 in (1, 3) && 圣洁鸣钟预计可用 > 0", lightWordOfGloryRule.Condition,
            "holy paladin light Word of Glory excludes severe targets and reserved AOE stages unless Divine Toll is ready");
        Equal("神圣能量 >= 3|auras.神圣意志 > 0",
            string.Join('|', lightWordOfGloryRule.SubConditions ?? []),
            "holy paladin light Word of Glory accepts three Holy Power or Divine Purpose");
        var expectedPriority = new[]
        {
            "牺牲祝福", "圣盾术", "圣疗术", "治疗石", "治疗药水", "美德道标", "美德道标", "美德道标", "黎明之光", "荣耀圣令", "圣洁鸣钟", "光环掌握", "清洁术",
            "荣耀圣令", "荣耀圣令", "圣光闪现", "圣光术", "圣光术", "神圣震击", "神圣震击", "审判", "圣光术", "圣光术",
            "暂停", "暂停", "荣耀圣令", "圣光术", "圣光闪现", "神圣震击", "圣光术", "圣光闪现", "神圣震击", "荣耀圣令",
            "审判", "圣光术", "黎明之光", "正义盾击", "神圣震击", "审判", "圣光闪现", "暂停"
        };
        Equal(string.Join('|', expectedPriority), string.Join('|', module.Rules.Select(rule => rule.Spell)),
            "holy paladin complete skill priority remains ordered");
        var groupDawnRule = module.Rules[8];
        Equal("真实群伤 > 0 && DTotal >= 30 && H85 >= 2 && AOE事件类型 != 2 && auras.美德道标 > 0 && 战斗时间 > 0", groupDawnRule.Condition,
            "holy paladin prioritizes Light of Dawn for current real group damage inside Virtue");
        Equal("神圣能量 >= 3|auras.神圣意志 > 0", string.Join('|', groupDawnRule.SubConditions ?? []),
            "holy paladin group Light of Dawn accepts Holy Power or Divine Purpose");
        var finalGcdPause = module.Rules[24];
        Equal("AOE事件阶段 == 5 && H90 == 0 && AbsorbAny == 0", finalGcdPause.Condition,
            "holy paladin only pauses full-health offense during the final safe GCD window");
        foreach (var offenseRule in module.Rules.Where(rule => rule.Condition.Contains("H90 == 0", StringComparison.Ordinal)
            && rule.Spell is "神圣震击" or "审判"))
        {
            Equal("AOE事件阶段 == 0|AOE事件阶段 == 1|AOE事件阶段 == 3|AOE事件阶段 == 4",
                string.Join('|', offenseRule.SubConditions ?? []),
                $"holy paladin full-health offense crosses reserve and absorb-wait stages: {offenseRule.Spell}");
        }
        Equal("AOE事件类型 == 2 && AOE事件阶段 == 3 && spells.美德道标 == 0 && auras.美德道标 == 0",
            module.Rules[6].Condition,
            "heal absorb cast completion has an explicit Virtue timing rule");
        Equal("真实群伤 > 0 && DTotal >= 30 && H85 >= 2 && AOE事件类型 != 2 && spells.美德道标 == 0 && auras.美德道标 == 0 && 战斗时间 > 0",
            module.Rules[7].Condition,
            "reactive group Virtue uses current real damage outside the DiGua reserve windows");
        Equal("AOE事件阶段 == 0|AOE事件阶段 == 4", string.Join('|', module.Rules[7].SubConditions ?? []),
            "DiGua reserve and final safe-GCD stages keep Virtue for the verified execution window");
        var lightOfDawnRules = module.Rules.Where(rule => rule.Spell == "黎明之光").ToArray();
        Equal(2, lightOfDawnRules.Length, "holy paladin keeps separate Virtue and healthy-group Light of Dawn rules");
        Equal(true, lightOfDawnRules.Any(rule =>
                rule.Condition.Contains("auras.美德道标 > 0", StringComparison.Ordinal)),
            "burst Light of Dawn requires Virtue");
        Equal(true, lightOfDawnRules.Any(rule =>
                rule.Condition.Contains("H95 >= 3", StringComparison.Ordinal)
                && rule.Condition.Contains("DTotal >= 15", StringComparison.Ordinal)
                && rule.Condition.Contains("auras.美德道标 == 0", StringComparison.Ordinal)
                && (rule.SubConditions?.Contains("神圣能量 >= 3") == true)),
            "non-Virtue Light of Dawn requires three light injuries and no severe target");
        var shieldRule = module.Rules.Single(rule => rule.Spell == "正义盾击");
        Equal(true, shieldRule.Condition.Contains("目标类型 == 1 && 目标距离 <= 5", StringComparison.Ordinal)
            && !shieldRule.Condition.Contains("目标正面", StringComparison.Ordinal),
            "Shield of the Righteous requires a nearby hostile target without the restricted frontal API gate");
        Equal("神圣能量 >= 3|auras.神圣意志 > 0", string.Join('|', module.Rules[35].SubConditions ?? []),
            "non-Virtue Light of Dawn accepts Holy Power or Divine Purpose");
        Equal(true, module.Rules.Any(rule => rule.Spell == "神圣震击"
                && rule.Condition.Contains("H90 == 0 && AbsorbAny == 0 && 战斗时间 > 0", StringComparison.Ordinal)),
            "healthy-group offensive Holy Shock uses combat state instead of nameplate counts");
        var healthyJudgment = module.Rules.Single(rule => rule.Spell == "审判"
            && rule.Condition.Contains("H90 == 0", StringComparison.Ordinal));
        Equal(true,
            healthyJudgment.Condition.Contains("auras.圣光灌注 == 0 && 神圣能量 < 5", StringComparison.Ordinal)
            && healthyJudgment.Condition.Contains("auras.圣光灌注 > 0 && 神圣能量 <= 3", StringComparison.Ordinal)
            && healthyJudgment.Condition.Contains("目标类型 != 0 && 目标距离 > 0 && 目标距离 <= 28", StringComparison.Ordinal),
            "healthy-group Judgment predicts Infusion's two Holy Power and requires a valid target");
        var holyLightFallbacks = module.Rules.Where(rule => rule.Spell == "圣光术").ToArray();
        Equal(true, holyLightFallbacks
                .Where(rule => rule.Comment?.Contains("裸读", StringComparison.Ordinal) == true)
                .All(rule => rule.Condition.Contains("spells.神圣震击层数 == 0", StringComparison.Ordinal)
                    && rule.Condition.Contains("spells.审判 != 0", StringComparison.Ordinal)
                    && rule.Condition.Contains("auras.圣光灌注 == 0", StringComparison.Ordinal)),
            "Holy Light fallback yields to available Holy Shock, Judgment, and Infusion");
        Equal(true, module.Rules
                .Where(rule => rule.Spell is "正义盾击" or "审判"
                    || rule.Spell == "神圣震击" && rule.Unit == ReservedUnit.None)
                .All(rule => rule.Condition.Contains("战斗时间 > 0", StringComparison.Ordinal)
                    && !rule.Condition.Contains("敌人数量", StringComparison.Ordinal)),
            "every offensive filler relies on immediate combat state without nameplate gating");
        var infusionConversionRule = module.Rules.Single(rule =>
            rule.Comment?.Contains("脱战且队伍安全时", StringComparison.Ordinal) == true);
        Equal(true, infusionConversionRule.Condition.Contains("auras.圣光灌注层数 > 0", StringComparison.Ordinal)
            && infusionConversionRule.Condition.Contains("施法技能 == 0", StringComparison.Ordinal),
            "out-of-combat Infusion conversion requires a real stack and no active cast");
        Equal(true, module.Rules.Where(rule => rule.Spell == "光环掌握").All(rule =>
                rule.Condition.Contains("H51 == 0", StringComparison.Ordinal)),
            "Aura Mastery yields to direct healing while any member is critically low");

        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "c7a3e7febb2e61903ffcd127b5039fef2c1f72ddd5ff57d939a2c0954f6e4749"),
            "the bundled module with regressed Cleanse priority upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "2ba2982d36ac8b1c7a0e9f1901e0b4f03020aa60d65b22f888c9ef9f1f756184"),
            "the bundled module whose AOE pause blocks full-health offense upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "2706dce7ccc714b7431da797f75f4446d1750c9a1ef38d00b979bcba4d009af7"),
            "the deployed fixed-cadence and nameplate-gated module upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "e764a5d6cd20cbd14d58fdb588380d3f01ca350f4a7a3206a69144db61ff342d"),
            "the bundled module whose offense depends on the healer's current target upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "14715d2bf5b16a65ba8bf284aab0959a3be72bd0274391793fd8b0405b288e40"),
            "the bundled module whose AOE cooldowns starve direct healing upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "d85ab284b3ae5cecb0b8de11f84c30988cad362a96bcc8e424d02f5d08db36f1"),
            "the bundled module with imprecise Aura Mastery and light-healing priorities upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "8ea0a7863badd9997aac296710ce7db14cc9328a50be8ee46598245d2c52322a"),
            "the bundled module with idle healthy-group offense and late absorb Virtue upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "57eadc2bfee55118a2ff67bc6b8307d8bbe1a05bd85ae322a273c398902901bc"),
            "the bundled module that overwrites pending actions and locks its fallback burst chain upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "e791ca0580f6b9774dc0e7d78a5ea7e785dcec0ef6c9e17f1f81cec768f8fcf9"),
            "the bundled module missing protected timeline bridging and out-of-combat Infusion conversion upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "b8e3a88901f469c7396f3ac6ce9406d79d062e92fd330a41332f1b0f6568ead6"),
            "the bundled module without priority group Light of Dawn upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "cd6def85882ee7fceac6e7eaa0135c0dcf68f145c7d73261f2bd2750713a8aa1"),
            "the bundled module whose burst cooldowns escape the Virtue window upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "510f9fc7e7726752ef3a98959742dd324401119e225d55e7311d1995bb86b329"),
            "the bundled module with late absorb Virtue and missing full-health Divine Purpose spend upgrades in place");
        Equal(true, BundledModuleInstaller.IsKnownUpgradeableHash(
                module.Id,
                "020abc91a5d7dec5cb4a6e194af0f1d2505eece9cca4159e94f862346937d367"),
            "the deployed 1.2.1.20 module with the old Infusion and Holy Shock ordering upgrades in place");

        var keymapBaseDirectory = FindRepositoryRoot();
        var keymap = new KeymapService(
            keymapBaseDirectory,
            ConfigService.LoadFromBaseDirectory(keymapBaseDirectory));
        keymap.SelectForClass(2, 1);

        LogicDecision Evaluate(
            GameState state,
            ModuleDerivedStateTracker? tracker = null,
            IReadOnlySet<LogicActionKey>? suppressedActions = null)
        {
            ModuleLogic.ResolveDynamicFields(module, state);
            (tracker ?? new ModuleDerivedStateTracker(new ManualTimeProvider())).Apply(module, state, enabled: true);
            return ModuleLogic.Run(module, state, keymap, suppressedActions);
        }

        static string Action(LogicDecision decision) =>
            decision.UnitInfo.TryGetValue("动作技能", out var value) ? value?.ToString() ?? string.Empty : string.Empty;

        static GameState State(
            int[] health,
            int[]? absorb = null,
            int holyPower = 0,
            int aoeType = 0,
            int stage = 0,
            int combatTime = 100,
            int virtueCooldown = 10,
            int virtueAura = 0,
            int bellCooldown = 10,
            bool bellExpectedReady = false,
            int auraMasteryCooldown = 10,
            int shockCharges = 0,
            int? shockCooldown = null,
            int judgmentCooldown = 10,
            int layOnHandsCooldown = 1,
            int infusion = 0,
            int divinePurpose = 0,
            int divineHand = 0,
            int wings = 0,
            int dispelSlot = 0,
            int dispelType = 1,
            int forbearanceSlot = 0,
            int playerForbearance = 0,
            bool moving = false,
            int channeling = 0,
            int enemyCount = 1,
            int targetType = 1,
            int targetHealth = 100,
            int shieldCooldown = 1,
            int sacrificeCooldown = 1,
            int targetInFront = 1,
            int healthstoneAvailable = 0,
            int healthPotionAvailable = 0,
            int divineShieldAura = 0)
        {
            absorb ??= new int[health.Length];
            var group = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
            for (var index = 0; index < health.Length; index++)
            {
                group[(index + 1).ToString()] = new Dictionary<string, object?>
                {
                    ["职责"] = index == 0 ? 1 : 5,
                    ["生命值"] = health[index],
                    ["治疗吸收"] = absorb[index],
                    ["驱散"] = index + 1 == dispelSlot ? dispelType : 0,
                    ["自律"] = index + 1 == forbearanceSlot ? 1 : 0
                };
            }

            return new GameState(new Dictionary<string, object?>
            {
                ["职业"] = 2,
                ["队伍类型"] = 46,
                ["生命值"] = health[0],
                ["引导"] = channeling,
                ["延迟"] = 0,
                ["治疗药水"] = healthPotionAvailable,
                ["治疗石"] = healthstoneAvailable,
                ["自律"] = playerForbearance,
                ["法力值"] = 100,
                ["神圣能量"] = holyPower,
                ["施法技能"] = 0,
                ["AOE事件类型"] = aoeType,
                ["AOE事件阶段"] = stage,
                ["圣洁鸣钟预计可用"] = bellExpectedReady ? 1 : 0,
                ["战斗时间"] = combatTime,
                ["敌人数量"] = enemyCount,
                ["移动"] = moving,
                ["目标类型"] = targetType,
                ["目标距离"] = 3,
                ["目标正面"] = targetInFront,
                ["目标生命值"] = targetHealth,
                ["spells"] = new Dictionary<string, object?>
                {
                    ["圣盾术"] = shieldCooldown,
                    ["牺牲祝福"] = sacrificeCooldown,
                    ["清洁术"] = dispelSlot > 0 ? 0 : 1,
                    ["圣疗术"] = layOnHandsCooldown,
                    ["美德道标"] = virtueCooldown,
                    ["圣洁鸣钟"] = bellCooldown,
                    ["光环掌握"] = auraMasteryCooldown,
                    ["神圣震击"] = shockCooldown ?? (shockCharges > 0 ? 0 : 1),
                    ["神圣震击层数"] = shockCharges,
                    ["审判"] = judgmentCooldown
                },
                ["auras"] = new Dictionary<string, object?>
                {
                    ["美德道标"] = virtueAura,
                    ["神圣意志"] = divinePurpose,
                    ["神性之手"] = divineHand,
                    ["圣光灌注"] = infusion,
                    ["圣光灌注层数"] = infusion > 0 ? 1 : 0,
                    ["复仇之怒"] = wings,
                    ["圣盾术"] = divineShieldAura
                },
                ["group"] = group
            });
        }

        var playerShield = Evaluate(State(
            [20, 100, 100, 100, 100],
            shieldCooldown: 0,
            layOnHandsCooldown: 0));
        Equal("圣盾术", Action(playerShield), "MOD-01 player health below thirty uses Divine Shield before Lay on Hands");
        Equal(0, Convert.ToInt32(playerShield.UnitInfo["动作单位槽位"]),
            "MOD-01 Divine Shield uses the self-target macro slot");

        var sacrifice = Evaluate(State(
            [100, 30, 100, 100, 100],
            sacrificeCooldown: 0));
        Equal("牺牲祝福", Action(sacrifice),
            "MOD-01 Sacrifice Blessing protects the lowest-health other player before self defense");
        Equal(2, Convert.ToInt32(sacrifice.UnitInfo["动作单位槽位"]),
            "MOD-01 Sacrifice Blessing excludes the player slot from its target selector");

        Equal("治疗石", Action(Evaluate(State(
            [40, 100, 100, 100, 100],
            healthstoneAvailable: 1))),
            "MOD-01 healthstone is the first consumable emergency action");
        Equal("治疗药水", Action(Evaluate(State(
            [30, 100, 100, 100, 100],
            healthPotionAvailable: 1))),
            "MOD-01 health potion follows an unavailable healthstone");
        Equal(false, new[] { "治疗石", "治疗药水" }.Contains(Action(Evaluate(State(
            [40, 100, 100, 100, 100],
            shieldCooldown: 0,
            healthstoneAvailable: 1,
            healthPotionAvailable: 1))), StringComparer.Ordinal),
            "MOD-01 consumables are suppressed while Divine Shield is ready");
        Equal(false, new[] { "治疗石", "治疗药水" }.Contains(Action(Evaluate(State(
            [40, 100, 100, 100, 100],
            divineShieldAura: 10,
            healthstoneAvailable: 1,
            healthPotionAvailable: 1))), StringComparer.Ordinal),
            "MOD-01 consumables are suppressed while Divine Shield is active");

        var layOnHands = Evaluate(State(
            [20, 40, 100, 100, 100],
            [0, 80, 0, 0, 0],
            layOnHandsCooldown: 0,
            shockCharges: 2));
        Equal("圣疗术", Action(layOnHands), "MOD-01 Lay on Hands is the highest true-health emergency action");
        Equal(1, Convert.ToInt32(layOnHands.UnitInfo["动作单位槽位"]),
            "MOD-01 Lay on Hands uses the true-health target instead of the absorb target");
        Equal("圣疗术", layOnHands.CooldownConfirmationSpell,
            "MOD-01 Lay on Hands waits for cooldown confirmation");
        var staleGroupForbearance = Evaluate(State(
            [100, 20, 100, 100, 100],
            layOnHandsCooldown: 0,
            forbearanceSlot: 2));
        Equal("圣疗术", Action(staleGroupForbearance),
            "MOD-01 arbitrary party debuffs cannot masquerade as Forbearance");
        Equal(2, Convert.ToInt32(staleGroupForbearance.UnitInfo["动作单位槽位"]),
            "MOD-01 stale group Forbearance pixels are sanitized before target selection");
        Equal(false, staleGroupForbearance.UnitInfo.ContainsKey("目标自律"),
            "MOD-01 untrusted party Forbearance is not reported as decoded state");
        var playerForbearanceTarget = Evaluate(State(
            [20, 25, 100, 100, 100],
            layOnHandsCooldown: 0,
            playerForbearance: 30));
        Equal("圣疗术", Action(playerForbearanceTarget),
            "MOD-01 Lay on Hands skips the player while confirmed Forbearance is active");
        Equal(2, Convert.ToInt32(playerForbearanceTarget.UnitInfo["动作单位槽位"]),
            "MOD-01 player Forbearance selects the next eligible critical target");
        var suppressedLayOnHands = Evaluate(
            State([20, 100, 100, 100, 100], layOnHandsCooldown: 0, shockCharges: 2),
            suppressedActions: new HashSet<LogicActionKey> { new("圣疗术", 1) });
        Equal("神圣震击", Action(suppressedLayOnHands),
            "MOD-01 a repeatedly unconfirmed Lay on Hands yields to the next healing rule");
        Equal(true, suppressedLayOnHands.UnitInfo.ContainsKey("已跳过确认失败动作"),
            "MOD-01 action suppression is visible in diagnostics");

        var emergencyShock = Evaluate(State(
            [20, 100, 100, 100, 100],
            shockCharges: 2,
            dispelSlot: 2));
        Equal("神圣震击", Action(emergencyShock),
            "MOD-02 Holy Shock handles an emergency before Cleanse when Lay on Hands is unavailable");
        Equal("神圣震击", emergencyShock.CooldownConfirmationSpell,
            "MOD-02 Holy Shock waits for cooldown confirmation");
        Equal("spells.神圣震击层数", emergencyShock.CooldownConfirmationStateField,
            "MOD-02 Holy Shock confirms against its charge field");
        Equal(2, emergencyShock.CooldownConfirmationInitialValue,
            "MOD-02 Holy Shock captures the charge count before delivery");

        Equal("荣耀圣令", Action(Evaluate(State([84, 84, 84, 100, 100], holyPower: 3))),
            "MOD-03 three members below eighty-five with total deficit forty-eight skip real group damage but use light Word of Glory fallback");
        Equal("圣光术", Action(Evaluate(State([70, 100, 100, 100, 100]))),
            "MOD-03 exactly seventy percent is not classified as severe injury");
        Equal("荣耀圣令", Action(Evaluate(State([60, 100, 100, 100, 100], holyPower: 3))),
            "MOD-04 real health below seventy spends Holy Power on direct healing outside Virtue");
        Equal("荣耀圣令", Action(Evaluate(State([80, 100, 100, 100, 100], holyPower: 3))),
            "MOD-04 light health below ninety uses Word of Glory as the instant fallback");
        Equal("荣耀圣令", Action(Evaluate(State(
            [80, 100, 100, 100, 100],
            divinePurpose: 4))),
            "MOD-04 light health can use Word of Glory through Divine Purpose");
        Equal("圣光术", Action(Evaluate(State(
            [80, 100, 100, 100, 100],
            holyPower: 3,
            stage: 1))),
            "MOD-04 light AOE reserve uses Judgment as a Holy Power filler when it is ready");
        Equal("正义盾击", Action(Evaluate(State([96, 96, 96, 96, 100], holyPower: 3))),
            "MOD-04 every group member above ninety-five spends three Holy Power on Shield first");
        var virtueDecision = Evaluate(State([80, 80, 80, 80, 100], holyPower: 3, virtueCooldown: 0));
        Equal("美德道标", Action(virtueDecision),
            "MOD-05 four deficits totaling sixty open Virtue");
        Equal("美德道标", virtueDecision.CooldownConfirmationSpell,
            "MOD-05 Virtue waits for the game cooldown before retrying");
        Equal("美德道标", Action(Evaluate(State([50, 50, 50, 100, 100], holyPower: 3, virtueCooldown: 0))),
            "MOD-06 three players at fifty percent trigger catastrophe protection");
        Equal("圣光术", Action(Evaluate(State([84, 84, 84, 100, 100], virtueCooldown: 0))),
            "MOD-06 three members below eighty-five but total deficit forty-eight do not open Virtue");
        var groupDawnDecision = Evaluate(State(
            [80, 80, 80, 80, 100],
            holyPower: 3,
            virtueAura: 5,
            bellCooldown: 0,
            auraMasteryCooldown: 10));
        Equal("黎明之光", Action(groupDawnDecision),
            "MOD-07 real group damage spends Holy Power on Light of Dawn before single-target healing");
        Equal("神圣能量", groupDawnDecision.CooldownConfirmationStateField,
            "MOD-07 Light of Dawn confirms against Holy Power");
        var freeGroupDawn = Evaluate(State(
            [80, 80, 80, 100, 100],
            holyPower: 0,
            divinePurpose: 4,
            virtueAura: 5));
        Equal("黎明之光", Action(freeGroupDawn),
            "MOD-07 real group damage spends free Light of Dawn before single-target healing");
        Equal("auras.神圣意志", freeGroupDawn.CooldownConfirmationStateField,
            "MOD-07 free group Light of Dawn confirms against Divine Purpose");
        var eternalFlameDecision = Evaluate(State(
            [60, 100, 100, 100, 100],
            holyPower: 3,
            virtueAura: 5,
            bellCooldown: 0,
            auraMasteryCooldown: 10));
        Equal("荣耀圣令", Action(eternalFlameDecision),
            "MOD-07 a single injured target still spends Eternal Flame before Divine Toll");
        Equal("荣耀圣令", eternalFlameDecision.CooldownConfirmationSpell,
            "MOD-07 Holy Power spend waits for game-state confirmation");
        Equal("神圣能量", eternalFlameDecision.CooldownConfirmationStateField,
            "MOD-07 Holy Power spend confirms against Holy Power");
        Equal(3, eternalFlameDecision.CooldownConfirmationInitialValue,
            "MOD-07 Holy Power confirmation captures the pre-cast resource");
        var freeEternalFlame = Evaluate(State(
            [60, 100, 100, 100, 100],
            holyPower: 0,
            divinePurpose: 4));
        Equal("auras.神圣意志", freeEternalFlame.CooldownConfirmationStateField,
            "MOD-07 free Eternal Flame confirms against Divine Purpose");
        Equal(ConfirmationStateChangeKind.Cleared, freeEternalFlame.ConfirmationStateChange,
            "MOD-07 Divine Purpose must clear instead of merely counting down");

        var sequenceTracker = new ModuleDerivedStateTracker(new ManualTimeProvider());
        Equal("黎明之光", Action(Evaluate(State(
            [80, 80, 80, 80, 100],
            holyPower: 3,
            virtueAura: 5,
            bellCooldown: 0,
            auraMasteryCooldown: 10), sequenceTracker)),
            "MOD-08 burst sequence begins with Light of Dawn when real group damage is active");
        Equal("圣洁鸣钟", Action(Evaluate(State(
            [80, 80, 80, 100, 100],
            holyPower: 0,
            virtueAura: 5,
            bellCooldown: 0,
            auraMasteryCooldown: 10), sequenceTracker)),
            "MOD-08 Divine Toll requires three fifteen-percent deficits while the burst hold is active");
        Equal("圣光术", Action(Evaluate(State(
            [85, 85, 85, 95, 100],
            holyPower: 0,
            virtueAura: 5,
            bellCooldown: 0,
            auraMasteryCooldown: 10), sequenceTracker)),
            "MOD-08 Divine Toll does not cast when current total deficit is exactly fifty");
        var divineTollDecision = Evaluate(State(
            [85, 85, 85, 85, 100],
            holyPower: 2,
            virtueAura: 5,
            bellCooldown: 0,
            auraMasteryCooldown: 10));
        Equal("圣洁鸣钟", Action(divineTollDecision),
            "MOD-09 insufficient Holy Power uses Divine Toll before Eternal Flame");
        Equal("圣洁鸣钟", divineTollDecision.CooldownConfirmationSpell,
            "MOD-09 Divine Toll waits for the game cooldown before retrying");
        Equal("圣光术", Action(Evaluate(State(
            [80, 80, 80, 80, 100],
            virtueCooldown: 10,
            auraMasteryCooldown: 0))),
            "MOD-10 Aura Mastery cannot execute outside Virtue");
        Equal("圣光术", Action(Evaluate(
            State(
                [85, 85, 85, 100, 100],
                virtueCooldown: 0,
                bellCooldown: 0,
                auraMasteryCooldown: 10),
            suppressedActions: new HashSet<LogicActionKey> { new("美德道标", 1) })),
            "MOD-10 Divine Toll cannot take over after Virtue fails");
        Equal("圣光术", Action(Evaluate(
            State(
                [85, 85, 85, 100, 100],
                virtueCooldown: 0,
                bellCooldown: 10,
                auraMasteryCooldown: 0),
            suppressedActions: new HashSet<LogicActionKey> { new("美德道标", 1) })),
            "MOD-10 Aura Mastery cannot take over after Virtue fails");
        Equal("圣光闪现", Action(Evaluate(State(
            [40, 40, 40, 100, 100],
            virtueCooldown: 10,
            auraMasteryCooldown: 0,
            infusion: 1))),
            "MOD-10 direct healing precedes Aura Mastery while a member is critically low");

        Equal("荣耀圣令", Action(Evaluate(State([60, 100, 100, 100, 100], holyPower: 3))),
            "MOD-11 real health below seventy uses Eternal Flame");
        Equal("荣耀圣令", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            [15, 15, 15, 15, 0],
            holyPower: 3))),
            "MOD-12 healing absorb uses Eternal Flame and prevents Light of Dawn");
        Equal("圣光闪现", Action(Evaluate(State(
            [85, 100, 100, 100, 100],
            infusion: 1,
            divineHand: 1))),
            "MOD-13 Infusion Flash of Light precedes Hand of Divinity Holy Light");
        Equal("圣光闪现", Action(Evaluate(State(
            [85, 100, 100, 100, 100],
            infusion: 1,
            shockCharges: 2))),
            "MOD-14 stronger Infusion Flash of Light precedes even capped Holy Shock on real injury");
        Equal("神圣震击", Action(Evaluate(State(
            [85, 100, 100, 100, 100],
            shockCharges: 2,
            judgmentCooldown: 0))),
            "MOD-15 Holy Shock with two charges precedes Judgment");
        Equal("神圣震击", Action(Evaluate(State(
            [85, 100, 100, 100, 100],
            shockCharges: 1))),
            "MOD-16 Holy Shock with one charge remains available");
        Equal("审判", Action(Evaluate(State(
            [85, 100, 100, 100, 100],
            judgmentCooldown: 0))),
            "MOD-17 Judgment fills only after instant healing is unavailable");
        Equal("审判", Action(Evaluate(State(
            [85, 100, 100, 100, 100],
            judgmentCooldown: 0,
            targetType: 152))),
            "MOD-17 Judgment generates Holy Power before bare Holy Light while targeting a friendly unit");
        Equal("清洁术", Action(Evaluate(State(
            [85, 100, 100, 100, 100],
            holyPower: 3,
            dispelSlot: 2))),
            "MOD-18 Cleanse precedes ordinary healing when everyone is at least seventy percent");
        Equal("圣光术", Action(Evaluate(State(
            [69, 100, 100, 100, 100],
            combatTime: 0,
            dispelSlot: 2))),
            "MOD-18 sub-seventy emergency healing precedes Cleanse");
        Equal("清洁术", Action(Evaluate(State(
            [70, 100, 100, 100, 100],
            combatTime: 0,
            dispelSlot: 2))),
            "MOD-18 exactly seventy percent permits Cleanse");
        Equal("暂停", Action(Evaluate(State(
            [85, 100, 100, 100, 100],
            combatTime: 0,
            moving: true))),
            "MOD-19 movement blocks bare Holy Light when instant healing is unavailable");

        var magicCleanse = Evaluate(State(
            [100, 100, 100, 100, 100],
            dispelSlot: 2));
        Equal("清洁术", Action(magicCleanse),
            "MOD-20 Cleanse targeting and action remain intact when the group is safe");
        Equal(1, Convert.ToInt32(magicCleanse.UnitInfo["目标驱散类型"]),
            "MOD-20 Cleanse logs the selected Magic debuff type");
        Equal("清洁术", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            dispelSlot: 2,
            dispelType: 3))),
            "MOD-20 Cleanse also handles Disease");
        Equal("清洁术", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            dispelSlot: 2,
            dispelType: 4))),
            "MOD-20 Cleanse also handles Poison");
        Equal("清洁术", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            [20, 0, 0, 0, 0],
            holyPower: 3,
            dispelSlot: 2))),
            "MOD-20 Cleanse precedes absorb-only healing when real health is safe");
        Equal("清洁术", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            dispelSlot: 2,
            channeling: 1))),
            "MOD-20 Cleanse can be queued while another cast is in progress");
        Equal("神圣震击", Action(Evaluate(State(
            [100, 92, 100, 100, 100],
            infusion: 1,
            shockCharges: 2))),
            "MOD-21 ninety-two percent uses healthy-group offensive Holy Shock after the ninety-percent threshold change");
        Equal("神圣震击", Action(Evaluate(State(
            [100, 92, 100, 100, 100],
            infusion: 1,
            shockCharges: 1))),
            "MOD-21 ninety-two percent does not use the light-injury Flash of Light branch");
        Equal("神圣震击", Action(Evaluate(State(
            [100, 93, 100, 100, 100],
            shockCharges: 2,
            judgmentCooldown: 0))),
            "MOD-22 bare Flash of Light was removed");
        Equal("神圣震击", Action(Evaluate(State(
            [100, 93, 100, 100, 100],
            infusion: 1,
            shockCharges: 1,
            judgmentCooldown: 0))),
            "MOD-22 ninety-three percent uses healthy-group offensive Holy Shock without bare Flash of Light");
        Equal("正义盾击", Action(Evaluate(State(
            [100, 93, 100, 100, 100],
            holyPower: 5,
            infusion: 1))),
            "MOD-23 nearby hostile target allows Shield of the Righteous to prevent Holy Power overflow during light injury");
        Equal("正义盾击", Action(Evaluate(State(
            [100, 93, 100, 100, 100],
            holyPower: 5,
            infusion: 1,
            targetInFront: 0))),
            "MOD-23 restricted frontal state does not block Shield of the Righteous");
        Equal("黎明之光", Action(Evaluate(State(
            [92, 93, 94, 100, 100],
            holyPower: 3))),
            "MOD-24 three members below ninety-five use non-Virtue Light of Dawn before offensive fillers");
        Equal("正义盾击", Action(Evaluate(State(
            [95, 95, 95, 100, 100],
            holyPower: 3))),
            "MOD-24 exactly ninety-five percent does not qualify for non-Virtue Light of Dawn");
        Equal("暂停", Action(Evaluate(State(
            [100, 92, 100, 100, 100],
            combatTime: 0))),
            "MOD-25 ninety-two percent is outside the ninety-percent light-injury threshold");
        Equal("暂停", Action(Evaluate(State(
            [100, 95, 100, 100, 100],
            combatTime: 0))),
            "MOD-26 bare Holy Light does not cast on minor injury above ninety-two percent");
        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            combatTime: 0))),
            "MOD-27 full out-of-combat group does not cast healing spells");
        var expiringInfusion = Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 4,
            combatTime: 0,
            infusion: 5));
        Equal("圣光闪现", Action(expiringInfusion),
            "MOD-27 expiring out-of-combat Infusion converts to Holy Power");
        Equal(1, Convert.ToInt32(expiringInfusion.UnitInfo["动作单位槽位"]),
            "MOD-27 out-of-combat Infusion conversion targets the player slot");
        Equal("圣光闪现", expiringInfusion.CooldownConfirmationSpell,
            "MOD-27 Infusion conversion tracks the Flash of Light action");
        Equal("auras.圣光灌注层数", expiringInfusion.CooldownConfirmationStateField,
            "MOD-27 Infusion conversion confirms by the consumed Infusion stack");
        Equal(1, expiringInfusion.CooldownConfirmationInitialValue,
            "MOD-27 Infusion conversion captures the initial Infusion stack");
        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 4,
            combatTime: 0,
            infusion: 6))),
            "MOD-27 non-expiring Infusion remains reserved");
        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 5,
            combatTime: 0,
            infusion: 5))),
            "MOD-27 capped Holy Power does not consume Infusion");
        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 4,
            combatTime: 0,
            infusion: 5,
            stage: 1))),
            "MOD-27 AOE resource reserve blocks out-of-combat Infusion conversion");
        Equal("圣光闪现", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 4,
            combatTime: 0,
            infusion: 5,
            stage: 1,
            bellCooldown: 0))),
            "MOD-27 ready Divine Toll releases the AOE reservation for out-of-combat Infusion conversion");
        Equal("圣光闪现", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 4,
            combatTime: 0,
            infusion: 5,
            stage: 1,
            bellCooldown: 10,
            bellExpectedReady: true))),
            "MOD-27 predicted-ready Divine Toll releases the AOE reservation for out-of-combat Infusion conversion");

        Equal("荣耀圣令", Action(Evaluate(State(
            [80, 100, 100, 100, 100],
            holyPower: 3,
            stage: 1,
            bellCooldown: 0))),
            "MOD-27 ready Divine Toll releases the AOE reservation for light Word of Glory");
        Equal("荣耀圣令", Action(Evaluate(State(
            [80, 100, 100, 100, 100],
            holyPower: 3,
            stage: 1,
            bellCooldown: 10,
            bellExpectedReady: true))),
            "MOD-27 predicted-ready Divine Toll releases the AOE reservation before its cooldown reaches zero");
        Equal("圣光术", Action(Evaluate(State(
            [80, 100, 100, 100, 100],
            holyPower: 3,
            stage: 1,
            bellCooldown: 10))),
            "MOD-27 unavailable Divine Toll keeps the AOE resource reservation for light Word of Glory");

        var playerPriority = Evaluate(State([60, 60, 100, 100, 100], holyPower: 3));
        Equal("荣耀圣令", Action(playerPriority), "MOD-28 equal-risk player receives the expected heal");
        Equal(1, Convert.ToInt32(playerPriority.UnitInfo["动作单位槽位"]),
            "MOD-28 equal-risk player wins the stable slot tie");
        var freeHealthyShield = Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 3,
            divinePurpose: 4,
            shockCharges: 2,
            judgmentCooldown: 0));
        Equal("正义盾击", Action(freeHealthyShield),
            "MOD-29 full-health Divine Purpose uses Shield of the Righteous before Light of Dawn or fillers");
        Equal("正义盾击", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 4,
            judgmentCooldown: 0))),
            "MOD-29 full-health four Holy Power uses Shield instead of Light of Dawn");
        Equal("auras.神圣意志", freeHealthyShield.CooldownConfirmationStateField,
            "MOD-29 free Shield of the Righteous confirms by clearing Divine Purpose");
        Equal("神圣震击", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            shockCharges: 2,
            judgmentCooldown: 0))),
            "MOD-29 full-health damage uses Holy Shock before Judgment");
        Equal("审判", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            judgmentCooldown: 0))),
            "MOD-30 full-health damage uses Judgment when Holy Shock is unavailable");
        Equal("正义盾击", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 4,
            infusion: 1,
            stage: 1,
            judgmentCooldown: 0))),
            "MOD-30 healthy four Holy Power spends Shield before an Infusion Judgment overflow");
        Equal("正义盾击", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 3,
            infusion: 1,
            stage: 1,
            judgmentCooldown: 0))),
            "MOD-30 healthy three Holy Power spends Shield before Infusion Judgment");
        Equal("神圣震击", Action(Evaluate(State(
            [100, 97, 100, 100, 100],
            shockCharges: 1,
            judgmentCooldown: 0))),
            "MOD-30 a healthy group with a trivial deficit still uses offensive Holy Shock");
        Equal("审判", Action(Evaluate(State(
            [100, 97, 100, 100, 100],
            judgmentCooldown: 0))),
            "MOD-30 a healthy group with a trivial deficit still uses Judgment");
        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 5,
            shockCharges: 2,
            judgmentCooldown: 0,
            combatTime: 0,
            enemyCount: 0))),
            "MOD-30 immediate out-of-combat state stops all offensive fillers without nameplate counts");
        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 5,
            stage: 1,
            shockCharges: 2,
            judgmentCooldown: 0,
            targetType: 0))),
            "MOD-30 full Holy Power with no current hostile target stops non-emergency fillers");
        Equal("神圣震击", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            stage: 3,
            shockCharges: 2,
            judgmentCooldown: 0,
            targetType: 152))),
            "MOD-30 absorb waiting uses tank-target offensive Holy Shock while targeting a friendly unit");
        Equal("审判", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            absorb: [10, 10, 10, 10, 0],
            stage: 3,
            judgmentCooldown: 0,
            targetType: 1))),
            "MOD-30 absorb waiting uses Judgment when a valid hostile target exists");
        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            stage: 3,
            judgmentCooldown: 0,
            targetType: 0))),
            "MOD-30 absorb waiting stops offensive fillers without a valid hostile target");
        var npcShock = Evaluate(State(
            [100, 100, 100, 100, 100],
            shockCharges: 2,
            targetType: 152,
            targetHealth: 50));
        Equal("神圣震击", Action(npcShock), "MOD-31 safe group heals a friendly NPC");
        Equal(ReservedUnit.Target, Convert.ToInt32(npcShock.UnitInfo["动作单位槽位"]),
            "MOD-31 friendly NPC healing uses the current target");

        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            holyPower: 5,
            stage: 5))),
            "MOD-32 stage five blocks ordinary resource and damage fillers");
        Equal("神圣震击", Action(Evaluate(State(
            [85, 100, 100, 100, 100],
            stage: 5,
            shockCharges: 2))),
            "MOD-33 stage five still allows true low-health healing");
        Equal("美德道标", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            aoeType: 1,
            stage: 2,
            virtueCooldown: 0,
            dispelSlot: 2))),
            "MOD-34 ordinary AOE execution window pre-casts Virtue");
        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            stage: 3,
            virtueCooldown: 0))),
            "MOD-35 a non-absorb stage three does not pre-cast Virtue");
        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            aoeType: 2,
            stage: 1,
            virtueCooldown: 0))),
            "MOD-35 heal absorb resource reserve does not pre-cast Virtue");
        Equal("荣耀圣令", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            [15, 15, 15, 15, 0],
            holyPower: 3,
            aoeType: 2,
            stage: 1,
            virtueCooldown: 0))),
            "MOD-35 healing absorbs alone receive direct healing without spending Virtue early");
        Equal("圣光术", Action(Evaluate(State(
            [85, 85, 85, 85, 100],
            aoeType: 2,
            stage: 1,
            virtueCooldown: 0))),
            "MOD-35 DiGua resource reserve keeps reactive Virtue for the verified execution stage");
        Equal("美德道标", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            aoeType: 2,
            stage: 3,
            virtueCooldown: 0))),
            "MOD-35 heal absorb cast completion pre-casts Virtue before the health deficit appears");
        Equal("荣耀圣令", Action(Evaluate(State(
            [80, 80, 80, 100, 100],
            virtueAura: 5,
            aoeType: 2,
            stage: 3,
            holyPower: 3))),
            "MOD-35 absorb stage three uses direct healing instead of Virtue Light of Dawn");

        var heldGroupDamageTracker = new ModuleDerivedStateTracker(new ManualTimeProvider());
        Equal("美德道标", Action(Evaluate(State(
            [80, 80, 80, 80, 100],
            virtueCooldown: 0), heldGroupDamageTracker)),
            "MOD-35 reactive group damage opens Virtue before the hold window");
        Equal("正义盾击", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            virtueAura: 5,
            holyPower: 3), heldGroupDamageTracker)),
            "MOD-35 current healing need gates held group Light of Dawn after the group recovers");

        Equal("美德道标", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            aoeType: 1,
            stage: 2,
            virtueCooldown: 0,
            channeling: 1))),
            "MOD-36 AOE execution window interrupts an ordinary channel for Virtue");
        Equal("神圣震击", Action(Evaluate(State(
            [60, 100, 100, 100, 100],
            shockCharges: 2,
            channeling: 1))),
            "MOD-37 true low-health healing interrupts an ordinary channel");
        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            channeling: 1))),
            "MOD-37 an ordinary channel remains protected without an urgent action");

        Equal("暂停", Action(Evaluate(State(
            [100, 100, 100, 100, 100],
            virtueAura: 5,
            bellCooldown: 0,
            auraMasteryCooldown: 0))),
            "MOD-38 current priority checks stop burst follow-up when all healing need is gone");

        Equal("黎明之光", Action(Evaluate(State(
            [94, 94, 94, 100, 100],
            holyPower: 3))),
            "MOD-38 three members below ninety-five use non-Virtue Light of Dawn before offensive fillers");
        Equal("黎明之光", Action(Evaluate(State(
            [94, 94, 94, 100, 100],
            holyPower: 3,
            combatTime: 0))),
            "MOD-38 out-of-combat non-Virtue Light of Dawn remains allowed");
        Equal("暂停", Action(Evaluate(State(
            [85, 100, 100, 100, 100],
            stage: 5,
            judgmentCooldown: 0))),
            "MOD-38 stage five preserves the final GCD for the verified AOE execution window");

        Equal("圣光术", Action(Evaluate(State(
            [70, 70, 100, 100, 100],
            virtueAura: 5,
            bellCooldown: 10,
            auraMasteryCooldown: 0))),
            "MOD-38 Aura Mastery is saved when only two severe-boundary targets remain");
        Equal("光环掌握", Action(Evaluate(State(
            [60, 60, 60, 100, 100],
            virtueAura: 5,
            bellCooldown: 10,
            auraMasteryCooldown: 0))),
            "MOD-38 Aura Mastery requires three thirty-percent deficits totaling one hundred twenty");

        foreach (var reserveStage in new[] { 1, 3, 5 })
        {
            var npcDuringReserve = Evaluate(State(
                [100, 100, 100, 100, 100],
                stage: reserveStage,
                shockCharges: 2,
                targetType: 152,
                targetHealth: 50));
            Equal("神圣震击", Action(npcDuringReserve),
                $"MOD-39 friendly NPC healing passes AOE reserve stage {reserveStage}");
        }

        Console.WriteLine("Holy paladin module replay: full priority matrix passed");
        return 0;
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"Holy paladin module replay failed: {exception.Message}");
        return 1;
    }
}

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
    var row = new List<int>();
    AddHealAbsorbSlot(row, expectedRow: 2, unit: 4, value: 7, unitWidth: 2);
    AddHealAbsorbSlot(row, expectedRow: 2, unit: 30, value: 0, unitWidth: 2, zeroEdgePixels: 1);
    AddHealAbsorbSlot(row, expectedRow: 2, unit: 5, value: 1, unitWidth: 2);
    AddHealAbsorbSlot(row, expectedRow: 2, unit: 6, value: 2, unitWidth: 2);
    AddHealAbsorbSlot(row, expectedRow: 2, unit: 7, value: 3, unitWidth: 2);
    AddHealAbsorbSlot(row, expectedRow: 2, unit: 8, value: 100, unitWidth: 2);
    var decoded = new Dictionary<int, int>();

    PixelProtocolDecoder.DecodeHealAbsorbRow(CollectionsMarshal.AsSpan(row), 2, decoded);

    Equal(6, decoded.Count, "valid heal absorb units");
    Equal(7, decoded[4], "unit 4 heal absorb");
    Equal(0, decoded[30], "unit 30 lower bound value");
    Equal(1, decoded[5], "one percent heal absorb remains precise");
    Equal(2, decoded[6], "two percent heal absorb remains precise");
    Equal(3, decoded[7], "three percent heal absorb remains precise");
    Equal(100, decoded[8], "full heal absorb remains precise");

    var wrongRow = new Dictionary<int, int>();
    PixelProtocolDecoder.DecodeHealAbsorbRow(CollectionsMarshal.AsSpan(row), 1, wrongRow);
    Equal(0, wrongRow.Count, "heal absorb row requires a matching row anchor");
}

static void HealAbsorbStabilizationContract()
{
    var stabilizer = new HealAbsorbStabilizer();

    var first = stabilizer.Observe(new Dictionary<int, int> { [1] = 1, [2] = 0 });
    Equal(true, first.HasPendingPositive, "first positive absorb frame waits for confirmation");
    Equal(0, first.Values[1], "unconfirmed one-percent absorb cannot trigger healing");

    var second = stabilizer.Observe(new Dictionary<int, int> { [1] = 2, [2] = 0 });
    Equal(false, second.HasPendingPositive, "second positive absorb frame is confirmed");
    Equal(2, second.Values[1], "confirmed absorb preserves the latest precise value");

    var cleared = stabilizer.Observe(new Dictionary<int, int> { [1] = 0, [2] = 0 });
    Equal(0, cleared.Values[1], "zero absorb clears immediately");

    var newSpike = stabilizer.Observe(new Dictionary<int, int> { [1] = 3 });
    Equal(true, newSpike.HasPendingPositive, "a new positive spike must be reconfirmed after zero");
    stabilizer.Reset();
    var afterReset = stabilizer.Observe(new Dictionary<int, int> { [1] = 100 });
    Equal(true, afterReset.HasPendingPositive, "reset discards prior positive history");
    Equal(0, afterReset.Values[1], "even a full absorb requires two frames without losing its magnitude");
}

static void AddHealAbsorbSlot(
    List<int> row,
    int expectedRow,
    int unit,
    int value,
    int unitWidth,
    int zeroEdgePixels = 0)
{
    row.AddRange(Enumerable.Repeat(Argb(expectedRow, unit, 0), unitWidth));
    row.AddRange(Enumerable.Repeat(Argb(255, 255, 255), value * unitWidth + zeroEdgePixels));
    if (value >= 100)
    {
        row.Add(Argb(200, 200, 200));
    }
    else
    {
        row.AddRange(Enumerable.Repeat(
            Argb(expectedRow, value + 1, unit),
            Math.Max(1, unitWidth - zeroEdgePixels)));
    }
    row.Add(Argb(0, 0, 0));
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

static void UnitSelectorExcludesUnavailableRoleContract()
{
    var group = new Dictionary<string, IReadOnlyDictionary<string, object?>>
    {
        ["1"] = new Dictionary<string, object?>
        {
            ["职责"] = 1,
            ["生命值"] = 80,
            ["治疗吸收"] = 0,
            ["驱散"] = 0
        },
        ["2"] = new Dictionary<string, object?>
        {
            ["职责"] = 0,
            ["生命值"] = 35,
            ["治疗吸收"] = 45,
            ["驱散"] = 2
        }
    };
    var state = new GameState(new Dictionary<string, object?> { ["group"] = group });

    Equal(
        "1",
        UnitSelector.Resolve(new ModuleUnit { Kind = UnitSelectorKind.LowestHealth }, state),
        "lowest health excludes an unavailable unit whose encoded role is zero");
    Equal(
        null,
        UnitSelector.Resolve(new ModuleUnit { Kind = UnitSelectorKind.HighestHealingAbsorb }, state),
        "healing absorb excludes an unavailable unit whose encoded role is zero");
    Equal(
        null,
        UnitSelector.Resolve(new ModuleUnit { Kind = UnitSelectorKind.UnitWithDispelType, DispelType = 2 }, state),
        "dispel excludes an unavailable unit whose encoded role is zero");
    Equal(
        null,
        UnitSelector.Resolve(new ModuleUnit { Kind = UnitSelectorKind.UnitWithAnyDispelType }, state),
        "any-dispel selector excludes an unavailable unit whose encoded role is zero");
    Equal(
        0,
        UnitSelector.Resolve(new ModuleCountField { Kind = CountKind.UnitsBelowHealth, HealthThreshold = 50 }, state),
        "health count excludes an unavailable unit whose encoded role is zero");
    Equal(
        0,
        UnitSelector.Resolve(new ModuleCountField { Kind = CountKind.UnitsAboveHealingAbsorb, HealthThreshold = 20 }, state),
        "healing absorb count excludes an unavailable unit whose encoded role is zero");

    var stalePlayer = new GameState(new Dictionary<string, object?>
    {
        ["生命值"] = 45,
        ["队伍类型"] = 46,
        ["group"] = new Dictionary<string, IReadOnlyDictionary<string, object?>>
        {
            ["1"] = new Dictionary<string, object?> { ["职责"] = 0, ["生命值"] = 100 },
            ["2"] = new Dictionary<string, object?> { ["职责"] = 1, ["生命值"] = 50 }
        }
    });
    Equal(
        "1",
        UnitSelector.Resolve(new ModuleUnit { Kind = UnitSelectorKind.LowestHealth }, stalePlayer),
        "party player slot uses the fresher independent player health");
    Equal(
        1,
        UnitSelector.Resolve(new ModuleCountField { Kind = CountKind.UnitsBelowHealth, HealthThreshold = 46 }, stalePlayer),
        "party health count reconciles the player slot");
    var playerModule = ModuleDefinition.CreateDefault("玩家血量校正");
    playerModule.Units =
    [
        new ModuleUnit { Name = "最低单位", HealthName = "最低生命值", Kind = UnitSelectorKind.LowestHealth }
    ];
    ModuleLogic.ResolveDynamicFields(playerModule, stalePlayer);
    Equal(
        true,
        ModuleConditionEvaluator.TryResolveInt(stalePlayer, "最低生命值", out var reconciledPlayerHealth),
        "selected player health field resolves through the module condition path");
    Equal(45, reconciledPlayerHealth, "selected player health field uses the same reconciled value");

    var staleRaidPlayer = new GameState(new Dictionary<string, object?>
    {
        ["生命值"] = 40,
        ["队伍类型"] = 7,
        ["group"] = new Dictionary<string, IReadOnlyDictionary<string, object?>>
        {
            ["2"] = new Dictionary<string, object?> { ["职责"] = 1, ["生命值"] = 50 },
            ["7"] = new Dictionary<string, object?> { ["职责"] = 0, ["生命值"] = 100 }
        }
    });
    Equal(
        "7",
        UnitSelector.Resolve(new ModuleUnit { Kind = UnitSelectorKind.LowestHealth }, staleRaidPlayer),
        "raid player slot is resolved from group type before health reconciliation");
}

static void UnitSelectorOtherPlayerContract()
{
    var state = new GameState(new Dictionary<string, object?>
    {
        ["生命值"] = 25,
        ["队伍类型"] = 46,
        ["group"] = new Dictionary<string, IReadOnlyDictionary<string, object?>>
        {
            ["1"] = new Dictionary<string, object?> { ["职责"] = 1, ["生命值"] = 25 },
            ["2"] = new Dictionary<string, object?> { ["职责"] = 5, ["生命值"] = 30 },
            ["3"] = new Dictionary<string, object?> { ["职责"] = 0, ["生命值"] = 10 }
        }
    });

    Equal("2", UnitSelector.Resolve(
            new ModuleUnit { Kind = UnitSelectorKind.LowestHealthOtherPlayer, HealthThreshold = 40 },
            state),
        "other-player selector excludes the player and unavailable NPC-like slot");
}

static void ModuleMissingBindingFallbackContract()
{
    var module = ModuleDefinition.CreateDefault("缺键回退");
    module.Id = "missing-binding-fallback";
    module.Rules =
    [
        new ModuleRule { Condition = "职业 == 2", Unit = 1, Spell = "缺失技能", MacroCondition = "" },
        new ModuleRule { Condition = "职业 == 2", Unit = 0, Spell = "可用技能", MacroCondition = "" }
    ];
    var state = new GameState(new Dictionary<string, object?> { ["职业"] = 2 });
    var decision = ModuleLogic.Run(module, state, new ContractKeymapResolver());

    Equal("CTRL-A", decision.Hotkey, "a matched rule without a binding falls through to an executable rule");
    Equal(true, decision.UnitInfo.ContainsKey("已跳过缺失按键"), "missing high-priority binding remains diagnosable");

    var legacyShield = ModuleDefinition.CreateDefault("旧盾击单位");
    legacyShield.Id = "legacy-shield-unit";
    legacyShield.Rules =
    [
        new ModuleRule { Condition = "职业 == 2", Unit = 0, Spell = "正义盾击", MacroCondition = "" }
    ];
    var shieldDecision = ModuleLogic.Run(legacyShield, state, new ContractKeymapResolver());
    Equal("正义盾击", shieldDecision.UnitInfo["动作技能"], "legacy shield rule remains executable");
    Equal(ReservedUnit.Target, Convert.ToInt32(shieldDecision.UnitInfo["动作单位槽位"]),
        "legacy shield unit 0 falls back to current target unit 32");
    Equal("正义盾击：Unit 0 → Unit 32", shieldDecision.UnitInfo["按键兼容回退"],
        "legacy shield fallback is explicit in diagnostics");
}

static void HealingDeficitSelectorContract()
{
    var group = new Dictionary<string, IReadOnlyDictionary<string, object?>>
    {
        ["1"] = new Dictionary<string, object?> { ["生命值"] = 60, ["治疗吸收"] = 0 },
        ["2"] = new Dictionary<string, object?> { ["生命值"] = 100, ["治疗吸收"] = 35 },
        ["3"] = new Dictionary<string, object?> { ["生命值"] = 90, ["治疗吸收"] = 50 },
        ["4"] = new Dictionary<string, object?> { ["生命值"] = 0, ["治疗吸收"] = 100 },
        ["5"] = new Dictionary<string, object?> { ["生命值"] = 100, ["治疗吸收"] = 0 }
    };
    var state = new GameState(new Dictionary<string, object?> { ["group"] = group });

    Equal(
        "3",
        UnitSelector.Resolve(new ModuleUnit
        {
            Kind = UnitSelectorKind.HighestHealingDeficit,
            HealthThreshold = 35
        }, state),
        "highest healing deficit combines missing health and absorb");
    Equal(
        3,
        UnitSelector.Resolve(new ModuleCountField
        {
            Kind = CountKind.UnitsAboveHealingDeficit,
            HealthThreshold = 30
        }, state),
        "healing deficit count includes real damage and absorb");
    Equal(
        1,
        UnitSelector.Resolve(new ModuleCountField
        {
            Kind = CountKind.UnitsAboveHealingDeficit,
            HealthThreshold = 40
        }, state),
        "healing deficit threshold is strict");
    Equal(
        0,
        UnitSelector.Resolve(new ModuleCountField
        {
            Kind = CountKind.UnitsAboveHealingDeficit,
            HealthThreshold = 60
        }, state),
        "dead units and exact-threshold deficits are excluded");

    var metricState = new GameState(new Dictionary<string, object?>
    {
        ["group"] = new Dictionary<string, IReadOnlyDictionary<string, object?>>
        {
            ["1"] = new Dictionary<string, object?> { ["职责"] = 5, ["生命值"] = 100, ["治疗吸收"] = 10 },
            ["2"] = new Dictionary<string, object?> { ["职责"] = 1, ["生命值"] = 90, ["治疗吸收"] = 0 },
            ["3"] = new Dictionary<string, object?> { ["职责"] = 2, ["生命值"] = 50, ["治疗吸收"] = 100 },
            ["4"] = new Dictionary<string, object?> { ["职责"] = 0, ["生命值"] = 50, ["治疗吸收"] = 100 },
            ["5"] = new Dictionary<string, object?> { ["职责"] = 5, ["生命值"] = 0, ["治疗吸收"] = 100 },
            ["6"] = new Dictionary<string, object?> { ["职责"] = 5, ["生命值"] = 99, ["治疗吸收"] = 0 }
        }
    });
    Equal(
        3,
        UnitSelector.Resolve(new ModuleCountField
        {
            Kind = CountKind.UnitsAtOrAboveHealingDeficit,
            HealthThreshold = 10
        }, metricState),
        "healing load count includes exact threshold and excludes dead or unavailable units");
    Equal(
        121,
        UnitSelector.Resolve(new ModuleCountField
        {
            Kind = CountKind.TotalHealingDeficit
        }, metricState),
        "healing load total caps each living available unit at one hundred percent");
    Equal(
        61,
        UnitSelector.Resolve(new ModuleCountField
        {
            Kind = CountKind.TotalHealthDeficit
        }, metricState),
        "real health deficit excludes absorbs and unavailable or dead units");

    static GameState FourUnitLoadState(params int[] deficits)
    {
        var group = deficits
            .Select((deficit, index) => new KeyValuePair<string, IReadOnlyDictionary<string, object?>>(
                (index + 1).ToString(),
                new Dictionary<string, object?>
                {
                    ["职责"] = index == 0 ? 1 : 5,
                    ["生命值"] = 100 - deficit,
                    ["治疗吸收"] = 0
                }))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new GameState(new Dictionary<string, object?> { ["group"] = group });
    }

    Equal(
        59,
        UnitSelector.Resolve(
            new ModuleCountField { Kind = CountKind.TotalHealingDeficit },
            FourUnitLoadState(15, 15, 15, 14)),
        "healing load total preserves the boundary below sixty");
    Equal(
        60,
        UnitSelector.Resolve(
            new ModuleCountField { Kind = CountKind.TotalHealingDeficit },
            FourUnitLoadState(15, 15, 15, 15)),
        "healing load total preserves the sixty boundary");
}

static void ModuleDerivedStateTrackerContract()
{
    var timeProvider = new ManualTimeProvider();
    var tracker = new ModuleDerivedStateTracker(timeProvider);
    var module = ModuleDefinition.CreateDefault("保持状态甲");
    module.Id = "hold-a";
    module.DerivedStates =
    [
        new ModuleDerivedState
        {
            Name = "群疗爆发保持",
            Condition = "触发 == 1",
            HoldMs = 6000
        }
    ];

    var triggered = new GameState(new Dictionary<string, object?> { ["触发"] = 1 });
    tracker.Apply(module, triggered, enabled: true);
    Equal(
        true,
        ModuleConditionEvaluator.TryResolveInt(triggered, "群疗爆发保持", out var initialValue),
        "derived state is exposed to module conditions");
    Equal(1, initialValue, "derived state activates immediately");

    timeProvider.Advance(TimeSpan.FromSeconds(5));
    var held = new GameState(new Dictionary<string, object?> { ["触发"] = 0 });
    tracker.Apply(module, held, enabled: true);
    Equal(true, ModuleConditionEvaluator.TryResolveInt(held, "群疗爆发保持", out var heldValue),
        "held derived state remains exposed after its trigger clears");
    Equal(1, heldValue, "derived state remains active inside the hold window");

    timeProvider.Advance(TimeSpan.FromMilliseconds(1001));
    var expired = new GameState(new Dictionary<string, object?> { ["触发"] = 0 });
    tracker.Apply(module, expired, enabled: true);
    Equal(true, ModuleConditionEvaluator.TryResolveInt(expired, "群疗爆发保持", out var expiredValue),
        "expired derived state remains a resolvable zero");
    Equal(0, expiredValue, "derived state expires after the configured hold window");

    var retriggered = new GameState(new Dictionary<string, object?> { ["触发"] = 1 });
    tracker.Apply(module, retriggered, enabled: true);
    var disabled = new GameState(new Dictionary<string, object?> { ["触发"] = 0 });
    tracker.Apply(module, disabled, enabled: false);
    Equal(true, ModuleConditionEvaluator.TryResolveInt(disabled, "群疗爆发保持", out var disabledValue),
        "disabled logic exposes cleared derived states");
    Equal(0, disabledValue, "disabling logic clears held state immediately");

    var moduleB = module.Clone();
    moduleB.Id = "hold-b";
    var moduleBIdle = new GameState(new Dictionary<string, object?> { ["触发"] = 0 });
    tracker.Apply(moduleB, moduleBIdle, enabled: true);
    Equal(true, ModuleConditionEvaluator.TryResolveInt(moduleBIdle, "群疗爆发保持", out var switchedValue),
        "same-name state in another module remains resolvable");
    Equal(0, switchedValue, "switching modules does not inherit held state");

    var directory = Path.Combine(Path.GetTempPath(), $"shigure-derived-state-{Guid.NewGuid():N}");
    try
    {
        var store = new ModuleStore(directory);
        var integratedModule = ModuleDefinition.CreateDefault("派生状态集成");
        integratedModule.Id = "derived-integration";
        integratedModule.Match = new ModuleMatch { ClassId = 2, SpecId = 1, PartyType = "46" };
        integratedModule.Counts =
        [
            new ModuleCountField
            {
                Name = "明显负荷人数",
                Kind = CountKind.UnitsAtOrAboveHealingDeficit,
                HealthThreshold = 10
            },
            new ModuleCountField { Name = "治疗负荷总和", Kind = CountKind.TotalHealingDeficit }
        ];
        integratedModule.DerivedStates =
        [
            new ModuleDerivedState
            {
                Name = "群疗爆发保持",
                Condition = "明显负荷人数 >= 4 && 治疗负荷总和 >= 60",
                HoldMs = 6000
            }
        ];
        store.Save(integratedModule);
        var registry = new LogicRegistry(
            new ContractKeymapResolver(),
            store,
            integratedModule.Id,
            timeProvider: timeProvider);
        var group = new Dictionary<string, IReadOnlyDictionary<string, object?>>
        {
            ["1"] = new Dictionary<string, object?> { ["职责"] = 1, ["生命值"] = 85 },
            ["2"] = new Dictionary<string, object?> { ["职责"] = 5, ["生命值"] = 85 },
            ["3"] = new Dictionary<string, object?> { ["职责"] = 5, ["生命值"] = 85 },
            ["4"] = new Dictionary<string, object?> { ["职责"] = 5, ["生命值"] = 85 }
        };
        var integratedState = new GameState(new Dictionary<string, object?>
        {
            ["队伍类型"] = 46,
            ["group"] = group
        });
        registry.Evaluate(2, 1, "神圣", integratedState, runLogic: true);
        Equal(true, ModuleConditionEvaluator.TryResolveInt(integratedState, "群疗爆发保持", out var integratedValue),
            "logic registry exposes derived states after resolving group metrics");
        Equal(1, integratedValue, "group metric condition activates the integrated derived state");

        var logicDisabledState = new GameState(new Dictionary<string, object?>
        {
            ["队伍类型"] = 46,
            ["group"] = group
        });
        registry.Evaluate(2, 1, "神圣", logicDisabledState, runLogic: false);
        Equal(true, ModuleConditionEvaluator.TryResolveInt(logicDisabledState, "群疗爆发保持", out var logicDisabledValue),
            "disabled registry evaluation exposes a cleared derived state");
        Equal(0, logicDisabledValue, "disabled registry evaluation clears the hold immediately");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
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
              "有效性": { "step": 10, "type": "bool" },
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

        var builder = new StateBuilder(new ConfigService(fixturePath));
        var state = builder.Build(
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
                [10] = 5,
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
        Equal(5, state.GetInt("有效性"), "validity reason code survives bool config compatibility");
        Equal(false, state.GetBool("开关", true), "boolean state field");
        Equal("123", state.GetValue("标签") as string, "string state field");
        Equal(0, state.GetInt("缺失数字", -1), "missing configured number defaults to zero");
        Equal(19, state.GetInt("动作条值"), "bar state field");
        Equal(14, Convert.ToInt32(state.Spells["快速治疗"]), "spell field");
        Equal(true, Convert.ToBoolean(state.Auras["救赎"]), "aura field");

        File.WriteAllText(
            fixturePath,
            """
            {
              "锚点": { "step": 1, "type": "int" },
              "职业": { "step": 2, "type": "int" },
              "专精": { "step": 3, "type": "int" },
              "state": {},
              "2": {
                "1": {
                  "AOE事件类型": { "step": 4, "type": "int" },
                  "AOE事件阶段": { "step": 5, "type": "int" },
                  "公共冷却时长": { "step": 6, "type": "int" },
                  "AOE受保护读条": { "step": 7, "type": "bool" },
                  "AOE读条剩余": { "step": 8, "type": "int" }
                }
              }
            }
            """);
        builder = new StateBuilder(new ConfigService(fixturePath));
        var protectedWindow = builder.Build(
            new Dictionary<int, int>
            {
                [1] = 233, [2] = 2, [3] = 1, [4] = 1, [5] = 1, [6] = 150, [7] = 1, [8] = 9
            },
            new Dictionary<int, int>());
        Equal(2, protectedWindow.GetInt("AOE事件阶段"),
            "protected cast enters the ordinary AOE Virtue window from decoded remaining time");
        var protectedSafeGcd = builder.Build(
            new Dictionary<int, int>
            {
                [1] = 233, [2] = 2, [3] = 1, [4] = 1, [5] = 1, [6] = 150, [7] = 1, [8] = 25
            },
            new Dictionary<int, int>());
        Equal(5, protectedSafeGcd.GetInt("AOE事件阶段"),
            "protected cast enters the last safe GCD from decoded remaining time");
        var uncorrelated = builder.Build(
            new Dictionary<int, int>
            {
                [1] = 233, [2] = 2, [3] = 1, [4] = 1, [5] = 1, [6] = 150, [7] = 0, [8] = 9
            },
            new Dictionary<int, int>());
        Equal(1, uncorrelated.GetInt("AOE事件阶段"),
            "remaining time cannot open Virtue without a protected semantic match");
        Equal(30, state.Group.Count, "fixed group slot count");
        Equal(80, Convert.ToInt32(state.Group["1"]["生命值"]), "heal absorb does not alter health");
        Equal(12, Convert.ToInt32(state.Group["1"]["治疗吸收"]), "heal absorb remains independent");
        Equal(30, Convert.ToInt32(state.Group["2"]["生命值"]), "heal absorb remains separate from health");
        Equal(2, state.HealAbsorbDiagnostic?.DecodedUnitCount, "heal absorb decoded unit count");
        Equal(
            new HealAbsorbUnitDiagnostic(1, 80, 12, 80),
            state.HealAbsorbDiagnostic?.PositiveUnits[0],
            "heal absorb first diagnostic unit");
        Equal(
            new HealAbsorbUnitDiagnostic(2, 30, 50, 30),
            state.HealAbsorbDiagnostic?.PositiveUnits[1],
            "heal absorb second diagnostic unit");
        Equal(1, Convert.ToInt32(state.Group["2"]["职责"]), "relative group step");
        Equal(true, Convert.ToBoolean(state.Group["30"]["动作条状态"]), "group bar field");
    }
    finally
    {
        File.Delete(fixturePath);
    }
}

static void HealAbsorbDiagnosticLogContract()
{
    var tracker = new HealAbsorbLogTracker();
    Equal(
        "治疗吸收诊断：正值 0，解码槽位 5",
        tracker.Observe(new HealAbsorbDiagnosticSnapshot(5, [])),
        "first valid scan records a zero baseline");
    Equal(
        null,
        tracker.Observe(new HealAbsorbDiagnosticSnapshot(5, [])),
        "unchanged zero baseline is suppressed");

    var positive = new HealAbsorbDiagnosticSnapshot(
        5,
        [new HealAbsorbUnitDiagnostic(2, 100, 35, 100)]);
    Equal(
        "治疗吸收诊断：正值 1，解码槽位 5；单位 2：原始生命 100%，吸收 35%，规则生命 100%",
        tracker.Observe(positive),
        "positive absorb records the full decision path");
    Equal(null, tracker.Observe(positive), "unchanged absorb is suppressed");
    Equal(
        "治疗吸收诊断：正值 0，解码槽位 5",
        tracker.Observe(new HealAbsorbDiagnosticSnapshot(5, [])),
        "absorb removal is recorded");

    tracker.Reset();
    Equal(
        "治疗吸收诊断：正值 0，解码槽位 5",
        tracker.Observe(new HealAbsorbDiagnosticSnapshot(5, [])),
        "a new runtime session records its own baseline");
}

static void AoeWarningDiagnosticLogContract()
{
    var tracker = new AoeWarningLogTracker();
    var idle = new GameState(new Dictionary<string, object?>
    {
        ["AOE事件类型"] = 0,
        ["AOE事件阶段"] = 0
    });
    Equal(null, tracker.Observe(idle), "idle warning state is silent");
    Equal(0, tracker.ObserveDiagnostics(idle).Count, "idle AOE diagnostics are silent");

    var bridgeDiagnostic = new GameState(new Dictionary<string, object?>
    {
        ["AOE桥接请求数"] = 1,
        ["AOE桥接成功数"] = 1,
        ["AOE带技能预警数"] = 1,
        ["AOE敌方读条数"] = 0,
        ["AOE读条未采纳数"] = 0,
        ["AOE读条匹配数"] = 0,
        ["AOE读条未匹配数"] = 0,
        ["AOE读条成功数"] = 0,
        ["AOE读条失败数"] = 0,
        ["AOE预警技能低位"] = 149,
        ["AOE预警技能中位"] = 239,
        ["AOE预警技能高位"] = 19
    });
    Equal(
        "AOE诊断：桥接重发请求 +1，桥接重发成功 +1，带 Spell ID 预警 +1；预警 Spell ID 1306517",
        tracker.ObserveDiagnostics(bridgeDiagnostic).Single(),
        "bridge diagnostics include the reconstructed expected Spell ID");

    var unmatchedDiagnostic = new GameState(new Dictionary<string, object?>
    {
        ["AOE桥接请求数"] = 1,
        ["AOE桥接成功数"] = 1,
        ["AOE带技能预警数"] = 1,
        ["AOE敌方读条数"] = 1,
        ["AOE读条未采纳数"] = 0,
        ["AOE读条匹配数"] = 0,
        ["AOE读条未匹配数"] = 1,
        ["AOE读条成功数"] = 0,
        ["AOE读条失败数"] = 0,
        ["AOE预警技能低位"] = 149,
        ["AOE预警技能中位"] = 239,
        ["AOE预警技能高位"] = 19,
        ["AOE读条技能低位"] = 182,
        ["AOE读条技能中位"] = 239,
        ["AOE读条技能高位"] = 19
    });
    Equal(
        "AOE诊断：候选敌方读条 +1，读条未匹配 +1；预警 Spell ID 1306517，读条 Spell ID 1306550",
        tracker.ObserveDiagnostics(unmatchedDiagnostic).Single(),
        "unmatched diagnostics expose expected and observed Spell IDs");
    Equal(0, tracker.ObserveDiagnostics(unmatchedDiagnostic).Count, "unchanged AOE diagnostics are suppressed");

    tracker.ResetDiagnosticBaseline();
    var staleLayout = new GameState(new Dictionary<string, object?>
    {
        ["AOE桥接请求数"] = 100,
        ["AOE桥接成功数"] = 5,
        ["AOE带技能预警数"] = 255,
        ["AOE原始读条数"] = 200
    });
    Equal(0, tracker.ObserveDiagnostics(staleLayout).Count,
        "the first protocol sample establishes a silent diagnostic baseline");
    tracker.ResetDiagnosticBaseline();
    Equal(0, tracker.ObserveDiagnostics(idle).Count,
        "protocol recovery establishes a new baseline instead of modulo-wrap garbage");

    var reserve = new GameState(new Dictionary<string, object?>
    {
        ["AOE事件类型"] = 1,
        ["AOE事件阶段"] = 1,
        ["神圣能量"] = 5,
        ["D10AtLeast"] = 0,
        ["DTotal"] = 0,
        ["群疗爆发保持"] = 0,
        ["auras"] = new Dictionary<string, object?>
        {
            ["圣光灌注"] = 8,
            ["圣光灌注层数"] = 2
        }
    });
    Equal(
        "AOE预警：普通AOE / 资源预留；圣能 5，圣光灌注 2 层 / 8 秒；明显缺口 0 人，总负荷 0，爆发保持 否，鸣钟预计可用 否",
        tracker.Observe(reserve),
        "resource reservation transition is logged");
    Equal(null, tracker.Observe(reserve), "unchanged warning state is suppressed");

    var degradedTracker = new AoeWarningLogTracker();
    Equal(
        "AOE预警：普通AOE / 资源预留；圣能 5，圣光灌注 2 层 / 8 秒；明显缺口 0 人，总负荷 0，爆发保持 否，鸣钟预计可用 否",
        degradedTracker.Observe(reserve),
        "degraded warning starts from resource reservation");
    Equal(
        "AOE预警：已结束；未进入执行窗口，可能为读条未匹配、受保护值或预警取消",
        degradedTracker.Observe(idle),
        "reservation-only completion records the safe-degradation possibilities");

    var gcdHold = new GameState(new Dictionary<string, object?>
    {
        ["AOE事件类型"] = 1,
        ["AOE事件阶段"] = 5,
        ["神圣能量"] = 4,
        ["D10AtLeast"] = 4,
        ["DTotal"] = 60,
        ["群疗爆发保持"] = 1,
        ["auras"] = new Dictionary<string, object?>()
    });
    Equal(
        "AOE预警：普通AOE / 停止非紧急GCD；圣能 4，圣光灌注 0 层 / 0 秒；明显缺口 4 人，总负荷 60，爆发保持 是，鸣钟预计可用 否",
        tracker.Observe(gcdHold),
        "final safe-GCD transition and burst metrics are logged");

    var absorbWaiting = new GameState(new Dictionary<string, object?>
    {
        ["AOE事件类型"] = 2,
        ["AOE事件阶段"] = 3,
        ["神圣能量"] = 3,
        ["D10AtLeast"] = 2,
        ["DTotal"] = 30,
        ["群疗爆发保持"] = 0,
        ["auras"] = new Dictionary<string, object?>()
    });
    Equal(
        "AOE预警：治疗吸收 / 等待生效；圣能 3，圣光灌注 0 层 / 0 秒；明显缺口 2 人，总负荷 30，爆发保持 否，鸣钟预计可用 否",
        tracker.Observe(absorbWaiting),
        "heal absorb delay transition is logged");
    Equal("AOE预警：已结束；治疗吸收等待窗口结束", tracker.Observe(idle), "warning completion is logged");

    tracker.Reset();
    Equal(null, tracker.Observe(idle), "reset restores a silent baseline");
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
            },
            new ModuleUnit
            {
                Name = "治疗缺口目标",
                Kind = UnitSelectorKind.HighestHealingDeficit,
                HealthThreshold = 10
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
            },
            new ModuleCountField
            {
                Name = "大缺口人数",
                Kind = CountKind.UnitsAboveHealingDeficit,
                HealthThreshold = 30
            },
            new ModuleCountField
            {
                Name = "明显负荷人数",
                Kind = CountKind.UnitsAtOrAboveHealingDeficit,
                HealthThreshold = 10
            },
            new ModuleCountField
            {
                Name = "治疗负荷总和",
                Kind = CountKind.TotalHealingDeficit
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
        module.DerivedStates =
        [
            new ModuleDerivedState
            {
                Name = "群疗保持",
                Condition = "明显负荷人数 >= 4 && 治疗负荷总和 >= 60",
                HoldMs = 6000
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
        Equal(UnitSelectorKind.LowestHealthWithAnyAura, loaded.Units[0].Kind, "module unit kind round trips");
        Equal("光环甲,光环乙", string.Join(',', loaded.Units[0].AuraNames!), "module aura list round trips");
        Equal(UnitSelectorKind.HighestHealingDeficit, loaded.Units[1].Kind, "healing deficit unit kind round trips");
        Equal(CountKind.UnitsWithoutAuraBelowHealth, loaded.Counts[0].Kind, "module count kind round trips");
        Equal(CountKind.UnitsAboveHealingDeficit, loaded.Counts[1].Kind, "healing deficit count kind round trips");
        Equal(CountKind.UnitsAtOrAboveHealingDeficit, loaded.Counts[2].Kind, "inclusive healing load count kind round trips");
        Equal(CountKind.TotalHealingDeficit, loaded.Counts[3].Kind, "total healing load kind round trips");
        Equal("群疗保持", loaded.DerivedStates.Single().Name, "module derived state name round trips");
        Equal(6000, loaded.DerivedStates.Single().HoldMs, "module derived state duration round trips");
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

static void ModuleLoadFailureContract()
{
    var fixtureRoot = Path.Combine(Path.GetTempPath(), $"shigure-module-load-{Guid.NewGuid():N}");
    var moduleDirectory = Path.Combine(fixtureRoot, "module");
    Directory.CreateDirectory(moduleDirectory);
    try
    {
        File.WriteAllText(
            Path.Combine(moduleDirectory, "unknown-enum.json"),
            """
            {
              "Name": "未知枚举模块",
              "Units": [
                { "Name": "目标", "Kind": "UnknownSelector" }
              ]
            }
            """);

        var store = new ModuleStore(moduleDirectory);
        Equal(0, store.GetModules().Count, "invalid module is not loaded");
        var failure = store.GetLoadFailures().Single();
        Equal("unknown-enum.json", Path.GetFileName(failure.FilePath), "failed module path is retained");
        Equal("JsonException", failure.ErrorType, "failed module error type is retained");
        Equal(true, failure.Message.Contains("$.Units[0].Kind", StringComparison.Ordinal),
            "failed module error identifies the invalid enum field");
    }
    finally
    {
        Directory.Delete(fixtureRoot, recursive: true);
    }
}

static void LegacyModuleStateCompatibilityContract()
{
    var legacy = new ModuleDefinition
    {
        Id = "legacy-cast-module",
        Name = "旧施法字段模块",
        ValueAdjustments =
        [
            new ModuleValueAdjustment
            {
                Field = "施法",
                Condition = "施法 != 0",
                Formula = "施法 * 2"
            }
        ],
        Rules =
        [
            new ModuleRule
            {
                Condition = "施法 != 0 && 施法技能 == 0",
                SubConditions = ["state.施法 > 0", "目标施法可打断 == 1"],
                Spell = "暂停"
            }
        ],
        Dependencies = new ModuleDependencySnapshot
        {
            Config = new ModuleConfigSnapshot
            {
                Spec = new ModuleSpecSnapshot
                {
                    FlatStates = ["施法"],
                    CategorizedStates = new Dictionary<string, List<string>>
                    {
                        [ClassStateCatalog.CategoryState] = ["施法", "施法(倒计时)"]
                    }
                }
            }
        }
    };

    var parsed = ModuleStore.Parse(JsonSerializer.SerializeToUtf8Bytes(legacy));
    Equal("施法(倒计时)", parsed.ValueAdjustments.Single().Field, "legacy adjustment field is migrated");
    Equal("施法(倒计时) != 0", parsed.ValueAdjustments.Single().Condition, "legacy adjustment condition is migrated");
    Equal("施法(倒计时) * 2", parsed.ValueAdjustments.Single().Formula, "legacy adjustment formula is migrated");
    Equal("施法(倒计时) != 0 && 施法技能 == 0", parsed.Rules.Single().Condition,
        "standalone legacy rule field is migrated without rewriting prefixed fields");
    Equal("state.施法(倒计时) > 0", parsed.Rules.Single().SubConditions![0], "qualified legacy field is migrated");
    Equal("目标施法可打断 == 1", parsed.Rules.Single().SubConditions![1], "different cast field is preserved");
    Equal("施法(倒计时)", parsed.Dependencies!.Config.Spec.FlatStates.Single(), "flat dependency state is migrated");
    Equal("施法(倒计时)", parsed.Dependencies.Config.Spec.CategorizedStates[ClassStateCatalog.CategoryState].Single(),
        "categorized dependency state is migrated and deduplicated");

    var idle = new GameState(new Dictionary<string, object?>
    {
        ["施法(倒计时)"] = 0,
        ["施法技能"] = 0
    });
    Equal(true, ModuleConditionEvaluator.TryEvaluate(parsed.Rules.Single().Condition, idle, out var idleMatched, out _),
        "migrated idle condition evaluates");
    Equal(false, idleMatched, "migrated idle cast condition does not pause");
    Equal(true, ModuleConditionEvaluator.TryEvaluate("不存在字段 != 0", idle, out var missingMatched, out _),
        "missing field comparison evaluates safely");
    Equal(false, missingMatched, "missing field inequality does not become true");

    idle.Values["施法(倒计时)"] = 5;
    Equal(true, ModuleConditionEvaluator.TryEvaluate(parsed.Rules.Single().Condition, idle, out var castingMatched, out _),
        "migrated casting condition evaluates");
    Equal(true, castingMatched, "remaining cast time still pauses while casting");
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

        var paladinPath = Path.Combine(classDirectory, "Paladin.lua");
        File.Copy(Path.Combine(repositoryRoot, "Fuyutsui", "class", "Paladin.lua"), paladinPath);
        var paladinModule = ModuleDefinition.CreateDefault("圣骑队伍依赖");
        paladinModule.Match.ClassId = 2;
        paladinModule.Match.SpecId = 1;
        Equal(null, service.Capture(paladinModule), "paladin group dependency capture succeeds");
        Equal(
            ClassMacrosStore.SelectorTargetRoutingMode,
            paladinModule.Dependencies?.Macros.RoutingMode,
            "module dependency captures selector-target routing mode");
        Equal(false, paladinModule.Dependencies!.Config.Spec.Group!.Auras
                .Any(aura => aura.SpellId == 25771),
            "module dependency excludes the unsupported friendly Forbearance identity filter");
        Equal(6, paladinModule.Dependencies.Config.Spec.Group.Num,
            "official paladin dependency uses the six-field group stride");

        var paladinConfig = ClassBlocksStore.Load(paladinPath);
        var paladinGroup = paladinConfig.Specs[1].Group!;
        var spirit = paladinGroup.Auras.Single(aura => aura.SpellId == 27827);
        spirit.Offset = 7;
        paladinGroup.Auras.Add(new ClassBlocksStore.GroupAuraEntry
        {
            Offset = 8,
            Name = spirit.Name,
            SpellId = spirit.SpellId
        });
        paladinGroup.Auras.Add(new ClassBlocksStore.GroupAuraEntry
        {
            Offset = 4,
            Name = "救世道标",
            SpellId = 1244893
        });
        paladinGroup.Num = 29;
        ClassBlocksStore.Save(paladinConfig);

        var repairedGroupImport = service.Import([paladinModule]);
        Equal(true, repairedGroupImport.ConfigUpdated > 0, "group dependency compacts duplicate spell identities");
        var repairedGroup = ClassBlocksStore.Load(paladinPath).Specs[1].Group!;
        Equal(1, repairedGroup.Auras.Count(aura => aura.SpellId == 27827), "group aura spell is unique across offsets");
        Equal(7, repairedGroup.Num, "group stride shrinks to its highest retained occupied offset");
        Equal(false, repairedGroup.Auras.Any(aura => aura.SpellId == 25771),
            "group dependency import cannot restore the unsupported Forbearance slot");
        Equal(false, service.Import([paladinModule]).HasChanges, "repaired group import remains idempotent");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
}

static void CooldownConfirmationTrackerContract()
{
    static GameState State(int cooldown) => new(new Dictionary<string, object?>
    {
        ["spells"] = new Dictionary<string, object?> { ["美德道标"] = cooldown }
    });
    static GameState GcdState(int remaining) => new(new Dictionary<string, object?>
    {
        ["公共冷却剩余"] = remaining
    });

    var decision = new LogicDecision(
        "CTRL-A",
        "施放 美德道标",
        new Dictionary<string, object?>
        {
            ["动作技能"] = "美德道标",
            ["动作单位槽位"] = 1,
            ["规则编号"] = 4
        },
        CooldownConfirmationSpell: "美德道标");
    var tracker = new CooldownConfirmationTracker();
    var now = DateTimeOffset.UnixEpoch;

    tracker.RecordSent(decision, now);
    Equal(false, tracker.CanAttempt(
            decision,
            now.AddMilliseconds(249),
            allowPreemption: false,
            out var pendingSpell),
        "the same pending action cannot flood the input queue before the retry cadence");
    Equal("美德道标", pendingSpell, "pending action gate reports the blocking spell");
    Equal(true, tracker.CanAttempt(
            decision,
            now.Add(CooldownConfirmationTracker.RetryCadence),
            allowPreemption: false,
            out _),
        "the same pending action is retryable at the bounded cadence");
    Equal(false, tracker.CanAttempt(
            decision with
            {
                UnitInfo = new Dictionary<string, object?> { ["动作单位槽位"] = 2 }
            },
            now.Add(CooldownConfirmationTracker.RetryCadence),
            allowPreemption: false,
            out _),
        "the same healing spell cannot switch targets before the pending action confirms");
    var urgentSameSpell = decision with
    {
        UnitInfo = new Dictionary<string, object?>
        {
            ["动作技能"] = "美德道标",
            ["动作单位槽位"] = 1,
            ["规则编号"] = 1
        }
    };
    Equal(false, tracker.CanAttempt(
            urgentSameSpell,
            now.AddMilliseconds(10),
            allowPreemption: false,
            out _),
        "a higher-priority rule for the same spell and target coalesces instead of sending again");
    var queuedSameActionTracker = new CooldownConfirmationTracker();
    queuedSameActionTracker.RecordSent(decision, now, GcdState(0));
    Equal(false, queuedSameActionTracker.CanAttempt(
            urgentSameSpell,
            GcdState(0),
            now.AddMilliseconds(100),
            allowPreemption: false,
            out _),
        "an already queued spell and target is not resent when only its rule priority changes");
    var urgentRetarget = urgentSameSpell with
    {
        UnitInfo = new Dictionary<string, object?>
        {
            ["动作技能"] = "美德道标",
            ["动作单位槽位"] = 2,
            ["规则编号"] = 1
        }
    };
    Equal(false, queuedSameActionTracker.CanAttempt(
            urgentRetarget,
            GcdState(80),
            now.AddMilliseconds(200),
            allowPreemption: false,
            out _),
        "a real-time target change still waits for the GCD queue window");
    Equal(false, queuedSameActionTracker.CanAttempt(
            urgentRetarget,
            GcdState(CooldownConfirmationTracker.QueueWindowCentiseconds),
            now.AddMilliseconds(200),
            allowPreemption: false,
            out _),
        "a pending action cannot be replaced by another target before confirmation");

    var differentAction = decision with
    {
        CooldownConfirmationSpell = "清洁术",
        UnitInfo = new Dictionary<string, object?> { ["动作单位槽位"] = 2 }
    };
    Equal(false, tracker.CanAttempt(
            differentAction,
            now.Add(CooldownConfirmationTracker.RetryCadence),
            allowPreemption: false,
            out _),
        "a different action cannot overwrite a pending high-priority cast");
    var emergencyDecision = decision with
    {
        CooldownConfirmationSpell = "圣疗术",
        UnitInfo = new Dictionary<string, object?>
        {
            ["动作技能"] = "圣疗术",
            ["动作单位槽位"] = 2
        }
    };
    Equal(true, tracker.CanAttempt(
            emergencyDecision,
            now.AddMilliseconds(10),
            allowPreemption: true,
            out _),
        "an emergency action can preempt a lower-priority pending cast");
    var offGcdRetargetTracker = new CooldownConfirmationTracker();
    offGcdRetargetTracker.RecordSent(emergencyDecision, now);
    var emergencyRetarget = emergencyDecision with
    {
        UnitInfo = new Dictionary<string, object?>
        {
            ["动作技能"] = "圣疗术",
            ["动作单位槽位"] = 4
        }
    };
    Equal(false, offGcdRetargetTracker.CanAttempt(
            emergencyRetarget,
            GcdState(0),
            now.AddMilliseconds(100),
            allowPreemption: true,
            out _),
        "the same off-GCD emergency spell cannot retarget while its cast is pending");
    var sacrificeDecision = decision with
    {
        CooldownConfirmationSpell = "牺牲祝福",
        UnitInfo = new Dictionary<string, object?>
        {
            ["动作技能"] = "牺牲祝福",
            ["动作单位槽位"] = 2
        }
    };
    Equal(true, tracker.CanAttempt(
            sacrificeDecision,
            GcdState(80),
            now.AddMilliseconds(10),
            allowPreemption: false,
            out _),
        "Sacrifice Blessing is treated as an off-GCD action");

    var failedCooldownTracker = new CooldownConfirmationTracker();
    var confirmedActionDecision = decision with { PlayerActionCode = 24 };
    failedCooldownTracker.RecordSent(
        confirmedActionDecision,
        now,
        new GameState(new Dictionary<string, object?>
        {
            ["spells"] = new Dictionary<string, object?> { ["美德道标"] = 0 },
            ["玩家动作序号"] = 4
        }));
    var failedWithCooldown = failedCooldownTracker.Observe(
        new GameState(new Dictionary<string, object?>
        {
            ["spells"] = new Dictionary<string, object?> { ["美德道标"] = 120 },
            ["玩家动作序号"] = 5,
            ["玩家动作技能"] = 24,
            ["玩家动作状态"] = 4
        }),
        now.AddMilliseconds(100)).Single();
    Equal(false, failedWithCooldown.Confirmed,
        "a definite failed action cannot be confirmed by a transient positive cooldown");

    var emergencyTracker = new CooldownConfirmationTracker();
    emergencyTracker.RecordSent(emergencyDecision, now);
    Equal(false, emergencyTracker.CanAttempt(
            decision,
            now.Add(CooldownConfirmationTracker.RetryCadence),
            allowPreemption: false,
            out _),
        "a lower-priority action cannot overwrite a pending emergency cast");

    var priorityTracker = new CooldownConfirmationTracker();
    var offensiveDecision = new LogicDecision(
        "ALT-A",
        "施放 审判",
        new Dictionary<string, object?>
        {
            ["动作技能"] = "审判",
            ["动作单位槽位"] = 0,
            ["规则编号"] = 34
        },
        CooldownConfirmationSpell: "审判");
    var healingDecision = new LogicDecision(
        "CTRL-B",
        "施放 荣耀圣令",
        new Dictionary<string, object?>
        {
            ["动作技能"] = "荣耀圣令",
            ["动作单位槽位"] = 2,
            ["规则编号"] = 10
        },
        CooldownConfirmationSpell: "荣耀圣令");
    priorityTracker.RecordSent(offensiveDecision, now);
    Equal(false, priorityTracker.CanAttempt(
            healingDecision,
            now.AddMilliseconds(10),
            allowPreemption: false,
            out _),
        "a GCD healing action waits for the pending offense confirmation");
    var offGcdPreemption = new LogicDecision(
        "CTRL-O",
        "施放 光环掌握",
        new Dictionary<string, object?>
        {
            ["动作技能"] = "光环掌握",
            ["动作单位槽位"] = 0,
            ["规则编号"] = 8
        },
        CooldownConfirmationSpell: "光环掌握",
        PlayerActionCode: 31);
    Equal(true, priorityTracker.CanAttempt(
            offGcdPreemption,
            now.AddMilliseconds(10),
            allowPreemption: true,
            out _),
        "an explicitly off-GCD action can replace a stale pending GCD action");
    priorityTracker.RecordSent(offGcdPreemption, now.AddMilliseconds(10));
    Equal(0, priorityTracker.Observe(
            ActionState(0, serial: 1, actionCode: 34, actionStatus: 2),
            now.AddMilliseconds(100)).Count,
        "a late event from the replaced GCD action cannot confirm the new off-GCD action");
    Equal(false, priorityTracker.CanAttempt(
            offensiveDecision,
            now.Add(CooldownConfirmationTracker.RetryCadence),
            allowPreemption: false,
            out var healingBlocker),
        "the replaced GCD action cannot overwrite the pending off-GCD action");
    Equal("光环掌握", healingBlocker, "the off-GCD preemption replaces the stale confirmation");

    var gcdPreemptionTracker = new CooldownConfirmationTracker();
    gcdPreemptionTracker.RecordSent(offensiveDecision, now, GcdState(0));
    Equal(false, gcdPreemptionTracker.CanAttempt(
            healingDecision,
            GcdState(80),
            now.AddMilliseconds(10),
            allowPreemption: false,
            out _),
        "a higher-priority GCD heal cannot race the pending offense confirmation");
    Equal(false, gcdPreemptionTracker.CanAttempt(
            healingDecision,
            GcdState(CooldownConfirmationTracker.QueueWindowCentiseconds),
            now.AddMilliseconds(20),
            allowPreemption: false,
            out _),
        "a higher-priority GCD heal still waits for the pending action confirmation");

    var cleanseTracker = new CooldownConfirmationTracker();
    var cleanseDecision = differentAction with
    {
        UnitInfo = new Dictionary<string, object?>
        {
            ["动作技能"] = "清洁术",
            ["动作单位槽位"] = 2,
            ["规则编号"] = 9
        }
    };
    var flashDecision = new LogicDecision(
        "CTRL-C",
        "施放 圣光闪现",
        new Dictionary<string, object?>
        {
            ["动作技能"] = "圣光闪现",
            ["动作单位槽位"] = 4,
            ["规则编号"] = 13
        },
        PlayerActionCode: 23);
    var untrackedCooldownlessAction = new CooldownConfirmationTracker();
    Equal(false, untrackedCooldownlessAction.CanAttempt(
            flashDecision,
            GcdState(80),
            now,
            allowPreemption: false,
            out _),
        "a cooldown-less action still waits for the real GCD queue window");
    Equal(true, untrackedCooldownlessAction.CanAttempt(
            flashDecision,
            GcdState(CooldownConfirmationTracker.QueueWindowCentiseconds),
            now,
            allowPreemption: false,
            out _),
        "a cooldown-less action enters the queue window before its first send");
    Equal(false, untrackedCooldownlessAction.CanAttempt(
            flashDecision,
            GcdState(CooldownConfirmationTracker.QueueWindowCentiseconds + 1),
            now,
            allowPreemption: false,
            out _),
        "a cooldown-less action stays blocked until the narrow queue window");
    untrackedCooldownlessAction.RecordSent(flashDecision, now, ActionState(35));
    cleanseTracker.RecordSent(cleanseDecision, now);
    Equal(false, cleanseTracker.CanAttempt(
            flashDecision,
            now.AddMilliseconds(10),
            allowPreemption: false,
            out _),
        "a GCD heal cannot race a pending Cleanse confirmation");
    Equal(false, cleanseTracker.Observe(State(0), now.Add(CooldownConfirmationTracker.RetryAfter)).Single().Confirmed,
        "the pending Cleanse action owns its timeout when no replacement was sent");
    Equal(true, untrackedCooldownlessAction.Observe(
            ActionState(0, serial: 1, actionCode: 23, actionStatus: 1),
            now.AddMilliseconds(100)).Single().Confirmed,
        "a cooldown-less action is confirmed by its exact player action code");
    tracker.RecordSent(decision, now.AddMilliseconds(900));
    Equal(0, tracker.Observe(State(0), now.AddMilliseconds(999)).Count,
        "unchanged ready state remains inside the confirmation window");
    Equal(false, tracker.Observe(State(0), now.AddMilliseconds(1000)).Single().Confirmed,
        "continuous input does not reset the original confirmation timeout");

    tracker.RecordSent(decision, now);
    var confirmed = tracker.Observe(State(8), now.AddMilliseconds(100)).Single();
    Equal(true, confirmed.Confirmed, "positive game cooldown confirms the cast");
    Equal(8, confirmed.Cooldown, "confirmation reports the observed cooldown");
    Equal(false, tracker.CanAttempt(
            decision,
            now.AddMilliseconds(200),
            allowPreemption: false,
            out _),
        "a confirmed action has a short post-confirmation hold against duplicate sends");
    Equal(true, tracker.CanAttempt(
            decision,
            now.AddMilliseconds(100).Add(CooldownConfirmationTracker.PostConfirmationHold),
            allowPreemption: false,
            out _),
        "the post-confirmation hold expires at its bounded deadline");

    tracker.RecordSent(decision, now);
    var timedOut = tracker.Observe(State(0), now.Add(CooldownConfirmationTracker.RetryAfter)).Single();
    Equal(false, timedOut.Confirmed, "missing cooldown confirmation times out");

    var chargeDecision = new LogicDecision(
        "CTRL-B",
        "施放 神圣震击",
        new Dictionary<string, object?>(),
        CooldownConfirmationSpell: "神圣震击",
        CooldownConfirmationStateField: "spells.神圣震击层数",
        CooldownConfirmationInitialValue: 2);
    var chargeState = new GameState(new Dictionary<string, object?>
    {
        ["spells"] = new Dictionary<string, object?>
        {
            ["神圣震击"] = 0,
            ["神圣震击层数"] = 1
        }
    });
    tracker.RecordSent(chargeDecision, now);
    var chargeConfirmed = tracker.Observe(chargeState, now.AddMilliseconds(100)).Single();
    Equal(true, chargeConfirmed.Confirmed,
        "charge decrease confirms the cast even when the base cooldown remains ready");
    Equal("spells.神圣震击层数", chargeConfirmed.StateField,
        "charge confirmation reports the observed state field");
    Equal(2, chargeConfirmed.InitialValue, "charge confirmation reports the initial count");
    Equal(1, chargeConfirmed.ObservedValue, "charge confirmation reports the decreased count");

    var infusionConversionDecision = new LogicDecision(
        "CTRL-F",
        "施放 圣光闪现",
        new Dictionary<string, object?> { ["动作单位槽位"] = 1 },
        CooldownConfirmationSpell: "圣光闪现",
        CooldownConfirmationStateField: "auras.圣光灌注层数",
        CooldownConfirmationInitialValue: 1,
        PlayerActionCode: 23);
    var infusionConversionTracker = new CooldownConfirmationTracker();
    infusionConversionTracker.RecordSent(infusionConversionDecision, now, new GameState(new Dictionary<string, object?>
    {
        ["auras"] = new Dictionary<string, object?> { ["圣光灌注层数"] = 1 },
        ["玩家动作序号"] = 10,
        ["玩家动作技能"] = 0,
        ["玩家动作状态"] = 0
    }));
    Equal(0, infusionConversionTracker.Observe(new GameState(new Dictionary<string, object?>
    {
        ["auras"] = new Dictionary<string, object?> { ["圣光灌注层数"] = 0 },
        ["玩家动作序号"] = 11,
        ["玩家动作技能"] = 23,
        ["玩家动作状态"] = 1
    }), now.AddMilliseconds(100)).Count,
        "Infusion Flash of Light does not confirm before the cast succeeds");
    var completedInfusionConversion = infusionConversionTracker.Observe(new GameState(new Dictionary<string, object?>
    {
        ["auras"] = new Dictionary<string, object?> { ["圣光灌注层数"] = 0 },
        ["玩家动作序号"] = 11,
        ["玩家动作技能"] = 23,
        ["玩家动作状态"] = 2
    }), now.AddMilliseconds(200));
    Equal(1, completedInfusionConversion.Count,
        "successful Infusion Flash of Light emits one confirmation update");
    Equal(true, completedInfusionConversion.Single().Confirmed,
        "successful Infusion Flash of Light confirms after its stack is consumed");

    var resourceDecision = new LogicDecision(
        "CTRL-C",
        "施放 荣耀圣令",
        new Dictionary<string, object?> { ["动作单位槽位"] = 3 },
        CooldownConfirmationSpell: "荣耀圣令",
        CooldownConfirmationStateField: "神圣能量",
        CooldownConfirmationInitialValue: 5);
    tracker.RecordSent(resourceDecision, now);
    var resourceConfirmed = tracker.Observe(new GameState(new Dictionary<string, object?>
    {
        ["神圣能量"] = 2
    }), now.AddMilliseconds(100)).Single();
    Equal(true, resourceConfirmed.Confirmed, "Holy Power decrease confirms a cooldown-less spender");
    Equal(new LogicActionKey("荣耀圣令", 3), resourceConfirmed.Actions.Single(),
        "confirmation retains the spell and target for failure isolation");

    var shieldDecision = new LogicDecision(
        "ALT-7",
        "施放 正义盾击",
        new Dictionary<string, object?> { ["动作单位槽位"] = 0 },
        CooldownConfirmationSpell: "正义盾击",
        CooldownConfirmationStateField: "神圣能量",
        CooldownConfirmationInitialValue: 5,
        PlayerActionCode: 11);
    tracker.RecordSent(shieldDecision, now);
    Equal(0, tracker.Observe(new GameState(new Dictionary<string, object?>
    {
        ["神圣能量"] = 2,
        ["玩家动作序号"] = 1,
        ["玩家动作技能"] = 34,
        ["玩家动作状态"] = 2
    }), now.AddMilliseconds(100)).Count,
        "shared Holy Power change from a different action must not confirm Shield");
    Equal(true, tracker.Observe(new GameState(new Dictionary<string, object?>
    {
        ["神圣能量"] = 2,
        ["玩家动作序号"] = 2,
        ["玩家动作技能"] = 11,
        ["玩家动作状态"] = 2
    }), now.AddMilliseconds(200)).Single().Confirmed,
        "matching Shield action confirms after the shared resource changes");

    var delayedActionTracker = new CooldownConfirmationTracker();
    delayedActionTracker.RecordSent(shieldDecision, now, new GameState(new Dictionary<string, object?>
    {
        ["神圣能量"] = 3,
        ["玩家动作序号"] = 68,
        ["玩家动作技能"] = 10,
        ["玩家动作状态"] = 2,
        ["公共冷却剩余"] = 0
    }));
    var delayedActionConfirmation = delayedActionTracker.Observe(new GameState(new Dictionary<string, object?>
    {
        ["神圣能量"] = 0,
        ["玩家动作序号"] = 68,
        ["玩家动作技能"] = 10,
        ["玩家动作状态"] = 2,
        ["公共冷却剩余"] = 16
    }), now.AddMilliseconds(1800)).Single();
    Equal(true, delayedActionConfirmation.Confirmed,
        "a delayed action event cannot turn a successful Shield resource change into a retry");
    Equal(true, delayedActionConfirmation.UsedDelayedActionAcknowledgement,
        "delayed Shield confirmation reports the stale action acknowledgement source");

    var targetFour = chargeDecision with
    {
        UnitInfo = new Dictionary<string, object?> { ["动作单位槽位"] = 4 }
    };
    var targetFive = chargeDecision with
    {
        UnitInfo = new Dictionary<string, object?> { ["动作单位槽位"] = 5 }
    };
    tracker.RecordSent(targetFour, now);
    tracker.RecordSent(targetFive, now.AddMilliseconds(100));
    var ambiguousTarget = tracker.Observe(new GameState(new Dictionary<string, object?>
    {
        ["spells"] = new Dictionary<string, object?>
        {
            ["神圣震击"] = 0,
            ["神圣震击层数"] = 2
        }
    }), now.AddMilliseconds(1100)).Single();
    Equal(1, ambiguousTarget.Actions.Count,
        "a confirmation window retains one target generation");
    Equal(new LogicActionKey("神圣震击", 4), ambiguousTarget.Actions.Single(),
        "same-spell target retargeting cannot overwrite the pending confirmation generation");

    var procDecision = resourceDecision with
    {
        CooldownConfirmationStateField = "auras.神圣意志",
        CooldownConfirmationInitialValue = 4,
        ConfirmationStateChange = ConfirmationStateChangeKind.Cleared
    };
    tracker.RecordSent(procDecision, now);
    Equal(0, tracker.Observe(new GameState(new Dictionary<string, object?>
    {
        ["auras"] = new Dictionary<string, object?> { ["神圣意志"] = 3 }
    }), now.AddMilliseconds(100)).Count,
        "a naturally decreasing Divine Purpose duration does not falsely confirm the cast");
    Equal(true, tracker.Observe(new GameState(new Dictionary<string, object?>
    {
        ["auras"] = new Dictionary<string, object?> { ["神圣意志"] = 0 }
    }), now.AddMilliseconds(200)).Single().Confirmed,
        "Divine Purpose clearing confirms the free spender");

    static GameState ActionState(
        int gcdRemaining,
        int serial = 0,
        int actionCode = 0,
        int actionStatus = 0) => new(new Dictionary<string, object?>
    {
        ["公共冷却剩余"] = gcdRemaining,
        ["玩家动作序号"] = serial,
        ["玩家动作技能"] = actionCode,
        ["玩家动作状态"] = actionStatus,
        ["spells"] = new Dictionary<string, object?> { ["圣光术"] = 0 }
    });

    var castDecision = new LogicDecision(
        "CTRL-D",
        "施放 圣光术",
        new Dictionary<string, object?>
        {
            ["动作技能"] = "圣光术",
            ["动作单位槽位"] = 2,
            ["规则编号"] = 35
        },
        CooldownConfirmationSpell: "圣光术",
        PlayerActionCode: 22);
    var actionTracker = new CooldownConfirmationTracker();
    Equal(false, actionTracker.CanAttempt(
            castDecision,
            ActionState(80),
            now,
            allowPreemption: false,
            out _),
        "a first GCD action waits until the real spell queue window");
    Equal(true, actionTracker.CanAttempt(
            castDecision,
            ActionState(CooldownConfirmationTracker.QueueWindowCentiseconds),
            now,
            allowPreemption: false,
            out _),
        "a first GCD action enters the narrow real spell queue window");
    Equal(true, actionTracker.CanAttempt(
            emergencyDecision,
            ActionState(80),
            now,
            allowPreemption: true,
            out _),
        "an emergency action can bypass the first-attempt GCD gate");
    var offGcdDecision = castDecision with
    {
        CooldownConfirmationSpell = "光环掌握",
        UnitInfo = new Dictionary<string, object?>
        {
            ["动作技能"] = "光环掌握",
            ["动作单位槽位"] = 0,
            ["规则编号"] = 8
        }
    };
    Equal(true, actionTracker.CanAttempt(
            offGcdDecision,
            ActionState(80),
            now,
            allowPreemption: false,
            out _),
        "an explicitly off-GCD action bypasses the first-attempt GCD gate");
    actionTracker.RecordSent(castDecision, now, ActionState(120));
    Equal(false, actionTracker.CanAttempt(
            castDecision,
            ActionState(80),
            now.AddMilliseconds(250),
            allowPreemption: false,
            out _),
        "a pending GCD action is not repeatedly delivered before the spell queue window");
    Equal(true, actionTracker.CanAttempt(
            castDecision,
            ActionState(CooldownConfirmationTracker.QueueWindowCentiseconds),
            now.AddMilliseconds(300),
            allowPreemption: false,
            out _),
        "an ignored first delivery gets one retry in the real GCD queue window");
    actionTracker.RecordSent(castDecision, now.AddMilliseconds(300), ActionState(CooldownConfirmationTracker.QueueWindowCentiseconds));
    Equal(false, actionTracker.CanAttempt(
            castDecision,
            ActionState(0),
            now.AddMilliseconds(500),
            allowPreemption: false,
            out _),
        "the queue-window retry cannot turn into fixed-cadence input flooding");
    var started = actionTracker.Observe(
        ActionState(0, serial: 1, actionCode: 22, actionStatus: 2),
        now.AddMilliseconds(550)).Single();
    Equal(true, started.Confirmed, "UNIT_SPELLCAST_SUCCEEDED confirms a completed Holy Light cast");

    var protectedActionTracker = new CooldownConfirmationTracker();
    protectedActionTracker.RecordSent(castDecision, now, ActionState(0));
    var protectedAction = protectedActionTracker.Observe(
        ActionState(0, serial: 1, actionCode: 0, actionStatus: 2),
        now.AddMilliseconds(100)).Single();
    Equal(true, protectedAction.Confirmed,
        "an unattributed protected player cast confirms the only pending action");
    Equal(true, protectedAction.UsedGenericPlayerAction,
        "protected player cast confirmation remains visible in runtime diagnostics");

    var failedTracker = new CooldownConfirmationTracker();
    failedTracker.RecordSent(castDecision, now, ActionState(100));
    Equal(0, failedTracker.Observe(
            ActionState(90, serial: 1, actionCode: 22, actionStatus: 4),
            now.AddMilliseconds(100)).Count,
        "a GCD-time failure waits for the queue window instead of entering a retry loop");
    Equal(true, failedTracker.CanAttempt(
            castDecision,
            ActionState(CooldownConfirmationTracker.QueueWindowCentiseconds, serial: 1, actionCode: 22, actionStatus: 4),
            now.AddMilliseconds(700),
            allowPreemption: false,
            out _),
        "a failed early delivery remains eligible for its single queue-window retry");

    var unattributedFailureTracker = new CooldownConfirmationTracker();
    unattributedFailureTracker.RecordSent(
        new LogicDecision(
            "ALT-A",
            "施放 牺牲祝福",
            new Dictionary<string, object?>
            {
                ["动作技能"] = "牺牲祝福",
                ["动作单位槽位"] = 1
            },
            CooldownConfirmationSpell: "牺牲祝福",
            PlayerActionCode: 24),
        now,
        ActionState(0, serial: 10));
    var unattributedFailure = unattributedFailureTracker.Observe(
        ActionState(0, serial: 11, actionCode: 0, actionStatus: 4),
        now.AddMilliseconds(100)).Single();
    Equal(false, unattributedFailure.Confirmed,
        "an unattributed failed action releases the pending confirmation immediately");
    Equal(true, unattributedFailure.DefinitiveFailure,
        "an unattributed failed action is marked as definitive for backoff");
}

static void AoeAbsorbReserveGuardContract()
{
    static GameState State(int stage) => new(new Dictionary<string, object?>
    {
        ["AOE事件类型"] = 2,
        ["AOE事件阶段"] = stage
    });

    static LogicDecision Decision(
        string spell,
        int unitType = 1,
        int health = 80,
        int dispel = 0) => new(
        "ALT-A",
        $"施放 {spell}",
        new Dictionary<string, object?>
        {
            ["动作技能"] = spell,
            ["动作单位槽位"] = 1,
            ["目标类型"] = unitType,
            ["目标生命值"] = health,
            ["目标驱散"] = dispel
        });

    Equal(true, AoeAbsorbStageGuard.ShouldBlock(State(5), Decision("圣光术")),
        "ordinary GCD is blocked during the absorb reserve stage");
    Equal(false, AoeAbsorbStageGuard.ShouldBlock(State(5), Decision("圣疗术", health: 20)),
        "emergency Lay on Hands remains available during the reserve stage");
    Equal(false, AoeAbsorbStageGuard.ShouldBlock(State(5), Decision("清洁术", dispel: 1)),
        "a dispel remains available during the reserve stage");
    Equal(false, AoeAbsorbStageGuard.ShouldBlock(State(5), Decision("荣耀圣令", unitType: 152, health: 90)),
        "an injured friendly NPC remains available during the reserve stage");
    Equal(false, AoeAbsorbStageGuard.ShouldBlock(State(3), Decision("圣光术")),
        "ordinary GCD is not blocked once the Virtue window is open");

    Equal(true, AoeAbsorbStageGuard.EnteredReserveStage(State(1), State(5)),
        "stage transition is detected when entering the absorb reserve stage");
    Equal(false, AoeAbsorbStageGuard.EnteredReserveStage(State(5), State(5)),
        "steady reserve stage does not repeatedly reset state");
    Equal(false, AoeAbsorbStageGuard.EnteredReserveStage(State(1), new GameState(new Dictionary<string, object?>
    {
        ["AOE事件类型"] = 1,
        ["AOE事件阶段"] = 5
    })),
        "ordinary AOE stage five does not clear absorb confirmations");
}

static void ActionFailureBackoffContract()
{
    var now = DateTimeOffset.UnixEpoch;
    var action = new LogicActionKey("圣疗术", 3);
    var failed = new CooldownConfirmationUpdate(
        "圣疗术", false, 0, null, null, null, now, new HashSet<LogicActionKey> { action });
    var backoff = new ActionFailureBackoff();

    Equal(false, backoff.Observe(failed with { DefinitiveFailure = false }, now),
        "an ambiguous confirmation timeout never activates failure backoff");

    Equal(false, backoff.Observe(failed, now), "first unconfirmed cast remains retryable");
    Equal(true, backoff.GetSuppressed(now.AddMilliseconds(100)).Contains(action),
        "the first definitive failure applies a short retry backoff");
    Equal(true, backoff.Observe(failed, now.AddSeconds(1)), "second unconfirmed cast activates backoff");
    var suppressed = backoff.GetSuppressed(now.AddSeconds(1));
    Equal(true, suppressed.Contains(action), "backoff suppresses the exact failed spell and target");
    Equal(false, suppressed.Contains(new LogicActionKey("圣疗术", 2)),
        "backoff does not suppress the same emergency spell on another target");
    Equal(TimeSpan.FromSeconds(5), ActionFailureBackoff.BackoffDuration,
        "repeated unconfirmed actions yield long enough for fallback healing to run");
    Equal(false, backoff.GetSuppressed(now.AddSeconds(1).Add(ActionFailureBackoff.BackoffDuration)).Contains(action),
        "failed action becomes retryable after the bounded backoff");

    var firstTarget = new LogicActionKey("清洁术", 4);
    var secondTarget = new LogicActionKey("清洁术", 5);
    var ambiguousFailure = new CooldownConfirmationUpdate(
        "清洁术", false, 0, null, null, null, now,
        new HashSet<LogicActionKey> { firstTarget, secondTarget });
    Equal(false, backoff.Observe(ambiguousFailure, now),
        "an ambiguous multi-target timeout never activates target backoff");
    Equal(false, backoff.Observe(ambiguousFailure, now.AddSeconds(1)),
        "repeated ambiguous timeouts remain unattributed");
    Equal(false, backoff.GetSuppressed(now.AddSeconds(1)).Contains(firstTarget),
        "ambiguous timeout does not suppress the first attempted target");
    Equal(false, backoff.GetSuppressed(now.AddSeconds(1)).Contains(secondTarget),
        "ambiguous timeout does not suppress the later attempted target");

    var confirmedTargets = ambiguousFailure with { Confirmed = true };
    backoff.Observe(new CooldownConfirmationUpdate(
        "清洁术", false, 0, null, null, null, now,
        new HashSet<LogicActionKey> { firstTarget }), now);
    backoff.Observe(new CooldownConfirmationUpdate(
        "清洁术", false, 0, null, null, null, now,
        new HashSet<LogicActionKey> { firstTarget }), now.AddSeconds(1));
    Equal(true, backoff.GetSuppressed(now.AddSeconds(1)).Contains(firstTarget),
        "a certain single-target failure remains suppressible");
    backoff.Observe(confirmedTargets, now.AddSeconds(2));
    Equal(false, backoff.GetSuppressed(now.AddSeconds(2)).Contains(firstTarget),
        "multi-target success clears prior failure state for every attempted target");
}

static void EmergencyActionGuardContract()
{
    static GameState State(int playerHealth, params (int Unit, int Health)[] members)
    {
        var group = members.ToDictionary(
            member => member.Unit.ToString(),
            member => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
            {
                ["生命值"] = member.Health,
                ["治疗吸收"] = 0
            });
        return new GameState(new Dictionary<string, object?>
        {
            ["生命值"] = playerHealth,
            ["group"] = group
        });
    }

    static LogicDecision Decision(string spell, int unit, string rateLimitKey = "rule-8") => new(
        "ALT-CTRL-\\",
        $"施放 {spell}",
        new Dictionary<string, object?>
        {
            ["动作技能"] = spell,
            ["动作单位槽位"] = unit
        },
        RateLimitKey: rateLimitKey);

    var guard = new EmergencyActionGuard();
    Equal(true, guard.Observe(Decision("神圣震击", 1), State(100, (1, 100))).Allowed,
        "ordinary healing does not require emergency confirmation");

    var inconsistentSelf = guard.Observe(Decision("圣疗术", 1), State(100, (1, 20)));
    Equal(false, inconsistentSelf.Allowed, "full-health player blocks a false unit-one emergency");
    Equal(true, inconsistentSelf.Reason?.Contains("独立自身生命值为 100%", StringComparison.Ordinal) == true,
        "self-health disagreement is diagnosable");

    var firstCritical = guard.Observe(Decision("圣疗术", 1), State(20, (1, 20)));
    var secondCritical = guard.Observe(Decision("圣疗术", 1), State(19, (1, 19)));
    Equal(false, firstCritical.Allowed, "first critical frame is held");
    Equal(1, firstCritical.ConsecutiveFrames, "first critical frame is counted");
    Equal(true, secondCritical.Allowed, "same critical target is allowed on the second frame");
    Equal(true, guard.Observe(Decision("圣疗术", 1), State(18, (1, 18))).Allowed,
        "confirmed critical target remains eligible for continuous input");

    guard.Reset();
    Equal(false, guard.Observe(Decision("圣疗术", 2), State(100, (2, 20))).Allowed,
        "party critical target starts confirmation");
    Equal(false, guard.Observe(Decision("圣疗术", 3), State(100, (3, 20))).Allowed,
        "switching targets restarts confirmation");
    Equal(true, guard.Observe(Decision("圣疗术", 3), State(100, (3, 18))).Allowed,
        "new party target is allowed after its own second frame");

    guard.Reset();
    Equal(false, guard.Observe(Decision("圣疗术", 2), State(100, (2, 100))).Allowed,
        "non-critical target is rejected even if a module condition is wrong");
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
    Equal(true, latch.Consume(keyPulse), "rapid key press pulses are queued independently");
    Equal(false, latch.Consume(keyPulse), "queued key press pulses are consumed exactly once");

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

    sources[0].RequiresRestart = true;
    Equal(false, input.ConsumePulse(key), "stopped event tap is replaced before pulse consumption");
    Equal(2, sources.Count, "stopped event tap creates one replacement");
    Equal(1, sources[0].DisposeCount, "stopped event tap is disposed");
    sources[1].PressPulses.Add(key);
    Equal(true, input.ConsumePulse(key), "replacement event tap supplies keyboard pulses");

    input.Dispose();
    input.Dispose();
    Equal(1, sources[0].DisposeCount, "event tap is disposed once");
    Equal(1, sources[1].DisposeCount, "replacement event tap is disposed once");
    Equal(false, input.ConsumePulse(wheel), "disposed input does not expose pulses");
    Equal(null, input.Resolve("A"), "disposed input does not resolve triggers");

    using var rebuilt = new MacTriggerInput(stateApi, CreateSource);
    Equal(wheel, rebuilt.Resolve("WHEELDOWN"), "new input rebuilds wheel trigger");
    Equal(3, sources.Count, "new input owns a new event tap");
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

    const int firstHealRowY = markerY + 2;
    logicalPixels[firstHealRowY * width] = Argb(0, 1, 0);
    logicalPixels[firstHealRowY * width + 1] = Argb(0, 1, 0);
    for (var x = 2; x < 22; x++)
    {
        logicalPixels[firstHealRowY * width + x] = Argb(255, 255, 255);
    }
    logicalPixels[firstHealRowY * width + 22] = Argb(0, 11, 1);

    logicalPixels[(firstHealRowY + 10) * width] = Argb(5, 30, 0);
    logicalPixels[(firstHealRowY + 10) * width + 1] = Argb(5, 30, 0);
    for (var x = 2; x < 42; x++)
    {
        logicalPixels[(firstHealRowY + 10) * width + x] = Argb(255, 255, 255);
    }
    logicalPixels[(firstHealRowY + 10) * width + 42] = Argb(5, 21, 30);

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

static void RuntimeCooldownConfirmationContract()
{
    RuntimeCooldownConfirmationContractAsync().GetAwaiter().GetResult();
}

static async Task RuntimeCooldownConfirmationContractAsync()
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var output = new CooldownAwareTargetKeyOutput();
    var snapshots = new System.Collections.Concurrent.ConcurrentQueue<RenderSnapshot>();
    var runtime = new ShigureRuntime(
        new AppOptions(
            "A",
            SendMode.Switch,
            null,
            TimeSpan.FromMilliseconds(25),
            TimeSpan.FromSeconds(5)),
        new ValidRuntimeScanner(),
        new CooldownAwareRuntimeStateBuilder(output),
        output,
        new PressedTriggerInput(),
        new CooldownAwareRuntimeLogic(),
        TimeProvider.System);
    runtime.SnapshotUpdated += snapshot =>
    {
        snapshots.Enqueue(snapshot);
        if (snapshot.CurrentStep.Contains("技能确认：美德道标 已释放", StringComparison.Ordinal))
        {
            cancellation.Cancel();
        }
    };

    try
    {
        await runtime.RunAsync(cancellation.Token);
    }
    catch (OperationCanceledException)
    {
    }

    Equal(1, output.SendCount,
        "player action acknowledgement confirms the first accepted delivery without fixed-cadence retries");
    Equal(true, snapshots.Any(snapshot =>
            snapshot.UnitInfo.TryGetValue("发送结果", out var result)
            && string.Equals(result?.ToString(), "已投递到 WoW 进程", StringComparison.Ordinal)
            && snapshot.UnitInfo.TryGetValue("发送结果说明", out var explanation)
            && explanation?.ToString()?.Contains("不等于技能已施放", StringComparison.Ordinal) == true
            && snapshot.UnitInfo.ContainsKey("技能确认")),
        "successful delivery is published immediately with pending cooldown confirmation");
    Equal(true, snapshots.Any(snapshot =>
            snapshot.CurrentStep.Contains("技能确认：美德道标 已释放", StringComparison.Ordinal)
            && snapshot.UnitInfo.TryGetValue("技能确认", out var confirmation)
            && string.Equals(confirmation?.ToString(), "释放成功", StringComparison.Ordinal)
            && snapshot.UnitInfo.ContainsKey("确认耗时")),
        "player action acknowledgement produces an explicit successful-cast snapshot");
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
    Equal(
        TimeSpan.FromMilliseconds(50),
        RuntimeScanCadence.Resolve(
            configured,
            enabled: true,
            scanUnavailable: false,
            hasPendingConfirmation: true),
        "pending skill confirmations use a fast temporary scan cadence");
    Equal(
        TimeSpan.FromMilliseconds(500),
        RuntimeScanCadence.Resolve(
            configured,
            enabled: true,
            scanUnavailable: true,
            hasPendingConfirmation: true),
        "unavailable scans keep the failure backoff even while confirmation is pending");
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
    reacquired?.Dispose();

    for (var attempt = 0; attempt < 16; attempt++)
    {
        using var rapidHandoff = SingleInstanceLease.TryAcquire(name);
        Equal(true, rapidHandoff is not null, "single-instance lease survives rapid version handoff");
    }

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
    Equal(true, MacFrontmostApplication.IsTarget(target, identity.ProcessId),
        "shared Mac foreground policy accepts the configured target process");
    Equal(false, MacFrontmostApplication.IsTarget(target, identity.ProcessId + 1),
        "shared Mac foreground policy rejects a background target process");
    Equal(false, MacFrontmostApplication.IsTarget(null, identity.ProcessId),
        "shared Mac foreground policy rejects a missing target");
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
    Equal(0, eventApi.Waits.Count, "single hotkey does not add a routing delay");

    var sequencePermissions = new FakePlatformPermissionService(accessibilityReady: true);
    var sequenceApi = new FakeMacKeyEventApi();
    var sequenceResult = new MacKeySender(locator, sequencePermissions, sequenceApi, frontmost)
        .SendSequence(["CTRL-A", "ALT-B"], identity);
    Equal(true, sequenceResult.Succeeded, "mac routed key sequence succeeds");
    Equal(4, sequenceApi.Posts.Count, "two routed hotkeys post four ordered key events");
    Equal("50", string.Join(',', sequenceApi.Waits.Select(delay => delay.TotalMilliseconds)),
        "routed sequence waits once between selector and target hotkeys");
    Equal("post:101,post:102,wait:50,post:103,post:104", string.Join(',', sequenceApi.Operations),
        "routing delay occurs after selector release and before target press");
    Equal(1, sequencePermissions.CheckCount, "routed sequence validates accessibility once");
    Equal(5, sequenceApi.Released.Count, "four routed events and one source are released");

    var invalidSequenceApi = new FakeMacKeyEventApi();
    var invalidSequence = new MacKeySender(locator, permissions, invalidSequenceApi, frontmost)
        .SendSequence(["CTRL-A", "CTRL-NOT_A_KEY"], identity);
    Equal(KeySendFailureKind.UnknownKey, invalidSequence.FailureKind, "invalid routed step rejects the complete sequence");
    Equal(0, invalidSequenceApi.Posts.Count, "invalid routed sequence never partially sends");
    Equal(0, invalidSequenceApi.Waits.Count, "invalid routed sequence never starts its timing sequence");

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
    Equal(true, pixelBlocks.Contains("local function AlignToPhysicalPixel(width)", StringComparison.Ordinal),
        "heal absorb cells align to physical pixels");
    Equal(true, pixelBlocks.Contains("math.max(2, math.floor(width * effectiveScale + 0.5))",
            StringComparison.Ordinal),
        "heal absorb cells retain a visible background at zero");
    Equal(true, pixelBlocks.Contains(
            "bar:SetSize(HEAL_ABSORB_BAR_UNITS * HEAL_ABSORB_UNIT_WIDTH, BAR_CONFIG.height)",
            StringComparison.Ordinal),
        "heal absorb status bar does not add zero-value width");
    Equal(false, pixelBlocks.Contains(
            "bar:SetSize(HEAL_ABSORB_BAR_UNITS * HEAL_ABSORB_UNIT_WIDTH + 1",
            StringComparison.Ordinal),
        "heal absorb status bar has no extra pixel");
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
    var block = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "block.lua"));
    var player = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "player.lua"));
    var target = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "target.lua"));
    var macro = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "macro.lua"));
    var classMacros = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "classmacros.lua"));
    var events = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "events.lua"));
    var aoeWarning = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "aoewarning.lua"));
    var diGuaBridge = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "core", "diguabridge.lua"));
    var compatibilityBridge = File.ReadAllText(Path.Combine(repositoryRoot, "FuyutsuiDiGuaBridge", "Bridge.lua"));
    var paladin = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "class", "Paladin.lua"));
    var paladinConfig = File.ReadAllText(Path.Combine(repositoryRoot, "config", "Paladin.json"));
    var unitSelector = File.ReadAllText(Path.Combine(repositoryRoot, "Modules", "UnitSelector.cs"));
    var toc = File.ReadAllText(Path.Combine(repositoryRoot, "Fuyutsui", "Fuyutsui.toc"));

    Equal(true, curves.Contains("CreateColorCurve(25.5, 255)", StringComparison.Ordinal),
        "cast protocol encodes one second as ten units");
    Equal(true, ClassStateCatalog.IsKnown(ClassStateCatalog.CategoryState, "施法(正计时)"),
        "state catalog exposes elapsed cast time");
    Equal(true, ClassStateCatalog.IsKnown(ClassStateCatalog.CategoryState, "施法(倒计时)"),
        "state catalog exposes remaining cast time");
    Equal(false, ClassStateCatalog.IsKnown(ClassStateCatalog.CategoryState, "施法"),
        "legacy cast state is no longer selectable");
    Equal("施法(倒计时)", ClassStateCatalog.NormalizeLegacyStateName("施法"),
        "legacy cast state has one shared compatibility mapping");
    Equal(true, ClassStateCatalog.TopCategories.Contains(ClassStateCatalog.CategoryMouseover),
        "state catalog exposes mouseover category");
    Equal(true, ClassStateCatalog.TopCategories.Contains(ClassStateCatalog.CategoryBoss5),
        "state catalog exposes every boss category");
    Equal(true, main.Contains("\"鼠标\"", StringComparison.Ordinal)
        && main.Contains("\"首领5\"", StringComparison.Ordinal),
        "addon block loader includes mouseover and boss categories");
    Equal(true, main.Contains("self:UpdateStateBlock(\"状态\", \"DiGua桥接就绪\")", StringComparison.Ordinal),
        "player block initialization republishes bridge readiness after state pixels exist");
    Equal(true, main.Contains("AppendAuraList(t.auras.player, \"player\", \"HELPFUL\")", StringComparison.Ordinal)
        && main.Contains("AppendAuraList(t.auras.target.harmful, \"target\", \"HARMFUL|PLAYER\")", StringComparison.Ordinal),
        "player auras accept any source while target auras retain player ownership");
    Equal(true, stateBlocks.Contains("[\"施法(正计时)\"]", StringComparison.Ordinal)
        && stateBlocks.Contains("[\"施法(倒计时)\"]", StringComparison.Ordinal),
        "addon runtime registers both cast directions");
    Equal(true, player.Contains("reason = 6", StringComparison.Ordinal)
        && player.Contains("state.valid = reason / 255", StringComparison.Ordinal),
        "validity keeps pause reasons in its existing protocol byte");
    Equal(true, target.Contains("UnitIsPlayer(unit)", StringComparison.Ordinal)
        && target.Contains("index = 52", StringComparison.Ordinal)
        && target.Contains("UnitPosition", StringComparison.Ordinal)
        && target.Contains("GetPlayerFacing", StringComparison.Ordinal)
        && target.Contains("return 2", StringComparison.Ordinal)
        && target.Contains("math.atan2(px - tx, ty - py)", StringComparison.Ordinal)
        && target.Contains("cache.inFront", StringComparison.Ordinal),
        "target type reuses its existing byte and tracks frontal hostility with an unknown fallback");
    Equal(true, stateBlocks.Contains("target.inFront or 0", StringComparison.Ordinal),
        "target frontal state preserves the unknown value for WoW-side validation");
    Equal(true, macro.Contains("SecureHandlerClickTemplate", StringComparison.Ordinal)
        && macro.Contains("SetAttribute('macrotext'", StringComparison.Ordinal),
        "selector-target routing changes direct target macros through a secure handler");
    Equal(true, macro.Contains("RegisterForClicks(\"AnyUp\", \"AnyDown\")", StringComparison.Ordinal),
        "secure macro buttons accept both keyboard edges used by override bindings");
    Equal(false, macro.Contains("target:SetAttribute(\"type\", \"click\")", StringComparison.Ordinal),
        "selector-target routing avoids blocked scripted click delegation");
    Equal(true, macro.Contains("UnitGroupRolesAssigned(unit) == \"TANK\"", StringComparison.Ordinal)
        && macro.Contains("[@%starget,harm,nodead][@targettarget,harm,nodead][harm,nodead]", StringComparison.Ordinal)
        && macro.Contains("[@targettarget,harm,nodead][harm,nodead]", StringComparison.Ordinal),
        "tank-target macros fall back through the current friendly target and current hostile target");
    Equal(true, classMacros.Contains("[@target,harm,nodead]正义盾击", StringComparison.Ordinal)
        && classMacros.Contains("[\"治疗石\"]", StringComparison.Ordinal)
        && classMacros.Contains("[\"治疗药水\"] = \"item:271884", StringComparison.Ordinal)
        && classMacros.Contains("牺牲祝福", StringComparison.Ordinal),
        "Paladin macros expose direct hostile Shield, consumables and Sacrifice Blessing");
    Equal(true, events.Contains("function Fuyutsui:PLAYER_ROLES_ASSIGNED()", StringComparison.Ordinal)
        && events.Contains("self:LoadPlayerMacros()", StringComparison.Ordinal),
        "tank-target macros refresh after group role assignments change");
    Equal(true, !toc.Contains("core/aoewarningdata.lua", StringComparison.Ordinal)
        && toc.Contains("core/aoewarning.lua", StringComparison.Ordinal)
        && toc.Contains("core/diguabridge.lua", StringComparison.Ordinal),
        "addon manifest uses the timeline Spell ID as the single AOE identity source");
    Equal(true, diGuaBridge.Contains("function Fuyutsui:InitializeDiGuaBridge()", StringComparison.Ordinal)
        && diGuaBridge.Contains("self.state.diGuaBridgeReady = true", StringComparison.Ordinal)
        && diGuaBridge.Contains("HasTimelineCapabilities", StringComparison.Ordinal)
        && diGuaBridge.Contains("type(timeline.AddScriptEvent) == \"function\"", StringComparison.Ordinal)
        && diGuaBridge.Contains("type(hooksecurefunc) == \"function\"", StringComparison.Ordinal)
        && diGuaBridge.Contains("loadFrame:RegisterEvent(\"PLAYER_LOGIN\")", StringComparison.Ordinal)
        && diGuaBridge.Contains("loadFrame:RegisterEvent(\"PLAYER_ENTERING_WORLD\")", StringComparison.Ordinal)
        && !diGuaBridge.Contains("~= SUPPORTED_DIGUA_VERSION", StringComparison.Ordinal)
        && compatibilityBridge.Contains("Fuyutsui:InitializeDiGuaBridge()", StringComparison.Ordinal)
        && !compatibilityBridge.Contains("CAST_SPELL_BY_ICON", StringComparison.Ordinal),
        "the loaded Fuyutsui addon owns capability-based bridge readiness and the compatibility addon has no duplicate registry");
    Equal(true, compatibilityBridge.Contains("frame:RegisterEvent(\"NAME_PLATE_UNIT_ADDED\")", StringComparison.Ordinal)
        && compatibilityBridge.Contains("frame:RegisterEvent(\"NAME_PLATE_UNIT_REMOVED\")", StringComparison.Ordinal)
        && compatibilityBridge.Contains("Fuyutsui:ObserveAOEDiGuaBar(132334, 11.7, \"准备吸奶盾\", unit)", StringComparison.Ordinal)
        && compatibilityBridge.Contains("Fuyutsui:CancelAOEDiGuaBar(unit)", StringComparison.Ordinal)
        && aoeWarning.Contains("absorbVirtueDelaySeconds = 2", StringComparison.Ordinal)
        && aoeWarning.Contains("local impactAt = event.impactAt or now", StringComparison.Ordinal)
        && aoeWarning.Contains("event.virtueReadyAt = impactAt + config.absorbVirtueDelaySeconds", StringComparison.Ordinal)
        && aoeWarning.Contains("local function TraceLog(message, ...)\n    DebugLog(message, ...)\nend", StringComparison.Ordinal)
        && !compatibilityBridge.Contains("|cff00ff00[Fuyutsui AOE]|r", StringComparison.Ordinal)
        && !compatibilityBridge.Contains("hooksecurefunc(addonTable, \"CustomEncounterBar\"", StringComparison.Ordinal),
        "heal-absorb Virtue mirrors DiGua's live nameplate countdown, keeps chat quiet by default and waits two seconds");
    Equal(true, paladin.Contains("\"AOE事件类型\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE事件阶段\"", StringComparison.Ordinal)
        && paladin.Contains("\"公共冷却时长\"", StringComparison.Ordinal)
        && paladin.Contains("\"公共冷却剩余\"", StringComparison.Ordinal)
        && paladin.Contains("\"DiGua桥接就绪\"", StringComparison.Ordinal)
        && paladin.Contains("\"宏绑定状态\"", StringComparison.Ordinal)
        && paladin.Contains("\"宏绑定数量\"", StringComparison.Ordinal)
        && paladin.Contains("\"玩家动作序号\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE桥接请求数\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE原始读条数\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE技能受保护数\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE敌对状态受保护数\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE受保护匹配数\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE读条未匹配数\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE预警技能高位\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE读条技能高位\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE受保护读条\"", StringComparison.Ordinal)
        && paladin.Contains("\"AOE读条剩余\"", StringComparison.Ordinal)
        && paladin.Contains("\"圣洁鸣钟预计可用\"", StringComparison.Ordinal)
        && paladin.Contains("\"正面\"", StringComparison.Ordinal)
        && target.Contains("if canAttack then\n        return 1 / 255", StringComparison.Ordinal)
        && paladin.Contains("\"治疗石\"", StringComparison.Ordinal)
        && paladin.Contains("name = \"圣盾术\", spellId = 642", StringComparison.Ordinal)
        && paladin.Contains("name = \"美德道标\", spellId = 200025", StringComparison.Ordinal),
        "holy paladin protocol exposes warning, bridge, action acknowledgement and measured GCD state");
    Equal(true, stateBlocks.Contains("[\"宏绑定状态\"] = function() return (state.macroBindingStatus or 0) / 255 end", StringComparison.Ordinal)
        && stateBlocks.Contains("[\"宏绑定数量\"]", StringComparison.Ordinal)
        && stateBlocks.Contains("self.state[countKey] <= 0", StringComparison.Ordinal),
        "addon protocol exposes macro binding readiness diagnostics");
    Equal(true, aoeWarning.Contains("function Fuyutsui:PublishAOEDiagnostic", StringComparison.Ordinal)
        && aoeWarning.Contains("Fuyutsui:PublishAOEDiagnostic(\"castUnmatched\"", StringComparison.Ordinal)
        && aoeWarning.Contains("真实读条直连", StringComparison.Ordinal)
        && aoeWarning.Contains("受保护读条直连", StringComparison.Ordinal)
        && diGuaBridge.Contains("Fuyutsui:PublishAOEDiagnostic(\"bridgeSuccess\"", StringComparison.Ordinal)
        && diGuaBridge.Contains("castEventTypeBySpell", StringComparison.Ordinal)
        && stateBlocks.Contains("[\"AOE预警技能低位\"]", StringComparison.Ordinal),
        "AOE diagnostics publish bridge, direct cast monitoring and split Spell IDs through the pixel protocol");
    Equal(true, aoeWarning.Contains("function Fuyutsui:TryBindPendingAOECast", StringComparison.Ordinal)
        && aoeWarning.Contains("protectedCorrelationSeconds = 0.5", StringComparison.Ordinal)
        && aoeWarning.Contains("protectedTiming = timing == nil", StringComparison.Ordinal)
        && aoeWarning.Contains("impactAnchor = \"actual\"", StringComparison.Ordinal)
        && aoeWarning.Contains("missing_end_anchor", StringComparison.Ordinal)
        && aoeWarning.Contains("absorbDiGuaCastSeconds = 11.7", StringComparison.Ordinal)
        && aoeWarning.Contains("锚点=%s", StringComparison.Ordinal)
        && stateBlocks.Contains("[\"AOE读条剩余\"]", StringComparison.Ordinal),
        "protected instanced casts use a narrow DiGua correlation and never fake a missing cast-end anchor");
    Equal(true, aoeWarning.Contains("function Fuyutsui:GetEstimatedGCDSeconds()", StringComparison.Ordinal)
        && aoeWarning.Contains("function Fuyutsui:GetGCDRemainingSeconds()", StringComparison.Ordinal)
        && aoeWarning.Contains("local function DivineTollExpectedReady", StringComparison.Ordinal)
        && aoeWarning.Contains("cooldownRemaining <= math.max(0, virtueAt - now)", StringComparison.Ordinal)
        && stateBlocks.Contains("[\"圣洁鸣钟预计可用\"] = function() return state.divineTollExpectedReady and 1 or 0 end", StringComparison.Ordinal)
        && aoeWarning.Contains("Fuyutsui:UpdateStateBlock(\"状态\", \"圣洁鸣钟预计可用\")", StringComparison.Ordinal)
        && stateBlocks.Contains("self:GetEstimatedGCDSeconds()", StringComparison.Ordinal)
        && stateBlocks.Contains("self:GetGCDRemainingSeconds()", StringComparison.Ordinal)
        && events.Contains("self:UpdateStateBlock(\"状态\", \"公共冷却时长\")", StringComparison.Ordinal)
        && events.Contains("self:UpdateStateBlock(\"状态\", \"公共冷却剩余\")", StringComparison.Ordinal)
        && paladinConfig.Contains("\"公共冷却时长\"", StringComparison.Ordinal),
        "AOE planning and runtime input pacing share one measured GCD source");
    Equal(true, unitSelector.Contains("LowestHealthOtherPlayer", StringComparison.Ordinal),
        "module unit contract exposes a selector for other players only");
    Equal(true, player.Contains("function Fuyutsui:PublishPlayerAction", StringComparison.Ordinal)
        && events.Contains("self:PublishPlayerAction(spellID, 1)", StringComparison.Ordinal)
        && events.Contains("self:PublishPlayerAction(spellID, 2)", StringComparison.Ordinal)
        && player.Contains("state.playerActionSpell = 0", StringComparison.Ordinal)
        && events.Contains("self:UpdatePlayerCombatTime()", StringComparison.Ordinal)
        && !target.Contains("GetUnitName(unit, true)", StringComparison.Ordinal),
        "player action acknowledgement and combat transitions avoid polling and protected nameplate names");
    var playerActionSpellUpdate = player.IndexOf(
        "self:UpdateStateBlock(\"状态\", \"玩家动作技能\")",
        player.IndexOf("function Fuyutsui:PublishPlayerAction", StringComparison.Ordinal),
        StringComparison.Ordinal);
    var playerActionStatusUpdate = player.IndexOf(
        "self:UpdateStateBlock(\"状态\", \"玩家动作状态\")",
        playerActionSpellUpdate,
        StringComparison.Ordinal);
    var playerActionSerialUpdate = player.IndexOf(
        "self:UpdateStateBlock(\"状态\", \"玩家动作序号\")",
        playerActionStatusUpdate,
        StringComparison.Ordinal);
    Equal(true, playerActionSpellUpdate >= 0
        && playerActionStatusUpdate > playerActionSpellUpdate
        && playerActionSerialUpdate > playerActionStatusUpdate,
        "player action serial is published last as the atomic acknowledgement commit marker");
    Equal(true, !paladin.Contains("spellId = 25771, filter = \"HARMFUL\"", StringComparison.Ordinal)
        && block.Contains("local filter = def.includeSpellIDs[27827] and \"HELPFUL\" or \"HELPFUL|PLAYER\"", StringComparison.Ordinal)
        && !paladinConfig.Contains("\"自律\": {\n        \"step\": 7", StringComparison.Ordinal)
        && unitSelector.Contains("sanitized.Remove(\"自律\")", StringComparison.Ordinal)
        && unitSelector.Contains("playerForbearance = state.GetInt(\"自律\")", StringComparison.Ordinal),
        "holy paladin routing rejects untrusted party Forbearance pixels and preserves player state");
    Equal(true, aoeWarning.Contains("spellID = SafeNumber(eventInfo.spellID)", StringComparison.Ordinal)
        && aoeWarning.Contains("if name == \"准备AOE\" then return 1 end", StringComparison.Ordinal)
        && aoeWarning.Contains("if name == \"准备吸奶盾\" then return 2 end", StringComparison.Ordinal)
        && aoeWarning.Contains("function Fuyutsui:ObserveAOEEnemyCast", StringComparison.Ordinal)
        && aoeWarning.Contains("function Fuyutsui:FinishAOEEnemyCast", StringComparison.Ordinal)
        && aoeWarning.Contains("FuyutsuiAOECastEventFrame", StringComparison.Ordinal)
        && aoeWarning.Contains("DiGua式事件帧收到", StringComparison.Ordinal),
        "both warning types bind canonical timeline names and official Spell IDs to real cast timing");
    Equal(true, aoeWarning.Contains("return 5", StringComparison.Ordinal)
        && aoeWarning.Contains("remaining <= config.virtueWindowSeconds", StringComparison.Ordinal)
        && aoeWarning.Contains("event.virtueConfirmed", StringComparison.Ordinal),
        "real cast timing exposes the ordinary GCD hold and persistent Virtue execution windows");
    Equal(true, aoeWarning.Contains("if event.status == \"succeeded\" and event.completed", StringComparison.Ordinal)
        && aoeWarning.Contains("event.completed = true", StringComparison.Ordinal)
        && aoeWarning.Contains("吸收值变化但未收到读条成功", StringComparison.Ordinal)
        && !aoeWarning.Contains("local function CommitAbsorbObservation", StringComparison.Ordinal)
        && !aoeWarning.Contains("event.absorbZeroObserved", StringComparison.Ordinal)
        && aoeWarning.Contains("outcome=unknown，禁止进入美德窗口", StringComparison.Ordinal)
        && !aoeWarning.Contains("event.timelineExecutionFallback = true", StringComparison.Ordinal),
        "heal absorb stage three requires an explicit successful cast and rejects timeline-only fallback");
    Equal(true, aoeWarning.Contains("if state == \"finished\" then", StringComparison.Ordinal)
        && aoeWarning.Contains("时间轴移除状态不可判定", StringComparison.Ordinal),
        "timeline removal distinguishes Finished from unknown state");
    Equal(true, aoeWarning.Contains("if event.cast and state ~= \"canceled\" then", StringComparison.Ordinal),
        "timeline replacement removal preserves a correlated active cast");
    Equal(true, aoeWarning.Contains("if event.eventType == 2 then", StringComparison.Ordinal)
        && aoeWarning.Contains("event.timelineCanceled = true", StringComparison.Ordinal),
        "absorb timeline replacement removal preserves its reservation window, including bridge cancellation");
    Equal(true, aoeWarning.Contains("local terminalPriority", StringComparison.Ordinal)
        && aoeWarning.Contains("STOP is emitted for both a completed cast", StringComparison.Ordinal)
        && aoeWarning.Contains("C_Timer.After(delay, function()", StringComparison.Ordinal),
        "cast terminal signals are deferred, merged by priority, and STOP receives a grace period");
    Equal(true, events.Contains("self:ObserveAOETimelineEvent(eventInfo)", StringComparison.Ordinal)
        && events.Contains("self:ObserveAOETimelineState(eventID)", StringComparison.Ordinal)
        && events.Contains("self:RemoveAOETimelineEvent(eventID)", StringComparison.Ordinal),
        "timeline warning lifecycle caches state before removal");
    Equal(true, events.Contains("self:ObserveAOEEnemyCast(unitTarget, castGUID, spellID, false)", StringComparison.Ordinal)
        && events.Contains("self:ObserveAOEEnemyCast(unitTarget, castGUID, spellID, true)", StringComparison.Ordinal)
        && events.Contains("self:FinishAOEEnemyCast(unitTarget, castGUID, spellID, \"interrupted\")", StringComparison.Ordinal)
        && events.Contains("self:FinishAOEEnemyCast(unitTarget, castGUID, spellID, \"succeeded\")", StringComparison.Ordinal)
        && events.Contains("self:ConfirmAOEVirtue(spellID)", StringComparison.Ordinal),
        "enemy cast start, channel, interruption, completion, and Virtue confirmation drive the warning state machine");
}

static void AoeWarningStateMachineReplayContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var startInfo = new ProcessStartInfo("/usr/bin/env")
    {
        WorkingDirectory = repositoryRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    startInfo.ArgumentList.Add("luajit");
    startInfo.ArgumentList.Add(Path.Combine(
        repositoryRoot,
        "Tests",
        "Shigure.Core.ContractTests",
        "Fixtures",
        "aoe-warning-replay.lua"));
    startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "Fuyutsui", "core", "aoewarning.lua"));

    using var replay = Process.Start(startInfo)
        ?? throw new InvalidOperationException("failed to start the production Lua replay");
    var stdout = replay.StandardOutput.ReadToEnd();
    var stderr = replay.StandardError.ReadToEnd();
    replay.WaitForExit();
    if (replay.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"production Lua replay failed with exit code {replay.ExitCode}:{Environment.NewLine}{stdout}{stderr}");
    }
    Equal(true, stdout.Contains("AOE warning production Lua replay passed", StringComparison.Ordinal),
        "production Lua state machine executes the lifecycle replay");
}

static void DiGuaBridgeProductionLuaReplayContract()
{
    var repositoryRoot = FindRepositoryRoot();
    var startInfo = new ProcessStartInfo("/usr/bin/env")
    {
        WorkingDirectory = repositoryRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    startInfo.ArgumentList.Add("luajit");
    startInfo.ArgumentList.Add(Path.Combine(
        repositoryRoot,
        "Tests",
        "Shigure.Core.ContractTests",
        "Fixtures",
        "digua-bridge-replay.lua"));
    startInfo.ArgumentList.Add(Path.Combine(repositoryRoot, "Fuyutsui", "core", "diguabridge.lua"));

    using var replay = Process.Start(startInfo)
        ?? throw new InvalidOperationException("failed to start the DiGua bridge Lua replay");
    var stdout = replay.StandardOutput.ReadToEnd();
    var stderr = replay.StandardError.ReadToEnd();
    replay.WaitForExit();
    if (replay.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"DiGua bridge Lua replay failed with exit code {replay.ExitCode}:{Environment.NewLine}{stdout}{stderr}");
    }
    Equal(true, stdout.Contains("DiGua bridge production Lua replay passed", StringComparison.Ordinal),
        "production DiGua bridge re-emits verified Spell IDs and propagates cancellation");
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
        Equal(
            ClassMacrosStore.SelectorTargetRoutingMode,
            document.Classes["PALADIN"].RoutingMode,
            "macro editor loads selector-target routing mode");
        Equal(true, document.Classes["PALADIN"].StaticSpells.Any(entry => entry.Text == "[@tanktarget]审判"),
            "paladin judgment uses the generated tank-target macro");
        Equal(false, document.Classes["PALADIN"].StaticSpells.Any(entry => entry.Text == "[@tanktarget]神圣震击"),
            "paladin Holy Shock is not forced through the hostile tank-target macro");
        var tankTargetJudgment = FuyutsuiKeymapConverter.ParseStaticMacro("[@tanktarget]审判");
        Equal(ReservedUnit.None, tankTargetJudgment.Unit, "tank-target judgment keeps the untargeted keymap binding");
        Equal("审判", tankTargetJudgment.Spell, "tank-target judgment preserves the module spell name");
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
        Equal(
            39,
            FuyutsuiKeymapConverter.CalculateRequiredSlots(
                2,
                3,
                4,
                routingMode: ClassMacrosStore.SelectorTargetRoutingMode),
            "selector-target routing shares one target key block");

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

        var paladinKeymapPath = Path.Combine(keymapDirectory, "paladin.json");
        var paladinKeymap = JsonNode.Parse(File.ReadAllText(paladinKeymapPath))
            ?? throw new InvalidDataException("generated paladin keymap is empty");
        var holySpec = paladinKeymap["专精"]?["1"]
            ?? throw new InvalidDataException("generated paladin keymap is missing holy spec");
        Equal(
            ClassMacrosStore.SelectorTargetRoutingMode,
            holySpec["路由模式"]?.GetValue<string>(),
            "holy paladin keymap uses selector-target routing");
        Equal("清洁术", holySpec["route-2-1"]?["技能"]?.GetValue<string>(),
            "holy paladin routes the magic-capable cleanse");
        Equal("美德道标", holySpec["route-8-1"]?["技能"]?.GetValue<string>(), "virtue has a routed player slot");
        Equal(1, holySpec["route-8-1"]?["unit"]?.GetValue<int>() ?? -1, "virtue route retains logical unit one");
        Equal(
            2,
            holySpec["route-8-1"]?["按键序列"]?.AsArray().Count ?? 0,
            "routed action emits selector and target hotkeys");
        var protectionSpec = paladinKeymap["专精"]?["2"]
            ?? throw new InvalidDataException("generated paladin keymap is missing protection spec");
        Equal("清毒术", protectionSpec["route-2-1"]?["技能"]?.GetValue<string>(),
            "non-holy paladin keeps cleanse toxins");

        var keymap = new KeymapService(fixtureRoot, new ConfigService(Path.Combine(repositoryRoot, "config")));
        keymap.SelectForClass(2, 1);
        var virtue = keymap.GetBinding(1, "美德道标", "");
        Equal(2, virtue?.Hotkeys.Count ?? 0, "keymap service preserves the routed hotkey sequence");
        Equal(virtue?.DisplayText, keymap.GetHotkey(1, "美德道标", ""), "legacy hotkey display stays readable");
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
        var bridgeSourceRoot = Path.Combine(fixtureRoot, "source", "FuyutsuiDiGuaBridge");
        var sourceCore = Path.Combine(sourceRoot, "core");
        Directory.CreateDirectory(sourceCore);
        Directory.CreateDirectory(bridgeSourceRoot);
        var tocPath = Path.Combine(sourceRoot, "Fuyutsui.toc");
        var nestedRelativePath = Path.Combine("core", "state.lua");
        var nestedSourcePath = Path.Combine(sourceRoot, nestedRelativePath);
        File.WriteAllText(tocPath, "version-one");
        File.WriteAllText(nestedSourcePath, "state-one");
        File.WriteAllText(Path.Combine(bridgeSourceRoot, "Bridge.lua"), "bridge-one");

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
        Equal(3, first.CopiedFiles.Count, "first deployment copies both managed addons");
        Equal("state-one", File.ReadAllText(Path.Combine(targetRoot, nestedRelativePath)), "nested file copied");
        Equal(
            "bridge-one",
            File.ReadAllText(Path.Combine(flavorRoot, "Interface", "AddOns", "FuyutsuiDiGuaBridge", "Bridge.lua")),
            "DiGua bridge is deployed beside Fuyutsui");

        var same = service.SynchronizeAll();
        Equal(true, same.CompletedSuccessfully, "same-version deployment succeeds");
        Equal(0, same.CopiedFiles.Count, "same-version deployment copies nothing");
        Equal(3, same.SkippedFiles.Count, "same-version deployment skips all files");

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
        var sourceMacro = Path.Combine(sourceRoot, "Fuyutsui", "core", "macro.lua");
        var sourceClassMacros = Path.Combine(sourceRoot, "Fuyutsui", "core", "classmacros.lua");
        var sourceTexture = Path.Combine(sourceRoot, "Fuyutsui", "media", "icon.blp");
        var sourceClass = Path.Combine(sourceRoot, "Fuyutsui", "class", "Mage.lua");
        var sourceConfig = Path.Combine(sourceRoot, "config", "common.json");
        var sourceKeymap = Path.Combine(sourceRoot, "keymap", "base.json");
        var sourcePaladinKeymap = Path.Combine(sourceRoot, "keymap", "paladin.json");
        var sourceProcess = Path.Combine(sourceRoot, "wow_process.txt");
        var sourceBridge = Path.Combine(sourceRoot, "FuyutsuiDiGuaBridge", "Bridge.lua");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceLua)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceTexture)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceClass)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceConfig)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceKeymap)!);
        Directory.CreateDirectory(Path.GetDirectoryName(sourceBridge)!);
        File.WriteAllText(sourceLua, "source-v1");
        File.WriteAllText(sourceMacro, "macro-v1");
        File.Copy(Path.Combine(repositoryRoot, "Fuyutsui", "core", "classmacros.lua"), sourceClassMacros);
        File.WriteAllBytes(sourceTexture, [0, 1, 2, 255]);
        foreach (var classPath in Directory.EnumerateFiles(
                     Path.Combine(repositoryRoot, "Fuyutsui", "class"),
                     "*.lua",
                     SearchOption.TopDirectoryOnly))
        {
            File.Copy(classPath, Path.Combine(Path.GetDirectoryName(sourceClass)!, Path.GetFileName(classPath)));
        }
        File.WriteAllText(sourceConfig, "{\"version\":1}");
        File.WriteAllText(sourceKeymap, "{\"key\":1}");
        File.Copy(Path.Combine(repositoryRoot, "keymap", "paladin.json"), sourcePaladinKeymap);
        File.WriteAllText(sourceProcess, "Wow");
        File.WriteAllText(sourceBridge, "bridge-v1");
        var managedSourceCount = new[] { "Fuyutsui", "FuyutsuiDiGuaBridge", "config", "keymap" }
            .Sum(directory => Directory.EnumerateFiles(
                Path.Combine(sourceRoot, directory),
                "*",
                SearchOption.AllDirectories).Count()) + 1;

        var service = new RuntimeResourceWorkspaceService();
        var first = service.Initialize(sourceRoot, userDataRoot);
        Equal(managedSourceCount, first.CreatedFiles.Count, "workspace first initialization creates every source");
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
        Equal(managedSourceCount, second.SkippedFiles.Count, "workspace unchanged files are skipped");
        Equal(0, second.RegeneratedFiles.Count, "workspace does not report unchanged derived resources");

        var targetLua = Path.Combine(first.WorkspaceDirectory, "Fuyutsui", "core", "state.lua");
        var targetMacro = Path.Combine(first.WorkspaceDirectory, "Fuyutsui", "core", "macro.lua");
        var targetClassMacros = Path.Combine(first.WorkspaceDirectory, "Fuyutsui", "core", "classmacros.lua");
        var targetConfig = Path.Combine(first.WorkspaceDirectory, "config", "common.json");
        var targetClass = Path.Combine(first.WorkspaceDirectory, "Fuyutsui", "class", "Mage.lua");
        var targetPaladinClass = Path.Combine(first.WorkspaceDirectory, "Fuyutsui", "class", "Paladin.lua");
        var targetOldKeymap = Path.Combine(first.WorkspaceDirectory, "keymap", "base.json");
        var targetPaladinKeymap = Path.Combine(first.WorkspaceDirectory, "keymap", "paladin.json");
        File.WriteAllText(targetLua, "user-change");
        File.WriteAllText(targetMacro, "user-macro-change");
        File.WriteAllText(
            targetClassMacros,
            File.ReadAllText(targetClassMacros).Replace(
                "common = { \"荣耀圣令\" },",
                "common = { \"荣耀圣令\", \"清毒术\" },",
                StringComparison.Ordinal).Replace(
                "[@target,harm,nodead]正义盾击",
                "[@tanktarget]正义盾击",
                StringComparison.Ordinal));
        File.WriteAllText(
            targetClass,
            File.ReadAllText(targetClass).Replace("\"施法(倒计时)\"", "\"施法\"", StringComparison.Ordinal));
        var legacyPaladinText = File.ReadAllText(targetPaladinClass);
        foreach (var stateName in new[]
                 {
                     "公共冷却剩余", "DiGua桥接就绪", "宏绑定状态", "宏绑定数量", "玩家动作序号", "玩家动作技能", "玩家动作状态",
                     "AOE桥接请求数", "AOE桥接成功数", "AOE带技能预警数", "AOE原始读条数",
                     "AOE技能受保护数", "AOE敌对状态受保护数", "AOE受保护匹配数", "AOE敌方读条数",
                     "AOE读条未采纳数", "AOE读条匹配数", "AOE读条未匹配数", "AOE读条成功数",
                     "AOE读条失败数", "AOE预警技能低位", "AOE预警技能中位", "AOE预警技能高位",
                     "AOE读条技能低位", "AOE读条技能中位", "AOE读条技能高位", "AOE受保护读条",
                     "AOE读条剩余"
                 })
        {
            legacyPaladinText = legacyPaladinText.Replace(
                $"                \"{stateName}\",{Environment.NewLine}",
                string.Empty,
                StringComparison.Ordinal);
        }
        foreach (var spellId in new[] { 275773, 20473, 4987, 85673, 156322, 85222 })
        {
            legacyPaladinText = string.Join(
                Environment.NewLine,
                legacyPaladinText.Split(Environment.NewLine)
                    .Where(line => !line.Contains($"[{spellId}]", StringComparison.Ordinal)));
        }
        File.WriteAllText(targetPaladinClass, legacyPaladinText);
        File.WriteAllText(
            targetPaladinKeymap,
            """
            {
              "专精": {
                "1": {
                  "62": { "unit": 2, "宏条件": "", "技能": "清洁术", "热键": "ALT-F9" }
                }
              }
            }
            """);
        File.WriteAllText(sourceLua, "source-v2");
        File.WriteAllText(sourceMacro, "macro-v2");
        File.WriteAllText(sourceConfig, "{\"version\":2}");
        File.Delete(sourceKeymap);
        var sourceNewKeymap = Path.Combine(sourceRoot, "keymap", "new.json");
        File.WriteAllText(sourceNewKeymap, "{\"key\":2}");

        var upgraded = service.Initialize(sourceRoot, userDataRoot);
        Equal(true, upgraded.UpdatedFiles.Contains("config/common.json"), "unchanged target receives source update");
        Equal(true, upgraded.CreatedFiles.Contains("keymap/new.json"), "new source file is created");
        Equal(true, upgraded.ConflictingFiles.Contains("Fuyutsui/core/state.lua"), "user edit is reported as conflict");
        Equal(true, upgraded.ConflictingFiles.Contains("Fuyutsui/core/macro.lua"), "macro engine edit is reported as conflict");
        Equal(true, upgraded.ConflictingFiles.Contains("Fuyutsui/core/classmacros.lua"), "custom macro authority is preserved");
        Equal(true, upgraded.ConflictingFiles.Contains("Fuyutsui/class/Mage.lua"), "custom class is reported as preserved conflict");
        Equal(true, upgraded.ConflictingFiles.Contains("Fuyutsui/class/Paladin.lua"), "custom Paladin class is preserved before structural migration");
        Equal(false, upgraded.ConflictingFiles.Contains("keymap/paladin.json"), "derived legacy keymap is reconciled instead of preserved");
        Equal(true, upgraded.ProtocolConflictingFiles.Contains("Fuyutsui/core/state.lua"), "core conflict blocks mixed protocol runtime");
        Equal(true, upgraded.ProtocolConflictingFiles.Contains("Fuyutsui/core/macro.lua"), "macro routing conflict blocks mixed protocol runtime");
        Equal(false, upgraded.ProtocolConflictingFiles.Contains("Fuyutsui/core/classmacros.lua"), "macro customization is safe after keymap regeneration");
        Equal(false, upgraded.ProtocolConflictingFiles.Contains("Fuyutsui/class/Mage.lua"), "migratable class customization does not block runtime");
        Equal(true, upgraded.MigratedFiles.Contains("Fuyutsui/class/Mage.lua"), "legacy cast field is structurally migrated");
        Equal(true, upgraded.MigratedFiles.Contains("Fuyutsui/class/Paladin.lua"), "required Paladin runtime fields are structurally migrated");
        Equal(true, upgraded.MigratedFiles.Contains("Fuyutsui/core/classmacros.lua"), "Paladin tank-target macro is structurally migrated");
        Equal(true, upgraded.RegeneratedFiles.Contains("keymap/paladin.json"), "legacy direct-key map is regenerated from macro authority");
        var reconciledPaladin = JsonNode.Parse(File.ReadAllText(targetPaladinKeymap))
            ?? throw new InvalidDataException("reconciled paladin keymap is empty");
        var reconciledHoly = reconciledPaladin["专精"]?["1"]
            ?? throw new InvalidDataException("reconciled paladin keymap is missing holy spec");
        Equal(
            ClassMacrosStore.SelectorTargetRoutingMode,
            reconciledHoly["路由模式"]?.GetValue<string>(),
            "reconciled holy keymap uses the current routing contract");
        Equal("清洁术", reconciledHoly["route-3-2"]?["技能"]?.GetValue<string>(),
            "reconciled keymap follows the preserved macro spell order");
        Equal(2, reconciledHoly["route-3-2"]?["按键序列"]?.AsArray().Count ?? 0,
            "reconciled cleanse uses selector and target hotkeys");
        Equal(
            false,
            ClassBlocksStore.Load(targetClass).Specs.Values.Any(spec =>
                spec.FlatStates.Contains("施法", StringComparer.Ordinal)
                || spec.CategorizedStates.Values.Any(states => states.Contains("施法", StringComparer.Ordinal))),
            "legacy cast state is removed from every class spec");
        var migratedPaladin = ClassBlocksStore.Load(targetPaladinClass);
        var migratedHolyStates = migratedPaladin.Specs[1].CategorizedStates[ClassStateCatalog.CategoryState];
        Equal(true, new[]
            {
                "公共冷却剩余", "DiGua桥接就绪", "宏绑定状态", "宏绑定数量", "玩家动作序号", "玩家动作技能", "玩家动作状态",
                "AOE桥接请求数", "AOE桥接成功数", "AOE带技能预警数", "AOE敌方读条数",
                "AOE读条未采纳数", "AOE读条匹配数", "AOE读条未匹配数", "AOE读条成功数",
                "AOE读条失败数", "AOE预警技能低位", "AOE预警技能中位", "AOE预警技能高位",
                "AOE读条技能低位", "AOE读条技能中位", "AOE读条技能高位"
            }
                .All(stateName => migratedHolyStates.Contains(stateName, StringComparer.Ordinal)),
            "Paladin migration restores every runtime acknowledgement field");
        Equal(true, new long[] { 275773, 20473, 4987, 85673, 156322, 85222 }
                .All(spellId => migratedPaladin.SpellsList.Any(entry => entry.SpellId == spellId)),
            "Paladin migration restores every player action mapping");
        Equal(true, ClassMacrosStore.Load(targetClassMacros).Classes["PALADIN"].StaticSpells
                .Any(entry => string.Equals(entry.Text, "[@target,harm,nodead]正义盾击", StringComparison.Ordinal)),
            "Paladin migration restores the direct hostile Shield of the Righteous macro");
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

static void LocalRuntimeLogStoreContract()
{
    var root = Path.Combine(Path.GetTempPath(), $"shigure-log-{Guid.NewGuid():N}");
    var path = Path.Combine(root, "runtime-detailed.log");
    try
    {
        var store = new Shigure.MacUI.LocalRuntimeLogStore(path);
        store.Append(new RuntimeLogEntry(DateTimeOffset.UnixEpoch, "详细日志测试"));
        Equal(true, File.ReadAllText(path).Contains("详细日志测试", StringComparison.Ordinal),
            "local runtime log store writes UTF-8 entries");

        var payload = new string('x', 1024 * 1024);
        for (var index = 0; index < 9; index++)
        {
            store.Append(new RuntimeLogEntry(DateTimeOffset.UnixEpoch, payload));
        }

        Equal(true, File.Exists(path + ".1"), "local runtime log store rotates oversized files");
        Equal(true, new FileInfo(path).Length <= Shigure.MacUI.LocalRuntimeLogStore.MaximumFileBytes,
            "local runtime log store keeps the active file below the size limit");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static void RuntimeUiUpdateGuardContract()
{
    var fixtureRoot = Path.Combine(Path.GetTempPath(), $"shigure-runtime-ui-guard-{Guid.NewGuid():N}");
    var logPath = Path.Combine(fixtureRoot, "logs", "runtime-ui-errors.log");
    try
    {
        var guard = new Shigure.MacUI.RuntimeUiUpdateGuard(logPath);
        var successfulUpdates = 0;
        Equal(true, guard.TryRun("runtime-status", () => successfulUpdates++),
            "successful runtime UI update");
        Equal(1, successfulUpdates, "successful runtime UI update count");

        Equal(false, guard.TryRun(
                "runtime-snapshot",
                () => throw new InvalidOperationException("snapshot failed")),
            "failing runtime UI update is contained");
        Equal(true, guard.IsDisabled("runtime-snapshot"),
            "failing runtime UI source is disabled");

        var repeatedUpdates = 0;
        Equal(false, guard.TryRun("runtime-snapshot", () => repeatedUpdates++),
            "disabled runtime UI source remains contained");
        Equal(0, repeatedUpdates, "disabled runtime UI source is not invoked again");

        var log = File.ReadAllText(logPath);
        Equal(true, log.Contains("runtime-snapshot", StringComparison.Ordinal),
            "runtime UI failure log source");
        Equal(true, log.Contains("InvalidOperationException: snapshot failed", StringComparison.Ordinal),
            "runtime UI failure log exception");
    }
    finally
    {
        if (Directory.Exists(fixtureRoot))
        {
            Directory.Delete(fixtureRoot, recursive: true);
        }
    }
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
    var bundledModuleItem = project.Descendants("None").Single(element =>
        (element.Attribute("Include")?.Value ?? string.Empty).Contains("BundledModules", StringComparison.Ordinal));
    Equal(true,
        (bundledModuleItem.Attribute("Link")?.Value ?? string.Empty).StartsWith("bundled-modules", StringComparison.Ordinal),
        "Mac UI publishes bundled modules under one stable resource directory");

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
    Equal(true, compositionText.Contains("new BundledModuleInstaller().Install", StringComparison.Ordinal)
        && compositionText.Contains("ResolveBundledModuleDirectory()", StringComparison.Ordinal),
        "Mac UI installs bundled modules before constructing the module store");
    Equal(true, compositionText.Contains("workspace.ProtocolConflictingFiles", StringComparison.Ordinal)
        && compositionText.Contains("FuyutsuiAddonSyncResult.Skipped", StringComparison.Ordinal),
        "Mac UI skips addon deployment when protocol files conflict");
    Equal(true, mainWindowText.Contains("游戏插件已同步", StringComparison.Ordinal),
        "Mac UI reports startup addon deployment");
    Equal(true, mainWindowText.Contains("if (_logicToast.IsVisible != true)", StringComparison.Ordinal),
        "runtime status overlay is not shown twice while already visible");
    Equal(true, mainWindowText.Contains(
            "MacFrontmostApplication.IsTarget(_statusTargetLocator.FindFrontmostTarget())",
            StringComparison.Ordinal)
        && mainWindowText.Contains("HideRuntimeToastWhenTargetIsNotFrontmost", StringComparison.Ordinal),
        "runtime status overlay is shown only while the configured WoW target is frontmost");
    Equal(true, mainWindowText.Contains("EnsureAddonSynchronizedBeforeRuntimeAsync", StringComparison.Ordinal)
        && mainWindowText.Contains("_addonSync.SynchronizeAll", StringComparison.Ordinal),
        "Mac UI retries addon deployment before a runtime session starts");
    Equal(true, mainWindowText.Contains("_addonReloadRequired = services.AddonSync.CopiedFiles.Count > 0", StringComparison.Ordinal)
        && mainWindowText.Contains("_addonReloadRequired = true", StringComparison.Ordinal)
        && mainWindowText.Contains("请在 WoW 输入 /reload", StringComparison.Ordinal)
        && mainWindowText.Contains("ShowConfirmationAsync(reloadMessage, \"已完成重载\")", StringComparison.Ordinal)
        && mainWindowText.Contains("已确认 WoW 完成 /reload，允许运行时启动", StringComparison.Ordinal),
        "runtime start requires explicit reload confirmation after any addon update");
    Equal(true, mainWindowText.Contains("RunButton.IsEnabled = false", StringComparison.Ordinal),
        "Mac UI disables runtime controls when protocol files conflict");
    Equal(true, mainWindowText.Contains("new MacPermissionService()", StringComparison.Ordinal), "Mac UI refreshes native permission services for current status");
    Equal(true, mainWindowText.Contains("permissionService.Check()", StringComparison.Ordinal), "Mac UI exposes side-effect-free permission checks");
    Equal(true, mainWindowText.Contains("_permissions.Request(permission)", StringComparison.Ordinal), "Mac UI permission prompts require an explicit button path");
    Equal(true, mainWindowText.Contains("_permissionRequestGate.WaitAsync(0)", StringComparison.Ordinal), "Mac UI serializes explicit permission requests");
    Equal(true, mainWindowText.Contains("SetPermissionCommandsEnabled", StringComparison.Ordinal), "Mac UI disables permission controls while a request is active");
    Equal(true, appText.Contains("Shigure.MacUI.Application", StringComparison.Ordinal), "Mac UI owns an application-level single-instance lease");
    Equal(true, programText.Contains("string.Equals(args[0], \"--help\"", StringComparison.Ordinal), "Mac UI exposes a side-effect-free bundle smoke command");
    Equal(true, programText.Contains("args[0], \"--permission-check\"", StringComparison.Ordinal)
        && programText.Contains("new MacPermissionService().Check()", StringComparison.Ordinal),
        "Mac UI exposes a native permission probe for the running bundle");
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
    Equal(true, mainWindowText.Contains("ShowScanFailureToast()", StringComparison.Ordinal),
        "Mac UI shows scan failures in the centered status toast");
    Equal(true, mainWindowText.Contains("_logicToast?.IsVisible != true", StringComparison.Ordinal),
        "Mac UI retries a persistent scan warning when its status toast is hidden");
    Equal(true, mainWindowText.Contains("return snapshot.ScanFailureReason", StringComparison.Ordinal),
        "Mac UI overlay surfaces the concrete scan failure reason");
    Equal(true, mainWindowText.Contains("色块识别已恢复", StringComparison.Ordinal),
        "Mac UI reports scan recovery on screen");
    Equal(true, mainWindowText.Contains("_activeScanFailureReason = null", StringComparison.Ordinal),
        "Mac UI clears persistent scan warnings when runtime stops");
    Equal(true, sourceText.Contains("ShowActivated = false", StringComparison.Ordinal), "Mac logic status toast does not steal focus");
    Equal(true, windowInteractionText.Contains("setIgnoresMouseEvents:", StringComparison.Ordinal), "Mac logic status toast uses native click-through");
    Equal(true, windowInteractionText.Contains("setCollectionBehavior:", StringComparison.Ordinal)
        && windowInteractionText.Contains("CanJoinAllSpaces | FullScreenAuxiliary", StringComparison.Ordinal),
        "Mac scan warning joins every Space and native full-screen windows");
    Equal(true, buildScriptText.Contains("bundled-modules", StringComparison.Ordinal),
        "production packaging moves bundled modules into app resources");
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
    Equal("22", versionProperties["ShigureBuildNumber"], "version authority defines the global build number");
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
    Equal(true, infoPlistText.Contains("NSScreenCaptureUsageDescription", StringComparison.Ordinal), "Info.plist declares the screen capture purpose description");
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
    Equal(true, buildScriptText.Contains("不允许生成 ad-hoc 应用", StringComparison.Ordinal), "production packaging rejects ad-hoc signing");
    Equal(false, buildScriptText.Contains("codesign --force --deep --sign -", StringComparison.Ordinal), "production packaging cannot emit ad-hoc bundles");
    Equal(true, buildScriptText.Contains("designated_requirement", StringComparison.Ordinal), "persistent signing reads back the designated requirement");
    Equal(true, buildScriptText.Contains("*\"cdhash\"*", StringComparison.Ordinal), "persistent signing rejects version-bound designated requirements");
    Equal(true, buildScriptText.Contains("=designated => certificate root = H\\\"$codesign_identity_lower\\\" and identifier", StringComparison.Ordinal), "local signing keeps the designated requirement stable for one certificate root");
    Equal(true, buildScriptText.Contains("bundle_identifier=\"$(plutil -extract CFBundleIdentifier raw", StringComparison.Ordinal), "local signing derives the designated requirement identifier from Info.plist");
    Equal(true, buildScriptText.Contains("\"$code_path\" == \"$macos_path/Shigure.MacApp\"", StringComparison.Ordinal)
        && buildScriptText.Contains("code_identifier=\"Shigure\"", StringComparison.Ordinal),
        "runtime helper preserves the legacy Shigure identifier");
    Equal(true, buildScriptText.Contains("helper_requirement", StringComparison.Ordinal), "runtime helper designated requirement is validated");
    Equal(true, localSigningScriptText.Contains("local-signing-identity.sha1", StringComparison.Ordinal), "local signing pins one certificate fingerprint");
    Equal(true, localSigningScriptText.Contains("当前 Shigure 签名身份与已固定证书不一致", StringComparison.Ordinal), "local signing rejects silent certificate rotation");
    Equal(true, localSigningScriptText.Contains("多个同名 Shigure 签名身份", StringComparison.Ordinal), "local signing rejects ambiguous duplicate identities");
    Equal(true, localSigningScriptText.Contains("已拒绝创建新证书", StringComparison.Ordinal), "local signing does not replace a missing pinned identity");
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
    Equal(true, macroText.Contains(
            "self:GetFrameRef('t%d'):SetAttribute('macrotext', self:GetAttribute('%s'))",
            StringComparison.Ordinal),
        "selector target routing updates directly-bound target macros");
    Equal(true, macroText.Contains("RegisterForClicks(\"AnyUp\", \"AnyDown\")", StringComparison.Ordinal),
        "secure macro buttons accept both keyboard edges used by override bindings");
    Equal(false, macroText.Contains("target:SetAttribute(\"type\", \"click\")", StringComparison.Ordinal),
        "selector target routing does not delegate protected clicks");
    Equal(false, macroText.Contains("self:SetBindingClick", StringComparison.Ordinal),
        "selector target routing does not create transient target bindings in combat");
    Equal(true, macroText.Contains("return true\nend", StringComparison.Ordinal),
        "macro creation reports success");
    Equal(true, mainText.Contains("self.macrosPending = not created", StringComparison.Ordinal),
        "failed macro creation is retained for retry");
    Equal(true, macroText.Contains("local offset = keyOffset or 0", StringComparison.Ordinal)
        && macroText.Contains("local i = 1 + offset", StringComparison.Ordinal),
        "macro creation applies the class key offset");
    Equal(true, mainText.Contains(
            "self:CreateMacro(dynamicSpells, m.staticSpells, m.specialSpells, m.keyOffset, m.routingMode)",
            StringComparison.Ordinal),
        "player macro loading forwards the class key offset and routing mode");
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
    public bool RequiresRestart { get; set; }
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
    public List<TimeSpan> Waits { get; } = [];
    public List<string> Operations { get; } = [];
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
        Operations.Add($"post:{eventRef}");
    }

    public void Wait(TimeSpan delay)
    {
        Waits.Add(delay);
        Operations.Add($"wait:{delay.TotalMilliseconds:0}");
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
                var sourceY = localY + (int)Math.Floor(y / actualScaleY);
            if (sourceY >= _sourceBounds.Height)
            {
                break;
            }

            for (var x = 0; x < pixelWidth; x++)
            {
                var sourceX = localX + (int)Math.Floor(x / actualScaleX);
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

sealed class CooldownAwareRuntimeStateBuilder(CooldownAwareTargetKeyOutput output) : IRuntimeStateBuilder
{
    public GameState Build(
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData,
        IReadOnlyDictionary<int, int>? healAbsorbData = null) =>
        new(new Dictionary<string, object?>
        {
            ["有效性"] = 1,
            ["职业"] = 2,
            ["专精"] = 1,
            ["DiGua桥接就绪"] = true,
            ["公共冷却剩余"] = 0,
            ["玩家动作序号"] = output.SendCount > 0 ? 1 : 0,
            ["玩家动作技能"] = output.SendCount > 0 ? 24 : 0,
            ["玩家动作状态"] = output.SendCount > 0 ? 2 : 0,
            ["spells"] = new Dictionary<string, object?>
            {
                ["美德道标"] = 0
            }
        });
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

sealed class CooldownAwareRuntimeLogic : IRuntimeLogic
{
    public LogicEvaluation Evaluate(
        int? classId,
        int? specId,
        string? specName,
        GameState state,
        bool runLogic) =>
        new(
            "冷却确认测试",
            runLogic && state.GetInt("spells.美德道标") == 0
                ? new LogicDecision(
                    "CTRL-A",
                    "冷却确认测试: 施放 美德道标",
                    new Dictionary<string, object?> { ["动作技能"] = "美德道标" },
                    "冷却确认测试",
                    RateLimitKey: "cooldown-test",
                    CooldownConfirmationSpell: "美德道标",
                    PlayerActionCode: 24)
                : null);
}

sealed class FakeTargetKeyOutput : ITargetKeyOutput
{
    public KeySendResult Send(string hotkey, TargetIdentity? expectedTarget) =>
        KeySendResult.Success;
}

sealed class CooldownAwareTargetKeyOutput : ITargetKeyOutput
{
    private int _sendCount;

    public int SendCount => Volatile.Read(ref _sendCount);

    public KeySendResult Send(string hotkey, TargetIdentity? expectedTarget)
    {
        Interlocked.Increment(ref _sendCount);
        return KeySendResult.Success;
    }
}

sealed class ValidRuntimeScanner : IRuntimeScreenScanner
{
    public ScreenScanResult ScanScreenData() => new(
        new Dictionary<int, int> { [0] = 1 },
        new Dictionary<int, int>(),
        new Dictionary<int, int>(),
        null);
}

sealed class ContractKeymapResolver : IKeymapResolver
{
    public void SelectForClass(int? classId)
    {
    }

    public void SelectForClass(int? classId, int? specId)
    {
    }

    public string? GetHotkey(int? unit, string spell, string? macroCondition = null) =>
        spell == "可用技能"
            ? "CTRL-A"
            : unit == ReservedUnit.Target && spell == "正义盾击" ? "CTRL-S" : null;

    public IReadOnlyDictionary<int, string> GetCurrentFailedSpells() => new Dictionary<int, string>();

    public IReadOnlyDictionary<int, string> GetCurrentOneKeySpells() => new Dictionary<int, string>();
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
