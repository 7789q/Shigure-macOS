using Avalonia;
using Shigure.Platform.MacOS;

namespace Shigure.MacUI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--permission-check", StringComparison.Ordinal))
        {
            if (!OperatingSystem.IsMacOS())
            {
                Console.WriteLine("macOS only");
                return 2;
            }

            var permissions = new MacPermissionService().Check();
            Console.WriteLine($"screen-capture={permissions.ScreenCapture.State};restart-required={permissions.ScreenCapture.RestartRequired}");
            Console.WriteLine($"accessibility={permissions.Accessibility.State};restart-required={permissions.Accessibility.RestartRequired}");
            return permissions.IsReady ? 0 : 1;
        }

        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            Console.WriteLine("Shigure.MacUI [--permission-check]");
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
