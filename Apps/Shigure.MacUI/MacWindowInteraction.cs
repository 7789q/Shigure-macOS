using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Shigure.MacUI;

internal static class MacWindowInteraction
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    public static void MakeClickThrough(Window window)
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
    }

    [DllImport(ObjectiveCLibrary)]
    private static extern nint sel_registerName(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void SetIgnoresMouseEvents(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.I1)] bool ignoresMouseEvents);
}
