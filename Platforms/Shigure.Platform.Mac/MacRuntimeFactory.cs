using System.Runtime.Versioning;
using Shigure.Platform;

namespace Shigure.Platform.MacOS;

public sealed class MacRuntimeFactory : IShigureRuntimeFactory
{
    private readonly Func<IRuntimeStateBuilder> _stateBuilderFactory;
    private readonly Func<AppOptions, IRuntimeLogic> _logicFactory;
    private readonly Func<ITargetWindowLocator> _targetLocatorFactory;
    private readonly Func<IPlatformPermissionService> _permissionServiceFactory;
    private readonly Func<IPlatformPermissionService, IScreenRegionCapturer> _screenCapturerFactory;
    private readonly Func<ITargetWindowLocator, IPlatformPermissionService, ITargetKeyOutput> _keyOutputFactory;
    private readonly Func<ITriggerInput> _triggerInputFactory;
    private readonly TimeProvider _timeProvider;

    [SupportedOSPlatform("macos")]
    public MacRuntimeFactory(
        string baseDirectory,
        Func<IRuntimeStateBuilder> stateBuilderFactory,
        Func<AppOptions, IRuntimeLogic> logicFactory,
        TimeProvider? timeProvider = null)
        : this(
            stateBuilderFactory,
            logicFactory,
            () => new MacTargetWindowLocator(baseDirectory),
            static () => new MacPermissionService(),
            static permissionService => new MacScreenCapturer(
                permissionService,
                new MacCoreGraphicsCaptureBackend()),
            static (targetLocator, permissionService) =>
                new MacKeySender(targetLocator, permissionService),
            static () => new MacTriggerInput(),
            timeProvider)
    {
    }

    internal MacRuntimeFactory(
        Func<IRuntimeStateBuilder> stateBuilderFactory,
        Func<AppOptions, IRuntimeLogic> logicFactory,
        Func<ITargetWindowLocator> targetLocatorFactory,
        Func<IPlatformPermissionService> permissionServiceFactory,
        Func<IPlatformPermissionService, IScreenRegionCapturer> screenCapturerFactory,
        Func<ITargetWindowLocator, IPlatformPermissionService, ITargetKeyOutput> keyOutputFactory,
        Func<ITriggerInput> triggerInputFactory,
        TimeProvider? timeProvider = null)
    {
        _stateBuilderFactory = stateBuilderFactory;
        _logicFactory = logicFactory;
        _targetLocatorFactory = targetLocatorFactory;
        _permissionServiceFactory = permissionServiceFactory;
        _screenCapturerFactory = screenCapturerFactory;
        _keyOutputFactory = keyOutputFactory;
        _triggerInputFactory = triggerInputFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ShigureRuntime Create(AppOptions options)
    {
        var stateBuilder = _stateBuilderFactory();
        var logic = _logicFactory(options);
        var targetLocator = _targetLocatorFactory();
        var permissionService = _permissionServiceFactory();
        var screenCapturer = _screenCapturerFactory(permissionService);
        var keyOutput = _keyOutputFactory(targetLocator, permissionService);
        var triggerInput = _triggerInputFactory();

        try
        {
            return new ShigureRuntime(
                options,
                new RegionPixelScanner(targetLocator, screenCapturer),
                stateBuilder,
                keyOutput,
                triggerInput,
                logic,
                _timeProvider);
        }
        catch
        {
            triggerInput.Dispose();
            throw;
        }
    }
}
