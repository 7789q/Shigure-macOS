using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Shigure.Platform;

namespace Shigure.Platform.MacOS;

[SupportedOSPlatform("macos")]
internal static class MacWindowCatalog
{
    public static IReadOnlyList<MacWindowDescriptor> ReadOnScreenWindows()
    {
        var keys = new WindowKeys();
        var list = MacWindowInterop.CGWindowListCopyWindowInfo(
            MacWindowInterop.WindowListOptionOnScreen,
            0);

        try
        {
            if (list == 0)
            {
                return [];
            }

            var result = new List<MacWindowDescriptor>();
            var count = MacWindowInterop.CFArrayGetCount(list).ToInt64();
            for (long index = 0; index < count; index++)
            {
                var dictionary = MacWindowInterop.CFArrayGetValueAtIndex(list, (nint)index);
                if (dictionary == 0)
                {
                    continue;
                }

                var windowId = MacWindowInterop.ReadInt64(
                    MacWindowInterop.CFDictionaryGetValue(dictionary, keys.WindowNumber));
                var processId = MacWindowInterop.ReadInt32(
                    MacWindowInterop.CFDictionaryGetValue(dictionary, keys.OwnerProcessId));
                var layer = MacWindowInterop.ReadInt32(
                    MacWindowInterop.CFDictionaryGetValue(dictionary, keys.Layer));
                var bounds = ReadBounds(
                    MacWindowInterop.CFDictionaryGetValue(dictionary, keys.Bounds));
                if (windowId is null || processId is null || layer is null || bounds is null)
                {
                    continue;
                }

                result.Add(new MacWindowDescriptor(
                    windowId.Value,
                    processId.Value,
                    layer.Value,
                    bounds.Value));
            }

            return result;
        }
        finally
        {
            MacWindowInterop.Release(list);
            keys.Dispose();
        }
    }

    private static TargetBounds? ReadBounds(nint dictionary)
    {
        if (dictionary == 0
            || !MacWindowInterop.CGRectMakeWithDictionaryRepresentation(dictionary, out var rect))
        {
            return null;
        }

        return new TargetBounds(
            (int)Math.Round(rect.Origin.X),
            (int)Math.Round(rect.Origin.Y),
            (int)Math.Round(rect.Size.Width),
            (int)Math.Round(rect.Size.Height));
    }

    private sealed class WindowKeys : IDisposable
    {
        public WindowKeys()
        {
            WindowNumber = MacWindowInterop.CreateString("kCGWindowNumber");
            OwnerProcessId = MacWindowInterop.CreateString("kCGWindowOwnerPID");
            Layer = MacWindowInterop.CreateString("kCGWindowLayer");
            Bounds = MacWindowInterop.CreateString("kCGWindowBounds");
        }

        public nint WindowNumber { get; }
        public nint OwnerProcessId { get; }
        public nint Layer { get; }
        public nint Bounds { get; }

        public void Dispose()
        {
            MacWindowInterop.Release(WindowNumber);
            MacWindowInterop.Release(OwnerProcessId);
            MacWindowInterop.Release(Layer);
            MacWindowInterop.Release(Bounds);
        }
    }
}

[SupportedOSPlatform("macos")]
internal static class MacWindowInterop
{
    private const string CoreGraphics =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint StringEncodingUtf8 = 0x08000100;
    private const int NumberSInt32Type = 3;
    private const int NumberSInt64Type = 4;

    public const uint WindowListOptionOnScreen = (1 << 0) | (1 << 4);

    [DllImport(CoreGraphics)]
    public static extern nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool CGRectMakeWithDictionaryRepresentation(nint dictionary, out CGRect rect);

    [DllImport(CoreFoundation)]
    public static extern nint CFArrayGetCount(nint array);

    [DllImport(CoreFoundation)]
    public static extern nint CFArrayGetValueAtIndex(nint array, nint index);

    [DllImport(CoreFoundation)]
    public static extern nint CFDictionaryGetValue(nint dictionary, nint key);

    [DllImport(CoreFoundation)]
    private static extern nint CFStringCreateWithCString(nint allocator, string value, uint encoding);

    [DllImport(CoreFoundation, EntryPoint = "CFNumberGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetInt32(nint number, int numberType, out int value);

    [DllImport(CoreFoundation, EntryPoint = "CFNumberGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetInt64(nint number, int numberType, out long value);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(nint value);

    public static nint CreateString(string value)
    {
        return CFStringCreateWithCString(0, value, StringEncodingUtf8);
    }

    public static int? ReadInt32(nint number)
    {
        return number != 0 && CFNumberGetInt32(number, NumberSInt32Type, out var value)
            ? value
            : null;
    }

    public static long? ReadInt64(nint number)
    {
        return number != 0 && CFNumberGetInt64(number, NumberSInt64Type, out var value)
            ? value
            : null;
    }

    public static void Release(nint value)
    {
        if (value != 0)
        {
            CFRelease(value);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGPoint
{
    public double X;
    public double Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGSize
{
    public double Width;
    public double Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CGRect
{
    public CGPoint Origin;
    public CGSize Size;
}
