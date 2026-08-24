using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace Shigure.MacUI;

public sealed class MacroEditorView : UserControl
{
    private readonly Window _owner;
    private readonly ProjectConfigUpdateService _updates;
    private readonly Func<Task> _macrosChanged;
    private readonly Action<string> _setGlobalStatus;
    private readonly ObservableCollection<ClassOption> _classes = [];
    private readonly ObservableCollection<SpecOption> _specs = [];
    private readonly ObservableCollection<DynamicMacroRow> _dynamicRows = [];
    private readonly ObservableCollection<MacroRow> _staticRows = [];
    private readonly ObservableCollection<MacroRow> _specialRows = [];
    private readonly ListBox _classList = new() { MinWidth = 180 };
    private readonly ComboBox _specList = new() { MinWidth = 220 };
    private readonly TextBlock _path = new() { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _capacity = new() { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid _dynamicGrid;
    private readonly DataGrid _staticGrid;
    private readonly DataGrid _specialGrid;
    private readonly Control _editor;
    private readonly Button _saveButton;
    private ClassMacrosStore.MacrosDocument? _document;
    private ClassMacrosStore.ClassMacros? _macros;
    private string? _classFile;
    private int? _classId;
    private int? _specId;
    private bool _hasSpecSelection;
    private bool _suppress;
    private bool _dirty;
    private bool _busy;
    private Task _pendingSave = Task.CompletedTask;

    public MacroEditorView(
        Window owner,
        ProjectConfigUpdateService updates,
        Func<Task> macrosChanged,
        Action<string> setGlobalStatus)
    {
        _owner = owner;
        _updates = updates;
        _macrosChanged = macrosChanged;
        _setGlobalStatus = setGlobalStatus;

        _dynamicGrid = CreateGrid(
            _dynamicRows,
            TextColumn("法术名", nameof(DynamicMacroRow.Name), new DataGridLength(1, DataGridLengthUnitType.Star)));
        _staticGrid = CreateMacroGrid(_staticRows);
        _specialGrid = CreateMacroGrid(_specialRows);
        AutomationProperties.SetName(_classList, "宏职业列表");
        AutomationProperties.SetName(_specList, "动态宏专精选择");
        AutomationProperties.SetName(_dynamicGrid, "动态宏表格");
        AutomationProperties.SetName(_staticGrid, "静态宏表格");
        AutomationProperties.SetName(_specialGrid, "特殊宏表格");

        _dynamicGrid.CellEditEnded += (_, _) =>
        {
            MarkDirty();
            UpdateCapacityHint();
        };
        _staticGrid.CellEditEnded += (_, _) => HandleMacroEdited(_staticGrid, isSpecial: false);
        _specialGrid.CellEditEnded += (_, _) => HandleMacroEdited(_specialGrid, isSpecial: true);

        _classList.ItemsSource = _classes;
        _specList.ItemsSource = _specs;
        var reloadButton = CommandButton("刷新", async (_, _) => await ReloadAsync(confirmDiscard: true));
        _saveButton = CommandButton("保存并应用", async (_, _) => await QueueSaveAsync());

        var sidebar = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10 };
        sidebar.Children.Add(new TextBlock { Text = "职业", Classes = { "section-title" } });
        sidebar.Children.Add(At(_classList, 1, 0));
        sidebar.Children.Add(At(reloadButton, 2, 0));

        var header = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowSpacing = 8,
            ColumnSpacing = 10
        };
        header.Children.Add(new TextBlock { Text = "动态宏范围", VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(At(_specList, 0, 1));
        header.Children.Add(At(_saveButton, 0, 2));
        header.Children.Add(At(_path, 1, 0));
        Grid.SetColumnSpan(_path, 3);
        header.Children.Add(At(_capacity, 2, 0));
        Grid.SetColumnSpan(_capacity, 3);

        var tabs = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "动态宏", Content = BuildDynamicTab() },
                new TabItem { Header = "静态宏", Content = BuildMacroTab(_staticGrid, _staticRows, isSpecial: false) },
                new TabItem { Header = "特殊宏", Content = BuildMacroTab(_specialGrid, _specialRows, isSpecial: true) }
            }
        };

        _editor = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 12 };
        ((Grid)_editor).Children.Add(header);
        ((Grid)_editor).Children.Add(At(tabs, 1, 0));
        ((Grid)_editor).Children.Add(At(_status, 2, 0));

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*"), ColumnSpacing = 18 };
        root.Children.Add(sidebar);
        root.Children.Add(At(_editor, 0, 1));
        Content = root;

        _classList.SelectionChanged += async (_, _) => await SelectClassAsync(_classList.SelectedItem as ClassOption);
        _specList.SelectionChanged += (_, _) => SelectSpec(_specList.SelectedItem as SpecOption);
        _ = ReloadAsync(confirmDiscard: false);
    }

    public async Task<bool> ConfirmDiscardBeforeExitAsync()
    {
        await _pendingSave;
        return !_dirty
            || await ConfirmAsync("当前职业宏有未保存修改。是否放弃修改并退出？", "放弃并退出");
    }

    public bool HasUnsavedChanges => _dirty;

    public Task ReloadFromAddonAsync() => ReloadAsync(confirmDiscard: false);

    private Task QueueSaveAsync()
    {
        if (_busy)
        {
            return _pendingSave;
        }

        _pendingSave = SaveAsync();
        return _pendingSave;
    }

    private Control BuildDynamicTab()
    {
        var hint = new TextBlock
        {
            Text = $"每项动态宏展开为 {FuyutsuiKeymapConverter.DynamicMacroSlotCount} 个团队点名槽；通用项与所选专精项合并。",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap
        };
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        body.Children.Add(hint);
        body.Children.Add(At(_dynamicGrid, 1, 0));
        body.Children.Add(At(EditorActions(
            _dynamicGrid,
            _dynamicRows,
            () => new DynamicMacroRow()), 2, 0));
        return body;
    }

    private Control BuildMacroTab(
        DataGrid grid,
        ObservableCollection<MacroRow> rows,
        bool isSpecial)
    {
        var hint = new TextBlock
        {
            Text = isSpecial
                ? "完整宏文本按顺序占用一个槽位；空字符串会保留槽位。单位、条件和技能由共享转换器解析。"
                : "法术名或完整宏文本按顺序占用一个槽位；同行注释优先作为技能名，空字符串会保留槽位。",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap
        };
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        body.Children.Add(hint);
        body.Children.Add(At(grid, 1, 0));
        body.Children.Add(At(EditorActions(
            grid,
            rows,
            () => new MacroRow(string.Empty, string.Empty, isSpecial)), 2, 0));
        return body;
    }

    private StackPanel EditorActions<T>(DataGrid grid, ObservableCollection<T> rows, Func<T> factory) =>
        ActionRow(
            CommandButton("新增", (_, _) =>
            {
                rows.Add(factory());
                MarkDirty();
                UpdateCapacityHint();
            }),
            EditButton("删除", grid, rows, EditAction.Remove),
            EditButton("上移", grid, rows, EditAction.Up),
            EditButton("下移", grid, rows, EditAction.Down));

    private Button EditButton<T>(string text, DataGrid grid, ObservableCollection<T> rows, EditAction action) =>
        CommandButton(text, (_, _) =>
        {
            if (grid.SelectedItem is not T selected)
            {
                return;
            }

            var index = rows.IndexOf(selected);
            var target = action switch
            {
                EditAction.Up => index - 1,
                EditAction.Down => index + 1,
                _ => index
            };
            if (action == EditAction.Remove)
            {
                rows.RemoveAt(index);
            }
            else if (target >= 0 && target < rows.Count)
            {
                rows.Move(index, target);
                grid.SelectedItem = rows[target];
            }
            else
            {
                return;
            }

            MarkDirty();
            UpdateCapacityHint();
        });

    private async Task ReloadAsync(bool confirmDiscard)
    {
        if (confirmDiscard && _dirty
            && !await ConfirmAsync("刷新会放弃当前职业宏未保存的修改。是否继续？", "放弃并刷新"))
        {
            return;
        }

        _document = null;
        _macros = null;
        _classFile = null;
        _classId = null;
        _specId = null;
        _hasSpecSelection = false;
        _classes.Clear();
        _specs.Clear();
        ClearRows();
        SetDirty(false);

        try
        {
            _document = await Task.Run(() => ClassMacrosStore.Load(_updates.ClassMacrosPath));
            foreach (var (classId, className) in ClassNames.GetClasses())
            {
                var classFile = ClassMacrosStore.ToClassFileKey(classId);
                _classes.Add(new ClassOption(
                    classId,
                    className,
                    classFile,
                    _document.Classes.ContainsKey(classFile)));
            }

            foreach (var classFile in _document.ClassOrder)
            {
                if (_classes.Any(option => option.ClassFile.Equals(classFile, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _classes.Add(new ClassOption(0, classFile, classFile, true));
            }

            _path.Text = _document.FilePath;
            _status.Text = $"已加载 {_document.Classes.Count} 个职业宏表";
            _suppress = true;
            _classList.SelectedIndex = _classes.Count > 0 ? 0 : -1;
        }
        catch (Exception exception)
        {
            _path.Text = _updates.ClassMacrosPath;
            _status.Text = $"加载失败：{exception.Message}";
            _editor.IsEnabled = false;
        }
        finally
        {
            _suppress = false;
        }

        await SelectClassAsync(_classList.SelectedItem as ClassOption);
    }

    private async Task SelectClassAsync(ClassOption? option)
    {
        if (_suppress || option is null || option.ClassFile.Equals(_classFile, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previousClassFile = _classFile;
        if (_dirty
            && !await ConfirmAsync("当前职业宏有未保存修改。是否放弃修改并切换？", "放弃修改"))
        {
            SelectClass(previousClassFile);
            return;
        }

        if (_dirty && _document is not null)
        {
            _document = await Task.Run(() => ClassMacrosStore.Load(_document.FilePath));
        }
        else
        {
            CommitCurrent();
        }

        SetDirty(false);
        _classFile = option.ClassFile;
        _classId = option.ClassId > 0 ? option.ClassId : null;
        _specId = null;
        _hasSpecSelection = false;
        if (_document is null)
        {
            _macros = null;
            ClearRows();
            return;
        }

        if (!_document.Classes.TryGetValue(option.ClassFile, out _macros))
        {
            _macros = new ClassMacrosStore.ClassMacros();
            _document.Classes[option.ClassFile] = _macros;
            if (!_document.ClassOrder.Contains(option.ClassFile, StringComparer.OrdinalIgnoreCase))
            {
                _document.ClassOrder.Add(option.ClassFile);
            }
        }

        FillEditors();
        _editor.IsEnabled = true;
        _status.Text = option.HasData ? "可编辑" : "新建空表（修改后可保存）";
    }

    private void FillEditors()
    {
        _suppress = true;
        try
        {
            _specs.Clear();
            _dynamicRows.Clear();
            _staticRows.Clear();
            _specialRows.Clear();
            _specId = null;
            _hasSpecSelection = false;
            if (_macros is null)
            {
                return;
            }

            _specs.Add(new SpecOption(null, "通用"));
            var knownSpecIds = new HashSet<int>();
            if (_classId is { } classId)
            {
                foreach (var spec in ClassNames.GetSpecs(classId))
                {
                    knownSpecIds.Add(spec.Id);
                    _specs.Add(new SpecOption(spec.Id, spec.Name));
                }
            }

            foreach (var specId in _macros.DynamicBySpec.Keys.OrderBy(value => value))
            {
                if (knownSpecIds.Add(specId))
                {
                    _specs.Add(new SpecOption(specId, $"专精{specId}"));
                }
            }

            foreach (var entry in _macros.StaticSpells)
            {
                _staticRows.Add(new MacroRow(entry.Text, entry.Comment ?? string.Empty, isSpecial: false));
            }
            foreach (var entry in _macros.SpecialSpells)
            {
                _specialRows.Add(new MacroRow(entry.Text, entry.Comment ?? string.Empty, isSpecial: true));
            }

            _specList.SelectedIndex = _specs.Count > 0 ? 0 : -1;
        }
        finally
        {
            _suppress = false;
        }

        SelectSpec(_specList.SelectedItem as SpecOption);
    }

    private void SelectSpec(SpecOption? option)
    {
        if (_suppress || _macros is null || option is null
            || (_hasSpecSelection && option.SpecId == _specId))
        {
            return;
        }

        if (_hasSpecSelection)
        {
            CommitDynamicRows();
        }
        _specId = option.SpecId;
        _hasSpecSelection = true;
        _suppress = true;
        try
        {
            _dynamicRows.Clear();
            IReadOnlyList<string> values = option.SpecId is { } specId
                ? _macros.DynamicBySpec.GetValueOrDefault(specId) ?? []
                : _macros.DynamicCommon;
            foreach (var value in values)
            {
                _dynamicRows.Add(new DynamicMacroRow { Name = value });
            }
        }
        finally
        {
            _suppress = false;
        }

        UpdateCapacityHint();
    }

    private void CommitCurrent()
    {
        if (_macros is null || !_hasSpecSelection)
        {
            return;
        }

        CommitDynamicRows();
        WriteMacroRows(_staticRows, _macros.StaticSpells);
        WriteMacroRows(_specialRows, _macros.SpecialSpells);
    }

    private void CommitDynamicRows()
    {
        if (_macros is null)
        {
            return;
        }

        var values = _dynamicRows.Select(row => row.Name.Trim()).ToList();
        if (_specId is not { } specId)
        {
            _macros.DynamicCommon.Clear();
            _macros.DynamicCommon.AddRange(values);
            return;
        }

        if (values.Count > 0 || _macros.DynamicBySpec.ContainsKey(specId))
        {
            _macros.UsesSpecDynamicSpells = true;
            _macros.DynamicBySpec[specId] = values;
        }
    }

    private static void WriteMacroRows(
        IEnumerable<MacroRow> rows,
        ICollection<ClassMacrosStore.ArrayEntry> target)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(new ClassMacrosStore.ArrayEntry
            {
                Text = row.Body.Replace("\r\n", "\n", StringComparison.Ordinal),
                Comment = string.IsNullOrWhiteSpace(row.Comment) ? null : row.Comment.Trim()
            });
        }
    }

    private async Task SaveAsync()
    {
        if (_busy || _document is null)
        {
            return;
        }

        CommitCurrent();
        var capacityIssues = FuyutsuiKeymapConverter.ValidateCapacity(_document);
        if (capacityIssues.Count > 0)
        {
            var issue = capacityIssues[0];
            var scope = issue.SpecIndex is { } specId ? $"专精 {specId}" : "通用";
            var message = $"{issue.ClassFile} {scope} 需要 {issue.RequiredSlots} 个槽位，最多 {issue.Capacity} 个。请删除或合并宏后再保存。";
            _status.Text = "宏容量超限，未保存";
            await ShowMessageAsync("宏无法保存", message);
            return;
        }

        var localSaved = false;
        SetBusy(true);
        try
        {
            _status.Text = "正在保存本地 Lua…";
            await Task.Run(() => ClassMacrosStore.Save(_document));
            localSaved = true;
            SetDirty(false);
            _status.Text = "本地 Lua 已保存，正在生成 keymap 并同步插件…";
            var result = await Task.Run(() => _updates.Update(_document.FilePath));
            var warnings = result.Config.Warnings.Count + (result.Keymap?.Warnings.Count ?? 0);
            var sync = result.AddonSync;
            _status.Text = sync.CompletedSuccessfully
                ? $"已保存并应用 · 警告 {warnings}"
                : $"本地宏与 keymap 已更新 · {sync.SkippedReason ?? $"插件同步失败 {sync.Failures.Count} 项"}";
            _setGlobalStatus(_status.Text);
            await _macrosChanged();
        }
        catch (Exception exception)
        {
            var title = localSaved ? "保存后的更新失败" : "保存失败";
            var message = localSaved ? $"本地 Lua 已保存，但后续更新失败：\n{exception.Message}" : exception.Message;
            _status.Text = localSaved ? "本地已保存，后续更新失败" : "保存失败";
            await ShowMessageAsync(title, message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void HandleMacroEdited(DataGrid grid, bool isSpecial)
    {
        if (grid.SelectedItem is MacroRow row)
        {
            row.Refresh(isSpecial);
        }
        MarkDirty();
        UpdateCapacityHint();
    }

    private void UpdateCapacityHint()
    {
        if (_macros is null)
        {
            _capacity.Text = "未加载宏容量";
            return;
        }

        var commonCount = _specId is null ? _dynamicRows.Count : _macros.DynamicCommon.Count;
        var specCount = _specId is null ? 0 : _dynamicRows.Count;
        var dynamicCount = commonCount + specCount;
        var required = FuyutsuiKeymapConverter.CalculateRequiredSlots(
            dynamicCount,
            _staticRows.Count,
            _specialRows.Count);
        var scope = _specId is null
            ? $"通用 {commonCount} 项"
            : $"{(_specList.SelectedItem as SpecOption)?.Name ?? $"专精{_specId}"}：通用 {commonCount} + 专精 {specCount} 项";
        _capacity.Text =
            $"{scope}；动态 {dynamicCount * FuyutsuiKeymapConverter.DynamicMacroSlotCount} 槽，静态 {_staticRows.Count} 槽，特殊 {_specialRows.Count} 槽；" +
            $"共 {required}/{FuyutsuiKeymapConverter.MacroSlotCapacity} 槽";
        _capacity.Foreground = required > FuyutsuiKeymapConverter.MacroSlotCapacity
            ? Brushes.OrangeRed
            : new SolidColorBrush(Color.Parse("#AEB5BA"));
    }

    private void MarkDirty()
    {
        if (!_suppress && !_busy && _document is not null)
        {
            SetDirty(true);
        }
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _status.Text = dirty ? "已修改（未保存）" : _document is null ? _status.Text : "可编辑";
        _saveButton.IsEnabled = !dirty ? !_busy && _document is not null : !_busy;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _saveButton.IsEnabled = !busy && _document is not null;
        _editor.IsEnabled = !busy && _document is not null;
    }

    private void SelectClass(string? classFile)
    {
        _suppress = true;
        _classList.SelectedItem = _classes.FirstOrDefault(option =>
            option.ClassFile.Equals(classFile, StringComparison.OrdinalIgnoreCase));
        _suppress = false;
    }

    private void ClearRows()
    {
        _dynamicRows.Clear();
        _staticRows.Clear();
        _specialRows.Clear();
        _capacity.Text = "未加载宏容量";
    }

    private async Task<bool> ConfirmAsync(string message, string confirmText)
    {
        var result = false;
        var dialog = Dialog("确认", 440, 190);
        var cancel = new Button { Content = "取消", MinWidth = 90 };
        var confirm = new Button { Content = confirmText, MinWidth = 100 };
        cancel.Click += (_, _) => dialog.Close();
        confirm.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        dialog.Content = DialogContent(message, cancel, confirm);
        await dialog.ShowDialog(_owner);
        return result;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = Dialog(title, 480, 220);
        var close = new Button { Content = "确定", MinWidth = 90 };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = DialogContent(message, close);
        await dialog.ShowDialog(_owner);
    }

    private static Window Dialog(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner
    };

    private static Control DialogContent(string message, params Button[] buttons)
    {
        var actions = ActionRow(buttons);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        root.Children.Add(At(actions, 1, 0));
        return root;
    }

    private static DataGrid CreateMacroGrid(ObservableCollection<MacroRow> rows) => CreateGrid(
        rows,
        TextColumn("完整宏", nameof(MacroRow.Body), new DataGridLength(2, DataGridLengthUnitType.Star)),
        TextColumn("技能注释", nameof(MacroRow.Comment), new DataGridLength(1, DataGridLengthUnitType.Star)),
        TextColumn("单位", nameof(MacroRow.Unit), new DataGridLength(90), readOnly: true),
        TextColumn("条件", nameof(MacroRow.Condition), new DataGridLength(150), readOnly: true),
        TextColumn("技能", nameof(MacroRow.Spell), new DataGridLength(150), readOnly: true));

    private static DataGrid CreateGrid<T>(ObservableCollection<T> rows, params DataGridColumn[] columns)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserResizeColumns = true,
            CanUserReorderColumns = true,
            CanUserSortColumns = false,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        foreach (var column in columns)
        {
            grid.Columns.Add(column);
        }
        return grid;
    }

    private static DataGridTextColumn TextColumn(
        string title,
        string property,
        DataGridLength width,
        bool readOnly = false) => new()
    {
        Header = title,
        Binding = new Binding(property) { Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay },
        Width = width,
        IsReadOnly = readOnly
    };

    private static Button CommandButton(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button = new Button { Content = text, Classes = { "command" } };
        button.Click += handler;
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static StackPanel ActionRow(params Button[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var button in buttons)
        {
            panel.Children.Add(button);
        }
        return panel;
    }

    private static T At<T>(T control, int row, int column)
        where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    private enum EditAction { Remove, Up, Down }

    private sealed record ClassOption(int ClassId, string Name, string ClassFile, bool HasData)
    {
        public override string ToString() => HasData ? Name : $"{Name}（无）";
    }

    private sealed record SpecOption(int? SpecId, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed class DynamicMacroRow
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class MacroRow : INotifyPropertyChanged
    {
        private string _body;
        private string _comment;
        private string _unit = string.Empty;
        private string _condition = string.Empty;
        private string _spell = string.Empty;

        public MacroRow(string body, string comment, bool isSpecial)
        {
            _body = body;
            _comment = comment;
            Refresh(isSpecial);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Body
        {
            get => _body;
            set => _body = value ?? string.Empty;
        }

        public string Comment
        {
            get => _comment;
            set => _comment = value ?? string.Empty;
        }

        public string Unit
        {
            get => _unit;
            private set => SetField(ref _unit, value);
        }

        public string Condition
        {
            get => _condition;
            private set => SetField(ref _condition, value);
        }

        public string Spell
        {
            get => _spell;
            private set => SetField(ref _spell, value);
        }

        public void Refresh(bool isSpecial)
        {
            var parsed = isSpecial
                ? FuyutsuiKeymapConverter.ParseSpecialMacro(Body, Comment)
                : FuyutsuiKeymapConverter.ParseStaticMacro(Body, Comment);
            Unit = ReservedUnit.ToDisplayText(parsed.Unit);
            Condition = MacroConditionText.ToDisplayText(parsed.Condition);
            Spell = parsed.Spell;
        }

        private void SetField(ref string field, string value, [CallerMemberName] string? propertyName = null)
        {
            if (field == value)
            {
                return;
            }
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
