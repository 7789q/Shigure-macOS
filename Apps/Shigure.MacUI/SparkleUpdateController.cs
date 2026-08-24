using System.Runtime.InteropServices;

namespace Shigure.MacUI;

internal sealed class SparkleUpdateController : IDisposable
{
    private const string ObjectiveCLibrary = "/usr/lib/libobjc.A.dylib";

    private nint _controller;

    private SparkleUpdateController(nint controller)
    {
        _controller = controller;
    }

    public static SparkleUpdateController? TryCreate(out string unavailableReason)
    {
        unavailableReason = "当前构建未包含应用更新组件。";
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        var frameworkPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "Frameworks",
            "Sparkle.framework",
            "Versions",
            "B",
            "Sparkle"));
        if (!File.Exists(frameworkPath))
        {
            return null;
        }

        nint frameworkHandle = 0;
        try
        {
            frameworkHandle = NativeLibrary.Load(frameworkPath);
            var controllerClass = objc_getClass("SPUStandardUpdaterController");
            if (controllerClass == 0)
            {
                throw new InvalidOperationException("Sparkle 控制器类不可用。");
            }

            var allocated = objc_msgSend(controllerClass, sel_registerName("alloc"));
            var controller = objc_msgSend(
                allocated,
                sel_registerName("initWithStartingUpdater:updaterDelegate:userDriverDelegate:"),
                true,
                0,
                0);
            if (controller == 0)
            {
                throw new InvalidOperationException("Sparkle 控制器初始化失败。");
            }

            unavailableReason = string.Empty;
            return new SparkleUpdateController(controller);
        }
        catch
        {
            if (frameworkHandle != 0)
            {
                NativeLibrary.Free(frameworkHandle);
            }

            unavailableReason = "应用更新组件初始化失败，请重新安装当前版本。";
            return null;
        }
    }

    public void CheckForUpdates()
    {
        ObjectDisposedException.ThrowIf(_controller == 0, this);
        objc_msgSend(_controller, sel_registerName("checkForUpdates:"), 0);
    }

    public void Dispose()
    {
        if (_controller == 0)
        {
            return;
        }

        objc_msgSend(_controller, sel_registerName("release"));
        _controller = 0;
    }

    [DllImport(ObjectiveCLibrary)]
    private static extern nint objc_getClass(string name);

    [DllImport(ObjectiveCLibrary)]
    private static extern nint sel_registerName(string name);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(nint receiver, nint selector);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern nint objc_msgSend(
        nint receiver,
        nint selector,
        [MarshalAs(UnmanagedType.I1)] bool startUpdater,
        nint updaterDelegate,
        nint userDriverDelegate);

    [DllImport(ObjectiveCLibrary, EntryPoint = "objc_msgSend")]
    private static extern void objc_msgSend(nint receiver, nint selector, nint argument);
}
