namespace Shigure.Platform;

public enum CapturedPixelFormat
{
    Argb32
}

public enum CapturedColorSpace
{
    Srgb
}

public enum ScreenCaptureFailureKind
{
    None,
    InvalidRegion,
    PermissionDenied,
    CaptureUnavailable,
    InvalidPixelBuffer
}

public sealed record CapturedRegion(
    int PixelWidth,
    int PixelHeight,
    double ScaleX,
    double ScaleY,
    CapturedPixelFormat PixelFormat,
    CapturedColorSpace ColorSpace,
    ReadOnlyMemory<int> ArgbPixels);

public readonly record struct ScreenCaptureResult(
    CapturedRegion? Frame,
    ScreenCaptureFailureKind FailureKind,
    string? FailureReason)
{
    public bool Succeeded => Frame is not null && FailureKind == ScreenCaptureFailureKind.None;

    public static ScreenCaptureResult Success(CapturedRegion frame) =>
        new(frame, ScreenCaptureFailureKind.None, null);

    public static ScreenCaptureResult Failure(ScreenCaptureFailureKind kind, string reason) =>
        new(null, kind, reason);
}

public interface IScreenRegionCapturer
{
    ScreenCaptureResult Capture(TargetBounds region);
}

public interface ITargetWindowRegionCapturer : IScreenRegionCapturer
{
    ScreenCaptureResult Capture(TargetIdentity target, TargetBounds region);
}
