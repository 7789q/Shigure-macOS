using System.Collections.ObjectModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Shigure.MacUI;

public sealed class ModuleMarketplaceWindow : Window
{
    private readonly ModuleStore _moduleStore;
    private readonly ModuleMarketplaceClient _marketplace;
    private readonly Action _modulesChanged;
    private readonly ObservableCollection<ModuleShareSummary> _visibleShares = [];
    private readonly TextBox _search = new() { PlaceholderText = "搜索名称、作者、职业或描述" };
    private readonly ComboBox _profession = new() { MinWidth = 150 };
    private readonly TextBlock _status = new() { Text = "正在读取社区模块…" };
    private readonly DataGrid _grid;
    private readonly Button _refreshButton;
    private readonly Button _downloadButton;
    private IReadOnlyList<ModuleShareSummary> _allShares = [];

    public ModuleMarketplaceWindow(
        ModuleStore moduleStore,
        ModuleMarketplaceClient marketplace,
        Action modulesChanged)
    {
        _moduleStore = moduleStore;
        _marketplace = marketplace;
        _modulesChanged = modulesChanged;

        Title = "下载社区模块";
        Width = 980;
        Height = 620;
        MinWidth = 760;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.Parse("#111315"));

        _grid = new DataGrid
        {
            ItemsSource = _visibleShares,
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        _grid.Columns.Add(TextColumn("模块", nameof(ModuleShareSummary.Filename), 1.7));
        _grid.Columns.Add(TextColumn("职业", nameof(ModuleShareSummary.Profession), 0.8));
        _grid.Columns.Add(TextColumn("专精", nameof(ModuleShareSummary.Specialization), 0.9));
        _grid.Columns.Add(TextColumn("作者", nameof(ModuleShareSummary.Author), 0.9));
        _grid.Columns.Add(TextColumn("版本", nameof(ModuleShareSummary.Version), 0.8));
        _grid.Columns.Add(TextColumn("下载", nameof(ModuleShareSummary.DownloadCount), 0.55));
        _grid.Columns.Add(TextColumn("说明", nameof(ModuleShareSummary.Description), 2.1));
        AutomationProperties.SetName(_grid, "社区模块列表");

        _refreshButton = CommandButton("刷新", async (_, _) => await LoadAsync());
        _downloadButton = CommandButton("下载所选模块", async (_, _) => await DownloadSelectedAsync());
        _downloadButton.IsEnabled = false;
        var closeButton = CommandButton("关闭", (_, _) => Close());

        _search.TextChanged += (_, _) => ApplyFilter();
        _profession.SelectionChanged += (_, _) => ApplyFilter();
        _grid.SelectionChanged += (_, _) => _downloadButton.IsEnabled = _grid.SelectedItem is ModuleShareSummary;

        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 10 };
        filters.Children.Add(_search);
        filters.Children.Add(AddColumn(_profession, 1));
        filters.Children.Add(AddColumn(_refreshButton, 2));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { closeButton, _downloadButton }
        };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12 };
        footer.Children.Add(_status);
        footer.Children.Add(AddColumn(actions, 1));

        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12,
            Margin = new Avalonia.Thickness(22),
            Children =
            {
                filters,
                AddRow(_grid, 1),
                AddRow(footer, 2)
            }
        };

        Opened += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SetBusy(true, "正在读取社区模块…");
        try
        {
            _allShares = await _marketplace.GetSharesAsync();
            var professions = _allShares
                .Select(share => share.Profession)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .Order(StringComparer.CurrentCultureIgnoreCase)
                .Prepend("全部职业")
                .ToArray();
            _profession.ItemsSource = professions;
            _profession.SelectedIndex = 0;
            ApplyFilter();
            _status.Text = $"共 {_allShares.Count} 个社区模块";
        }
        catch (Exception exception)
        {
            _allShares = [];
            ApplyFilter();
            _status.Text = $"读取失败：{exception.Message}";
        }
        finally
        {
            SetBusy(false, _status.Text ?? string.Empty);
        }
    }

    private async Task DownloadSelectedAsync()
    {
        if (_grid.SelectedItem is not ModuleShareSummary selected)
        {
            return;
        }

        SetBusy(true, $"正在下载 {selected.Filename}…");
        try
        {
            var module = await _marketplace.DownloadAsync(selected.Id);
            var sameName = _moduleStore.GetModules().Any(candidate =>
                string.Equals(candidate.Name, module.Name, StringComparison.CurrentCultureIgnoreCase));
            if (sameName && !await ConfirmReplaceAsync(module.Name))
            {
                _status.Text = "已取消替换。";
                return;
            }

            var installed = _moduleStore.Install(module, replaceExisting: sameName);
            _modulesChanged();
            _status.Text = $"已安装：{installed.Name}";
        }
        catch (Exception exception)
        {
            _status.Text = $"下载失败：{exception.Message}";
        }
        finally
        {
            SetBusy(false, _status.Text ?? string.Empty);
        }
    }

    private void ApplyFilter()
    {
        var search = (_search.Text ?? string.Empty).Trim();
        var profession = _profession.SelectedItem as string;
        var filtered = _allShares.Where(share =>
            (string.IsNullOrWhiteSpace(profession)
                || profession == "全部职业"
                || string.Equals(share.Profession, profession, StringComparison.CurrentCultureIgnoreCase))
            && (search.Length == 0
                || string.Join(' ', share.Filename, share.Author, share.Profession, share.Specialization, share.Description)
                    .Contains(search, StringComparison.CurrentCultureIgnoreCase)));

        _visibleShares.Clear();
        foreach (var share in filtered)
        {
            _visibleShares.Add(share);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _refreshButton.IsEnabled = !busy;
        _downloadButton.IsEnabled = !busy && _grid.SelectedItem is ModuleShareSummary;
        _status.Text = status;
    }

    private async Task<bool> ConfirmReplaceAsync(string moduleName)
    {
        var confirmed = false;
        var dialog = new Window
        {
            Title = "替换模块",
            Width = 420,
            Height = 180,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var cancel = CommandButton("取消", (_, _) => dialog.Close());
        var replace = CommandButton("替换", (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        });
        dialog.Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Avalonia.Thickness(24),
            Children =
            {
                new TextBlock
                {
                    Text = $"本地已存在模块“{moduleName}”。是否用下载版本替换？",
                    TextWrapping = TextWrapping.Wrap
                },
                AddRow(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, replace }
                }, 1)
            }
        };
        await dialog.ShowDialog(this);
        return confirmed;
    }

    private static DataGridTextColumn TextColumn(string header, string property, double width) => new()
    {
        Header = header,
        Binding = new Binding(property),
        Width = new DataGridLength(width, DataGridLengthUnitType.Star)
    };

    private static Button CommandButton(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button = new Button { Content = text, MinHeight = 34, Padding = new Avalonia.Thickness(14, 6) };
        button.Click += handler;
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static T AddColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static T AddRow<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
