namespace Shigure.Platform;

public enum PlatformPermissionKind
{
    ScreenCapture,
    Accessibility
}

public enum PlatformPermissionState
{
    NotGranted,
    Granted
}

public enum PlatformPermissionRequestOutcome
{
    AlreadyGranted,
    Granted,
    RestartRequired,
    UserActionRequired
}

public readonly record struct PlatformPermissionStatus(
    PlatformPermissionKind Kind,
    PlatformPermissionState State,
    bool RestartRequired)
{
    public bool IsReady => State == PlatformPermissionState.Granted && !RestartRequired;
}

public sealed record PlatformPermissionSnapshot(
    PlatformPermissionStatus ScreenCapture,
    PlatformPermissionStatus Accessibility)
{
    public bool IsReady => ScreenCapture.IsReady && Accessibility.IsReady;
}

public sealed record PlatformPermissionRequestResult(
    PlatformPermissionStatus Permission,
    PlatformPermissionRequestOutcome Outcome);

public interface IPlatformPermissionService
{
    PlatformPermissionSnapshot Check();

    PlatformPermissionRequestResult Request(PlatformPermissionKind permission);
}

public sealed class PlatformPermissionSession
{
    private readonly bool _screenCaptureGrantedAtStartup;

    public PlatformPermissionSession(bool screenCaptureGrantedAtStartup)
    {
        _screenCaptureGrantedAtStartup = screenCaptureGrantedAtStartup;
    }

    public PlatformPermissionSnapshot Assess(bool screenCaptureGranted, bool accessibilityGranted)
    {
        return new PlatformPermissionSnapshot(
            AssessScreenCapture(screenCaptureGranted),
            AssessAccessibility(accessibilityGranted));
    }

    public PlatformPermissionStatus AssessScreenCapture(bool granted)
    {
        return new PlatformPermissionStatus(
            PlatformPermissionKind.ScreenCapture,
            granted ? PlatformPermissionState.Granted : PlatformPermissionState.NotGranted,
            RestartRequired: granted && !_screenCaptureGrantedAtStartup);
    }

    public static PlatformPermissionStatus AssessAccessibility(bool granted)
    {
        return new PlatformPermissionStatus(
            PlatformPermissionKind.Accessibility,
            granted ? PlatformPermissionState.Granted : PlatformPermissionState.NotGranted,
            RestartRequired: false);
    }

    public static PlatformPermissionRequestOutcome ClassifyRequest(
        bool wasGranted,
        PlatformPermissionStatus current)
    {
        if (current.RestartRequired)
        {
            return PlatformPermissionRequestOutcome.RestartRequired;
        }

        if (current.State != PlatformPermissionState.Granted)
        {
            return PlatformPermissionRequestOutcome.UserActionRequired;
        }

        return wasGranted
            ? PlatformPermissionRequestOutcome.AlreadyGranted
            : PlatformPermissionRequestOutcome.Granted;
    }
}
