using Avalonia;

namespace Shigure.MacUI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 1 && string.Equals(args[0], "--help", StringComparison.Ordinal))
        {
            Console.WriteLine("Shigure.MacUI");
            return 0;
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
