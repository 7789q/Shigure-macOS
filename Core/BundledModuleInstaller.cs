using System.Security.Cryptography;
using System.Text;

namespace Shigure;

public sealed record BundledModuleInstallResult(
    IReadOnlyList<string> InstalledModules,
    IReadOnlyList<string> UpdatedModules,
    IReadOnlyList<string> PreservedModules,
    IReadOnlyList<string> Failures)
{
    public static BundledModuleInstallResult Empty { get; } = new([], [], [], []);
}

public sealed class BundledModuleInstaller
{
    private const string HolyPaladinModuleId = "shigure-holy-paladin-virtue-12-1";
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> LegacyModuleIds =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [HolyPaladinModuleId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "烈日奶骑大秘境美德爆发-20260829"
            }
        };
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> UpgradeableHashes =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [HolyPaladinModuleId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                // 1.2.1.10 初始内置版本：仅 11 条规则，缺少圣疗术和神圣震击。
                "591a0616e6045de6a1f1fdd23f93ed964bb118c95e1ac50fd64bcc8024e7f4e8",
                // 同一内容仅补了文件末尾换行。
                "d941daf0aef0fb388edda2a8b266aec45faf1f155d59b2378a9e5076f49a8234",
                // 33 条完整规则的首个候选版本：尚未处理 AOE 优先级冲突。
                "3a42fd0b4f4f819d8eb8f79913e41862fde7502a0d103f02b1315f5ee99f2a11",
                // 1.2.1.10 完整优先级版本：清洁术只选择魔法减益，遗漏疾病与中毒。
                "95ec8854e404e7de0f3820b3d49e3ce2e6a5b6042eb8872c79bddb39319b1019",
                // 清洁术被普通治疗压低且运行时等待 GCD 的候选版本。
                "c7a3e7febb2e61903ffcd127b5039fef2c1f72ddd5ff57d939a2c0954f6e4749",
                // AOE 资源预留暂停会在全员满血时抢占进攻分支的候选版本。
                "2ba2982d36ac8b1c7a0e9f1901e0b4f03020aa60d65b22f888c9ef9f1f756184",
                // 满血进攻仍错误依赖治疗者当前目标为敌对单位的候选版本。
                "e764a5d6cd20cbd14d58fdb588380d3f01ca350f4a7a3206a69144db61ff342d",
                // AOE 高压时光环掌握和短失败退让会反复抢占直接治疗的候选版本。
                "14715d2bf5b16a65ba8bf284aab0959a3be72bd0274391793fd8b0405b288e40",
                // 1.2.1.11：光环掌握会消耗在双目标尾部缺口，轻伤治疗没有区分震击充能。
                "d85ab284b3ae5cecb0b8de11f84c30988cad362a96bcc8e424d02f5d08db36f1",
                // 1.2.1.12：微小血量缺口阻断进攻，吸奶盾等待阶段没有定时预铺美德。
                "8ea0a7863badd9997aac296710ce7db14cc9328a50be8ee46598245d2c52322a",
                // 旧 ID 的 1.2.1.9：全部规则带 900ms 逻辑暂停，且吸奶盾预留期会被反应式美德抢占。
                "1f2a37b5e49c62a5c744a81278d8d26310953a0447e9c920a595620e403d06c2",
                // 1.2.1.13：取消全局暂停后不同技能会覆盖待确认动作，且美德失败会锁死后续群疗链。
                "57eadc2bfee55118a2ff67bc6b8307d8bbe1a05bd85ae322a273c398902901bc",
                // 1.2.1.14：治疗吸收预警未匹配真实读条时，阶段 1 会阻断已经出现的真实团伤美德。
                "b4d14e76b08ea3f8c1688013293631c3db18e6cbf5f17b052a4b941244abf387",
                // 1.2.1.15：时间轴事件保护后桥接失效，输出尾部缺少存活敌人门槛，并遗漏脱战灌注转换。
                "e791ca0580f6b9774dc0e7d78a5ea7e785dcec0ef6c9e17f1f81cec768f8fcf9",
                // 1.2.1.16：姓名板敌人数硬门槛阻断输出，固定 250ms 重投递且灌注转换会重复读条。
                "2706dce7ccc714b7431da797f75f4446d1750c9a1ef38d00b979bcba4d009af7",
                // 1.2.1.17：多人真实群伤仍禁用黎明之光，群疗只能依赖鸣钟和逐个单抬。
                "b8e3a88901f469c7396f3ac6ce9406d79d062e92fd330a41332f1b0f6568ead6",
                // 1.2.1.18：美德失败后鸣钟和光环掌握仍会接管，爆发黎明之光也未绑定美德窗口。
                "cd6def85882ee7fceac6e7eaa0135c0dcf68f145c7d73261f2bd2750713a8aa1",
                // 1.2.1.19：吸奶盾缺少成功终态时美德迟到，神圣意志不驱动满血正义盾击。
                "510f9fc7e7726752ef3a98959742dd324401119e225d55e7311d1995bb86b329",
                // 1.2.1.20 旧优先级版本：灌注圣光闪现仍在第 13 位，且轻伤规则 26 仍使用神圣震击。
                "020abc91a5d7dec5cb4a6e194af0f1d2505eece9cca4159e94f862346937d367",
                // 1.2.1.20 已交付版本：健康队伍仍先空放黎明，吸奶盾资源预留到点不进入阶段 3。
                "ae998cdfb47303e7b5ebab27c6726a18838e3ce9748bc13ab8500b30a3090584",
                // 1.2.1.21 本地副本：健康规则未按团队轻伤门槛使用黎明之光。
                "296efdf7c9564016351ec2f2df8744259540629c0673f43e50ec65b9714959d1",
                // 1.2.1.21 仓库基线：鸣钟可用时仍错误保留 AOE 资源，脱战灌注转换未覆盖圣光闪现。
                "06fac28138cb0bf752c35e1e269a25998d07ce071a4bb5a28243f9903429460f"
            }
        };

    public BundledModuleInstallResult Install(string sourceDirectory, string targetDirectory)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return BundledModuleInstallResult.Empty;
        }

        Directory.CreateDirectory(targetDirectory);
        var installed = new List<string>();
        var updated = new List<string>();
        var preserved = new List<string>();
        var failures = new List<string>();
        var existingModules = LoadModules(targetDirectory);

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*.json", SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            try
            {
                var json = File.ReadAllText(sourcePath, Encoding.UTF8);
                var module = ModuleStore.Parse(Encoding.UTF8.GetBytes(json));
                if (string.IsNullOrWhiteSpace(module.Id))
                {
                    throw new InvalidDataException("内置模块缺少 ID。");
                }

                var existing = existingModules.FirstOrDefault(candidate =>
                    string.Equals(candidate.Module.Id, module.Id, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(candidate.Module.Name, module.Name, StringComparison.CurrentCultureIgnoreCase));
                if (existing is not null)
                {
                    if (CanUpgrade(module, existing))
                    {
                        Backup(targetDirectory, existing);
                        AtomicFile.WriteAllText(existing.FilePath, json,
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        existingModules.Remove(existing);
                        existingModules.Add(new InstalledModule(module, existing.FilePath, ComputeHash(existing.FilePath)));
                        updated.Add(module.Name);
                        continue;
                    }

                    preserved.Add(module.Name);
                    continue;
                }

                var targetPath = Path.Combine(targetDirectory, Path.GetFileName(sourcePath));
                if (File.Exists(targetPath))
                {
                    failures.Add($"{module.Name}: 目标文件已存在");
                    continue;
                }

                AtomicFile.WriteAllText(targetPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                existingModules.Add(new InstalledModule(module, targetPath, ComputeHash(targetPath)));
                installed.Add(module.Name);
            }
            catch (Exception exception)
            {
                failures.Add($"{Path.GetFileName(sourcePath)}: {exception.Message}");
            }
        }

        return new BundledModuleInstallResult(installed, updated, preserved, failures);
    }

    internal static bool IsKnownUpgradeableHash(string moduleId, string hash) =>
        UpgradeableHashes.TryGetValue(moduleId, out var hashes) && hashes.Contains(hash);

    internal static bool IsKnownUpgradeableModule(string sourceModuleId, string existingModuleId, string hash)
    {
        var identityMatches = string.Equals(sourceModuleId, existingModuleId, StringComparison.OrdinalIgnoreCase)
            || LegacyModuleIds.TryGetValue(sourceModuleId, out var legacyIds)
                && legacyIds.Contains(existingModuleId);
        return identityMatches && IsKnownUpgradeableHash(sourceModuleId, hash);
    }

    private static bool CanUpgrade(ModuleDefinition source, InstalledModule existing) =>
        IsKnownUpgradeableModule(source.Id, existing.Module.Id, existing.Hash);

    private static void Backup(string targetDirectory, InstalledModule existing)
    {
        var userDataDirectory = Directory.GetParent(Path.GetFullPath(targetDirectory))?.FullName
            ?? throw new InvalidOperationException("无法解析模块迁移备份目录。");
        var backupDirectory = Path.Combine(
            UserDataLayout.ResolveMigrationDirectory(userDataDirectory),
            "bundled-module-upgrades");
        Directory.CreateDirectory(backupDirectory);
        var backupName = $"{Path.GetFileNameWithoutExtension(existing.FilePath)}.{existing.Hash[..12]}.json";
        var backupPath = Path.Combine(backupDirectory, backupName);
        if (!File.Exists(backupPath))
        {
            AtomicFile.WriteAllText(
                backupPath,
                File.ReadAllText(existing.FilePath, Encoding.UTF8),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static List<InstalledModule> LoadModules(string directory)
    {
        var modules = new List<InstalledModule>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            try
            {
                modules.Add(new InstalledModule(
                    ModuleStore.Parse(File.ReadAllBytes(path)),
                    path,
                    ComputeHash(path)));
            }
            catch
            {
                // ModuleStore reports malformed user modules separately; they must not block built-in installation.
            }
        }

        return modules;
    }

    private static string ComputeHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed record InstalledModule(ModuleDefinition Module, string FilePath, string Hash);
}
