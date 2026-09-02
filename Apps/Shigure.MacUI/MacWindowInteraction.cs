using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Shigure.MacUI;

internal static class MacWindowInteraction
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";
    private const nuint CanJoinAllSpaces = 1;
    private const nuint FullScreenAuxiliary = 1 << 8;

    public static void ConfigureStatusOverlay(Window window)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var handle = window.TryGetPlatformHandle();
        if (handle is not { Handle: not 0 }
            || !string.Equals(handle.HandleDescriptor, "NSWindow", StringComparison.Ordinal))
        {
            return;
        }

        SetIgnoresMouseEvents(
            handle.Handle,
            sel_registerName("setIgnoresMouseEvents:"),
            true);
        SetCollectionBehavior(
            handle.Handle,
            sel_registerName("setCollectionBehavior:"),
            CanJoinAllSpaces | FullScreenAuxiliary);
    }

    [DllImport(ObjectiveCLibrary)]
    private static extern nint sel_registerName(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SetIgnoresMouseEvents(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.I1)] bool ignoresMouseEvents);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SetCollectionBehavior(
        nint receiver,
        nint selector,
        nuint behavior);
}
