using System.Runtime.InteropServices;

namespace Shigure.MacApp;

public sealed class MacLauncherParentMonitor
{
    public const string ParentProcessIdEnvironmentVariable = "SHIGURE_LAUNCHER_PID";

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    private readonly int expectedParentProcessId;
    private readonly Func<int> getParentProcessId;
    private readonly TimeSpan pollInterval;

    private MacLauncherParentMonitor(
        int expectedParentProcessId,
        Func<int> getParentProcessId,
        TimeSpan pollInterval)
    {
        this.expectedParentProcessId = expectedParentProcessId;
        this.getParentProcessId = getParentProcessId;
        this.pollInterval = pollInterval;
    }

    public static MacLauncherParentMonitor? FromEnvironment(
        Func<string, string?>? readEnvironmentVariable = null,
        Func<int>? getParentProcessId = null,
        TimeSpan? pollInterval = null)
    {
        var configuredValue = (readEnvironmentVariable ?? Environment.GetEnvironmentVariable)(
            ParentProcessIdEnvironmentVariable);
        if (configuredValue is null)
        {
            return null;
        }

        if (!int.TryParse(configuredValue, out var expectedParentProcessId)
            || expectedParentProcessId <= 0)
        {
            throw new InvalidOperationException("Launcher parent process ID is invalid.");
        }

        var interval = pollInterval ?? DefaultPollInterval;
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
        }

        return new MacLauncherParentMonitor(
            expectedParentProcessId,
            getParentProcessId ?? GetCurrentParentProcessId,
            interval);
    }

    public async Task WaitForParentExitAsync(CancellationToken cancellationToken)
    {
        while (getParentProcessId() == expectedParentProcessId)
        {
            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static int GetCurrentParentProcessId() => getppid();

    [DllImport("libSystem.B.dylib")]
    private static extern int getppid();
}
