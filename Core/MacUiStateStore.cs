using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shigure;

public enum MacOverlayLayout
{
    Horizontal,
    Vertical
}

public sealed class MacUiBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class MacUiState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public MacUiBounds? MainWindowBounds { get; set; }
    public string SelectedPage { get; set; } = "General";
    public Dictionary<string, double> ColumnWidths { get; set; } = new(StringComparer.Ordinal);
    public MacOverlayLayout OverlayLayout { get; set; } = MacOverlayLayout.Horizontal;
    public MacUiBounds? HorizontalOverlayBounds { get; set; }
    public MacUiBounds? VerticalOverlayBounds { get; set; }
    public string TriggerKey { get; set; } = "XBUTTON2";
    public SendMode SendMode { get; set; } = SendMode.Switch;
}

public sealed record MacUiStateLoadResult(MacUiState State, string? Warning);

public sealed class MacUiStateStore
{
    public const string StateFileName = "mac-ui-state-v1.json";
    private const int MaximumColumnCount = 64;
    private const int MaximumColumnKeyLength = 128;
    private const long MaximumStateFileBytes = 256 * 1024;
    private const double MaximumDimension = 16384;
    private const int MaximumCoordinateMagnitude = 262144;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public MacUiStateStore(string userDataDirectory)
    {
        var cacheDirectory = UserDataLayout.ResolveCacheDirectory(userDataDirectory);
        FilePath = Path.Combine(cacheDirectory, StateFileName);
    }

    public string FilePath { get; }

    public MacUiStateLoadResult Load()
    {
        if (!File.Exists(FilePath))
        {
            return new MacUiStateLoadResult(new MacUiState(), null);
        }

        try
        {
            if (new FileInfo(FilePath).Length > MaximumStateFileBytes)
            {
                return InvalidState("Mac 界面状态文件过大，已使用默认布局。");
            }

            var state = JsonSerializer.Deserialize<MacUiState>(File.ReadAllText(FilePath), JsonOptions);
            if (state is null || state.SchemaVersion != MacUiState.CurrentSchemaVersion)
            {
                return InvalidState("Mac 界面状态版本不受支持，已使用默认布局。");
            }

            return new MacUiStateLoadResult(Normalize(state), null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return InvalidState("Mac 界面状态无法读取，已使用默认布局。");
        }
    }

    public string? TrySave(MacUiState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            var json = JsonSerializer.Serialize(Normalize(state), JsonOptions);
            AtomicFile.WriteAllText(FilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "Mac 界面状态无法保存；本次窗口调整不会在下次启动恢复。";
        }
    }

    private static MacUiStateLoadResult InvalidState(string warning) =>
        new(new MacUiState(), warning);

    private static MacUiState Normalize(MacUiState state)
    {
        var widths = (state.ColumnWidths ?? new Dictionary<string, double>())
            .Where(pair => IsColumnKeyValid(pair.Key) && IsDimensionValid(pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(MaximumColumnCount)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return new MacUiState
        {
            SchemaVersion = MacUiState.CurrentSchemaVersion,
            MainWindowBounds = NormalizeBounds(state.MainWindowBounds),
            SelectedPage = NormalizePage(state.SelectedPage),
            ColumnWidths = widths,
            OverlayLayout = Enum.IsDefined(state.OverlayLayout)
                ? state.OverlayLayout
                : MacOverlayLayout.Horizontal,
            HorizontalOverlayBounds = NormalizeBounds(state.HorizontalOverlayBounds),
            VerticalOverlayBounds = NormalizeBounds(state.VerticalOverlayBounds),
            TriggerKey = NormalizeTriggerKey(state.TriggerKey),
            SendMode = Enum.IsDefined(state.SendMode) ? state.SendMode : SendMode.Switch
        };
    }

    private static MacUiBounds? NormalizeBounds(MacUiBounds? bounds)
    {
        if (bounds is null
            || Math.Abs((long)bounds.X) > MaximumCoordinateMagnitude
            || Math.Abs((long)bounds.Y) > MaximumCoordinateMagnitude
            || !IsDimensionValid(bounds.Width)
            || !IsDimensionValid(bounds.Height))
        {
            return null;
        }

        return new MacUiBounds
        {
            X = bounds.X,
            Y = bounds.Y,
            Width = bounds.Width,
            Height = bounds.Height
        };
    }

    private static bool IsColumnKeyValid(string key) =>
        !string.IsNullOrWhiteSpace(key) && key.Length <= MaximumColumnKeyLength;

    private static bool IsDimensionValid(double value) =>
        double.IsFinite(value) && value > 0 && value <= MaximumDimension;

    private static string NormalizePage(string? page) =>
        !string.IsNullOrWhiteSpace(page) && page.Length <= 64 ? page : "General";

    private static string NormalizeTriggerKey(string? triggerKey)
    {
        var value = triggerKey?.Trim();
        return !string.IsNullOrEmpty(value) && value.Length <= 64 ? value : "XBUTTON2";
    }
}
