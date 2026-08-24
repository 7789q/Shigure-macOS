using Shigure.Platform;

namespace Shigure;

public sealed record ScreenScanResult(
    IReadOnlyDictionary<int, int>? RowData,
    IReadOnlyDictionary<int, int> BarData,
    IReadOnlyDictionary<int, int> HealAbsorbData,
    string? FailureReason)
{
    public TargetWindow? Target { get; init; }

    public ScreenScanTiming Timing { get; init; }
}

public readonly record struct ScreenScanTiming(
    TimeSpan Locate,
    TimeSpan Capture,
    TimeSpan Decode)
{
    public TimeSpan Total => Locate + Capture + Decode;
}

public interface IRuntimeScreenScanner
{
    ScreenScanResult ScanScreenData();
}
