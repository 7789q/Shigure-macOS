using System.Runtime.Versioning;
using Shigure.Platform.MacOS;

namespace Shigure.MacApp;

public sealed class MacApplicationRuntimeFactory : IShigureRuntimeFactory
{
    private readonly string _baseDirectory;
    private readonly ModuleStore _moduleStore;
    private readonly TimeProvider _timeProvider;

    public MacApplicationRuntimeFactory(
        string baseDirectory,
        ModuleStore moduleStore,
        TimeProvider? timeProvider = null)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _moduleStore = moduleStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [SupportedOSPlatform("macos")]
    public ShigureRuntime Create(AppOptions options)
    {
        var config = ConfigService.LoadFromBaseDirectory(_baseDirectory);
        var platformFactory = new MacRuntimeFactory(
            _baseDirectory,
            () => new StateBuilder(config),
            currentOptions => new LogicRegistry(
                new KeymapService(_baseDirectory, config),
                _moduleStore,
                currentOptions.ModuleId),
            _timeProvider);

        return platformFactory.Create(options);
    }
}
