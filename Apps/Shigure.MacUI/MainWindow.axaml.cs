using System.Collections.ObjectModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Shigure.Platform;
using Shigure.Presentation;

namespace Shigure.MacUI;

public sealed partial class MainWindow : Window
{
    private const int MaximumLogLines = 2000;

    private readonly IReadOnlyList<NavigationItem> _navigation;
    private readonly RuntimeSessionController _runtime;
    private readonly IPlatformPermissionService? _permissions;
    private readonly string _baseDirectory;
    private readonly ObservableCollection<RuntimeDisplayRow> _stateRows = [];
    private readonly ObservableCollection<RuntimeDisplayRow> _auraRows = [];
    private readonly ObservableCollection<RuntimeDisplayRow> _dynamicRows = [];
    private readonly ObservableCollection<RuntimeDisplayRow> _spellRows = [];
    private readonly ObservableCollection<RuntimeDisplayRow> _partyRows = [];
    private readonly ObservableCollection<RuntimeDisplayRow> _logicRows = [];
    private readonly ObservableCollection<ModuleSelectionOption> _modules = [];
    private readonly List<string> _logLines = [];
    private readonly ModuleStore _moduleStore;
    private readonly ModuleDependencyService? _moduleDependencies;
    private readonly ModuleMarketplaceClient _moduleMarketplace;
    private readonly ProjectConfigUpdateService? _configUpdates;
    private readonly MacUiStateStore? _uiStateStore;
    private readonly MacUiState _uiState;
    private readonly Dictionary<string, DataGrid> _trackedColumnGrids = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _moduleImportGate = new(1, 1);
    private readonly SemaphoreSlim _permissionRequestGate = new(1, 1);
    private readonly object _runtimeSnapshotSync = new();
    private readonly DispatcherTimer _mainBoundsCaptureTimer;
    private readonly DispatcherTimer _logicToastTimer;
    private ConfigEditorView? _configEditor;
    private MacroEditorView? _macroEditor;
    private ModuleEditorView? _moduleEditor;
    private Window? _overlay;
    private TextBlock? _overlayStatus;
    private Window? _logicToast;
    private TextBlock? _logicToastText;
    private TextBox? _runtimeLogBox;
    private bool _capturingTrigger;
    private bool _allowClose;
    private bool _shutdownPrepared;
    private bool _moduleDependencyInitializationStarted;
    private bool _autoScrollLogs = true;
    private bool _suppressModuleSelection;
    private bool _uiStateInitialized;
    private bool _mainWindowBoundsRestored;
    private bool _uiStateSaveWarningShown;
    private bool _suppressOverlayBoundsCapture;
    private Control? _overlayPointerOwner;
    private PixelPoint _overlayPointerStart;
    private MacUiBounds? _overlayPointerBounds;
    private WindowEdge? _overlayResizeEdge;
    private Control? _overlayDragOwner;
    private PixelPoint _overlayDragPointerStart;
    private PixelPoint _overlayDragPointerCurrent;
    private MacUiBounds? _overlayDragBounds;
    private Button? _triggerButton;
    private string _triggerKey = "XBUTTON2";
    private SendMode _sendMode = SendMode.Switch;
    private string? _selectedModuleId;
    private MacOverlayLayout _overlayLayout;
    private bool? _lastObservedLogicEnabled;
    private RenderSnapshot? _pendingRuntimeSnapshot;
    private bool _runtimeSnapshotDispatchPending;

    public MainWindow()
        : this(MacUiComposition.Create())
    {
    }

    private MainWindow(MacUiServices services)
        : this(
            services.ModuleStore,
            services.Runtime,
            services.RuntimeBaseDirectory,
            services.ConfigUpdates,
            services.ModuleDependencies,
            services.UiStateStore,
            services.Permissions)
    {
        AppendLocalLog(
            $"运行资源工作副本已就绪：新增 {services.Workspace.CreatedFiles.Count}，更新 {services.Workspace.UpdatedFiles.Count}，保留冲突 {services.Workspace.ConflictingFiles.Count}");
        AppendLocalLog(services.AddonSync.TargetFound
            ? $"游戏插件已同步：更新 {services.AddonSync.CopiedFiles.Count}，无需更新 {services.AddonSync.SkippedFiles.Count}，失败 {services.AddonSync.Failures.Count}"
            : $"游戏插件未同步：{services.AddonSync.SkippedReason}");
    }

    public MainWindow(
        ModuleStore moduleStore,
        RuntimeSessionController runtime,
        string? baseDirectory = null,
        ProjectConfigUpdateService? configUpdates = null,
        ModuleDependencyService? moduleDependencies = null,
        MacUiStateStore? uiStateStore = null,
        IPlatformPermissionService? permissions = null)
    {
        _moduleStore = moduleStore;
        _runtime = runtime;
        _permissions = permissions;
        _baseDirectory = Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory);
        _configUpdates = configUpdates;
        _moduleDependencies = moduleDependencies;
        _uiStateStore = uiStateStore;
        var stateLoad = _uiStateStore?.Load() ?? new MacUiStateLoadResult(new MacUiState(), null);
        _uiState = stateLoad.State;
        _overlayLayout = _uiState.OverlayLayout;
        _triggerKey = _uiState.TriggerKey;
        _sendMode = _uiState.SendMode;
        InitializeComponent();
        _mainBoundsCaptureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _mainBoundsCaptureTimer.Tick += (_, _) =>
        {
            _mainBoundsCaptureTimer.Stop();
            CaptureMainWindowBounds();
        };
        _logicToastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _logicToastTimer.Tick += (_, _) =>
        {
            _logicToastTimer.Stop();
            _logicToast?.Hide();
        };
        _moduleMarketplace = new ModuleMarketplaceClient();
        RefreshModuleNames();
        _navigation = BuildNavigation();
        ApplyMonitor(RuntimeMonitorProjection.Create(CreateEmptySnapshot()));
        PageList.ItemsSource = _navigation;
        PageList.SelectionChanged += HandlePageChanged;
        PageList.SelectedIndex = ResolveSelectedPageIndex(_uiState.SelectedPage);
        RunButton.Click += async (_, _) => await ToggleRuntimeAsync();
        EnableButton.Click += (_, _) => _runtime.ToggleEnabled();
        OverlayButton.Click += (_, _) => ShowOverlay();
        KeyDown += HandleTriggerCapture;
        AddHandler(PointerPressedEvent, HandleTriggerPointerPressed, RoutingStrategies.Tunnel, true);
        AddHandler(PointerWheelChangedEvent, HandleTriggerPointerWheelChanged, RoutingStrategies.Tunnel, true);
        _runtime.StatusChanged += HandleRuntimeStatusChanged;
        _runtime.SnapshotUpdated += HandleRuntimeSnapshotUpdated;
        _runtime.LogAdded += HandleRuntimeLogAdded;
        PositionChanged += (_, _) => ScheduleMainWindowBoundsCapture();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty)
            {
                ScheduleMainWindowBoundsCapture();
            }
        };
        Opened += async (_, _) => await HandleOpenedAsync();
        AppendLog(new RuntimeLogEntry(DateTimeOffset.UtcNow, "界面已就绪"));
        if (stateLoad.Warning is not null)
        {
            AppendLocalLog(stateLoad.Warning);
        }
        ApplyRuntimeStatus(_runtime.Status);
        _uiStateInitialized = true;
        Closing += (_, e) =>
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                if (Application.Current is App app)
                {
                    _ = app.RequestQuitAsync();
                }
            }
        };
    }

    public void ShowAndActivate()
    {
        Show();
        Activate();
    }

    public void ShowOverlay()
    {
        if (_overlay is null)
        {
            _overlay = new Window
            {
                CanResize = true,
                WindowDecorations = WindowDecorations.None,
                ShowInTaskbar = false,
                Topmost = true,
                Background = new SolidColorBrush(Color.Parse("#E60B0D0F"))
            };
            AutomationProperties.SetName(_overlay, "Shigure 置顶浮动条");
            _overlay.PositionChanged += (_, _) => CaptureOverlayBounds();
            _overlay.PropertyChanged += (_, e) =>
            {
                if (e.Property == ClientSizeProperty)
                {
                    CaptureOverlayBounds();
                }
            };
            ApplyOverlayLayout(restoreBounds: true);
        }

        _overlay.Show();
        RestoreOverlayBounds();
        _overlay.Activate();
    }

    private void ApplyOverlayLayout(bool restoreBounds)
    {
        if (_overlay is null)
        {
            return;
        }

        _suppressOverlayBoundsCapture = true;
        try
        {
            _uiState.OverlayLayout = _overlayLayout;
            if (_overlayLayout == MacOverlayLayout.Vertical)
            {
                _overlay.MinWidth = 180;
                _overlay.MinHeight = 150;
                _overlay.Content = BuildVerticalOverlayContent();
                if (!restoreBounds || !TryApplyWindowBounds(_overlay, _uiState.VerticalOverlayBounds, 180, 150))
                {
                    _overlay.Width = 220;
                    _overlay.Height = 180;
                }
            }
            else
            {
                _overlay.MinWidth = 420;
                _overlay.MinHeight = 52;
                _overlay.Content = BuildHorizontalOverlayContent();
                if (!restoreBounds || !TryApplyWindowBounds(_overlay, _uiState.HorizontalOverlayBounds, 420, 52))
                {
                    _overlay.Width = 520;
                    _overlay.Height = 58;
                }
            }
        }
        finally
        {
            _suppressOverlayBoundsCapture = false;
        }
    }

    private Control BuildHorizontalOverlayContent()
    {
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(18, 0)
        };
        var brand = new TextBlock
        {
            Text = "SHIGURE",
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        var status = _overlayStatus = new TextBlock
        {
            Text = BuildOverlayStatus(_runtime.LastSnapshot),
            Foreground = new SolidColorBrush(Color.Parse("#AEB5BA")),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(22, 0),
            TextWrapping = TextWrapping.Wrap
        };
        var dragArea = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        dragArea.Children.Add(brand);
        dragArea.Children.Add(AddToGrid(status, 1));
        var dragHost = CreateOverlayDragHost(dragArea);
        content.Children.Add(dragHost);
        Grid.SetColumnSpan(dragHost, 2);
        content.Children.Add(AddToGrid(CreateOverlayHideButton(), 2));
        return AddOverlayResizeHandles(content);
    }

    private Control BuildVerticalOverlayContent()
    {
        var dragArea = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(18, 16, 18, 8)
        };
        dragArea.Children.Add(new TextBlock
        {
            Text = "SHIGURE",
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White
        });
        dragArea.Children.Add(_overlayStatus = new TextBlock
        {
            Text = BuildOverlayStatus(_runtime.LastSnapshot),
            Foreground = new SolidColorBrush(Color.Parse("#AEB5BA")),
            TextWrapping = TextWrapping.Wrap
        });
        var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        content.Children.Add(CreateOverlayDragHost(dragArea));
        var hideButton = CreateOverlayHideButton();
        hideButton.Margin = new Thickness(18, 0, 18, 14);
        hideButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        Grid.SetRow(hideButton, 1);
        content.Children.Add(hideButton);
        return AddOverlayResizeHandles(content);
    }

    private Button CreateOverlayHideButton()
    {
        var button = new Button
        {
            Content = "隐藏",
            VerticalAlignment = VerticalAlignment.Center
        };
        button.Click += (_, _) =>
        {
            CaptureOverlayBounds();
            SaveUiState();
            _overlay?.Hide();
        };
        AutomationProperties.SetName(button, "隐藏浮动条");
        return button;
    }

    private Control CreateOverlayDragHost(Control content)
    {
        var dragHost = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Child = content
        };
        AutomationProperties.SetName(dragHost, "拖动浮动条");
        dragHost.PointerPressed += (_, e) => StartOverlayDrag(dragHost, e);
        dragHost.PointerMoved += (_, e) => ContinueOverlayDrag(dragHost, e);
        dragHost.PointerReleased += (_, e) => EndOverlayDrag(dragHost, e);
        dragHost.PointerCaptureLost += (_, _) => ClearOverlayDrag(dragHost);
        return dragHost;
    }

    private void StartOverlayDrag(Control control, PointerPressedEventArgs e)
    {
        if (_overlay is null || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _overlayDragOwner = control;
        _overlayDragPointerStart = control.PointToScreen(e.GetPosition(control));
        _overlayDragPointerCurrent = _overlayDragPointerStart;
        _overlayDragBounds = CaptureBounds(_overlay);
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void ContinueOverlayDrag(Control control, PointerEventArgs e)
    {
        if (_overlayDragOwner != control)
        {
            return;
        }

        _overlayDragPointerCurrent = control.PointToScreen(e.GetPosition(control));
        e.Handled = true;
    }

    private void EndOverlayDrag(Control control, PointerReleasedEventArgs e)
    {
        if (_overlay is null || _overlayDragOwner != control || _overlayDragBounds is null)
        {
            return;
        }

        var current = _overlayDragPointerCurrent;
        var start = _overlayDragPointerStart;
        var bounds = _overlayDragBounds;
        e.Pointer.Capture(null);
        ClearOverlayDrag(control);
        _overlay.Position = new PixelPoint(
            bounds.X + current.X - start.X,
            bounds.Y + current.Y - start.Y);
        CaptureOverlayBounds();
        e.Handled = true;
    }

    private void ClearOverlayDrag(Control control)
    {
        if (_overlayDragOwner != control)
        {
            return;
        }

        _overlayDragOwner = null;
        _overlayDragBounds = null;
    }

    private void StartOverlayPointerOperation(
        Control control,
        WindowEdge resizeEdge,
        PointerPressedEventArgs e)
    {
        if (_overlay is null || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _overlayPointerOwner = control;
        _overlayPointerStart = control.PointToScreen(e.GetPosition(control));
        _overlayPointerBounds = CaptureBounds(_overlay);
        _overlayResizeEdge = resizeEdge;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private void ContinueOverlayPointerOperation(Control control, PointerEventArgs e)
    {
        if (_overlay is null
            || _overlayPointerOwner != control
            || _overlayPointerBounds is null)
        {
            return;
        }

        var current = control.PointToScreen(e.GetPosition(control));
        var deltaX = current.X - _overlayPointerStart.X;
        var deltaY = current.Y - _overlayPointerStart.Y;
        ApplyOverlayResize(_overlayResizeEdge!.Value, deltaX, deltaY);
        e.Handled = true;
    }

    private void ApplyOverlayResize(WindowEdge edge, int deltaX, int deltaY)
    {
        if (_overlay is null || _overlayPointerBounds is null)
        {
            return;
        }

        var scaling = Math.Max(0.25, _overlay.RenderScaling);
        var bounds = _overlayPointerBounds;
        var width = bounds.Width;
        var height = bounds.Height;
        var x = bounds.X;
        var y = bounds.Y;

        if (edge is WindowEdge.East or WindowEdge.NorthEast or WindowEdge.SouthEast)
        {
            width = Math.Max(_overlay.MinWidth, bounds.Width + deltaX / scaling);
        }
        else if (edge is WindowEdge.West or WindowEdge.NorthWest or WindowEdge.SouthWest)
        {
            width = Math.Max(_overlay.MinWidth, bounds.Width - deltaX / scaling);
            x = bounds.X + (int)Math.Round((bounds.Width - width) * scaling);
        }

        if (edge is WindowEdge.South or WindowEdge.SouthEast or WindowEdge.SouthWest)
        {
            height = Math.Max(_overlay.MinHeight, bounds.Height + deltaY / scaling);
        }
        else if (edge is WindowEdge.North or WindowEdge.NorthEast or WindowEdge.NorthWest)
        {
            height = Math.Max(_overlay.MinHeight, bounds.Height - deltaY / scaling);
            y = bounds.Y + (int)Math.Round((bounds.Height - height) * scaling);
        }

        _overlay.Width = width;
        _overlay.Height = height;
        _overlay.Position = new PixelPoint(x, y);
    }

    private void EndOverlayPointerOperation(Control control, IPointer pointer)
    {
        if (_overlayPointerOwner != control)
        {
            return;
        }

        pointer.Capture(null);
        ClearOverlayPointerOperation(control);
        CaptureOverlayBounds();
    }

    private void ClearOverlayPointerOperation(Control control)
    {
        if (_overlayPointerOwner != control)
        {
            return;
        }

        _overlayPointerOwner = null;
        _overlayPointerBounds = null;
        _overlayResizeEdge = null;
    }

    private Control AddOverlayResizeHandles(Control content)
    {
        var root = new Grid();
        root.Children.Add(content);
        AddResizeHandle(root, WindowEdge.North, StandardCursorType.SizeNorthSouth, horizontal: true, start: true);
        AddResizeHandle(root, WindowEdge.South, StandardCursorType.SizeNorthSouth, horizontal: true, start: false);
        AddResizeHandle(root, WindowEdge.West, StandardCursorType.SizeWestEast, horizontal: false, start: true);
        AddResizeHandle(root, WindowEdge.East, StandardCursorType.SizeWestEast, horizontal: false, start: false);
        AddCornerResizeHandle(root, WindowEdge.NorthWest, StandardCursorType.TopLeftCorner, true, true);
        AddCornerResizeHandle(root, WindowEdge.NorthEast, StandardCursorType.TopRightCorner, false, true);
        AddCornerResizeHandle(root, WindowEdge.SouthWest, StandardCursorType.BottomLeftCorner, true, false);
        AddCornerResizeHandle(root, WindowEdge.SouthEast, StandardCursorType.BottomRightCorner, false, false);
        return root;
    }

    private void AddResizeHandle(
        Grid root,
        WindowEdge edge,
        StandardCursorType cursor,
        bool horizontal,
        bool start)
    {
        var handle = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(cursor),
            Width = horizontal ? double.NaN : 6,
            Height = horizontal ? 6 : double.NaN,
            HorizontalAlignment = horizontal
                ? HorizontalAlignment.Stretch
                : start ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = horizontal
                ? start ? VerticalAlignment.Top : VerticalAlignment.Bottom
                : VerticalAlignment.Stretch
        };
        EnableOverlayResize(handle, edge);
        root.Children.Add(handle);
    }

    private void AddCornerResizeHandle(
        Grid root,
        WindowEdge edge,
        StandardCursorType cursor,
        bool left,
        bool top)
    {
        var handle = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new Cursor(cursor),
            Width = 10,
            Height = 10,
            HorizontalAlignment = left ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom
        };
        EnableOverlayResize(handle, edge);
        root.Children.Add(handle);
    }

    private void EnableOverlayResize(Control handle, WindowEdge edge)
    {
        handle.PointerPressed += (_, e) => StartOverlayPointerOperation(handle, edge, e);
        handle.PointerMoved += (_, e) => ContinueOverlayPointerOperation(handle, e);
        handle.PointerReleased += (_, e) => EndOverlayPointerOperation(handle, e.Pointer);
        handle.PointerCaptureLost += (_, _) => ClearOverlayPointerOperation(handle);
    }

    public async Task PrepareForShutdownAsync()
    {
        if (_shutdownPrepared)
        {
            return;
        }

        _shutdownPrepared = true;
        lock (_runtimeSnapshotSync)
        {
            _pendingRuntimeSnapshot = null;
        }
        _allowClose = true;
        _mainBoundsCaptureTimer.Stop();
        _logicToastTimer.Stop();
        CaptureUiState();
        SaveUiState();
        try
        {
            await _runtime.DisposeAsync();
        }
        finally
        {
            _runtime.StatusChanged -= HandleRuntimeStatusChanged;
            _runtime.SnapshotUpdated -= HandleRuntimeSnapshotUpdated;
            _runtime.LogAdded -= HandleRuntimeLogAdded;
            _overlay?.Close();
            _logicToast?.Close();
        }
    }

    public async Task<bool> ConfirmShutdownAsync()
    {
        if (_configEditor is not null && !await _configEditor.ConfirmDiscardBeforeExitAsync())
        {
            return false;
        }

        if (_macroEditor is not null && !await _macroEditor.ConfirmDiscardBeforeExitAsync())
        {
            return false;
        }

        return _moduleEditor is null || await _moduleEditor.ConfirmDiscardBeforeExitAsync();
    }

    private async Task HandleOpenedAsync()
    {
        if (!_mainWindowBoundsRestored)
        {
            _mainWindowBoundsRestored = true;
            TryApplyWindowBounds(this, _uiState.MainWindowBounds, MinWidth, MinHeight);
            CaptureMainWindowBounds();
        }

        if (_moduleDependencyInitializationStarted || _moduleDependencies is null)
        {
            return;
        }

        _moduleDependencyInitializationStarted = true;
        await ImportModuleDependenciesAsync(
            reloadStore: true,
            moduleSetChanged: false,
            showFeedback: true);
    }

    private int ResolveSelectedPageIndex(string pageName)
    {
        if (!Enum.TryParse<WorkspacePage>(pageName, ignoreCase: true, out var page))
        {
            return 0;
        }

        for (var index = 0; index < _navigation.Count; index++)
        {
            if (_navigation[index].Page == page)
            {
                return index;
            }
        }

        return 0;
    }

    private DataGrid TrackColumnGrid(string key, DataGrid grid)
    {
        _trackedColumnGrids[key] = grid;
        for (var index = 0; index < grid.Columns.Count; index++)
        {
            if (_uiState.ColumnWidths.TryGetValue(ColumnWidthKey(key, index), out var width)
                && double.IsFinite(width)
                && width >= 32)
            {
                grid.Columns[index].Width = new DataGridLength(width);
            }
        }

        return grid;
    }

    private void CaptureTrackedColumnWidths()
    {
        foreach (var (key, grid) in _trackedColumnGrids)
        {
            for (var index = 0; index < grid.Columns.Count; index++)
            {
                var width = grid.Columns[index].ActualWidth;
                if (double.IsFinite(width) && width >= 32)
                {
                    _uiState.ColumnWidths[ColumnWidthKey(key, index)] = width;
                }
            }
        }
    }

    private static string ColumnWidthKey(string gridKey, int columnIndex) =>
        $"{gridKey}.{columnIndex}";

    private void CaptureUiState()
    {
        CaptureTrackedColumnWidths();
        CaptureMainWindowBounds();
        CaptureOverlayBounds();
        _uiState.OverlayLayout = _overlayLayout;
        _uiState.TriggerKey = _triggerKey;
        _uiState.SendMode = _sendMode;
    }

    private void CaptureMainWindowBounds()
    {
        if (!_mainWindowBoundsRestored
            || !IsVisible
            || WindowState != WindowState.Normal
            || IsScreenFilling(this))
        {
            return;
        }

        _uiState.MainWindowBounds = CaptureBounds(this);
    }

    private void ScheduleMainWindowBoundsCapture()
    {
        if (!_mainWindowBoundsRestored || _shutdownPrepared)
        {
            return;
        }

        _mainBoundsCaptureTimer.Stop();
        _mainBoundsCaptureTimer.Start();
    }

    private static bool IsScreenFilling(Window window)
    {
        foreach (var screen in window.Screens.All)
        {
            var scaling = Math.Max(0.25, screen.Scaling);
            var width = window.ClientSize.Width * scaling;
            var height = window.ClientSize.Height * scaling;
            if (Fills(screen.Bounds) || Fills(screen.WorkingArea))
            {
                return true;
            }

            bool Fills(PixelRect area)
            {
                const double tolerance = 8;
                return Math.Abs(window.Position.X - area.X) <= tolerance
                    && Math.Abs(window.Position.Y - area.Y) <= tolerance
                    && width >= area.Width - tolerance
                    && height >= area.Height - tolerance;
            }
        }

        return false;
    }

    private void CaptureOverlayBounds()
    {
        if (_suppressOverlayBoundsCapture
            || _overlay?.IsVisible != true
            || _overlay.WindowState != WindowState.Normal)
        {
            return;
        }

        var bounds = CaptureBounds(_overlay);
        if (_overlayLayout == MacOverlayLayout.Vertical)
        {
            _uiState.VerticalOverlayBounds = bounds;
        }
        else
        {
            _uiState.HorizontalOverlayBounds = bounds;
        }
    }

    private static MacUiBounds CaptureBounds(Window window) => new()
    {
        X = window.Position.X,
        Y = window.Position.Y,
        Width = window.ClientSize.Width,
        Height = window.ClientSize.Height
    };

    private void RestoreOverlayBounds()
    {
        if (_overlay is null)
        {
            return;
        }

        var bounds = _overlayLayout == MacOverlayLayout.Vertical
            ? _uiState.VerticalOverlayBounds
            : _uiState.HorizontalOverlayBounds;
        var minWidth = _overlayLayout == MacOverlayLayout.Vertical ? 180 : 420;
        var minHeight = _overlayLayout == MacOverlayLayout.Vertical ? 150 : 52;
        _suppressOverlayBoundsCapture = true;
        try
        {
            TryApplyWindowBounds(_overlay, bounds, minWidth, minHeight);
        }
        finally
        {
            _suppressOverlayBoundsCapture = false;
        }
        CaptureOverlayBounds();
    }

    private static bool TryApplyWindowBounds(
        Window window,
        MacUiBounds? bounds,
        double minimumWidth,
        double minimumHeight)
    {
        if (bounds is null || !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height))
        {
            return false;
        }

        var requested = new PixelRect(
            bounds.X,
            bounds.Y,
            Math.Max(1, (int)Math.Ceiling(bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(bounds.Height)));
        var screen = window.Screens.All.FirstOrDefault(candidate => candidate.WorkingArea.Intersects(requested));
        if (screen is null)
        {
            return false;
        }

        var workArea = screen.WorkingArea;
        var scaling = Math.Max(0.25, screen.Scaling);
        var maximumWidth = Math.Max(1, workArea.Width / scaling);
        var maximumHeight = Math.Max(1, workArea.Height / scaling);
        var effectiveMinimumWidth = Math.Min(minimumWidth, maximumWidth);
        var effectiveMinimumHeight = Math.Min(minimumHeight, maximumHeight);
        var width = Math.Clamp(bounds.Width, effectiveMinimumWidth, maximumWidth);
        var height = Math.Clamp(bounds.Height, effectiveMinimumHeight, maximumHeight);
        var physicalWidth = Math.Max(1, (int)Math.Ceiling(width * scaling));
        var physicalHeight = Math.Max(1, (int)Math.Ceiling(height * scaling));
        var x = Math.Clamp(bounds.X, workArea.X, Math.Max(workArea.X, workArea.Right - physicalWidth));
        var y = Math.Clamp(bounds.Y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - physicalHeight));

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = width;
        window.Height = height;
        window.Position = new PixelPoint(x, y);
        return true;
    }

    private void SaveUiState()
    {
        if (_uiStateStore is null)
        {
            return;
        }

        var warning = _uiStateStore.TrySave(_uiState);
        if (warning is null || _uiStateSaveWarningShown)
        {
            return;
        }

        _uiStateSaveWarningShown = true;
        AppendLocalLog(warning);
    }

    private static T AddToGrid<T>(T control, int column)
        where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static IReadOnlyList<NavigationItem> BuildNavigation()
    {
        string? previousGroup = null;
        return WorkspacePageCatalog.All.Select(descriptor =>
        {
            var header = descriptor.Group == previousGroup ? string.Empty : descriptor.Group;
            previousGroup = descriptor.Group;
            return new NavigationItem(descriptor.Page, header, descriptor.Title, descriptor.Subtitle);
        }).ToArray();
    }

    private void HandlePageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PageList.SelectedItem is not NavigationItem selected)
        {
            return;
        }

        if (_uiStateInitialized)
        {
            CaptureTrackedColumnWidths();
        }
        _trackedColumnGrids.Clear();
        PageTitle.Text = selected.Title;
        PageSubtitle.Text = selected.Subtitle;
        PageHost.Content = BuildPage(selected.Page);
        _uiState.SelectedPage = selected.Page.ToString();
        if (_uiStateInitialized)
        {
            SaveUiState();
        }
    }

    private Control BuildPage(WorkspacePage page) => page switch
    {
        WorkspacePage.General => BuildGeneralPage(),
        WorkspacePage.Config => BuildConfigPage(),
        WorkspacePage.Macros => BuildMacrosPage(),
        WorkspacePage.Modules => BuildModulesPage(),
        WorkspacePage.Status => BuildMonitorPage(
            ("status.state", "状态", "基础字段与当前模块", _stateRows),
            ("status.aura", "光环", "光环数值状态", _auraRows),
            ("status.spell", "技能", "冷却与可用状态", _spellRows),
            ("status.dynamic", "动态单位", "模块运行时计算值", _dynamicRows)),
        WorkspacePage.Party => BuildSingleMonitorPage("party", "队伍成员", _partyRows, twoColumns: true),
        WorkspacePage.Logic => BuildSingleMonitorPage("logic", "逻辑信息", _logicRows, twoColumns: true),
        WorkspacePage.Logs => BuildLogsPage(),
        WorkspacePage.About => BuildAboutPage(),
        _ => new TextBlock { Text = page.ToString() }
    };

    private Control BuildGeneralPage()
    {
        var triggerButton = CommandButton(_triggerKey, (sender, _) =>
        {
            _capturingTrigger = true;
            _triggerButton = (Button)sender!;
            _triggerButton.Content = "请按一个键";
            Focus();
        });
        var mode = new ComboBox
        {
            ItemsSource = new[] { "开关 (switch)", "单击 (click)", "按住 (hold)" },
            SelectedIndex = _sendMode switch
            {
                SendMode.Click => 1,
                SendMode.Hold => 2,
                _ => 0
            },
            MinWidth = 220
        };
        AutomationProperties.SetName(mode, "发送模式");
        mode.SelectionChanged += (_, _) =>
        {
            _sendMode = mode.SelectedIndex switch
            {
                1 => SendMode.Click,
                2 => SendMode.Hold,
                _ => SendMode.Switch
            };
            SaveRuntimeSettings();
            _ = RestartRuntimeAfterSettingChangeAsync("发送模式已更改");
        };

        var module = new ComboBox
        {
            ItemsSource = _modules,
            SelectedIndex = IndexOfModuleOption(_selectedModuleId),
            MinWidth = 280
        };
        AutomationProperties.SetName(module, "模块选择");
        module.SelectionChanged += (_, _) =>
        {
            if (_suppressModuleSelection)
            {
                return;
            }

            _selectedModuleId = (module.SelectedItem as ModuleSelectionOption)?.Id;
            _ = RestartRuntimeAfterSettingChangeAsync("模块选择已更改");
        };

        var overlayLayout = new ComboBox
        {
            ItemsSource = new[] { "横向", "纵向" },
            SelectedIndex = _overlayLayout == MacOverlayLayout.Vertical ? 1 : 0,
            MinWidth = 220
        };
        AutomationProperties.SetName(overlayLayout, "浮动条布局");
        overlayLayout.SelectionChanged += (_, _) =>
        {
            var next = overlayLayout.SelectedIndex == 1
                ? MacOverlayLayout.Vertical
                : MacOverlayLayout.Horizontal;
            if (next == _overlayLayout)
            {
                return;
            }

            CaptureOverlayBounds();
            _overlayLayout = next;
            ApplyOverlayLayout(restoreBounds: true);
            if (_overlay?.IsVisible == true)
            {
                RestoreOverlayBounds();
            }
            SaveUiState();
        };

        var configPath = ConfigService.ResolveConfigPath(_baseDirectory);
        var configStatus = new TextBlock
        {
            Text = Directory.Exists(configPath) || File.Exists(configPath) ? "配置资源已就绪" : "找不到配置资源",
            Classes = { "muted" },
            VerticalAlignment = VerticalAlignment.Center
        };
        var validateConfig = CommandButton("验证配置", (_, _) => ValidateConfiguration(configStatus));
        var updateConfig = CommandButton("更新配置", async (_, _) => await UpdateConfigurationAsync(configStatus));
        var importModules = CommandButton("导入旧模块", async (_, _) => await ImportLegacyModulesAsync());

        return ScrollPage(
            Section("输入与运行", "修改后运行会话应以最新设置重启",
                SettingRow("触发键", triggerButton),
                SettingRow("发送模式", mode)),
            BuildPermissionsSection(),
            Section("模块选择", "按实时职业与专精自动匹配，或手动指定模块",
                SettingRow("当前模块", module),
                CommandRow(CommandButton("刷新模块", async (_, _) =>
                {
                    await ImportModuleDependenciesAsync(
                        reloadStore: true,
                        moduleSetChanged: true,
                        showFeedback: true);
                    PageHost.Content = BuildGeneralPage();
                })),
                CommandRow(
                    CommandButton("打开模块站点", async (_, _) => await OpenUriAsync(
                        new Uri(ModuleMarketplaceClient.WebsiteUrl),
                        "模块站点")),
                    CommandButton("打开模块目录", async (_, _) => await OpenDirectoryAsync(
                        _moduleStore.ModuleDirectory,
                        "模块目录",
                        createIfMissing: true)))),
            Section("浮动条", "布局切换会保留横向和纵向各自的位置与大小",
                SettingRow("布局", overlayLayout),
                CommandRow(CommandButton("显示浮动条", (_, _) => ShowOverlay()))),
            Section("配置资源", "当前运行会话读取 Application Support 工作副本",
                SettingRow("状态", configStatus),
                CommandRow(validateConfig, updateConfig)),
            Section("旧模块导入", "显式选择旧 Shigure 数据目录后，仅复制缺失模块",
                CommandRow(importModules)));
    }

    private Control BuildPermissionsSection()
    {
        var screenCaptureStatus = new TextBlock
        {
            Classes = { "muted" },
            VerticalAlignment = VerticalAlignment.Center
        };
        var accessibilityStatus = new TextBlock
        {
            Classes = { "muted" },
            VerticalAlignment = VerticalAlignment.Center
        };

        void Refresh()
        {
            if (_permissions is null)
            {
                screenCaptureStatus.Text = "当前启动方式不可用";
                accessibilityStatus.Text = "当前启动方式不可用";
                return;
            }

            try
            {
                var snapshot = _permissions.Check();
                screenCaptureStatus.Text = DescribePermission(snapshot.ScreenCapture);
                accessibilityStatus.Text = DescribePermission(snapshot.Accessibility);
            }
            catch (Exception exception)
            {
                screenCaptureStatus.Text = $"检查失败 · {exception.GetType().Name}";
                accessibilityStatus.Text = $"检查失败 · {exception.GetType().Name}";
            }
        }

        Refresh();
        Button? requestScreenCapture = null;
        Button? requestAccessibility = null;
        Button? refresh = null;

        void SetPermissionCommandsEnabled(bool enabled)
        {
            requestScreenCapture!.IsEnabled = enabled;
            requestAccessibility!.IsEnabled = enabled;
            refresh!.IsEnabled = enabled;
        }

        requestScreenCapture = CommandButton(
            "请求屏幕录制",
            async (_, _) => await RequestPermissionAsync(
                PlatformPermissionKind.ScreenCapture,
                Refresh,
                SetPermissionCommandsEnabled));
        requestAccessibility = CommandButton(
            "请求辅助功能",
            async (_, _) => await RequestPermissionAsync(
                PlatformPermissionKind.Accessibility,
                Refresh,
                SetPermissionCommandsEnabled));
        refresh = CommandButton("刷新权限状态", (_, _) => Refresh());
        requestScreenCapture.IsEnabled = _permissions is not null;
        requestAccessibility.IsEnabled = _permissions is not null;
        refresh.IsEnabled = _permissions is not null;

        return Section(
            "系统权限",
            "权限只在点击请求按钮时提示；检查状态不会触发系统弹窗",
            SettingRow("屏幕录制", screenCaptureStatus),
            SettingRow("辅助功能", accessibilityStatus),
            CommandRow(requestScreenCapture, requestAccessibility, refresh));
    }

    private async Task RequestPermissionAsync(
        PlatformPermissionKind permission,
        Action refresh,
        Action<bool> setCommandsEnabled)
    {
        if (_permissions is null || !await _permissionRequestGate.WaitAsync(0))
        {
            return;
        }

        setCommandsEnabled(false);
        var wasRunning = _runtime.Status.IsRunning;
        var restoreRuntime = wasRunning;
        try
        {
            if (wasRunning)
            {
                await _runtime.StopAsync();
            }

            var result = _permissions.Request(permission);
            refresh();
            restoreRuntime = wasRunning && result.Permission.IsReady;
            var title = permission == PlatformPermissionKind.ScreenCapture ? "屏幕录制权限" : "辅助功能权限";
            var message = result.Outcome switch
            {
                PlatformPermissionRequestOutcome.AlreadyGranted => "权限已经可用。",
                PlatformPermissionRequestOutcome.Granted => "权限已经授权。",
                PlatformPermissionRequestOutcome.RestartRequired => "权限已经授权，需要退出并重新打开 Shigure。",
                _ => "请在系统设置中完成授权，然后返回刷新权限状态。"
            };
            SetGlobalStatus(message);
            await ShowMessageAsync(title, message);
        }
        catch (Exception exception)
        {
            refresh();
            SetGlobalStatus("权限请求失败");
            await ShowMessageAsync("权限请求失败", exception.Message);
        }
        finally
        {
            if (restoreRuntime && !_shutdownPrepared)
            {
                try
                {
                    await _runtime.StartAsync(BuildRuntimeOptions());
                }
                catch (Exception exception)
                {
                    await ShowMessageAsync("运行时恢复失败", exception.Message);
                }
            }

            setCommandsEnabled(true);
            _permissionRequestGate.Release();
        }
    }

    private static string DescribePermission(PlatformPermissionStatus permission)
    {
        if (permission.RestartRequired)
        {
            return "已授权 · 需要重启应用";
        }

        return permission.State == PlatformPermissionState.Granted ? "已授权" : "未授权";
    }

    private Control BuildConfigPage()
    {
        if (_configUpdates is null)
        {
            return new TextBlock
            {
                Text = "当前启动方式未提供配置更新服务。",
                Classes = { "muted" }
            };
        }

        return _configEditor ??= new ConfigEditorView(
            this,
            _configUpdates,
            () => RestartRuntimeAfterSettingChangeAsync("职业配置已保存"),
            SetGlobalStatus);
    }

    private Control BuildMacrosPage()
    {
        if (_configUpdates is null)
        {
            return new TextBlock
            {
                Text = "当前启动方式未提供宏更新服务。",
                Classes = { "muted" }
            };
        }

        return _macroEditor ??= new MacroEditorView(
            this,
            _configUpdates,
            () => RestartRuntimeAfterSettingChangeAsync("职业宏已保存"),
            SetGlobalStatus);
    }

    private Control BuildModulesPage()
    {
        if (_moduleDependencies is null)
        {
            return new TextBlock
            {
                Text = "当前启动方式未提供模块依赖服务。",
                Classes = { "muted" }
            };
        }

        return _moduleEditor ??= new ModuleEditorView(
            this,
            _moduleStore,
            _moduleMarketplace,
            _moduleDependencies.Capture,
            (reloadStore, moduleSetChanged) => ImportModuleDependenciesAsync(
                reloadStore,
                moduleSetChanged,
                showFeedback: true),
            HandleModulesChanged,
            SetGlobalStatus);
    }

    private void RefreshModuleNames()
    {
        var selectedId = _selectedModuleId;
        _suppressModuleSelection = true;
        try
        {
            _modules.Clear();
            _modules.Add(new ModuleSelectionOption(null, "自动选择"));
            foreach (var module in _moduleStore.GetModules())
            {
                _modules.Add(new ModuleSelectionOption(
                    module.Id,
                    module.Enabled ? module.Name : $"[停用] {module.Name}"));
            }
        }
        finally
        {
            _suppressModuleSelection = false;
        }

        _selectedModuleId = selectedId is not null && _modules.Any(option => option.Id == selectedId)
            ? selectedId
            : null;
    }

    private void HandleModulesChanged()
    {
        RefreshModuleNames();
        _ = RestartRuntimeAfterSettingChangeAsync("模块已变更");
    }

    private async Task<bool> ImportModuleDependenciesAsync(
        bool reloadStore,
        bool moduleSetChanged,
        bool showFeedback)
    {
        if (_moduleDependencies is null)
        {
            return false;
        }

        await _moduleImportGate.WaitAsync();
        try
        {
            var hasModuleDraft = (reloadStore || moduleSetChanged)
                && _moduleEditor?.HasUnsavedChanges == true;
            if (_configEditor?.HasUnsavedChanges == true
                || _macroEditor?.HasUnsavedChanges == true
                || hasModuleDraft)
            {
                if (moduleSetChanged && !reloadStore)
                {
                    RefreshModuleNames();
                }

                var message = hasModuleDraft
                    ? "模块页面存在未保存修改。请先保存或放弃修改，再刷新模块。"
                    : "配置或宏页面存在未保存修改。请先保存或放弃修改，再导入模块依赖。";
                SetGlobalStatus("模块依赖未导入");
                if (showFeedback)
                {
                    await ShowMessageAsync("模块依赖未导入", message);
                }
                return false;
            }

            if (reloadStore)
            {
                _moduleStore.Reload();
            }

            ModuleDependencyImportResult result;
            try
            {
                result = _moduleDependencies.Import(_moduleStore.GetModules());
            }
            catch (Exception exception)
            {
                AppendLocalLog($"模块依赖导入失败：{exception.Message}");
                if (showFeedback)
                {
                    await ShowMessageAsync("模块依赖导入失败", exception.Message);
                }
                return false;
            }

            _moduleStore.RejectModules(result.Rejected.Select(item => item.ModuleId));
            RefreshModuleNames();
            if (reloadStore || moduleSetChanged || result.Rejected.Count > 0)
            {
                _moduleEditor?.ReloadModulesFromStore();
            }

            foreach (var rejected in result.Rejected)
            {
                AppendLocalLog($"模块“{rejected.ModuleName}”未导入：{rejected.Reason}");
            }
            foreach (var conflict in result.Conflicts.Take(50))
            {
                AppendLocalLog($"模块依赖冲突：{conflict}");
            }

            string? postUpdateError = null;
            string? syncWarning = null;
            if (result.HasChanges)
            {
                AppendLocalLog(
                    $"已从模块补充本地依赖：配置 {result.ConfigAdded} 项，宏 {result.MacrosAdded} 项；模块 {string.Join("、", result.ChangedModules)}");
                if (_configEditor is not null)
                {
                    await _configEditor.ReloadFromAddonAsync();
                }
                if (_macroEditor is not null)
                {
                    await _macroEditor.ReloadFromAddonAsync();
                }

                try
                {
                    if (_configUpdates is null)
                    {
                        throw new InvalidOperationException("配置更新服务不可用。");
                    }

                    var update = _configUpdates.Update();
                    if (!update.AddonSync.CompletedSuccessfully)
                    {
                        syncWarning = update.AddonSync.SkippedReason
                            ?? $"游戏插件同步失败 {update.AddonSync.Failures.Count} 项";
                        AppendLocalLog($"模块依赖的本地派生资源已更新，但{syncWarning}。");
                    }
                }
                catch (Exception exception)
                {
                    postUpdateError = exception.Message;
                    AppendLocalLog($"模块依赖已写入，但后续配置更新失败：{exception.Message}");
                }
            }

            if (postUpdateError is null
                && (moduleSetChanged || result.Rejected.Count > 0 || result.HasChanges))
            {
                await RestartRuntimeAfterSettingChangeAsync("模块或依赖已更新");
            }

            var lines = new List<string>();
            if (result.HasChanges)
            {
                lines.Add($"成功补充配置 {result.ConfigAdded} 项、宏 {result.MacrosAdded} 项。");
            }
            if (result.Rejected.Count > 0)
            {
                lines.Add($"未导入模块 {result.Rejected.Count} 个；详情见日志。");
            }
            if (result.Conflicts.Count > 0)
            {
                lines.Add($"发现 {result.Conflicts.Count} 项冲突，均已保留本地内容；详情见日志。");
            }
            if (postUpdateError is not null)
            {
                lines.Add($"本地依赖已写入，但 config/keymap 更新失败：{postUpdateError}");
            }
            if (syncWarning is not null)
            {
                lines.Add($"本地派生资源已更新，但{syncWarning}。");
            }
            if (lines.Count == 0)
            {
                lines.Add($"模块依赖已是最新；已加载 {_moduleStore.GetModules().Count} 个本地模块。");
            }

            var feedback = string.Join(Environment.NewLine, lines);
            SetGlobalStatus(feedback);
            if (showFeedback
                && (result.HasChanges || result.Rejected.Count > 0 || result.Conflicts.Count > 0 || postUpdateError is not null))
            {
                await ShowMessageAsync(
                    postUpdateError is null && result.Rejected.Count == 0
                        ? "模块依赖导入完成"
                        : "模块依赖导入完成（有警告）",
                    feedback);
            }

            return true;
        }
        finally
        {
            _moduleImportGate.Release();
        }
    }

    private int IndexOfModuleOption(string? moduleId)
    {
        for (var index = 0; index < _modules.Count; index++)
        {
            if (string.Equals(_modules[index].Id, moduleId, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return 0;
    }

    private Control BuildMonitorPage(params (string Key, string Title, string Subtitle, IReadOnlyList<RuntimeDisplayRow> Rows)[] sections)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("*,*"), ColumnSpacing = 18, RowSpacing = 18 };
        for (var index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            var control = Section(section.Title, section.Subtitle, MonitorGrid(section.Key, section.Rows, false));
            Grid.SetColumn(control, index % 2);
            Grid.SetRow(control, index / 2);
            grid.Children.Add(control);
        }

        return grid;
    }

    private Control BuildSingleMonitorPage(string key, string title, IReadOnlyList<RuntimeDisplayRow> rows, bool twoColumns) =>
        Section(title, "当前运行会话的共享快照投影", MonitorGrid(key, rows, twoColumns));

    private DataGrid MonitorGrid(string key, IReadOnlyList<RuntimeDisplayRow> rows, bool twoColumns)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            IsReadOnly = true,
            AutoGenerateColumns = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            CanUserResizeColumns = true
        };
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = twoColumns ? "名称" : "#",
            Binding = new Avalonia.Data.Binding(nameof(RuntimeDisplayRow.First)),
            Width = new DataGridLength(twoColumns ? 160 : 58)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = twoColumns ? "值" : "名称",
            Binding = new Avalonia.Data.Binding(nameof(RuntimeDisplayRow.Second)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        if (!twoColumns)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = "值",
                Binding = new Avalonia.Data.Binding(nameof(RuntimeDisplayRow.Third)),
                Width = new DataGridLength(2, DataGridLengthUnitType.Star)
            });
        }

        return TrackColumnGrid(key, grid);
    }

    private Control BuildLogsPage()
    {
        _runtimeLogBox = new TextBox
        {
            AcceptsReturn = true,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Menlo"),
            Text = string.Join(Environment.NewLine, _logLines)
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_runtimeLogBox, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_runtimeLogBox, ScrollBarVisibility.Auto);
        AutomationProperties.SetName(_runtimeLogBox, "运行日志");
        var autoScroll = new CheckBox
        {
            Content = "自动滚动",
            IsChecked = _autoScrollLogs,
            VerticalAlignment = VerticalAlignment.Center
        };
        autoScroll.IsCheckedChanged += (_, _) => _autoScrollLogs = autoScroll.IsChecked == true;
        return EditorPage(
            _runtimeLogBox,
            CommandButton("复制", async (_, _) =>
            {
                var clipboard = GetTopLevel(this)?.Clipboard;
                if (clipboard is not null)
                {
                    await clipboard.SetTextAsync(_runtimeLogBox.Text ?? string.Empty);
                }
            }),
            CommandButton("清空", async (_, _) =>
            {
                if (await ShowConfirmationAsync("清空当前日志？", "清空"))
                {
                    _logLines.Clear();
                    _runtimeLogBox.Text = string.Empty;
                }
            }),
            autoScroll);
    }

    private Control BuildAboutPage()
    {
        var fieldsSource = LoadConfiguredFields();
        var fields = new DataGrid
        {
            ItemsSource = fieldsSource,
            IsReadOnly = true,
            AutoGenerateColumns = false
        };
        fields.Columns.Add(new DataGridTextColumn
        {
            Header = "名称",
            Binding = new Avalonia.Data.Binding(nameof(AboutFieldRow.Name)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });
        fields.Columns.Add(new DataGridTextColumn
        {
            Header = "分类",
            Binding = new Avalonia.Data.Binding(nameof(AboutFieldRow.Category)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        TrackColumnGrid("about.fields", fields);
        var version = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Split('+')[0]
            ?? typeof(App).Assembly.GetName().Version?.ToString()
            ?? "未知";
        var configPath = ConfigService.ResolveConfigPath(_baseDirectory);
        return ScrollPage(
            Section($"Shigure {version}", "macOS 控制中心",
                SettingRow("UI 框架", new TextBlock { Text = "Avalonia 12.1.1" }),
                SettingRow("业务状态", new TextBlock { Text = _runtime.Status.Message }),
                SettingRow("模块目录", PathText(_moduleStore.ModuleDirectory)),
                SettingRow("配置目录", PathText(configPath)),
                CommandRow(
                    CommandButton("打开模块目录", async (_, _) => await OpenDirectoryAsync(
                        _moduleStore.ModuleDirectory,
                        "模块目录",
                        createIfMissing: true)),
                    CommandButton("复制模块路径", async (_, _) => await CopyTextAsync(_moduleStore.ModuleDirectory))),
                CommandRow(
                    CommandButton("打开配置目录", async (_, _) => await OpenDirectoryAsync(
                        configPath,
                        "配置目录")),
                    CommandButton("复制配置路径", async (_, _) => await CopyTextAsync(configPath)))),
            Section("可用状态字段", $"当前配置加载 {fieldsSource.Count} 个字段", fields));
    }

    private void HandleTriggerCapture(object? sender, KeyEventArgs e)
    {
        if (!_capturingTrigger || _triggerButton is null)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            _capturingTrigger = false;
            _triggerButton.Content = _triggerKey;
            e.Handled = true;
            return;
        }

        var triggerName = MapTriggerKey(e.Key);
        if (triggerName is null)
        {
            _capturingTrigger = false;
            _triggerButton.Content = e.Key is Key.LeftAlt or Key.RightAlt ? "ALT 不支持" : "不支持";
            AppendLocalLog("该触发键不受支持");
            e.Handled = true;
            return;
        }

        CommitTriggerCapture(triggerName);
        PageHost.Content = BuildGeneralPage();
        e.Handled = true;
    }

    private async Task ToggleRuntimeAsync()
    {
        try
        {
            if (_runtime.Status.IsRunning)
            {
                await _runtime.StopAsync();
            }
            else
            {
                await _runtime.StartAsync(BuildRuntimeOptions());
            }
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("运行时操作失败", exception.Message);
        }
    }

    private AppOptions BuildRuntimeOptions() => new(
        _triggerKey,
        _sendMode,
        _selectedModuleId,
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250));

    private async Task RestartRuntimeAfterSettingChangeAsync(string reason)
    {
        if (!_runtime.Status.IsRunning || _shutdownPrepared)
        {
            return;
        }

        AppendLocalLog($"{reason}，正在重启运行时");
        try
        {
            await _runtime.RestartAsync(BuildRuntimeOptions());
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("重启失败", exception.Message);
        }
    }

    private void HandleRuntimeStatusChanged(RuntimeSessionStatus status) =>
        PostToUi(() => ApplyRuntimeStatus(status));

    private void HandleRuntimeSnapshotUpdated(RenderSnapshot snapshot)
    {
        lock (_runtimeSnapshotSync)
        {
            if (_shutdownPrepared)
            {
                return;
            }

            _pendingRuntimeSnapshot = snapshot;
            if (_runtimeSnapshotDispatchPending)
            {
                return;
            }

            _runtimeSnapshotDispatchPending = true;
        }

        Dispatcher.UIThread.Post(ApplyPendingRuntimeSnapshot);
    }

    private void ApplyPendingRuntimeSnapshot()
    {
        RenderSnapshot? snapshot;
        lock (_runtimeSnapshotSync)
        {
            snapshot = _pendingRuntimeSnapshot;
            _pendingRuntimeSnapshot = null;
        }

        try
        {
            if (snapshot is not null && !_shutdownPrepared)
            {
                ApplyRuntimeSnapshot(snapshot);
            }
        }
        finally
        {
            var postNext = false;
            lock (_runtimeSnapshotSync)
            {
                if (_shutdownPrepared || _pendingRuntimeSnapshot is null)
                {
                    _runtimeSnapshotDispatchPending = false;
                }
                else
                {
                    postNext = true;
                }
            }

            if (postNext)
            {
                Dispatcher.UIThread.Post(ApplyPendingRuntimeSnapshot);
            }
        }
    }

    private void ApplyRuntimeSnapshot(RenderSnapshot snapshot)
    {
        if (_lastObservedLogicEnabled is bool previousEnabled
            && previousEnabled != snapshot.Enabled)
        {
            ShowLogicToast(snapshot.Enabled);
        }
        _lastObservedLogicEnabled = snapshot.Enabled;
        ApplyMonitor(RuntimeMonitorProjection.Create(snapshot));
        EnableButton.Content = snapshot.Enabled ? "关闭逻辑" : "开启逻辑";
        SetGlobalStatus(string.IsNullOrWhiteSpace(snapshot.ScanFailureReason)
            ? $"运行中 · {(snapshot.Enabled ? "逻辑开启" : "逻辑关闭")}"
            : $"运行中 · {snapshot.ScanFailureReason}");
        if (_overlayStatus is not null)
        {
            _overlayStatus.Text = BuildOverlayStatus(snapshot);
        }
    }

    private void HandleRuntimeLogAdded(RuntimeLogEntry entry) =>
        PostToUi(() => AppendLog(entry));

    private void ApplyRuntimeStatus(RuntimeSessionStatus status)
    {
        if (status.State is RuntimeSessionState.Starting
            or RuntimeSessionState.Stopping
            or RuntimeSessionState.Stopped)
        {
            _lastObservedLogicEnabled = null;
        }

        SetGlobalStatus(status.Message);
        RunButton.IsEnabled = !status.IsBusy;
        RunButton.Content = status.State switch
        {
            RuntimeSessionState.Running => "停止运行",
            RuntimeSessionState.Starting => "正在启动",
            RuntimeSessionState.Stopping => "正在停止",
            RuntimeSessionState.Faulted => "重新启动",
            _ => "启动运行"
        };
        EnableButton.IsEnabled = status.IsRunning;
        if (!status.IsRunning)
        {
            EnableButton.Content = "开启逻辑";
        }

        if (_overlayStatus is not null && !status.IsRunning)
        {
            _overlayStatus.Text = status.Message;
        }
    }

    private void ApplyMonitor(RuntimeMonitorView monitor)
    {
        ReplaceRows(_stateRows, monitor.State);
        ReplaceRows(_auraRows, monitor.Auras);
        ReplaceRows(_dynamicRows, monitor.DynamicValues);
        ReplaceRows(_spellRows, monitor.Spells);
        ReplaceRows(_partyRows, monitor.Party);
        ReplaceRows(_logicRows, monitor.Logic);
    }

    private static void ReplaceRows(
        ObservableCollection<RuntimeDisplayRow> target,
        IReadOnlyList<RuntimeDisplayRow> rows)
    {
        if (RowsMatch(target, rows))
        {
            return;
        }

        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    private static bool RowsMatch(
        IReadOnlyList<RuntimeDisplayRow> current,
        IReadOnlyList<RuntimeDisplayRow> next)
    {
        if (current.Count != next.Count)
        {
            return false;
        }

        for (var index = 0; index < current.Count; index++)
        {
            if (current[index] != next[index])
            {
                return false;
            }
        }

        return true;
    }

    private void AppendLocalLog(string message) =>
        AppendLog(new RuntimeLogEntry(DateTimeOffset.UtcNow, message));

    private void AppendLog(RuntimeLogEntry entry)
    {
        _logLines.Add($"[{entry.Timestamp.ToLocalTime():HH:mm:ss}] {entry.Message}");
        if (_logLines.Count > MaximumLogLines)
        {
            _logLines.RemoveRange(0, _logLines.Count - MaximumLogLines);
        }

        if (_runtimeLogBox is null)
        {
            return;
        }

        _runtimeLogBox.Text = string.Join(Environment.NewLine, _logLines);
        if (_autoScrollLogs)
        {
            _runtimeLogBox.CaretIndex = _runtimeLogBox.Text.Length;
        }
    }

    private void SetGlobalStatus(string message) => GlobalStatus.Text = message;

    private static string BuildOverlayStatus(RenderSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return "运行时已停止";
        }

        var classSpec = snapshot.ClassName is null
            ? "等待职业"
            : $"{snapshot.ClassName} · {snapshot.SpecName ?? "未知专精"}";
        var module = string.IsNullOrWhiteSpace(snapshot.ModuleName) ? "等待模块" : snapshot.ModuleName;
        return $"{(snapshot.Enabled ? "逻辑开启" : "逻辑关闭")} · {classSpec} · {module}";
    }

    private void ValidateConfiguration(TextBlock status)
    {
        try
        {
            var config = ConfigService.LoadFromBaseDirectory(_baseDirectory);
            var fieldCount = config.BuildStateConfig(null, null).Count;
            status.Text = $"配置可用 · 公共字段 {fieldCount}";
            AppendLocalLog($"配置验证通过：公共字段 {fieldCount}");
        }
        catch (Exception exception)
        {
            status.Text = $"配置无效 · {exception.GetType().Name}";
            AppendLocalLog($"配置验证失败：{exception.GetType().Name}");
        }
    }

    private async Task UpdateConfigurationAsync(TextBlock status)
    {
        if (_configUpdates is null)
        {
            status.Text = "配置更新服务不可用";
            return;
        }

        status.Text = "正在更新配置并同步插件…";
        try
        {
            var result = await Task.Run(() => _configUpdates.Update());
            var warningCount = result.Config.Warnings.Count + (result.Keymap?.Warnings.Count ?? 0);
            var sync = result.AddonSync;
            status.Text = sync.CompletedSuccessfully
                ? $"已更新 · 警告 {warningCount}"
                : $"本地已更新 · {sync.SkippedReason ?? $"同步失败 {sync.Failures.Count} 项"}";
            SetGlobalStatus(status.Text);
            await RestartRuntimeAfterSettingChangeAsync("配置资源已更新");
        }
        catch (Exception exception)
        {
            status.Text = $"更新失败 · {exception.GetType().Name}";
            await ShowMessageAsync("配置更新失败", exception.Message);
        }
    }

    private async Task ImportLegacyModulesAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择包含 module 文件夹的旧 Shigure 数据目录",
            AllowMultiple = false
        });
        if (folders.Count == 0)
        {
            return;
        }

        var sourcePath = folders[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            await ShowMessageAsync("无法导入", "所选目录不是可访问的本地目录。");
            return;
        }

        if (!await ShowConfirmationAsync(
                "将只复制旧目录中目标尚不存在的模块；不会删除旧文件或覆盖现有模块。是否继续？",
                "开始导入"))
        {
            return;
        }

        var wasRunning = _runtime.Status.IsRunning;
        try
        {
            if (wasRunning)
            {
                await _runtime.StopAsync();
            }

            var targetUserData = Path.GetDirectoryName(_moduleStore.ModuleDirectory)
                ?? throw new InvalidOperationException("无法确定用户数据目录。");
            var result = await Task.Run(() =>
                new LegacyModuleMigrationService().Migrate(sourcePath, targetUserData));
            _moduleStore.Reload();
            RefreshModuleNames();
            PageHost.Content = BuildGeneralPage();

            var message = result.Failures.Count > 0
                ? $"导入失败 {result.Failures.Count} 项；未覆盖现有模块。"
                : result.SkippedReason is not null
                    ? "所选目录没有可导入的 module 目录。"
                    : result.AlreadyCompleted
                        ? "该目录已经完成过导入。"
                        : $"已导入 {result.CopiedFiles.Count} 个模块，保留 {result.PreservedFiles.Count} 个现有模块。";
            AppendLocalLog(message);
            await ShowMessageAsync("旧模块导入", message);
        }
        catch (Exception exception)
        {
            AppendLocalLog($"旧模块导入失败：{exception.GetType().Name}");
            await ShowMessageAsync("旧模块导入失败", exception.Message);
        }
        finally
        {
            if (wasRunning && !_shutdownPrepared)
            {
                try
                {
                    await _runtime.StartAsync(BuildRuntimeOptions());
                }
                catch (Exception exception)
                {
                    await ShowMessageAsync("运行时恢复失败", exception.Message);
                }
            }
        }
    }

    private List<AboutFieldRow> LoadConfiguredFields()
    {
        try
        {
            var config = ConfigService.LoadFromBaseDirectory(_baseDirectory);
            var fields = new HashSet<AboutFieldRow>();
            AddFields(config.BuildStateConfig(null, null));
            foreach (var (classId, _) in ClassNames.GetClasses())
            {
                foreach (var (specId, _) in ClassNames.GetSpecs(classId))
                {
                    AddFields(config.BuildStateConfig(classId, specId));
                }
            }

            return fields
                .OrderBy(entry => entry.Category, StringComparer.Ordinal)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                .ToList();

            void AddFields(JsonObject stateConfig)
            {
                foreach (var (name, value) in stateConfig)
                {
                    var category = name switch
                    {
                        "auras" => "光环",
                        "spells" => "技能",
                        _ => "状态"
                    };
                    if (value is JsonObject nested && category is "光环" or "技能")
                    {
                        foreach (var fieldName in nested.Select(entry => entry.Key))
                        {
                            fields.Add(new AboutFieldRow(fieldName, category));
                        }
                    }
                    else
                    {
                        fields.Add(new AboutFieldRow(name, category));
                    }
                }
            }
        }
        catch
        {
            return [];
        }
    }

    private static TextBlock PathText(string path) => new()
    {
        Text = path,
        TextWrapping = TextWrapping.Wrap,
        Classes = { "muted" }
    };

    private async Task CopyTextAsync(string text)
    {
        var clipboard = GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private async Task OpenUriAsync(Uri uri, string displayName)
    {
        try
        {
            var launcher = GetTopLevel(this)?.Launcher;
            if (launcher is null || !await launcher.LaunchUriAsync(uri))
            {
                await ShowMessageAsync("无法打开", $"无法打开{displayName}。");
            }
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("无法打开", $"无法打开{displayName}：{exception.Message}");
        }
    }

    private async Task OpenDirectoryAsync(
        string path,
        string displayName,
        bool createIfMissing = false)
    {
        try
        {
            if (createIfMissing)
            {
                Directory.CreateDirectory(path);
            }
            else if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"目录不存在：{path}");
            }

            var launcher = GetTopLevel(this)?.Launcher;
            if (launcher is null || !await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path)))
            {
                await ShowMessageAsync("无法打开", $"无法打开{displayName}。");
            }
        }
        catch (Exception exception)
        {
            await ShowMessageAsync("无法打开", $"无法打开{displayName}：{exception.Message}");
        }
    }

    private void HandleTriggerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_capturingTrigger || _triggerButton is null)
        {
            return;
        }

        var properties = e.GetCurrentPoint(this).Properties;
        var triggerName = properties.IsMiddleButtonPressed
            ? "MIDDLE"
            : properties.IsXButton1Pressed
                ? "XBUTTON1"
                : properties.IsXButton2Pressed
                    ? "XBUTTON2"
                    : null;
        if (triggerName is null)
        {
            return;
        }

        CommitTriggerCapture(triggerName);
        e.Handled = true;
    }

    private void HandleTriggerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (!_capturingTrigger || _triggerButton is null || e.Delta.Y == 0)
        {
            return;
        }

        CommitTriggerCapture(e.Delta.Y > 0 ? "WHEELUP" : "WHEELDOWN");
        e.Handled = true;
    }

    private void CommitTriggerCapture(string triggerName)
    {
        _capturingTrigger = false;
        _triggerKey = triggerName;
        _triggerButton!.Content = triggerName;
        AppendLocalLog($"已录入触发键：{triggerName}");
        SaveRuntimeSettings();
        _ = RestartRuntimeAfterSettingChangeAsync("触发键已更改");
    }

    private void SaveRuntimeSettings()
    {
        _uiState.TriggerKey = _triggerKey;
        _uiState.SendMode = _sendMode;
        SaveUiState();
    }

    private void ShowLogicToast(bool enabled)
    {
        if (_logicToast is null)
        {
            _logicToastText = new TextBlock
            {
                FontSize = 36,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            _logicToast = new Window
            {
                Width = 420,
                Height = 90,
                CanResize = false,
                WindowDecorations = WindowDecorations.None,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Background = Brushes.Transparent,
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
                IsHitTestVisible = false,
                Content = _logicToastText
            };
            AutomationProperties.SetName(_logicToast, "逻辑开关状态提示");
            _logicToast.Opened += (_, _) => MacWindowInteraction.MakeClickThrough(_logicToast);
        }

        _logicToastText!.Text = enabled ? "逻辑已开启" : "逻辑已关闭";
        _logicToastText.Foreground = new SolidColorBrush(Color.Parse(enabled ? "#6EE7B7" : "#FCA5A5"));
        _logicToastTimer.Stop();
        _logicToast.Show();
        MacWindowInteraction.MakeClickThrough(_logicToast);

        var screen = _logicToast.Screens.Primary;
        if (screen is not null)
        {
            var scale = Math.Max(0.25, screen.Scaling);
            var width = (int)Math.Round(_logicToast.Width * scale);
            var height = (int)Math.Round(_logicToast.Height * scale);
            _logicToast.Position = new PixelPoint(
                screen.Bounds.X + (screen.Bounds.Width - width) / 2,
                screen.Bounds.Y + (screen.Bounds.Height - height) / 2);
        }

        _logicToastTimer.Start();
    }

    private static string? MapTriggerKey(Key key)
    {
        var name = key.ToString().ToUpperInvariant();
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]))
        {
            return name;
        }

        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
        {
            return name[1].ToString();
        }

        if (name.StartsWith("F", StringComparison.Ordinal)
            && int.TryParse(name[1..], out var functionKey)
            && functionKey is >= 1 and <= 12)
        {
            return name;
        }

        if (name.StartsWith("NUMPAD", StringComparison.Ordinal))
        {
            return name;
        }

        return name switch
        {
            "RETURN" or "ENTER" => "ENTER",
            "TAB" => "TAB",
            "SPACE" => "SPACE",
            "BACK" or "BACKSPACE" => "BACKSPACE",
            "OEMCOMMA" => ",",
            "OEMPERIOD" => ".",
            "OEM2" => "/",
            "OEM1" => ";",
            "OEM7" => "'",
            "OEM4" => "[",
            "OEM6" => "]",
            "OEMPLUS" => "=",
            "OEMMINUS" => "-",
            "OEM3" => "`",
            "OEM5" => "\\",
            _ => null
        };
    }

    private void PostToUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private static Button CommandButton(string text, EventHandler<RoutedEventArgs> handler)
    {
        var button = new Button { Content = text, Classes = { "command" } };
        button.Click += handler;
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Control ScrollPage(params Control[] sections)
    {
        var panel = new StackPanel { Spacing = 22 };
        foreach (var section in sections)
        {
            panel.Children.Add(section);
        }

        return new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel
        };
    }

    private static Border Section(string title, string subtitle, params Control[] content)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = title, Classes = { "section-title" } });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Children.Add(new TextBlock { Text = subtitle, Classes = { "muted" }, FontSize = 12 });
        }
        foreach (var item in content)
        {
            stack.Children.Add(item);
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#30353A")),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 0, 0, 20),
            Child = stack
        };
    }

    private static Grid SettingRow(string label, Control control)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("180,*"), MinHeight = 38 };
        row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.Parse("#AEB5BA")) });
        row.Children.Add(AddToGrid(control, 1));
        return row;
    }

    private static StackPanel CommandRow(params Control[] controls)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }
        return panel;
    }

    private static Grid EditorPage(Control editor, params Control[] actions)
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 12 };
        root.Children.Add(editor);
        root.Children.Add(AddToGridRow(CommandRow(actions), 1));
        return root;
    }

    private static DataGrid EditableGrid<T>(ObservableCollection<T> items, params DataGridColumn[] columns)
    {
        var grid = new DataGrid
        {
            ItemsSource = items,
            AutoGenerateColumns = false,
            CanUserReorderColumns = true,
            CanUserResizeColumns = true,
            SelectionMode = DataGridSelectionMode.Single
        };
        foreach (var column in columns)
        {
            grid.Columns.Add(column);
        }
        return grid;
    }

    private async Task<bool> ShowConfirmationAsync(string message, string confirmText = "删除")
    {
        var result = false;
        var dialog = new Window
        {
            Title = "确认",
            Width = 380,
            Height = 170,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancel = new Button { Content = "取消", MinWidth = 90 };
        var confirm = new Button { Content = confirmText, MinWidth = 90 };
        cancel.Click += (_, _) => dialog.Close();
        confirm.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = message, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                AddToGridRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, confirm }
                }, 1)
            }
        };
        await dialog.ShowDialog(this);
        return result;
    }

    internal async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var close = new Button { Content = "确定", MinWidth = 90 };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(24),
            Children =
            {
                new TextBlock { Text = message, FontSize = 14, TextWrapping = TextWrapping.Wrap },
                AddToGridRow(close, 1)
            }
        };
        close.HorizontalAlignment = HorizontalAlignment.Right;
        await dialog.ShowDialog(this);
    }

    private static T AddToGridRow<T>(T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static RenderSnapshot CreateEmptySnapshot() => new(
        false,
        null,
        null,
        null,
        null,
        null,
        null,
        "等待启动",
        new Dictionary<string, object?>(),
        [],
        null);

    private sealed record NavigationItem(WorkspacePage Page, string GroupHeader, string Title, string Subtitle);

    private sealed record ModuleSelectionOption(string? Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record AboutFieldRow(string Name, string Category);

}
