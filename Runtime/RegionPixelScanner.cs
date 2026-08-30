using System.Diagnostics;
using Shigure.Platform;

namespace Shigure;

public sealed class RegionPixelScanner : IRuntimeScreenScanner, IDisposable
{
    private const int MainRowHeight = 1;
    private const int CountBarHeight = 2;
    private const int HealAbsorbMaxRows = 6;
    private const int HealAbsorbRowHeight = 2;
    private const int ProtocolBandHeight =
        MainRowHeight + CountBarHeight + HealAbsorbMaxRows * HealAbsorbRowHeight;
    private readonly ITargetWindowLocator _targetLocator;
    private readonly IScreenRegionCapturer _capturer;

    public RegionPixelScanner(
        ITargetWindowLocator targetLocator,
        IScreenRegionCapturer capturer)
    {
        _targetLocator = targetLocator;
        _capturer = capturer;
    }

    public ScreenScanResult ScanScreenData()
    {
        var locateStarted = Stopwatch.GetTimestamp();
        var target = _targetLocator.FindFrontmostTarget();
        var locateElapsed = Stopwatch.GetElapsedTime(locateStarted);
        if (target is null)
        {
            return WithTiming(
                Empty($"未找到目标进程的可见窗口（wow_process.txt: {_targetLocator.DescribeConfiguredProcesses()}）"),
                locateElapsed,
                TimeSpan.Zero,
                TimeSpan.Zero);
        }

        if (target.IsMinimized)
        {
            return WithTiming(
                Empty("最靠前的目标进程窗口已最小化", target),
                locateElapsed,
                TimeSpan.Zero,
                TimeSpan.Zero);
        }

        if (target.Bounds is not { IsValid: true } bounds)
        {
            return WithTiming(
                Empty("目标窗口客户区尺寸无效", target),
                locateElapsed,
                TimeSpan.Zero,
                TimeSpan.Zero);
        }

        var captureElapsed = TimeSpan.Zero;
        var decodeStarted = 0L;
        try
        {
            var bandHeight = Math.Min(ProtocolBandHeight, bounds.Height);
            var captureStarted = Stopwatch.GetTimestamp();
            bool captureSucceeded;
            ReadOnlyMemory<int> protocolBand;
            int protocolWidth;
            int protocolHeight;
            double protocolScaleY;
            string failureReason;
            try
            {
                captureSucceeded = TryCaptureProtocolPixels(
                    target.Identity,
                    new TargetBounds(bounds.X, bounds.Y, bounds.Width, bandHeight),
                    out protocolBand,
                    out protocolWidth,
                    out protocolHeight,
                    out protocolScaleY,
                    out failureReason);
            }
            finally
            {
                captureElapsed = Stopwatch.GetElapsedTime(captureStarted);
            }

            if (!captureSucceeded)
            {
                return WithTiming(
                    Empty($"顶部协议窄带{failureReason}", target),
                    locateElapsed,
                    captureElapsed,
                    TimeSpan.Zero);
            }

            decodeStarted = Stopwatch.GetTimestamp();
            var protocolPixels = protocolBand.Span;
            var (rowData, stateRowY) = DecodeBestTopRow(
                protocolPixels,
                protocolWidth,
                protocolHeight);
            var (barData, markerY) = DecodeBestCountBarsRow(
                protocolPixels,
                protocolWidth,
                protocolHeight,
                stateRowY);
            if (markerY is null)
            {
                return WithTiming(
                    Result(
                        rowData,
                        new Dictionary<int, int>(),
                        new Dictionary<int, int>(),
                        rowData.Count == 0
                            ? "未找到有效的状态像素起始标记"
                            : "未找到 CountBars 标记，层数条和治疗吸收数据未采集",
                        target),
                    locateElapsed,
                    captureElapsed,
                    Stopwatch.GetElapsedTime(decodeStarted));
            }

            var healAbsorbData = new Dictionary<int, int>();
            var direction = stateRowY is not null && markerY.Value < stateRowY.Value ? -1 : 1;
            var firstRowY = markerY.Value
                + direction * PhysicalOffset(CountBarHeight, protocolScaleY);
            for (var row = 0; row < HealAbsorbMaxRows; row++)
            {
                var rowY = firstRowY
                    + direction * PhysicalOffset(row * HealAbsorbRowHeight, protocolScaleY);
                if (rowY < 0 || rowY >= protocolHeight)
                {
                    break;
                }

                PixelProtocolDecoder.DecodeHealAbsorbRow(
                    protocolPixels.Slice(rowY * protocolWidth, protocolWidth),
                    row,
                    healAbsorbData);
            }
            return WithTiming(
                Result(
                    rowData,
                    barData,
                    healAbsorbData,
                    rowData.Count == 0 ? "未找到有效的状态像素起始标记" : null,
                    target),
                locateElapsed,
                captureElapsed,
                Stopwatch.GetElapsedTime(decodeStarted));
        }
        catch (Exception ex)
        {
            return WithTiming(
                Empty($"{ex.GetType().Name}: {ex.Message}", target),
                locateElapsed,
                captureElapsed,
                decodeStarted == 0 ? TimeSpan.Zero : Stopwatch.GetElapsedTime(decodeStarted));
        }
    }

    private bool TryCaptureProtocolPixels(
        TargetIdentity target,
        TargetBounds region,
        out ReadOnlyMemory<int> protocolPixels,
        out int protocolWidth,
        out int protocolHeight,
        out double protocolScaleY,
        out string failureReason)
    {
        protocolPixels = ReadOnlyMemory<int>.Empty;
        protocolWidth = 0;
        protocolHeight = 0;
        protocolScaleY = 0;
        var capture = _capturer is ITargetWindowRegionCapturer targetCapturer
            ? targetCapturer.Capture(target, region)
            : _capturer.Capture(region);
        if (!capture.Succeeded)
        {
            failureReason = $"捕获失败: {capture.FailureReason ?? capture.FailureKind.ToString()}";
            return false;
        }

        var frame = capture.Frame!;
        if (!TryReadPhysicalProtocolPixels(frame, region.Width, region.Height, out protocolPixels))
        {
            failureReason = "捕获帧的尺寸、缩放、像素格式或缓冲区无效";
            return false;
        }

        protocolWidth = frame.PixelWidth;
        protocolHeight = frame.PixelHeight;
        protocolScaleY = frame.ScaleY;
        failureReason = string.Empty;
        return true;
    }

    private static (Dictionary<int, int> Data, int? RowY) DecodeBestTopRow(
        ReadOnlySpan<int> pixels,
        int width,
        int height)
    {
        var bestData = new Dictionary<int, int>();
        int? bestRowY = null;
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * width, width);
            if (PixelProtocolDecoder.FindTopRowStart(row) < 0)
            {
                continue;
            }

            var candidate = PixelProtocolDecoder.DecodeTopRow(row);
            if (candidate.Count > bestData.Count)
            {
                bestData = candidate;
                bestRowY = y;
            }
        }

        return (bestData, bestRowY);
    }

    private static (Dictionary<int, int> Data, int? RowY) DecodeBestCountBarsRow(
        ReadOnlySpan<int> pixels,
        int width,
        int height,
        int? stateRowY)
    {
        var bestData = new Dictionary<int, int>();
        int? bestRowY = null;
        Span<int> marker = stackalloc int[1];
        for (var y = 0; y < height; y++)
        {
            marker[0] = pixels[y * width];
            if (PixelProtocolDecoder.FindCountBarsMarkerY(marker) is null)
            {
                continue;
            }

            var candidate = PixelProtocolDecoder.DecodeCountBars(
                pixels.Slice(y * width, width));
            if (bestRowY is null
                || candidate.Count > bestData.Count
                || candidate.Count == bestData.Count
                    && stateRowY is not null
                    && Math.Abs(y - stateRowY.Value) < Math.Abs(bestRowY.Value - stateRowY.Value))
            {
                bestData = candidate;
                bestRowY = y;
            }
        }

        return (bestData, bestRowY);
    }

    private static int PhysicalOffset(int logicalOffset, double scaleY) =>
        (int)Math.Round(logicalOffset * scaleY, MidpointRounding.AwayFromZero);

    private static bool TryReadPhysicalProtocolPixels(
        CapturedRegion frame,
        int logicalWidth,
        int logicalHeight,
        out ReadOnlyMemory<int> protocolPixels)
    {
        protocolPixels = ReadOnlyMemory<int>.Empty;
        if (logicalWidth <= 0 || logicalHeight <= 0
            || frame.PixelWidth <= 0 || frame.PixelHeight <= 0
            || frame.PixelFormat != CapturedPixelFormat.Argb32
            || frame.ColorSpace != CapturedColorSpace.Srgb
            || !IsConsistentScale(frame.ScaleX, frame.PixelWidth, logicalWidth)
            || !IsConsistentScale(frame.ScaleY, frame.PixelHeight, logicalHeight))
        {
            return false;
        }

        int physicalPixelCount;
        try
        {
            physicalPixelCount = checked(frame.PixelWidth * frame.PixelHeight);
        }
        catch (OverflowException)
        {
            return false;
        }

        var physicalPixels = frame.ArgbPixels.Span;
        if (physicalPixels.Length != physicalPixelCount)
        {
            return false;
        }

        protocolPixels = frame.ArgbPixels;

        return true;
    }

    private static bool IsConsistentScale(double scale, int physicalSize, int logicalSize)
    {
        if (!double.IsFinite(scale) || scale < 1)
        {
            return false;
        }

        var expected = (double)physicalSize / logicalSize;
        return Math.Abs(scale - expected) <= 1e-9 * Math.Max(1, expected);
    }

    private static ScreenScanResult Result(
        IReadOnlyDictionary<int, int> rowData,
        IReadOnlyDictionary<int, int> barData,
        IReadOnlyDictionary<int, int> healAbsorbData,
        string? failureReason,
        TargetWindow target)
    {
        var detailedFailure = failureReason is null
            ? null
            : $"{failureReason}（状态字段 {rowData.Count}，CountBars {barData.Count}，治疗吸收 {healAbsorbData.Count}）";
        return new(rowData.Count == 0 ? null : rowData, barData, healAbsorbData, detailedFailure)
        {
            Target = target
        };
    }

    private static ScreenScanResult Empty(string failureReason, TargetWindow? target = null) =>
        new(null, new Dictionary<int, int>(), new Dictionary<int, int>(), failureReason)
        {
            Target = target
        };

    private static ScreenScanResult WithTiming(
        ScreenScanResult result,
        TimeSpan locate,
        TimeSpan capture,
        TimeSpan decode) =>
        result with { Timing = new ScreenScanTiming(locate, capture, decode) };

    public void Dispose()
    {
        if (_capturer is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
