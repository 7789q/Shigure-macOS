using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Shigure.Platform;

namespace Shigure.Platform.MacOS;

public sealed class MacPermissionService : IPlatformPermissionService
{
    private readonly IMacPermissionNativeApi _nativeApi;
    private readonly PlatformPermissionSession _session;

    [SupportedOSPlatform("macos")]
    public MacPermissionService()
        : this(new MacPermissionNativeApi())
    {
    }

    internal MacPermissionService(IMacPermissionNativeApi nativeApi)
    {
        _nativeApi = nativeApi;
        _session = new PlatformPermissionSession(nativeApi.HasScreenCaptureAccess());
    }

    public PlatformPermissionSnapshot Check()
    {
        return _session.Assess(
            _nativeApi.HasScreenCaptureAccess(),
            _nativeApi.HasAccessibilityAccess());
    }

    public PlatformPermissionRequestResult Request(PlatformPermissionKind permission)
    {
        var before = CheckPermission(permission);
        if (before.State != PlatformPermissionState.Granted)
        {
            switch (permission)
            {
                case PlatformPermissionKind.ScreenCapture:
                    _nativeApi.RequestScreenCaptureAccess();
                    break;
                case PlatformPermissionKind.Accessibility:
                    _nativeApi.RequestAccessibilityAccess();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(permission), permission, null);
            }
        }

        var current = CheckPermission(permission);
        return new PlatformPermissionRequestResult(
            current,
            PlatformPermissionSession.ClassifyRequest(before.State == PlatformPermissionState.Granted, current));
    }

    private PlatformPermissionStatus CheckPermission(PlatformPermissionKind permission)
    {
        return permission switch
        {
            PlatformPermissionKind.ScreenCapture =>
                _session.AssessScreenCapture(_nativeApi.HasScreenCaptureAccess()),
            PlatformPermissionKind.Accessibility =>
                PlatformPermissionSession.AssessAccessibility(_nativeApi.HasAccessibilityAccess()),
            _ => throw new ArgumentOutOfRangeException(nameof(permission), permission, null)
        };
    }
}

internal interface IMacPermissionNativeApi
{
    bool HasScreenCaptureAccess();

    bool HasAccessibilityAccess();

    bool RequestScreenCaptureAccess();

    bool RequestAccessibilityAccess();
}

[SupportedOSPlatform("macos")]
internal sealed class MacPermissionNativeApi : IMacPermissionNativeApi
{
    public bool HasScreenCaptureAccess() => MacPermissionInterop.CGPreflightScreenCaptureAccess();

    public bool HasAccessibilityAccess() => MacPermissionInterop.AXIsProcessTrusted();

    public bool RequestScreenCaptureAccess() => MacPermissionInterop.CGRequestScreenCaptureAccess();

    public bool RequestAccessibilityAccess()
    {
        using var options = AccessibilityPromptOptions.Create();
        return MacPermissionInterop.AXIsProcessTrustedWithOptions(options.Dictionary);
    }
}

[SupportedOSPlatform("macos")]
internal static class MacPermissionInterop
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGPreflightScreenCaptureAccess();

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGRequestScreenCaptureAccess();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXIsProcessTrusted();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool AXIsProcessTrustedWithOptions(nint options);

    [DllImport(CoreFoundation)]
    public static extern nint CFDictionaryCreate(
        nint allocator,
        nint[] keys,
        nint[] values,
        nint count,
        nint keyCallbacks,
        nint valueCallbacks);

    [DllImport(CoreFoundation)]
    public static extern void CFRelease(nint value);
}

[SupportedOSPlatform("macos")]
internal sealed class AccessibilityPromptOptions : IDisposable
{
    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private AccessibilityPromptOptions(nint dictionary)
    {
        Dictionary = dictionary;
    }

    public nint Dictionary { get; }

    public static AccessibilityPromptOptions Create()
    {
        var key = ReadExportedObject(ApplicationServices, "kAXTrustedCheckOptionPrompt");
        var value = ReadExportedObject(CoreFoundation, "kCFBooleanTrue");
        var dictionary = MacPermissionInterop.CFDictionaryCreate(
            0,
            [key],
            [value],
            1,
            0,
            0);

        if (dictionary == 0)
        {
            throw new InvalidOperationException("Unable to create accessibility permission request options.");
        }

        return new AccessibilityPromptOptions(dictionary);
    }

    public void Dispose()
    {
        MacPermissionInterop.CFRelease(Dictionary);
    }

    private static nint ReadExportedObject(string library, string symbol)
    {
        var handle = NativeLibrary.Load(library);
        try
        {
            var address = NativeLibrary.GetExport(handle, symbol);
            var value = Marshal.ReadIntPtr(address);
            if (value == 0)
            {
                throw new InvalidOperationException($"Native symbol '{symbol}' is null.");
            }

            return value;
        }
        finally
        {
            NativeLibrary.Free(handle);
        }
    }
}
