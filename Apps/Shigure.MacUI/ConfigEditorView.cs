using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Shigure.MacUI;

public sealed class ConfigEditorView : UserControl
{
    private static readonly string[] FixedStateNames = ["锚点", "职业", "专精"];
    private static readonly AuraBucket[] AuraBuckets =
    [
        new("player", "玩家"),
        new("target.harmful", "目标 · 敌对"),
        new("target.helpful", "目标 · 友善"),
        new("focus.harmful", "焦点 · 敌对"),
        new("focus.helpful", "焦点 · 友善")
    ];

    private readonly Window _owner;
    private readonly ProjectConfigUpdateService _updates;
    private readonly Func<Task> _configChanged;
    private readonly Action<string> _setGlobalStatus;
    private readonly ObservableCollection<ClassOption> _classes = [];
    private readonly ObservableCollection<SpecOption> _specs = [];
    private readonly ObservableCollection<StateRow> _states = [];
    private readonly ObservableCollection<AuraRow> _auras = [];
    private readonly ObservableCollection<SpellRow> _spells = [];
    private readonly ObservableCollection<SpellListRow> _spellList = [];
    private readonly ObservableCollection<GroupAuraRow> _groupAuras = [];
    private readonly Dictionary<int, ClassBlocksStore.ClassFileDocument> _documents = [];
    private readonly ListBox _classList = new() { MinWidth = 180 };
    private readonly ComboBox _specList = new() { MinWidth = 220 };
    private readonly ComboBox _stateCategory = new() { ItemsSource = ClassStateCatalog.TopCategories };
    private readonly ComboBox _auraBucket = new() { ItemsSource = AuraBuckets };
    private readonly TextBlock _path = new() { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _groupEnabled = new() { Content = "启用队伍字段" };
    private readonly CheckBox _groupHasHealth = new() { Content = "生命值" };
    private readonly CheckBox _groupHasRole = new() { Content = "职责" };
    private readonly CheckBox _groupHasDispel = new() { Content = "驱散" };
    private readonly TextBox _groupNum = NumberBox("1–40", "5");
    private readonly TextBox _groupHealth = NumberBox("0–40", "1");
    private readonly TextBox _groupRole = NumberBox("0–40", "2");
    private readonly TextBox _groupDispel = NumberBox("0–40", "3");
    private readonly DataGrid _statesGrid;
    private readonly DataGrid _aurasGrid;
    private readonly DataGrid _spellsGrid;
    private readonly DataGrid _spellListGrid;
    private readonly DataGrid _groupAurasGrid;
    private readonly Control _editor;
    private readonly Button _saveButton;
    private ClassBlocksStore.ClassFileDocument? _document;
    private ClassBlocksStore.SpecBlocks? _spec;
    private int? _classId;
    private int? _specId;
    private string _loadedStateCategory = ClassStateCatalog.CategoryState;
    private string _loadedAuraBucket = "player";
    private bool _suppress;
    private bool _dirty;
    private bool _busy;
    private Task _pendingSave = Task.CompletedTask;

    public ConfigEditorView(
        Window owner,
        ProjectConfigUpdateService updates,
        Func<Task> configChanged,
        Action<string> setGlobalStatus)
    {
        _owner = owner;
        _updates = updates;
        _configChanged = configChanged;
        _setGlobalStatus = setGlobalStatus;

        _statesGrid = CreateGrid(_states, TextColumn("状态名称", nameof(StateRow.Name), 260));
        _aurasGrid = CreateGrid(_auras,
            TextColumn("名称", nameof(AuraRow.Name), 180),
            TextColumn("法术 ID", nameof(AuraRow.SpellId), 120),
            TextColumn("法术 ID 列表", nameof(AuraRow.SpellIds), 220),
            TextColumn("最大层数", nameof(AuraRow.MaxApps), 100));
        _spellsGrid = CreateGrid(_spells,
            TextColumn("名称", nameof(SpellRow.Name), 170),
            TextColumn("法术 ID", nameof(SpellRow.SpellId), 120),
            CheckColumn("充能", nameof(SpellRow.Charge), 70),
            TextColumn("最大充能", nameof(SpellRow.MaxCharge), 90),
            TextColumn("施法次数", nameof(SpellRow.CastCount), 90),
            CheckColumn("强制已知", nameof(SpellRow.ForcedKnown), 85),
            CheckColumn("法术书", nameof(SpellRow.InSpellBook), 75));
        _spellListGrid = CreateGrid(_spellList,
            TextColumn("法术 ID", nameof(SpellListRow.SpellId), 140),
            TextColumn("索引 1–100", nameof(SpellListRow.Index), 120),
            TextColumn("名称", nameof(SpellListRow.Name), 240));
        _groupAurasGrid = CreateGrid(_groupAuras,
            TextColumn("偏移", nameof(GroupAuraRow.Offset), 90),
            TextColumn("名称", nameof(GroupAuraRow.Name), 180),
            TextColumn("法术 ID", nameof(GroupAuraRow.SpellId), 120),
            TextColumn("法术 ID 列表", nameof(GroupAuraRow.SpellIds), 220));

        foreach (var grid in new[] { _statesGrid, _aurasGrid, _spellsGrid, _spellListGrid, _groupAurasGrid })
        {
            grid.CellEditEnded += (_, _) => MarkDirty();
        }

        _classList.ItemsSource = _classes;
        _specList.ItemsSource = _specs;
        AutomationProperties.SetName(_classList, "职业配置列表");
        AutomationProperties.SetName(_specList, "专精选择");
        AutomationProperties.SetName(_statesGrid, "状态配置表格");
        AutomationProperties.SetName(_aurasGrid, "光环配置表格");
        AutomationProperties.SetName(_spellsGrid, "技能配置表格");
        AutomationProperties.SetName(_spellListGrid, "技能列表表格");
        AutomationProperties.SetName(_groupAurasGrid, "队伍光环表格");

        var reloadButton = CommandButton("刷新", async (_, _) => await ReloadAsync(true));
        _saveButton = CommandButton("保存并应用", async (_, _) => await QueueSaveAsync());
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10 };
        sidebar.Children.Add(new TextBlock { Text = "职业", Classes = { "section-title" } });
        sidebar.Children.Add(At(_classList, 1, 0));
        sidebar.Children.Add(At(reloadButton, 2, 0));

        var header = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            RowSpacing = 8,
            ColumnSpacing = 10
        };
        header.Children.Add(new TextBlock { Text = "专精", VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(At(_specList, 0, 1));
        header.Children.Add(At(_saveButton, 0, 2));
        header.Children.Add(At(_path, 1, 0));
        Grid.SetColumnSpan(_path, 3);

        var tabs = new TabControl
        {
            ItemsSource = new object[]
            {
                new TabItem { Header = "状态", Content = BuildStatesTab() },
                new TabItem { Header = "光环", Content = BuildAurasTab() },
                new TabItem { Header = "技能", Content = TableEditor(_spellsGrid, _spells, () => new SpellRow()) },
                new TabItem { Header = "技能列表", Content = BuildSpellListTab() },
                new TabItem { Header = "队伍", Content = BuildGroupTab() }
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
        _specList.SelectionChanged += async (_, _) => await SelectSpecAsync(_specList.SelectedItem as SpecOption);
        _stateCategory.SelectionChanged += async (_, _) => await ChangeStateCategoryAsync();
        _auraBucket.SelectionChanged += async (_, _) => await ChangeAuraBucketAsync();
        _groupEnabled.IsCheckedChanged += (_, _) => { UpdateGroupEnabled(); MarkDirty(); };
        foreach (var checkBox in new[] { _groupHasHealth, _groupHasRole, _groupHasDispel })
        {
            checkBox.IsCheckedChanged += (_, _) => { UpdateGroupEnabled(); MarkDirty(); };
        }
        foreach (var box in new[] { _groupNum, _groupHealth, _groupRole, _groupDispel })
        {
            box.TextChanged += (_, _) => MarkDirty();
        }

        _stateCategory.SelectedIndex = 0;
        _auraBucket.SelectedIndex = 0;
        _ = ReloadAsync(false);
    }

    public async Task<bool> ConfirmDiscardBeforeExitAsync()
    {
        await _pendingSave;
        return !_dirty
            || await ConfirmAsync("当前职业有未保存修改。是否放弃修改并退出？", "放弃并退出");
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

    private Control BuildStatesTab()
    {
        var selector = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        selector.Children.Add(new TextBlock { Text = "分类", VerticalAlignment = VerticalAlignment.Center });
        selector.Children.Add(At(_stateCategory, 0, 1));
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        body.Children.Add(selector);
        body.Children.Add(At(_statesGrid, 1, 0));
        body.Children.Add(At(ActionRow(
            CommandButton("新增", (_, _) => AddState()),
            EditButton("删除", _statesGrid, _states, EditAction.Remove),
            EditButton("上移", _statesGrid, _states, EditAction.Up),
            EditButton("下移", _statesGrid, _states, EditAction.Down)), 2, 0));
        return body;
    }

    private Control BuildAurasTab()
    {
        var selector = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        selector.Children.Add(new TextBlock { Text = "单位与类型", VerticalAlignment = VerticalAlignment.Center });
        selector.Children.Add(At(_auraBucket, 0, 1));
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        body.Children.Add(selector);
        body.Children.Add(At(_aurasGrid, 1, 0));
        body.Children.Add(At(EditorActions(_aurasGrid, _auras, () => new AuraRow()), 2, 0));
        return body;
    }

    private Control BuildSpellListTab()
    {
        var hint = new TextBlock
        {
            Text = "编辑当前职业 Lua 中索引 1–100 的 spellsList；索引 101+ 保留且不会显示。",
            Classes = { "muted" },
            TextWrapping = TextWrapping.Wrap
        };
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        body.Children.Add(hint);
        body.Children.Add(At(_spellListGrid, 1, 0));
        body.Children.Add(At(ActionRow(CommandButton("新增", (_, _) => AddSpellListEntry())), 2, 0));
        return body;
    }

    private Control BuildGroupTab()
    {
        var fields = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,90,Auto,90,Auto,90,Auto,90"),
            ColumnSpacing = 8
        };
        fields.Children.Add(_groupEnabled);
        fields.Children.Add(At(_groupNum, 0, 1));
        fields.Children.Add(At(_groupHasHealth, 0, 2));
        fields.Children.Add(At(_groupHealth, 0, 3));
        fields.Children.Add(At(_groupHasRole, 0, 4));
        fields.Children.Add(At(_groupRole, 0, 5));
        fields.Children.Add(At(_groupHasDispel, 0, 6));
        fields.Children.Add(At(_groupDispel, 0, 7));
        var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        body.Children.Add(fields);
        body.Children.Add(At(_groupAurasGrid, 1, 0));
        body.Children.Add(At(EditorActions(_groupAurasGrid, _groupAuras, () => new GroupAuraRow()), 2, 0));
        return body;
    }

    private Control TableEditor<T>(DataGrid grid, ObservableCollection<T> rows, Func<T> factory)
    {
        var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 8 };
        body.Children.Add(grid);
        body.Children.Add(At(EditorActions(grid, rows, factory), 1, 0));
        return body;
    }

    private Control EditorActions<T>(DataGrid grid, ObservableCollection<T> rows, Func<T> factory) =>
        ActionRow(
            CommandButton("新增", (_, _) => { rows.Add(factory()); MarkDirty(); }),
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
        });

    private async Task ReloadAsync(bool confirmDiscard)
    {
        if (confirmDiscard && _dirty && !await ConfirmAsync("刷新会放弃当前职业未保存的修改。是否继续？", "放弃并刷新"))
        {
            return;
        }

        _documents.Clear();
        _classes.Clear();
        _document = null;
        _spec = null;
        _classId = null;
        _specId = null;
        SetDirty(false);
        try
        {
            foreach (var (classId, className) in ClassNames.GetClasses())
            {
                var fileName = ClassNames.GetConfigFileName(classId);
                var filePath = Path.Combine(_updates.ClassDirectory, fileName + ".lua");
                if (!File.Exists(filePath))
                {
                    continue;
                }

                try
                {
                    var document = ClassBlocksStore.Load(filePath);
                    _documents[classId] = document;
                    _classes.Add(new ClassOption(classId, className, document.IsModernFormat, null));
                }
                catch (Exception exception)
                {
                    _classes.Add(new ClassOption(classId, className, false, exception.Message));
                }
            }

            _status.Text = $"已加载 {_documents.Count} 个职业文件";
            _suppress = true;
            _classList.SelectedIndex = _classes.Count > 0 ? 0 : -1;
        }
        finally
        {
            _suppress = false;
        }

        await SelectClassAsync(_classList.SelectedItem as ClassOption);
    }

    private async Task SelectClassAsync(ClassOption? option)
    {
        if (_suppress || option is null || option.ClassId == _classId)
        {
            return;
        }

        var previousClassId = _classId;
        if (_dirty && !await ConfirmAsync("当前职业有未保存修改。是否放弃修改并切换？", "放弃修改"))
        {
            SelectClass(previousClassId);
            return;
        }

        if (_dirty && previousClassId is { } oldId && _documents.TryGetValue(oldId, out var oldDocument))
        {
            _documents[oldId] = ClassBlocksStore.Load(oldDocument.FilePath);
        }

        SetDirty(false);
        _classId = option.ClassId;
        _spec = null;
        _specId = null;
        _documents.TryGetValue(option.ClassId, out _document);
        _path.Text = _document?.FilePath ?? option.Error ?? "无法加载职业文件";
        _specs.Clear();
        if (_document is not null)
        {
            foreach (var known in ClassNames.GetSpecs(option.ClassId).Where(item => _document.Specs.ContainsKey(item.Id)))
            {
                _specs.Add(new SpecOption(known.Id, known.Name));
            }
            foreach (var id in _document.Specs.Keys.OrderBy(value => value).Where(id => _specs.All(item => item.SpecId != id)))
            {
                _specs.Add(new SpecOption(id, $"专精{id}"));
            }
        }

        _suppress = true;
        _specList.SelectedIndex = _specs.Count > 0 ? 0 : -1;
        _suppress = false;
        _editor.IsEnabled = _document?.IsModernFormat == true;
        _status.Text = _document is null
            ? option.Error ?? "无法加载职业文件"
            : _document.IsModernFormat ? "可编辑" : "旧版稀疏索引格式暂不支持编辑";
        await SelectSpecAsync(_specList.SelectedItem as SpecOption);
    }

    private async Task SelectSpecAsync(SpecOption? option)
    {
        if (_suppress || _document is null || option is null || option.SpecId == _specId)
        {
            return;
        }

        var previousSpecId = _specId;
        if (_spec is not null && !TryCommitCurrentSpec(out var error))
        {
            await ShowMessageAsync("无法切换专精", error);
            SelectSpec(previousSpecId);
            return;
        }

        _specId = option.SpecId;
        if (!_document.Specs.TryGetValue(option.SpecId, out _spec))
        {
            _spec = new ClassBlocksStore.SpecBlocks();
            _document.Specs[option.SpecId] = _spec;
            MarkDirty();
        }

        LoadEditors();
    }

    private async Task ChangeStateCategoryAsync()
    {
        if (_suppress || _spec is null || _stateCategory.SelectedItem is not string category || category == _loadedStateCategory)
        {
            return;
        }
        var preserveDirty = _dirty;
        if (!TryCommitStates(_loadedStateCategory, out var error))
        {
            await ShowMessageAsync("状态配置无效", error);
            SelectCombo(_stateCategory, _loadedStateCategory);
            return;
        }
        _loadedStateCategory = category;
        LoadStates();
        RestoreCleanStateAfterBinding(preserveDirty);
    }

    private async Task ChangeAuraBucketAsync()
    {
        if (_suppress || _spec is null || _auraBucket.SelectedItem is not AuraBucket bucket || bucket.Key == _loadedAuraBucket)
        {
            return;
        }
        var preserveDirty = _dirty;
        if (!TryCommitAuras(_loadedAuraBucket, out var error))
        {
            await ShowMessageAsync("光环配置无效", error);
            SelectCombo(_auraBucket, AuraBuckets.First(item => item.Key == _loadedAuraBucket));
            return;
        }
        _loadedAuraBucket = bucket.Key;
        LoadAuras();
        RestoreCleanStateAfterBinding(preserveDirty);
    }

    private void LoadEditors()
    {
        var preserveDirty = _dirty;
        _suppress = true;
        try
        {
            _loadedStateCategory = _stateCategory.SelectedItem as string ?? ClassStateCatalog.CategoryState;
            _loadedAuraBucket = (_auraBucket.SelectedItem as AuraBucket)?.Key ?? "player";
            LoadStates();
            LoadAuras();
            _spells.Clear();
            foreach (var spell in _spec?.Spells ?? [])
            {
                _spells.Add(new SpellRow(spell));
            }
            _spellList.Clear();
            foreach (var entry in _document?.SpellsList.Where(item => item.Index is >= 1 and <= 100) ?? [])
            {
                _spellList.Add(new SpellListRow(entry));
            }
            LoadGroup();
        }
        finally
        {
            _suppress = false;
        }

        RestoreCleanStateAfterBinding(preserveDirty);
    }

    private void LoadStates()
    {
        _states.Clear();
        if (_spec is null) return;
        var names = _spec.NestedStates
            ? _spec.CategorizedStates.GetValueOrDefault(ClassStateCatalog.GetStorageCategory(_loadedStateCategory)) ?? []
            : _spec.FlatStates;
        foreach (var name in names.Where(name => ClassStateCatalog.IsInCategory(name, _loadedStateCategory) && !IsFixedState(name)))
        {
            _states.Add(new StateRow { Name = name });
        }
    }

    private void LoadAuras()
    {
        _auras.Clear();
        foreach (var aura in ResolveAuraList(_loadedAuraBucket))
        {
            _auras.Add(new AuraRow(aura));
        }
    }

    private void LoadGroup()
    {
        _groupAuras.Clear();
        var group = _spec?.Group;
        _groupEnabled.IsChecked = group is not null;
        _groupNum.Text = (group?.Num ?? 5).ToString(CultureInfo.InvariantCulture);
        _groupHasHealth.IsChecked = group?.HealthPercent is not null;
        _groupHealth.Text = (group?.HealthPercent ?? 1).ToString(CultureInfo.InvariantCulture);
        _groupHasRole.IsChecked = group?.Role is not null;
        _groupRole.Text = (group?.Role ?? 2).ToString(CultureInfo.InvariantCulture);
        _groupHasDispel.IsChecked = group?.Dispel is not null;
        _groupDispel.Text = (group?.Dispel ?? 3).ToString(CultureInfo.InvariantCulture);
        foreach (var aura in group?.Auras ?? [])
        {
            _groupAuras.Add(new GroupAuraRow(aura));
        }
        UpdateGroupEnabled();
    }

    private bool TryCommitCurrentSpec(out string error)
    {
        error = string.Empty;
        if (_spec is null || _document?.IsModernFormat != true) return true;
        if (!TryCommitStates(_loadedStateCategory, out error)
            || !TryCommitAuras(_loadedAuraBucket, out error)
            || !TryCommitSpells(out error)
            || !TryCommitGroup(out error)) return false;
        NormalizeFixedStates(_spec);
        return true;
    }

    private bool TryCommitStates(string category, out string error)
    {
        error = string.Empty;
        if (_spec is null) return true;
        var names = _states.Select(row => row.Name.Trim()).Where(name => name.Length > 0).ToList();
        var duplicate = names.GroupBy(name => name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            error = $"状态名称“{duplicate}”重复。";
            return false;
        }
        var storage = ClassStateCatalog.GetStorageCategory(category);
        var list = _spec.NestedStates ? _spec.CategorizedStates[storage] : _spec.FlatStates;
        var insertAt = list.FindIndex(name => ClassStateCatalog.IsInCategory(name, category) && !IsFixedState(name));
        if (insertAt < 0)
        {
            var anchor = category == ClassStateCatalog.CategoryState ? list.FindLastIndex(IsFixedState) : -1;
            insertAt = anchor >= 0 ? anchor + 1 : list.Count;
        }
        list.RemoveAll(name => ClassStateCatalog.IsInCategory(name, category) && !IsFixedState(name));
        list.InsertRange(Math.Min(insertAt, list.Count), names);
        return true;
    }

    private bool TryCommitAuras(string bucket, out string error)
    {
        error = string.Empty;
        var parsed = new List<ClassBlocksStore.AuraEntry>();
        for (var index = 0; index < _auras.Count; index++)
        {
            var row = _auras[index];
            if (!TryOptionalLong(row.SpellId, out var spellId)
                || !TryLongList(row.SpellIds, out var spellIds)
                || !TryOptionalInt(row.MaxApps, out var maxApps))
            {
                error = $"光环第 {index + 1} 行包含无效数字。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(row.Name) && spellId is null && spellIds.Count == 0) continue;
            var entry = new ClassBlocksStore.AuraEntry { Name = row.Name.Trim(), SpellId = spellId, MaxApps = maxApps };
            entry.SpellIds.AddRange(spellIds);
            parsed.Add(entry);
        }
        var target = ResolveAuraList(bucket);
        target.Clear();
        target.AddRange(parsed);
        return true;
    }

    private bool TryCommitSpells(out string error)
    {
        error = string.Empty;
        if (_spec is null) return true;
        var parsed = new List<ClassBlocksStore.SpellEntry>();
        for (var index = 0; index < _spells.Count; index++)
        {
            var row = _spells[index];
            if (!long.TryParse(row.SpellId, NumberStyles.None, CultureInfo.InvariantCulture, out var spellId) || spellId <= 0
                || !TryOptionalInt(row.MaxCharge, out var maxCharge)
                || !TryOptionalInt(row.CastCount, out var castCount))
            {
                error = $"技能第 {index + 1} 行包含无效数字。";
                return false;
            }
            parsed.Add(new ClassBlocksStore.SpellEntry
            {
                Name = row.Name.Trim(), SpellId = spellId, Charge = row.Charge,
                MaxCharge = maxCharge, CastCount = castCount,
                ForcedKnown = row.ForcedKnown, InSpellBook = row.InSpellBook
            });
        }
        _spec.Spells.Clear();
        _spec.Spells.AddRange(parsed);
        return true;
    }

    private bool TryCommitSpellList(out string error)
    {
        error = string.Empty;
        if (_document is null) return true;
        var ids = new HashSet<long>();
        for (var index = 0; index < _spellList.Count; index++)
        {
            var row = _spellList[index];
            if (!long.TryParse(row.SpellId, NumberStyles.None, CultureInfo.InvariantCulture, out var spellId) || spellId <= 0)
            {
                error = $"技能列表第 {index + 1} 行的法术 ID 必须是正整数。";
                return false;
            }
            if (!int.TryParse(row.Index, NumberStyles.None, CultureInfo.InvariantCulture, out var slot) || slot is < 1 or > 100)
            {
                error = $"技能列表第 {index + 1} 行的索引必须是 1–100。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(row.Name) || !ids.Add(spellId))
            {
                error = string.IsNullOrWhiteSpace(row.Name) ? $"技能列表第 {index + 1} 行的名称不能为空。" : $"法术 ID {spellId} 重复。";
                return false;
            }
        }
        var hiddenIds = _document.SpellsList.Where(item => item.Index is < 1 or > 100).Select(item => item.SpellId).ToHashSet();
        var conflict = ids.FirstOrDefault(hiddenIds.Contains);
        if (conflict != 0)
        {
            error = $"法术 ID {conflict} 已被索引 101+ 的条目使用。";
            return false;
        }
        foreach (var row in _spellList)
        {
            row.Source.SpellId = long.Parse(row.SpellId, CultureInfo.InvariantCulture);
            row.Source.Index = int.Parse(row.Index, CultureInfo.InvariantCulture);
            row.Source.Name = row.Name.Trim();
        }
        return true;
    }

    private bool TryCommitGroup(out string error)
    {
        error = string.Empty;
        if (_spec is null) return true;
        if (_groupEnabled.IsChecked != true)
        {
            _spec.Group = null;
            return true;
        }
        if (!TryRange(_groupNum.Text, 1, 40, out var num)
            || !TryOptionalRange(_groupHasHealth, _groupHealth.Text, 0, 40, out var health)
            || !TryOptionalRange(_groupHasRole, _groupRole.Text, 0, 40, out var role)
            || !TryOptionalRange(_groupHasDispel, _groupDispel.Text, 0, 40, out var dispel))
        {
            error = "队伍字段超出允许范围：人数 1–40，可选字段 0–40。";
            return false;
        }
        var group = new ClassBlocksStore.GroupBlocks { Num = num, HealthPercent = health, Role = role, Dispel = dispel };
        for (var index = 0; index < _groupAuras.Count; index++)
        {
            var row = _groupAuras[index];
            if (!int.TryParse(row.Offset, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)
                || !TryOptionalLong(row.SpellId, out var spellId)
                || !TryLongList(row.SpellIds, out var spellIds))
            {
                error = $"队伍光环第 {index + 1} 行包含无效数字。";
                return false;
            }
            var entry = new ClassBlocksStore.GroupAuraEntry
            {
                Offset = offset,
                Name = row.Name.Trim(),
                SpellId = spellId
            };
            entry.SpellIds.AddRange(spellIds);
            group.Auras.Add(entry);
        }
        _spec.Group = group;
        return true;
    }

    private async Task SaveAsync()
    {
        if (_busy || _document is null || _document.IsModernFormat == false) return;
        if (!TryCommitCurrentSpec(out var error) || !TryCommitSpellList(out error))
        {
            _status.Text = error;
            await ShowMessageAsync("配置无法保存", error);
            return;
        }

        var localSaved = false;
        SetBusy(true);
        try
        {
            _status.Text = "正在保存本地 Lua…";
            await Task.Run(() => ClassBlocksStore.Save(_document));
            localSaved = true;
            SetDirty(false);
            _status.Text = "本地 Lua 已保存，正在更新配置并同步插件…";
            var result = await Task.Run(() => _updates.Update(_document.FilePath));
            var warnings = result.Config.Warnings.Count + (result.Keymap?.Warnings.Count ?? 0);
            var sync = result.AddonSync;
            _status.Text = sync.CompletedSuccessfully
                ? $"已保存并应用 · 警告 {warnings}"
                : $"本地配置已更新 · {sync.SkippedReason ?? $"插件同步失败 {sync.Failures.Count} 项"}";
            _setGlobalStatus(_status.Text);
            await _configChanged();
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

    private void AddState()
    {
        var category = _stateCategory.SelectedItem as string ?? ClassStateCatalog.CategoryState;
        var used = _states.Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        var name = ClassStateCatalog.GetOptions(category).Select(option => option.Name)
            .FirstOrDefault(option => !used.Contains(option) && !IsFixedState(option)) ?? "新状态";
        _states.Add(new StateRow { Name = name });
        MarkDirty();
    }

    private void AddSpellListEntry()
    {
        if (_document is null) return;
        var usedIndices = _spellList.Select(row => int.TryParse(row.Index, out var value) ? value : 0).ToHashSet();
        var next = Enumerable.Range(1, 100).FirstOrDefault(index => !usedIndices.Contains(index));
        if (next == 0)
        {
            _status.Text = "索引 1–100 已全部使用";
            return;
        }
        var source = new ClassBlocksStore.SpellsListEntry { Index = next };
        _document.SpellsList.Add(source);
        _spellList.Add(new SpellListRow(source));
        MarkDirty();
    }

    private List<ClassBlocksStore.AuraEntry> ResolveAuraList(string key) => key switch
    {
        "target.harmful" => _spec?.TargetHarmfulAuras ?? [],
        "target.helpful" => _spec?.TargetHelpfulAuras ?? [],
        "focus.harmful" => _spec?.FocusHarmfulAuras ?? [],
        "focus.helpful" => _spec?.FocusHelpfulAuras ?? [],
        _ => _spec?.PlayerAuras ?? []
    };

    private void UpdateGroupEnabled()
    {
        var enabled = _groupEnabled.IsChecked == true;
        _groupNum.IsEnabled = enabled;
        _groupHasHealth.IsEnabled = enabled;
        _groupHealth.IsEnabled = enabled && _groupHasHealth.IsChecked == true;
        _groupHasRole.IsEnabled = enabled;
        _groupRole.IsEnabled = enabled && _groupHasRole.IsChecked == true;
        _groupHasDispel.IsEnabled = enabled;
        _groupDispel.IsEnabled = enabled && _groupHasDispel.IsChecked == true;
        _groupAurasGrid.IsEnabled = enabled;
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        _status.Text = dirty ? "已修改（未保存）" : _document is null ? _status.Text : "可编辑";
    }

    private void RestoreCleanStateAfterBinding(bool preserveDirty)
    {
        if (preserveDirty)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => Dispatcher.UIThread.Post(() => SetDirty(false), DispatcherPriority.Background),
            DispatcherPriority.Background);
    }

    private void MarkDirty()
    {
        if (!_suppress && !_busy && _document?.IsModernFormat == true) SetDirty(true);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _saveButton.IsEnabled = !busy && _document?.IsModernFormat == true;
        _editor.IsEnabled = !busy && _document?.IsModernFormat == true;
    }

    private void SelectClass(int? classId)
    {
        _suppress = true;
        _classList.SelectedItem = _classes.FirstOrDefault(item => item.ClassId == classId);
        _suppress = false;
    }

    private void SelectSpec(int? specId)
    {
        _suppress = true;
        _specList.SelectedItem = _specs.FirstOrDefault(item => item.SpecId == specId);
        _suppress = false;
    }

    private void SelectCombo(ComboBox combo, object value)
    {
        _suppress = true;
        combo.SelectedItem = value;
        _suppress = false;
    }

    private async Task<bool> ConfirmAsync(string message, string confirmText)
    {
        var result = false;
        var dialog = Dialog("确认", 440, 190);
        var cancel = new Button { Content = "取消", MinWidth = 90 };
        var confirm = new Button { Content = confirmText, MinWidth = 100 };
        cancel.Click += (_, _) => dialog.Close();
        confirm.Click += (_, _) => { result = true; dialog.Close(); };
        dialog.Content = DialogContent(message, cancel, confirm);
        await dialog.ShowDialog(_owner);
        return result;
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = Dialog(title, 470, 210);
        var close = new Button { Content = "确定", MinWidth = 90 };
        close.Click += (_, _) => dialog.Close();
        dialog.Content = DialogContent(message, close);
        await dialog.ShowDialog(_owner);
    }

    private static Window Dialog(string title, double width, double height) => new()
    {
        Title = title, Width = width, Height = height, CanResize = false,
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

    private static bool TryOptionalLong(string text, out long? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0) return false;
        value = parsed;
        return true;
    }

    private static bool TryOptionalInt(string text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return false;
        value = parsed;
        return true;
    }

    private static bool TryLongList(string text, out List<long> values)
    {
        values = [];
        foreach (var part in text.Split([',', ' ', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!long.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value <= 0) return false;
            values.Add(value);
        }
        return true;
    }

    private static bool TryRange(string? text, int min, int max, out int value) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;

    private static bool TryOptionalRange(CheckBox enabled, string? text, int min, int max, out int? value)
    {
        value = null;
        if (enabled.IsChecked != true) return true;
        if (!TryRange(text, min, max, out var parsed)) return false;
        value = parsed;
        return true;
    }

    private static void NormalizeFixedStates(ClassBlocksStore.SpecBlocks spec)
    {
        var states = spec.NestedStates ? spec.CategorizedStates[ClassStateCatalog.CategoryState] : spec.FlatStates;
        states.RemoveAll(IsFixedState);
        states.InsertRange(0, FixedStateNames);
    }

    private static bool IsFixedState(string? value) => value is not null && FixedStateNames.Contains(value, StringComparer.Ordinal);
    private static TextBox NumberBox(string placeholder, string value) => new() { PlaceholderText = placeholder, Text = value };

    private static DataGrid CreateGrid<T>(ObservableCollection<T> rows, params DataGridColumn[] columns)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows, AutoGenerateColumns = false, SelectionMode = DataGridSelectionMode.Single,
            CanUserResizeColumns = true, CanUserReorderColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        foreach (var column in columns) grid.Columns.Add(column);
        return grid;
    }

    private static DataGridTextColumn TextColumn(string title, string property, double width) => new()
    {
        Header = title, Binding = new Binding(property) { Mode = BindingMode.TwoWay }, Width = new DataGridLength(width)
    };

    private static DataGridCheckBoxColumn CheckColumn(string title, string property, double width) => new()
    {
        Header = title, Binding = new Binding(property) { Mode = BindingMode.TwoWay }, Width = new DataGridLength(width)
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
        foreach (var button in buttons) panel.Children.Add(button);
        return panel;
    }

    private static T At<T>(T control, int row, int column) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    private enum EditAction { Remove, Up, Down }
    private sealed record AuraBucket(string Key, string Name) { public override string ToString() => Name; }
    private sealed record ClassOption(int ClassId, string Name, bool IsModern, string? Error)
    {
        public override string ToString() => Error is not null ? $"{Name}（错误）" : IsModern ? Name : $"{Name}（旧格式）";
    }
    private sealed record SpecOption(int SpecId, string Name) { public override string ToString() => Name; }
    private sealed class StateRow { public string Name { get; set; } = string.Empty; }
    private sealed class AuraRow
    {
        public AuraRow() { }
        public AuraRow(ClassBlocksStore.AuraEntry source)
        {
            Name = source.Name; SpellId = source.SpellId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            SpellIds = string.Join(", ", source.SpellIds); MaxApps = source.MaxApps?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }
        public string Name { get; set; } = string.Empty;
        public string SpellId { get; set; } = string.Empty;
        public string SpellIds { get; set; } = string.Empty;
        public string MaxApps { get; set; } = string.Empty;
    }
    private sealed class SpellRow
    {
        public SpellRow() { }
        public SpellRow(ClassBlocksStore.SpellEntry source)
        {
            Name = source.Name; SpellId = source.SpellId.ToString(CultureInfo.InvariantCulture); Charge = source.Charge;
            MaxCharge = source.MaxCharge?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            CastCount = source.CastCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            ForcedKnown = source.ForcedKnown; InSpellBook = source.InSpellBook;
        }
        public string Name { get; set; } = string.Empty;
        public string SpellId { get; set; } = string.Empty;
        public bool Charge { get; set; }
        public string MaxCharge { get; set; } = string.Empty;
        public string CastCount { get; set; } = string.Empty;
        public bool ForcedKnown { get; set; }
        public bool InSpellBook { get; set; }
    }
    private sealed class SpellListRow
    {
        public SpellListRow(ClassBlocksStore.SpellsListEntry source)
        {
            Source = source; SpellId = source.SpellId == 0 ? string.Empty : source.SpellId.ToString(CultureInfo.InvariantCulture);
            Index = source.Index.ToString(CultureInfo.InvariantCulture); Name = source.Name;
        }
        public ClassBlocksStore.SpellsListEntry Source { get; }
        public string SpellId { get; set; }
        public string Index { get; set; }
        public string Name { get; set; }
    }
    private sealed class GroupAuraRow
    {
        public GroupAuraRow() { }
        public GroupAuraRow(ClassBlocksStore.GroupAuraEntry source)
        {
            Offset = source.Offset.ToString(CultureInfo.InvariantCulture); Name = source.Name;
            SpellId = source.SpellId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty; SpellIds = string.Join(", ", source.SpellIds);
        }
        public string Offset { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SpellId { get; set; } = string.Empty;
        public string SpellIds { get; set; } = string.Empty;
    }
}
