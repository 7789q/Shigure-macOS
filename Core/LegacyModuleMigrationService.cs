using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shigure;

public sealed class LegacyModuleMigrationService
{
    public const int MarkerFormatVersion = 1;
    public const string MarkerFileName = "legacy-modules-v1.json";

    private static readonly JsonSerializerOptions MarkerJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public LegacyModuleMigrationResult Migrate(string legacyDataDirectory, string targetUserDataDirectory)
    {
        var sourceDataRoot = RequireFullPath(legacyDataDirectory, nameof(legacyDataDirectory));
        var targetDataRoot = RequireFullPath(targetUserDataDirectory, nameof(targetUserDataDirectory));
        var sourceModuleRoot = UserDataLayout.ResolveModuleDirectory(sourceDataRoot);
        var targetModuleRoot = UserDataLayout.ResolveModuleDirectory(targetDataRoot);
        var markerPath = Path.Combine(
            UserDataLayout.ResolveMigrationDirectory(targetDataRoot),
            MarkerFileName);

        if (!Directory.Exists(sourceModuleRoot))
        {
            return LegacyModuleMigrationResult.Skipped(
                sourceDataRoot,
                targetDataRoot,
                markerPath,
                "旧数据目录中没有 module 目录，已跳过迁移。");
        }

        var failures = new List<LegacyModuleMigrationFailure>();
        var marker = LoadMarker(markerPath, sourceDataRoot, failures);
        if (failures.Count > 0)
        {
            return BuildResult(sourceDataRoot, targetDataRoot, markerPath, failures: failures);
        }

        if (marker?.Completed == true)
        {
            return BuildResult(
                sourceDataRoot,
                targetDataRoot,
                markerPath,
                alreadyCompleted: true);
        }

        marker ??= LegacyModuleMigrationMarker.Create(sourceDataRoot);
        ResolvePendingFiles(marker, sourceModuleRoot, targetModuleRoot, failures);
        if (failures.Count > 0)
        {
            return BuildResult(sourceDataRoot, targetDataRoot, markerPath, failures: failures);
        }

        string[] sourceFiles;
        try
        {
            sourceFiles = Directory
                .EnumerateFiles(sourceModuleRoot, "*.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failures.Add(new LegacyModuleMigrationFailure(
                UserDataLayout.ModuleDirectoryName,
                LegacyModuleMigrationFailureKind.IoFailure,
                ex.Message));
            return BuildResult(sourceDataRoot, targetDataRoot, markerPath, failures: failures);
        }

        var candidates = new List<MigrationCandidate>();
        var preserved = new List<string>();
        foreach (var sourcePath in sourceFiles)
        {
            var relativePath = Path.GetRelativePath(sourceModuleRoot, sourcePath);
            if (!TryValidateJsonObject(sourcePath, out var sourceError))
            {
                failures.Add(new LegacyModuleMigrationFailure(
                    relativePath,
                    LegacyModuleMigrationFailureKind.InvalidSourceFile,
                    sourceError));
                continue;
            }

            var targetPath = ResolveRelativePath(targetModuleRoot, relativePath);
            if (!File.Exists(targetPath))
            {
                candidates.Add(new MigrationCandidate(sourcePath, targetPath, relativePath));
                continue;
            }

            if (!TryValidateJsonObject(targetPath, out var targetError))
            {
                failures.Add(new LegacyModuleMigrationFailure(
                    relativePath,
                    LegacyModuleMigrationFailureKind.InvalidTargetFile,
                    $"目标文件已存在但不是有效的 JSON 对象，已保留原文件：{targetError}"));
                continue;
            }

            preserved.Add(relativePath);
        }

        if (failures.Count > 0)
        {
            return BuildResult(
                sourceDataRoot,
                targetDataRoot,
                markerPath,
                preservedFiles: preserved,
                failures: failures);
        }

        var copied = new List<string>();
        foreach (var candidate in candidates)
        {
            if (!TryCopyCandidate(candidate, marker, markerPath, out var copyError))
            {
                failures.Add(new LegacyModuleMigrationFailure(
                    candidate.RelativePath,
                    LegacyModuleMigrationFailureKind.IoFailure,
                    copyError));
                break;
            }

            copied.Add(candidate.RelativePath);
        }

        if (failures.Count == 0)
        {
            marker.Completed = true;
            if (!TryWriteMarker(markerPath, marker, out var markerError))
            {
                failures.Add(new LegacyModuleMigrationFailure(
                    MarkerFileName,
                    LegacyModuleMigrationFailureKind.IoFailure,
                    markerError));
            }
        }

        return BuildResult(
            sourceDataRoot,
            targetDataRoot,
            markerPath,
            copied,
            preserved,
            failures);
    }

    private static LegacyModuleMigrationMarker? LoadMarker(
        string markerPath,
        string sourceDataRoot,
        ICollection<LegacyModuleMigrationFailure> failures)
    {
        if (!File.Exists(markerPath))
        {
            return null;
        }

        try
        {
            var marker = JsonSerializer.Deserialize<LegacyModuleMigrationMarker>(
                File.ReadAllText(markerPath),
                MarkerJsonOptions);
            if (marker is null
                || marker.FormatVersion != MarkerFormatVersion
                || string.IsNullOrWhiteSpace(marker.SourceDataDirectory)
                || marker.CreatedFiles is null
                || marker.PendingFiles is null
                || marker.CreatedFiles.Any(path => !IsSafeRelativePath(path))
                || marker.PendingFiles.Any(path => !IsSafeRelativePath(path))
                || (marker.Completed && marker.PendingFiles.Count > 0))
            {
                throw new InvalidDataException("迁移标记格式无效或版本不受支持。");
            }

            if (!PathsEqual(marker.SourceDataDirectory, sourceDataRoot))
            {
                throw new InvalidDataException("迁移标记属于另一个旧数据目录，已拒绝混合来源。");
            }

            if (marker.CreatedFiles.Intersect(marker.PendingFiles, StringComparer.Ordinal).Any())
            {
                throw new InvalidDataException("迁移标记的 createdFiles 与 pendingFiles 不能重叠。");
            }

            marker.CreatedFiles = marker.CreatedFiles
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            marker.PendingFiles = marker.PendingFiles
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();
            return marker;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            failures.Add(new LegacyModuleMigrationFailure(
                MarkerFileName,
                LegacyModuleMigrationFailureKind.InvalidMarker,
                ex.Message));
            return null;
        }
    }

    private static void ResolvePendingFiles(
        LegacyModuleMigrationMarker marker,
        string sourceModuleRoot,
        string targetModuleRoot,
        ICollection<LegacyModuleMigrationFailure> failures)
    {
        foreach (var relativePath in marker.PendingFiles.ToArray())
        {
            try
            {
                var sourcePath = ResolveRelativePath(sourceModuleRoot, relativePath);
                var targetPath = ResolveRelativePath(targetModuleRoot, relativePath);
                if (!File.Exists(targetPath))
                {
                    marker.PendingFiles.Remove(relativePath);
                    continue;
                }

                if (!File.Exists(sourcePath) || !FilesHaveSameHash(sourcePath, targetPath))
                {
                    failures.Add(new LegacyModuleMigrationFailure(
                        relativePath,
                        LegacyModuleMigrationFailureKind.PendingFileConflict,
                        "未完成迁移的目标文件与旧数据源不一致，已停止续跑。"));
                    continue;
                }

                marker.PendingFiles.Remove(relativePath);
                AddSorted(marker.CreatedFiles, relativePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                failures.Add(new LegacyModuleMigrationFailure(
                    relativePath,
                    LegacyModuleMigrationFailureKind.IoFailure,
                    ex.Message));
            }
        }
    }

    private static bool TryCopyCandidate(
        MigrationCandidate candidate,
        LegacyModuleMigrationMarker marker,
        string markerPath,
        out string error)
    {
        string? tempPath = null;
        try
        {
            var targetDirectory = Path.GetDirectoryName(candidate.TargetPath)
                ?? throw new InvalidOperationException("无法确定目标模块目录。");
            Directory.CreateDirectory(targetDirectory);
            tempPath = Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(candidate.TargetPath)}.{Guid.NewGuid():N}.tmp");
            File.Copy(candidate.SourcePath, tempPath, overwrite: false);

            AddSorted(marker.PendingFiles, candidate.RelativePath);
            if (!TryWriteMarker(markerPath, marker, out error))
            {
                marker.PendingFiles.Remove(candidate.RelativePath);
                return false;
            }

            File.Move(tempPath, candidate.TargetPath, overwrite: false);
            tempPath = null;
            marker.PendingFiles.Remove(candidate.RelativePath);
            AddSorted(marker.CreatedFiles, candidate.RelativePath);
            if (!TryWriteMarker(markerPath, marker, out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            marker.PendingFiles.Remove(candidate.RelativePath);
            error = ex.Message;
            return false;
        }
        finally
        {
            DeleteTemporaryFile(tempPath);
        }
    }

    private static bool TryWriteMarker(
        string markerPath,
        LegacyModuleMigrationMarker marker,
        out string error)
    {
        string? tempPath = null;
        try
        {
            var markerDirectory = Path.GetDirectoryName(markerPath)
                ?? throw new InvalidOperationException("无法确定迁移标记目录。");
            Directory.CreateDirectory(markerDirectory);
            tempPath = Path.Combine(markerDirectory, $".{MarkerFileName}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(
                tempPath,
                JsonSerializer.Serialize(marker, MarkerJsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, markerPath, overwrite: true);
            tempPath = null;
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            DeleteTemporaryFile(tempPath);
        }
    }

    private static bool TryValidateJsonObject(string path, out string error)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "根节点必须是 JSON 对象。";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool FilesHaveSameHash(string firstPath, string secondPath)
    {
        using var first = File.OpenRead(firstPath);
        using var second = File.OpenRead(secondPath);
        return SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second));
    }

    private static string ResolveRelativePath(string root, string relativePath)
    {
        if (!IsSafeRelativePath(relativePath))
        {
            throw new InvalidDataException($"不安全的模块相对路径：{relativePath}");
        }

        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException($"模块相对路径逃出目标目录：{relativePath}");
        }

        return fullPath;
    }

    private static bool IsSafeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
        {
            return false;
        }

        return !path
            .Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("..", StringComparison.Ordinal));
    }

    private static void DeleteTemporaryFile(string? tempPath)
    {
        if (tempPath is null)
        {
            return;
        }

        try
        {
            File.Delete(tempPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 目标文件从不使用 .tmp；残留临时文件不会被 ModuleStore 加载。
        }
    }

    private static string RequireFullPath(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("目录不能为空。", parameterName);
        }

        return Path.GetFullPath(path);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void AddSorted(ICollection<string> paths, string path)
    {
        if (!paths.Contains(path, StringComparer.Ordinal))
        {
            paths.Add(path);
        }

        if (paths is List<string> list)
        {
            list.Sort(StringComparer.Ordinal);
        }
    }

    private static LegacyModuleMigrationResult BuildResult(
        string sourceDataRoot,
        string targetDataRoot,
        string markerPath,
        IReadOnlyList<string>? copiedFiles = null,
        IReadOnlyList<string>? preservedFiles = null,
        IReadOnlyList<LegacyModuleMigrationFailure>? failures = null,
        bool alreadyCompleted = false) =>
        new(
            sourceDataRoot,
            targetDataRoot,
            markerPath,
            copiedFiles ?? [],
            preservedFiles ?? [],
            failures ?? [],
            alreadyCompleted,
            null);

    private sealed record MigrationCandidate(string SourcePath, string TargetPath, string RelativePath);
}

public enum LegacyModuleMigrationFailureKind
{
    InvalidSourceFile,
    InvalidTargetFile,
    InvalidMarker,
    PendingFileConflict,
    IoFailure
}

public sealed record LegacyModuleMigrationFailure(
    string RelativePath,
    LegacyModuleMigrationFailureKind Kind,
    string Message);

public sealed record LegacyModuleMigrationResult(
    string SourceDataDirectory,
    string TargetDataDirectory,
    string MarkerPath,
    IReadOnlyList<string> CopiedFiles,
    IReadOnlyList<string> PreservedFiles,
    IReadOnlyList<LegacyModuleMigrationFailure> Failures,
    bool AlreadyCompleted,
    string? SkippedReason)
{
    public bool CompletedSuccessfully =>
        Failures.Count == 0 && SkippedReason is null;

    public static LegacyModuleMigrationResult Skipped(
        string sourceDataDirectory,
        string targetDataDirectory,
        string markerPath,
        string reason) =>
        new(sourceDataDirectory, targetDataDirectory, markerPath, [], [], [], false, reason);
}

internal sealed class LegacyModuleMigrationMarker
{
    public int FormatVersion { get; set; }
    public string SourceDataDirectory { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public List<string> CreatedFiles { get; set; } = [];
    public List<string> PendingFiles { get; set; } = [];

    public static LegacyModuleMigrationMarker Create(string sourceDataDirectory) => new()
    {
        FormatVersion = LegacyModuleMigrationService.MarkerFormatVersion,
        SourceDataDirectory = sourceDataDirectory
    };
}
