using System.Diagnostics;

namespace Shigure.Platform;

public sealed class TargetProcessConfig
{
    public const string ProcessFileName = "wow_process.txt";
    private readonly object _cacheSync = new();
    private IReadOnlyList<string>? _cachedNames;
    private DateTime _cachedLastWriteTimeUtc;
    private long _cachedLength = -1;

    public TargetProcessConfig(string processFilePath)
    {
        ProcessFilePath = processFilePath;
    }

    public string ProcessFilePath { get; }

    public static TargetProcessConfig FromBaseDirectory(string baseDirectory)
    {
        return new TargetProcessConfig(Path.Combine(baseDirectory, ProcessFileName));
    }

    public IReadOnlyList<string> ReadProcessNames()
    {
        if (!TryReadFileStamp(out var lastWriteTimeUtc, out var length))
        {
            lock (_cacheSync)
            {
                _cachedNames = null;
                _cachedLength = -1;
            }

            return [];
        }

        lock (_cacheSync)
        {
            if (_cachedNames is not null
                && _cachedLastWriteTimeUtc == lastWriteTimeUtc
                && _cachedLength == length)
            {
                return _cachedNames;
            }
        }

        try
        {
            var names = File.ReadLines(ProcessFilePath)
                .Select(NormalizeProcessName)
                .Where(name => name is not null)
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            lock (_cacheSync)
            {
                _cachedNames = names;
                _cachedLastWriteTimeUtc = lastWriteTimeUtc;
                _cachedLength = length;
            }

            return names;
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public HashSet<int> FindCandidateProcessIds()
    {
        var result = new HashSet<int>();
        foreach (var processName in ReadProcessNames())
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        result.Add(process.Id);
                    }
                    catch (InvalidOperationException)
                    {
                        // The process may exit while candidates are enumerated.
                    }
                }
            }
        }

        return result;
    }

    public string DescribeConfiguredProcesses()
    {
        var names = ReadProcessNames();
        return names.Count == 0 ? "未配置" : string.Join("、", names);
    }

    private static string? NormalizeProcessName(string line)
    {
        var name = line.Trim();
        if (name.Length == 0 || name.StartsWith('#') || name.StartsWith(';'))
        {
            return null;
        }

        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? name[..^4].Trim()
            : name;
    }

    private bool TryReadFileStamp(out DateTime lastWriteTimeUtc, out long length)
    {
        try
        {
            var info = new FileInfo(ProcessFilePath);
            if (!info.Exists)
            {
                lastWriteTimeUtc = default;
                length = 0;
                return false;
            }

            lastWriteTimeUtc = info.LastWriteTimeUtc;
            length = info.Length;
            return true;
        }
        catch (IOException)
        {
            lastWriteTimeUtc = default;
            length = 0;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            lastWriteTimeUtc = default;
            length = 0;
            return false;
        }
    }
}
