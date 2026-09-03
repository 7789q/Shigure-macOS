using System.Text;
using Shigure.Presentation;

namespace Shigure.MacUI;

internal sealed class LocalRuntimeLogStore
{
    internal const long MaximumFileBytes = 8 * 1024 * 1024;
    internal const int MaximumArchives = 3;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _sync = new();
    private readonly string _path;

    public LocalRuntimeLogStore(string path)
    {
        _path = System.IO.Path.GetFullPath(path);
    }

    public string Path => _path;

    public void Append(RuntimeLogEntry entry)
    {
        var line = $"[{entry.Timestamp:O}] {entry.Message}{Environment.NewLine}";
        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                RotateIfNeeded(Utf8.GetByteCount(line));
                File.AppendAllText(_path, line, Utf8);
            }
        }
        catch
        {
            // Diagnostics must never interrupt the runtime or UI thread.
        }
    }

    private void RotateIfNeeded(int incomingBytes)
    {
        if (!File.Exists(_path)
            || new FileInfo(_path).Length + incomingBytes <= MaximumFileBytes)
        {
            return;
        }

        for (var index = MaximumArchives - 1; index >= 1; index--)
        {
            var source = $"{_path}.{index}";
            var destination = $"{_path}.{index + 1}";
            if (File.Exists(source))
            {
                File.Move(source, destination, overwrite: true);
            }
        }

        File.Move(_path, $"{_path}.1", overwrite: true);
    }
}
