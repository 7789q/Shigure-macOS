namespace Shigure.Platform;

public static class TargetPlatforms
{
    public const string Windows = "windows";
    public const string MacOS = "macos";
}

public readonly record struct TargetIdentity(string Platform, int ProcessId, long WindowId)
{
    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Platform) && ProcessId > 0 && WindowId > 0;
}

public readonly record struct TargetBounds(int X, int Y, int Width, int Height)
{
    public bool IsValid => Width > 0 && Height > 0;
}

public sealed record TargetWindow(
    TargetIdentity Identity,
    string? ProcessPath,
    TargetBounds? Bounds,
    bool IsMinimized = false);

public interface ITargetWindowLocator
{
    TargetWindow? FindFrontmostTarget();

    string DescribeConfiguredProcesses();
}
