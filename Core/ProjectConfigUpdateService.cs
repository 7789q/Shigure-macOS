namespace Shigure;

public sealed record ProjectConfigUpdateResult(
    FuyutsuiConfigConverter.UpdateResult Config,
    FuyutsuiKeymapConverter.UpdateResult? Keymap,
    FuyutsuiAddonSyncResult AddonSync);

public sealed class ProjectConfigUpdateService
{
    private readonly string _baseDirectory;
    private readonly FuyutsuiAddonSyncService _addonSyncService;
    private readonly object _updateGate = new();

    public ProjectConfigUpdateService(
        string baseDirectory,
        FuyutsuiAddonSyncService addonSyncService)
    {
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _addonSyncService = addonSyncService;
    }

    public string ClassDirectory => Path.Combine(_addonSyncService.SourceRoot, "class");
    public string ClassMacrosPath => Path.Combine(_addonSyncService.SourceRoot, "core", "classmacros.lua");

    public ProjectConfigUpdateResult Update(string? savedAddonFilePath = null)
    {
        lock (_updateGate)
        {
            return UpdateCore(savedAddonFilePath);
        }
    }

    private ProjectConfigUpdateResult UpdateCore(string? savedAddonFilePath)
    {
        if (!Directory.Exists(ClassDirectory))
        {
            throw new DirectoryNotFoundException($"找不到 Fuyutsui class 目录: {ClassDirectory}");
        }

        var configDirectory = ConfigService.ResolveConfigPath(_baseDirectory);
        if (!Directory.Exists(configDirectory))
        {
            throw new DirectoryNotFoundException($"配置目录不存在: {configDirectory}");
        }

        var config = FuyutsuiConfigConverter.UpdateFromClassDirectory(
            ClassDirectory,
            configDirectory);
        var keymap = File.Exists(ClassMacrosPath)
            ? FuyutsuiKeymapConverter.UpdateFromClassMacros(
                ClassMacrosPath,
                Path.Combine(_baseDirectory, "keymap"))
            : null;
        var addonSync = string.IsNullOrWhiteSpace(savedAddonFilePath)
            ? _addonSyncService.SynchronizeAll()
            : _addonSyncService.SynchronizeFile(savedAddonFilePath);

        return new ProjectConfigUpdateResult(config, keymap, addonSync);
    }
}
