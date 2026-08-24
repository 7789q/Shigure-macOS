using System.Diagnostics;
using System.Runtime.Versioning;
using Shigure.Platform;

namespace Shigure.Platform.MacOS;

[SupportedOSPlatform("macos")]
public sealed class MacTargetWindowLocator : ITargetWindowLocator, IMacFreshTargetWindowLocator
{
    private static readonly TimeSpan DefaultTargetCacheDuration = TimeSpan.FromMilliseconds(200);

    private readonly Func<IReadOnlySet<int>> _findCandidateProcessIds;
    private readonly Func<IReadOnlyList<MacWindowDescriptor>> _readOnScreenWindows;
    private readonly Func<int, string?> _resolveProcessPath;
    private readonly Func<string> _describeConfiguredProcesses;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _targetCacheDuration;
    private readonly object _targetCacheSync = new();
    private TargetIdentity? _cachedProcessIdentity;
    private string? _cachedProcessPath;
    private bool _hasCachedTarget;
    private TargetWindow? _cachedTarget;
    private long _cachedTargetAt;

    public MacTargetWindowLocator(string baseDirectory)
        : this(TargetProcessConfig.FromBaseDirectory(baseDirectory))
    {
    }

    private MacTargetWindowLocator(TargetProcessConfig processConfig)
        : this(
            processConfig.FindCandidateProcessIds,
            MacWindowCatalog.ReadOnScreenWindows,
            MacProcessPathResolver.TryResolve,
            processConfig.DescribeConfiguredProcesses,
            TimeProvider.System,
            DefaultTargetCacheDuration)
    {
    }

    internal MacTargetWindowLocator(
        Func<IReadOnlySet<int>> findCandidateProcessIds,
        Func<IReadOnlyList<MacWindowDescriptor>> readOnScreenWindows,
        Func<int, string?> resolveProcessPath,
        Func<string> describeConfiguredProcesses,
        TimeProvider timeProvider,
        TimeSpan targetCacheDuration)
    {
        ArgumentNullException.ThrowIfNull(findCandidateProcessIds);
        ArgumentNullException.ThrowIfNull(readOnScreenWindows);
        ArgumentNullException.ThrowIfNull(resolveProcessPath);
        ArgumentNullException.ThrowIfNull(describeConfiguredProcesses);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (targetCacheDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(targetCacheDuration));
        }

        _findCandidateProcessIds = findCandidateProcessIds;
        _readOnScreenWindows = readOnScreenWindows;
        _resolveProcessPath = resolveProcessPath;
        _describeConfiguredProcesses = describeConfiguredProcesses;
        _timeProvider = timeProvider;
        _targetCacheDuration = targetCacheDuration;
    }

    public TargetWindow? FindFrontmostTarget() => FindFrontmostTarget(forceRefresh: false);

    TargetWindow? IMacFreshTargetWindowLocator.FindFrontmostTargetFresh() =>
        FindFrontmostTarget(forceRefresh: true);

    public string DescribeConfiguredProcesses() => _describeConfiguredProcesses();

    private TargetWindow? FindFrontmostTarget(bool forceRefresh)
    {
        lock (_targetCacheSync)
        {
            var now = _timeProvider.GetTimestamp();
            if (!forceRefresh
                && _hasCachedTarget
                && _timeProvider.GetElapsedTime(_cachedTargetAt, now) < _targetCacheDuration)
            {
                return _cachedTarget;
            }

            _cachedTarget = ResolveFrontmostTarget();
            _cachedTargetAt = now;
            _hasCachedTarget = true;
            return _cachedTarget;
        }
    }

    private TargetWindow? ResolveFrontmostTarget()
    {
        var candidateProcessIds = _findCandidateProcessIds();
        if (candidateProcessIds.Count == 0)
        {
            ClearCachedProcessPath();
            return null;
        }

        var match = MacTargetSelection.FindFrontmost(
            _readOnScreenWindows(),
            candidateProcessIds);
        if (match is null)
        {
            ClearCachedProcessPath();
            return null;
        }

        var identity = new TargetIdentity(
            TargetPlatforms.MacOS,
            match.Value.OwnerProcessId,
            match.Value.WindowId);
        return new TargetWindow(
            identity,
            ResolveProcessPath(identity),
            match.Value.Bounds);
    }

    private string? ResolveProcessPath(TargetIdentity identity)
    {
        if (_cachedProcessIdentity == identity && _cachedProcessPath is not null)
        {
            return _cachedProcessPath;
        }

        _cachedProcessIdentity = identity;
        _cachedProcessPath = _resolveProcessPath(identity.ProcessId);
        return _cachedProcessPath;
    }

    private void ClearCachedProcessPath()
    {
        _cachedProcessIdentity = null;
        _cachedProcessPath = null;
    }
}

internal interface IMacFreshTargetWindowLocator
{
    TargetWindow? FindFrontmostTargetFresh();
}

internal static class MacProcessPathResolver
{
    public static string? TryResolve(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

internal readonly record struct MacWindowDescriptor(
    long WindowId,
    int OwnerProcessId,
    int Layer,
    TargetBounds Bounds);

internal static class MacTargetSelection
{
    public static MacWindowDescriptor? FindFrontmost(
        IEnumerable<MacWindowDescriptor> windows,
        IReadOnlySet<int> candidateProcessIds)
    {
        foreach (var window in windows)
        {
            if (window.WindowId > 0
                && window.OwnerProcessId > 0
                && window.Layer == 0
                && window.Bounds.IsValid
                && candidateProcessIds.Contains(window.OwnerProcessId))
            {
                return window;
            }
        }

        return null;
    }
}
