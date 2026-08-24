using System.Text;

namespace Shigure;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidOperationException($"无法确定文件目录: {path}");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, contents, encoding);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
