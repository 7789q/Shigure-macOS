using System.Security.Cryptography;
using Shigure.Platform;

namespace Shigure;

/// <summary>
/// 将程序目录中的 Fuyutsui 单向部署到当前目标游戏。项目目录始终是权威源。
/// </summary>
public sealed class FuyutsuiAddonSyncService
{
    private const string AddonDirectoryName = "Fuyutsui";
    private readonly string _sourceRoot;
    private readonly ITargetWindowLocator _targetLocator;
    private readonly Action<string, string, bool> _copyFile;

    public FuyutsuiAddonSyncService(string sourceRoot, ITargetWindowLocator targetLocator)
        : this(sourceRoot, targetLocator, static (source, target, overwrite) =>
            File.Copy(source, target, overwrite))
    {
    }

    internal FuyutsuiAddonSyncService(
        string sourceRoot,
        ITargetWindowLocator targetLocator,
        Action<string, string, bool> copyFile)
    {
        _sourceRoot = Path.GetFullPath(sourceRoot);
        _targetLocator = targetLocator;
        _copyFile = copyFile;
    }

    public string SourceRoot => _sourceRoot;

    public FuyutsuiAddonSyncResult SynchronizeAll()
    {
        if (!Directory.Exists(_sourceRoot))
        {
            throw new DirectoryNotFoundException($"找不到项目 Fuyutsui 目录: {_sourceRoot}");
        }

        var targetRoot = ResolveTargetRoot();
        if (targetRoot is null)
        {
            return FuyutsuiAddonSyncResult.TargetNotFound(_sourceRoot);
        }

        var copied = new List<string>();
        var skipped = new List<string>();
        var failures = new List<FuyutsuiAddonSyncFailure>();
        foreach (var sourcePath in Directory.EnumerateFiles(_sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(_sourceRoot, sourcePath);
            SynchronizeCore(sourcePath, relativePath, targetRoot, copied, skipped, failures);
        }

        return new FuyutsuiAddonSyncResult(_sourceRoot, targetRoot, copied, skipped, failures, null);
    }

    public FuyutsuiAddonSyncResult SynchronizeFile(string sourcePath)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var relativePath = Path.GetRelativePath(_sourceRoot, fullSourcePath);
        if (Path.IsPathRooted(relativePath)
            || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"待同步文件不在项目 Fuyutsui 目录内: {fullSourcePath}");
        }

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException("找不到待同步的项目插件文件。", fullSourcePath);
        }

        var targetRoot = ResolveTargetRoot();
        if (targetRoot is null)
        {
            return FuyutsuiAddonSyncResult.TargetNotFound(_sourceRoot);
        }

        var copied = new List<string>();
        var skipped = new List<string>();
        var failures = new List<FuyutsuiAddonSyncFailure>();
        SynchronizeCore(fullSourcePath, relativePath, targetRoot, copied, skipped, failures);
        return new FuyutsuiAddonSyncResult(_sourceRoot, targetRoot, copied, skipped, failures, null);
    }

    private string? ResolveTargetRoot()
    {
        var addOnsDirectory = WowAddonLocator.FindAddOnsDirectory(_targetLocator);
        return string.IsNullOrWhiteSpace(addOnsDirectory)
            ? null
            : Path.Combine(addOnsDirectory, AddonDirectoryName);
    }

    private void SynchronizeCore(
        string sourcePath,
        string relativePath,
        string targetRoot,
        ICollection<string> copied,
        ICollection<string> skipped,
        ICollection<FuyutsuiAddonSyncFailure> failures)
    {
        try
        {
            var targetPath = Path.Combine(targetRoot, relativePath);
            if (File.Exists(targetPath) && FilesHaveSameHash(sourcePath, targetPath))
            {
                skipped.Add(relativePath);
                return;
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            _copyFile(sourcePath, targetPath, true);
            copied.Add(relativePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            failures.Add(new FuyutsuiAddonSyncFailure(relativePath, ex.Message));
        }
    }

    private static bool FilesHaveSameHash(string firstPath, string secondPath)
    {
        using var first = File.OpenRead(firstPath);
        using var second = File.OpenRead(secondPath);
        var firstHash = SHA256.HashData(first);
        var secondHash = SHA256.HashData(second);
        return firstHash.AsSpan().SequenceEqual(secondHash);
    }
}

public sealed record FuyutsuiAddonSyncFailure(string RelativePath, string Message);

public sealed record FuyutsuiAddonSyncResult(
    string SourceRoot,
    string? TargetRoot,
    IReadOnlyList<string> CopiedFiles,
    IReadOnlyList<string> SkippedFiles,
    IReadOnlyList<FuyutsuiAddonSyncFailure> Failures,
    string? SkippedReason)
{
    public bool TargetFound => TargetRoot is not null;
    public bool CompletedSuccessfully => TargetFound && Failures.Count == 0;

    public static FuyutsuiAddonSyncResult TargetNotFound(string sourceRoot) => new(
        sourceRoot,
        null,
        [],
        [],
        [],
        "未找到目标游戏进程，已跳过游戏插件同步。");

    public static FuyutsuiAddonSyncResult Skipped(string sourceRoot, string reason) => new(
        sourceRoot,
        null,
        [],
        [],
        [],
        reason);
}
