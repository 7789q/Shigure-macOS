using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Shigure.Platform;

namespace Shigure.Platform.MacOS;

public sealed class MacScreenCapturer : ITargetWindowRegionCapturer, IDisposable
{
    private readonly IPlatformPermissionService _permissionService;
    private readonly IMacScreenCaptureBackend _backend;

    [SupportedOSPlatform("macos")]
    public MacScreenCapturer(IPlatformPermissionService permissionService)
        : this(permissionService, new MacCoreGraphicsCaptureBackend())
    {
    }

    internal MacScreenCapturer(
        IPlatformPermissionService permissionService,
        IMacScreenCaptureBackend backend)
    {
        _permissionService = permissionService;
        _backend = backend;
    }

    public ScreenCaptureResult Capture(TargetBounds region) => CaptureCore(region, null);

    public ScreenCaptureResult Capture(TargetIdentity target, TargetBounds region)
    {
        if (!target.IsValid
            || target.Platform != TargetPlatforms.MacOS
            || target.WindowId > uint.MaxValue)
        {
            return Fail(ScreenCaptureFailureKind.InvalidRegion, "目标窗口标识无效");
        }

        return CaptureCore(region, (uint)target.WindowId);
    }

    private ScreenCaptureResult CaptureCore(TargetBounds region, uint? windowId)
    {
        if (!region.IsValid)
        {
            return Fail(ScreenCaptureFailureKind.InvalidRegion, "捕获区域尺寸无效");
        }

        if (!_permissionService.Check().ScreenCapture.IsReady)
        {
            return Fail(ScreenCaptureFailureKind.PermissionDenied, "缺少可立即使用的屏幕录制权限");
        }

        var nativeFrame = _backend.Capture(region, windowId);
        if (nativeFrame is null)
        {
            return Fail(ScreenCaptureFailureKind.CaptureUnavailable, "macOS 未返回区域图像");
        }

        if (!BgraPixelConverter.TryConvert(nativeFrame, out var pixels))
        {
            return Fail(ScreenCaptureFailureKind.InvalidPixelBuffer, "macOS 区域图像的尺寸、stride 或缓冲区无效");
        }

        var scaleX = (double)nativeFrame.PixelWidth / region.Width;
        var scaleY = (double)nativeFrame.PixelHeight / region.Height;
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0)
        {
            return Fail(ScreenCaptureFailureKind.InvalidPixelBuffer, "macOS 区域图像的缩放比例无效");
        }

        return ScreenCaptureResult.Success(new CapturedRegion(
            nativeFrame.PixelWidth,
            nativeFrame.PixelHeight,
            scaleX,
            scaleY,
            CapturedPixelFormat.Argb32,
            CapturedColorSpace.Srgb,
            pixels));
    }

    private static ScreenCaptureResult Fail(ScreenCaptureFailureKind kind, string reason) =>
        ScreenCaptureResult.Failure(kind, reason);

    public void Dispose()
    {
        if (_backend is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}

internal sealed record MacNativeFrame(
    int PixelWidth,
    int PixelHeight,
    int BytesPerRow,
    byte[]? BgraBytes,
    int[]? PackedArgbPixels = null);

internal static class BgraPixelConverter
{
    public static bool TryConvert(MacNativeFrame frame, out int[] pixels)
    {
        pixels = [];
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
        {
            return false;
        }

        int packedRowBytes;
        int requiredBytes;
        int pixelCount;
        try
        {
            packedRowBytes = checked(frame.PixelWidth * 4);
            requiredBytes = checked(frame.BytesPerRow * frame.PixelHeight);
            pixelCount = checked(frame.PixelWidth * frame.PixelHeight);
        }
        catch (OverflowException)
        {
            return false;
        }

        if (frame.PackedArgbPixels is not null)
        {
            if (frame.BytesPerRow != packedRowBytes || frame.PackedArgbPixels.Length != pixelCount)
            {
                return false;
            }

            pixels = frame.PackedArgbPixels;
            return true;
        }

        if (frame.BgraBytes is null
            || frame.BytesPerRow < packedRowBytes
            || frame.BgraBytes.Length < requiredBytes)
        {
            return false;
        }

        pixels = new int[pixelCount];
        for (var y = 0; y < frame.PixelHeight; y++)
        {
            var sourceRow = y * frame.BytesPerRow;
            var destinationRow = y * frame.PixelWidth;
            for (var x = 0; x < frame.PixelWidth; x++)
            {
                var source = sourceRow + x * 4;
                var blue = frame.BgraBytes[source];
                var green = frame.BgraBytes[source + 1];
                var red = frame.BgraBytes[source + 2];
                var alpha = frame.BgraBytes[source + 3];
                pixels[destinationRow + x] =
                    (alpha << 24) | (red << 16) | (green << 8) | blue;
            }
        }

        return true;
    }
}

internal interface IMacScreenCaptureBackend
{
    MacNativeFrame? Capture(TargetBounds region, uint? windowId = null);
}

internal sealed class MacStreamCaptureBackend : IMacScreenCaptureBackend, IDisposable
{
    private readonly IMacStreamCaptureApi _streamApi;
    private readonly IMacScreenCaptureBackend _compatibilityBackend;
    private nint _handle;
    private TargetBounds? _activeRegion;
    private uint? _activeWindowId;
    private bool _bridgeUnavailable;
    private bool _disposed;

    [SupportedOSPlatform("macos")]
    public MacStreamCaptureBackend()
        : this(new MacNativeStreamCaptureApi(), new MacCoreGraphicsCaptureBackend())
    {
    }

    internal MacStreamCaptureBackend(
        IMacStreamCaptureApi streamApi,
        IMacScreenCaptureBackend compatibilityBackend)
    {
        _streamApi = streamApi;
        _compatibilityBackend = compatibilityBackend;
    }

    public MacNativeFrame? Capture(TargetBounds region, uint? windowId = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (windowId is null || _bridgeUnavailable)
        {
            return _compatibilityBackend.Capture(region, windowId);
        }

        try
        {
            if (_handle == 0)
            {
                _handle = _streamApi.Create();
                if (_handle == 0)
                {
                    return null;
                }
            }

            if (_activeRegion != region || _activeWindowId != windowId)
            {
                _streamApi.Stop(_handle);
                if (_streamApi.Start(_handle, windowId.Value, region) != 0)
                {
                    _activeRegion = null;
                    _activeWindowId = null;
                    _bridgeUnavailable = true;
                    ReleaseHandle();
                    return _compatibilityBackend.Capture(region, windowId);
                }

                _activeRegion = region;
                _activeWindowId = windowId;
            }

            for (var attempt = 0; attempt < 50; attempt++)
            {
                var byteCount = _streamApi.GetLatestSize(
                    _handle,
                    out var width,
                    out var height,
                    out var bytesPerRow);
                if (byteCount > 0)
                {
                    if (width <= 0 || height <= 0 || bytesPerRow < width * 4)
                    {
                        return null;
                    }

                    int requiredBytes;
                    try
                    {
                        requiredBytes = checked(bytesPerRow * height);
                    }
                    catch (OverflowException)
                    {
                        return null;
                    }

                    if (byteCount != requiredBytes)
                    {
                        return null;
                    }

                    var bytes = GC.AllocateUninitializedArray<byte>(requiredBytes);
                    var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    try
                    {
                        return _streamApi.CopyLatest(
                                _handle,
                                pin.AddrOfPinnedObject(),
                                requiredBytes) == requiredBytes
                            ? new MacNativeFrame(width, height, bytesPerRow, bytes)
                            : null;
                    }
                    finally
                    {
                        pin.Free();
                    }
                }

                if (attempt == 49)
                {
                    break;
                }

                Thread.Sleep(10);
            }

            return null;
        }
        catch (Exception ex) when (ex is DllNotFoundException
                                   or EntryPointNotFoundException
                                   or BadImageFormatException)
        {
            _bridgeUnavailable = true;
            ReleaseHandle();
            return _compatibilityBackend.Capture(region, windowId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ReleaseHandle();
        if (_compatibilityBackend is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void ReleaseHandle()
    {
        if (_handle == 0)
        {
            return;
        }

        try
        {
            _streamApi.Destroy(_handle);
        }
        catch (Exception ex) when (ex is DllNotFoundException
                                   or EntryPointNotFoundException
                                   or BadImageFormatException)
        {
        }
        finally
        {
            _handle = 0;
            _activeRegion = null;
            _activeWindowId = null;
        }
    }
}

internal interface IMacStreamCaptureApi
{
    nint Create();

    int Start(nint handle, uint windowId, TargetBounds region);

    int GetLatestSize(nint handle, out int width, out int height, out int bytesPerRow);

    int CopyLatest(nint handle, nint destination, int capacity);

    void Stop(nint handle);

    void Destroy(nint handle);
}

[SupportedOSPlatform("macos")]
internal sealed class MacNativeStreamCaptureApi : IMacStreamCaptureApi
{
    public nint Create() => MacStreamCaptureInterop.Create();

    public int Start(nint handle, uint windowId, TargetBounds region) =>
        MacStreamCaptureInterop.Start(
            handle,
            windowId,
            region.X,
            region.Y,
            region.Width,
            region.Height);

    public int GetLatestSize(nint handle, out int width, out int height, out int bytesPerRow) =>
        MacStreamCaptureInterop.GetLatestSize(handle, out width, out height, out bytesPerRow);

    public int CopyLatest(nint handle, nint destination, int capacity) =>
        MacStreamCaptureInterop.CopyLatest(handle, destination, capacity);

    public void Stop(nint handle) => MacStreamCaptureInterop.Stop(handle);

    public void Destroy(nint handle) => MacStreamCaptureInterop.Destroy(handle);
}

[SupportedOSPlatform("macos")]
internal static class MacStreamCaptureInterop
{
    private const string CaptureLibrary = "libShigureCapture.dylib";

    [DllImport(CaptureLibrary, EntryPoint = "shigure_capture_create")]
    public static extern nint Create();

    [DllImport(CaptureLibrary, EntryPoint = "shigure_capture_start")]
    public static extern int Start(
        nint handle,
        uint windowId,
        double x,
        double y,
        double width,
        double height);

    [DllImport(CaptureLibrary, EntryPoint = "shigure_capture_latest_size")]
    public static extern int GetLatestSize(
        nint handle,
        out int width,
        out int height,
        out int bytesPerRow);

    [DllImport(CaptureLibrary, EntryPoint = "shigure_capture_copy_latest")]
    public static extern int CopyLatest(nint handle, nint destination, int capacity);

    [DllImport(CaptureLibrary, EntryPoint = "shigure_capture_stop")]
    public static extern void Stop(nint handle);

    [DllImport(CaptureLibrary, EntryPoint = "shigure_capture_destroy")]
    public static extern void Destroy(nint handle);
}

internal sealed class MacCoreGraphicsCaptureBackend : IMacScreenCaptureBackend
{
    private readonly IMacScreenCaptureNativeApi _nativeApi;

    [SupportedOSPlatform("macos")]
    public MacCoreGraphicsCaptureBackend()
        : this(new MacScreenCaptureNativeApi())
    {
    }

    internal MacCoreGraphicsCaptureBackend(IMacScreenCaptureNativeApi nativeApi)
    {
        _nativeApi = nativeApi;
    }

    public MacNativeFrame? Capture(TargetBounds region, uint? windowId = null)
    {
        var image = _nativeApi.CreateImage(region, windowId);
        if (image == 0)
        {
            return null;
        }

        try
        {
            var widthValue = _nativeApi.GetImageWidth(image);
            var heightValue = _nativeApi.GetImageHeight(image);
            if (widthValue == 0 || heightValue == 0
                || widthValue > int.MaxValue || heightValue > int.MaxValue)
            {
                return null;
            }

            var width = (int)widthValue;
            var height = (int)heightValue;
            int bytesPerRow;
            int pixelCount;
            try
            {
                bytesPerRow = checked(width * 4);
                pixelCount = checked(width * height);
            }
            catch (OverflowException)
            {
                return null;
            }

            var colorSpace = _nativeApi.CreateSrgbColorSpace();
            if (colorSpace == 0)
            {
                return null;
            }

            try
            {
                var pixels = GC.AllocateUninitializedArray<int>(pixelCount);
                var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    var context = _nativeApi.CreateBitmapContext(
                        pin.AddrOfPinnedObject(),
                        width,
                        height,
                        bytesPerRow,
                        colorSpace);
                    if (context == 0)
                    {
                        return null;
                    }

                    try
                    {
                        _nativeApi.DrawImage(context, image, width, height);
                    }
                    finally
                    {
                        _nativeApi.ReleaseContext(context);
                    }

                    return new MacNativeFrame(width, height, bytesPerRow, null, pixels);
                }
                finally
                {
                    pin.Free();
                }
            }
            finally
            {
                _nativeApi.ReleaseColorSpace(colorSpace);
            }
        }
        finally
        {
            _nativeApi.ReleaseImage(image);
        }
    }
}

internal interface IMacScreenCaptureNativeApi
{
    nint CreateImage(TargetBounds region, uint? windowId);

    nuint GetImageWidth(nint image);

    nuint GetImageHeight(nint image);

    nint CreateSrgbColorSpace();

    nint CreateBitmapContext(
        nint data,
        int width,
        int height,
        int bytesPerRow,
        nint colorSpace);

    void DrawImage(nint context, nint image, int width, int height);

    void ReleaseContext(nint context);

    void ReleaseColorSpace(nint colorSpace);

    void ReleaseImage(nint image);
}

[SupportedOSPlatform("macos")]
internal sealed class MacScreenCaptureNativeApi : IMacScreenCaptureNativeApi
{
    public nint CreateImage(TargetBounds region, uint? windowId)
    {
        if (windowId is not null)
        {
            var displayImage = CreateDisplayRegionImage(region);
            if (displayImage != 0)
            {
                return displayImage;
            }
        }

        return MacScreenCaptureInterop.CGWindowListCreateImage(
            Rect(region.X, region.Y, region.Width, region.Height),
            windowId is null
                ? MacScreenCaptureInterop.WindowListOptionOnScreen
                : MacScreenCaptureInterop.WindowListOptionIncludingWindow,
            windowId ?? 0,
            MacScreenCaptureInterop.WindowImageDefault);
    }

    private static nint CreateDisplayRegionImage(TargetBounds region)
    {
        var globalRect = Rect(region.X, region.Y, region.Width, region.Height);
        if (MacScreenCaptureInterop.CGGetDisplaysWithRect(
                globalRect,
                1,
                out var display,
                out var displayCount) != 0
            || displayCount == 0)
        {
            return 0;
        }

        var displayBounds = MacScreenCaptureInterop.CGDisplayBounds(display);
        var localX = region.X - displayBounds.Origin.X;
        var localY = region.Y - displayBounds.Origin.Y;
        if (localX < 0 || localY < 0
            || localX + region.Width > displayBounds.Size.Width
            || localY + region.Height > displayBounds.Size.Height)
        {
            return 0;
        }

        return MacScreenCaptureInterop.CGDisplayCreateImageForRect(
            display,
            Rect(localX, localY, region.Width, region.Height));
    }

    private static CGRect Rect(double x, double y, double width, double height) => new()
    {
        Origin = new CGPoint { X = x, Y = y },
        Size = new CGSize { Width = width, Height = height }
    };

    public nuint GetImageWidth(nint image) => MacScreenCaptureInterop.CGImageGetWidth(image);

    public nuint GetImageHeight(nint image) => MacScreenCaptureInterop.CGImageGetHeight(image);

    public nint CreateSrgbColorSpace()
    {
        var name = MacScreenCaptureInterop.CFStringCreateWithCString(
            0,
            "kCGColorSpaceSRGB",
            MacScreenCaptureInterop.StringEncodingUtf8);
        if (name == 0)
        {
            return 0;
        }

        try
        {
            return MacScreenCaptureInterop.CGColorSpaceCreateWithName(name);
        }
        finally
        {
            MacScreenCaptureInterop.CFRelease(name);
        }
    }

    public nint CreateBitmapContext(
        nint data,
        int width,
        int height,
        int bytesPerRow,
        nint colorSpace) =>
        MacScreenCaptureInterop.CGBitmapContextCreate(
            data,
            (nuint)width,
            (nuint)height,
            8,
            (nuint)bytesPerRow,
            colorSpace,
            MacScreenCaptureInterop.BitmapBgra);

    public void DrawImage(nint context, nint image, int width, int height) =>
        MacScreenCaptureInterop.CGContextDrawImage(
            context,
            new CGRect
            {
                Origin = new CGPoint(),
                Size = new CGSize { Width = width, Height = height }
            },
            image);

    public void ReleaseContext(nint context) => MacScreenCaptureInterop.CGContextRelease(context);

    public void ReleaseColorSpace(nint colorSpace) => MacScreenCaptureInterop.CGColorSpaceRelease(colorSpace);

    public void ReleaseImage(nint image) => MacScreenCaptureInterop.CGImageRelease(image);
}

[SupportedOSPlatform("macos")]
internal static class MacScreenCaptureInterop
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const uint WindowListOptionOnScreen = (1 << 0) | (1 << 4);
    public const uint WindowListOptionIncludingWindow = 1 << 3;
    public const uint WindowImageDefault = 0;
    public const uint StringEncodingUtf8 = 0x08000100;
    public const uint BitmapBgra = 2 | (2u << 12);

    [DllImport(CoreGraphics)]
    public static extern nint CGWindowListCreateImage(
        CGRect screenBounds,
        uint listOption,
        uint windowId,
        uint imageOption);

    [DllImport(CoreGraphics)]
    public static extern int CGGetDisplaysWithRect(
        CGRect rect,
        uint maxDisplays,
        out uint display,
        out uint displayCount);

    [DllImport(CoreGraphics)]
    public static extern CGRect CGDisplayBounds(uint display);

    [DllImport(CoreGraphics)]
    public static extern nint CGDisplayCreateImageForRect(uint display, CGRect rect);

    [DllImport(CoreGraphics)]
    public static extern nuint CGImageGetWidth(nint image);

    [DllImport(CoreGraphics)]
    public static extern nuint CGImageGetHeight(nint image);

    [DllImport(CoreGraphics)]
    public static extern void CGImageRelease(nint image);

    [DllImport(CoreGraphics)]
    public static extern nint CGColorSpaceCreateWithName(nint name);

    [DllImport(CoreGraphics)]
    public static extern void CGColorSpaceRelease(nint colorSpace);

    [DllImport(CoreGraphics)]
    public static extern nint CGBitmapContextCreate(
        nint data,
        nuint width,
        nuint height,
        nuint bitsPerComponent,
        nuint bytesPerRow,
        nint colorSpace,
        uint bitmapInfo);

    [DllImport(CoreGraphics)]
    public static extern void CGContextDrawImage(nint context, CGRect rect, nint image);

    [DllImport(CoreGraphics)]
    public static extern void CGContextRelease(nint context);

    [DllImport(CoreFoundation)]
    public static extern nint CFStringCreateWithCString(
        nint allocator,
        string value,
        uint encoding);

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(nint value);
}
