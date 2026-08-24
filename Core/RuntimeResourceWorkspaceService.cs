using System.Security.Cryptography;
using System.Text.Json;

namespace Shigure;

public sealed class RuntimeResourceWorkspaceService
{
    public const int ManifestFormatVersion = 1;
    public const string ManifestFileName = "runtime-resources-v1.json";
    public const string LockFileName = "runtime-resources-v1.lock";

    private const long MaximumManifestBytes = 4 * 1024 * 1024;
    private static readonly string[] ManagedDirectories = ["Fuyutsui", "config", "keymap"];
    private static readonly string[] ManagedFiles = ["wow_process.txt"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public RuntimeResourceWorkspaceResult Initialize(
        string sourceBaseDirectory,
        string targetUserDataDirectory)
    {
        var sourceRoot = RequireFullPath(sourceBaseDirectory, nameof(sourceBaseDirectory));
        var userDataRoot = RequireFullPath(targetUserDataDirectory, nameof(targetUserDataDirectory));
        var workspaceRoot = UserDataLayout.ResolveRuntimeDirectory(userDataRoot);
        if (PathsOverlap(sourceRoot, workspaceRoot))
        {
            throw new InvalidOperationException("运行资源源目录与工作目录不能重叠。");
        }

        var migrationRoot = UserDataLayout.ResolveMigrationDirectory(userDataRoot);
        Directory.CreateDirectory(userDataRoot);
        RejectLink(userDataRoot);
        Directory.CreateDirectory(migrationRoot);
        RejectLink(migrationRoot);
        Directory.CreateDirectory(workspaceRoot);
        RejectLink(workspaceRoot);
        var lockPath = Path.Combine(migrationRoot, LockFileName);
        if (File.Exists(lockPath))
        {
            RejectLink(lockPath);
        }

        using var workspaceLock = new FileStream(
            lockPath,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);

        var sourceFiles = EnumerateManagedFiles(sourceRoot);
        var manifestPath = Path.Combine(migrationRoot, ManifestFileName);
        var previousManifest = LoadManifest(manifestPath);
        var currentHashes = sourceFiles.ToDictionary(
            entry => entry.RelativePath,
            entry => ComputeSha256(entry.SourcePath),
            StringComparer.Ordinal);
        var created = new List<string>();
        var updated = new List<string>();
        var skipped = new List<string>();
        var conflicts = new List<string>();

        foreach (var sourceFile in sourceFiles)
        {
            var relativePath = sourceFile.RelativePath;
            var sourceHash = currentHashes[relativePath];
            var targetPath = ResolveTargetPath(workspaceRoot, relativePath);
            EnsureTargetPathSafe(workspaceRoot, targetPath);
            if (!File.Exists(targetPath))
            {
                CopyAtomic(sourceFile.SourcePath, workspaceRoot, targetPath);
                created.Add(relativePath);
                continue;
            }

            var targetHash = ComputeSha256(targetPath);
            if (string.Equals(targetHash, sourceHash, StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(relativePath);
                continue;
            }

            if (previousManifest?.Files.TryGetValue(relativePath, out var previousHash) == true
                && string.Equals(targetHash, previousHash, StringComparison.OrdinalIgnoreCase))
            {
                CopyAtomic(sourceFile.SourcePath, workspaceRoot, targetPath);
                updated.Add(relativePath);
                continue;
            }

            conflicts.Add(relativePath);
        }

        var manifest = new RuntimeResourceManifest
        {
            FormatVersion = ManifestFormatVersion,
            Files = currentHashes
        };
        WriteAtomic(
            manifestPath,
            JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));

        return new RuntimeResourceWorkspaceResult(
            sourceRoot,
            workspaceRoot,
            manifestPath,
            created,
            updated,
            skipped,
            conflicts);
    }

    private static IReadOnlyList<ManagedSourceFile> EnumerateManagedFiles(string sourceRoot)
    {
        var files = new List<ManagedSourceFile>();
        foreach (var directoryName in ManagedDirectories)
        {
            var directoryPath = Path.Combine(sourceRoot, directoryName);
            if (!Directory.Exists(directoryPath))
            {
                throw new DirectoryNotFoundException($"运行资源目录不存在：{directoryName}");
            }

            EnumerateDirectory(sourceRoot, directoryPath, files);
        }

        foreach (var fileName in ManagedFiles)
        {
            var sourcePath = Path.Combine(sourceRoot, fileName);
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("运行资源文件不存在。", sourcePath);
            }

            RejectLink(sourcePath);
            files.Add(new ManagedSourceFile(fileName, sourcePath));
        }

        return files
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static void EnumerateDirectory(
        string sourceRoot,
        string directoryPath,
        ICollection<ManagedSourceFile> files)
    {
        RejectLink(directoryPath);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directoryPath).Order(StringComparer.Ordinal))
        {
            RejectLink(entry);
            if (Directory.Exists(entry))
            {
                EnumerateDirectory(sourceRoot, entry, files);
                continue;
            }

            if (!File.Exists(entry))
            {
                throw new InvalidDataException($"不支持的运行资源条目：{entry}");
            }

            var relativePath = NormalizeRelativePath(Path.GetRelativePath(sourceRoot, entry));
            files.Add(new ManagedSourceFile(relativePath, entry));
        }
    }

    private static RuntimeResourceManifest? LoadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        if (new FileInfo(manifestPath).Length > MaximumManifestBytes)
        {
            throw new InvalidDataException("运行资源 manifest 超过大小限制。");
        }

        RuntimeResourceManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RuntimeResourceManifest>(
                File.ReadAllBytes(manifestPath),
                JsonOptions) ?? throw new InvalidDataException("运行资源 manifest 为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("运行资源 manifest 不是有效 JSON。", exception);
        }

        if (manifest.FormatVersion != ManifestFormatVersion || manifest.Files is null)
        {
            throw new InvalidDataException("运行资源 manifest 版本不受支持。");
        }

        foreach (var (relativePath, hash) in manifest.Files)
        {
            if (!IsManagedRelativePath(relativePath) || !IsSha256(hash))
            {
                throw new InvalidDataException("运行资源 manifest 包含无效条目。");
            }
        }

        return manifest;
    }

    private static string ResolveTargetPath(string workspaceRoot, string relativePath)
    {
        if (!IsManagedRelativePath(relativePath))
        {
            throw new InvalidDataException("运行资源相对路径不在允许范围内。");
        }

        var targetPath = Path.GetFullPath(Path.Combine(
            workspaceRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsPathWithin(targetPath, workspaceRoot))
        {
            throw new InvalidDataException("运行资源目标路径越界。");
        }

        return targetPath;
    }

    private static bool IsManagedRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || Path.IsPathRooted(relativePath)
            || relativePath.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (ManagedFiles.Contains(relativePath, StringComparer.Ordinal))
        {
            return true;
        }

        return ManagedDirectories.Any(directory =>
            relativePath.StartsWith(directory + "/", StringComparison.Ordinal));
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        if (!IsManagedRelativePath(normalized))
        {
            throw new InvalidDataException("运行资源源路径不在允许范围内。");
        }

        return normalized;
    }

    private static void CopyAtomic(string sourcePath, string workspaceRoot, string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath)!;
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(sourcePath, tempPath, overwrite: false);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void WriteAtomic(string targetPath, byte[] contents)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("无法确定运行资源 manifest 目录。");
        Directory.CreateDirectory(directory);
        RejectLink(directory);
        if (File.Exists(targetPath))
        {
            RejectLink(targetPath);
        }

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(tempPath, contents);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void EnsureTargetPathSafe(string workspaceRoot, string targetPath)
    {
        var relativePath = Path.GetRelativePath(workspaceRoot, targetPath);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = workspaceRoot;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = Path.Combine(current, segments[index]);
            if (File.Exists(current) && !Directory.Exists(current))
            {
                throw new InvalidDataException($"运行资源目标目录被文件占用：{current}");
            }

            Directory.CreateDirectory(current);
            RejectLink(current);
        }

        if (File.Exists(targetPath) || Directory.Exists(targetPath))
        {
            RejectLink(targetPath);
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"运行资源不允许符号链接：{path}");
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

    private static bool PathsOverlap(string first, string second) =>
        IsPathWithin(first, second) || IsPathWithin(second, first);

    private static bool IsPathWithin(string path, string root)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(fullPath, fullRoot, StringComparison.Ordinal)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));

    private sealed record ManagedSourceFile(string RelativePath, string SourcePath);

    private sealed class RuntimeResourceManifest
    {
        public int FormatVersion { get; init; }
        public Dictionary<string, string> Files { get; init; } = new(StringComparer.Ordinal);
    }
}

public sealed record RuntimeResourceWorkspaceResult(
    string SourceDirectory,
    string WorkspaceDirectory,
    string ManifestPath,
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> UpdatedFiles,
    IReadOnlyList<string> SkippedFiles,
    IReadOnlyList<string> ConflictingFiles);
