using System.Collections.Concurrent;

namespace Shigure.MacUI;

internal sealed class RuntimeUiUpdateGuard
{
    private readonly string _logPath;
    private readonly ConcurrentDictionary<string, byte> _disabledSources = new(StringComparer.Ordinal);

    public RuntimeUiUpdateGuard(string logPath)
    {
        _logPath = Path.GetFullPath(logPath);
    }

    public bool IsDisabled(string source) => _disabledSources.ContainsKey(source);

    public bool TryRun(string source, Action update)
    {
        if (IsDisabled(source))
        {
            return false;
        }

        try
        {
            update();
            return true;
        }
        catch (Exception exception)
        {
            _disabledSources.TryAdd(source, 0);
            TryWriteFailure(source, exception);
            return false;
        }
    }

    private void TryWriteFailure(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(
                _logPath,
                $"[{DateTimeOffset.Now:O}] {source}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
            // 诊断写入失败不能再次影响 UI 主线程。
        }
    }
}
