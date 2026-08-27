using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Shigure.MacUI;

public sealed class ModuleEditorView : UserControl
{
    private readonly Window _owner;
    private readonly ModuleStore _store;
    private readonly ModuleMarketplaceClient _marketplace;
    private readonly Func<ModuleDefinition, string?> _captureDependencies;
    private readonly Func<bool, bool, Task<bool>> _importDependencies;
    private readonly Action _modulesChanged;
    private readonly Action<string> _setStatus;
    private readonly ObservableCollection<string> _moduleNames = [];
    private readonly ObservableCollection<UnitRow> _units = [];
    private readonly ObservableCollection<CountRow> _counts = [];
    private readonly ObservableCollection<AdjustmentRow> _adjustments = [];
    private readonly ObservableCollection<RuleRow> _rules = [];
    private readonly ListBox _moduleList = new() { MinWidth = 210 };
    private readonly TextBox _name = new() { PlaceholderText = "模块名称" };
    private readonly TextBox _author = new() { PlaceholderText = "作者" };
    private readonly TextBox _recommendedTalent = new() { PlaceholderText = "推荐天赋" };
    private readonly TextBox _classId = new() { PlaceholderText = "任意" };
    private readonly TextBox _specId = new() { PlaceholderText = "任意" };
    private readonly TextBox _partyType = new() { PlaceholderText = "任意 / 单人 / 团队 / 队伍" };
    private readonly TextBox _heroTalent = new() { PlaceholderText = "任意" };
    private readonly CheckBox _enabled = new() { Content = "启用模块", VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _path = new() { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _dependencySummary = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _editorStatus = new() { Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center };
    private readonly DataGrid _unitsGrid;
    private readonly DataGrid _countsGrid;
    private readonly DataGrid _adjustmentsGrid;
    private readonly DataGrid _rulesGrid;
    private readonly Control _editor;
    private readonly Button _saveButton;
    private readonly Button _deleteButton;
    private IReadOnlyList<ModuleDefinition> _loadedModules = [];
    private ModuleDefinition? _selected;
    private string? _baseline;
    private bool _suppressSelection;
    private bool _busy;

    public ModuleEditorView(
        Window owner,
        ModuleStore store,
        ModuleMarketplaceClient marketplace,
        Func<ModuleDefinition, string?> captureDependencies,
        Func<bool, bool, Task<bool>> importDependencies,
        Action modulesChanged,
        Action<string> setStatus)
    {
        _owner = owner;
        _store = store;
        _marketplace = marketplace;
        _captureDependencies = captureDependencies;
        _importDependencies = importDependencies;
        _modulesChanged = modulesChanged;
        _setStatus = setStatus;

        AutomationProperties.SetName(_moduleList, "本地模块列表");
        AutomationProperties.SetName(_name, "模块名称");
        AutomationProperties.SetName(_author, "模块作者");
        AutomationProperties.SetName(_recommendedTalent, "推荐天赋");

        _unitsGrid = CreateGrid(_units,
            TextColumn("名称", nameof(UnitRow.Name), 130),
            ComboColumn<UnitRow>(
                "类型",
                row => row.Kind,
                (row, value) => row.Kind = value,
                Enum.GetNames<UnitSelectorKind>(),
                190),
            TextColumn("生命值名", nameof(UnitRow.HealthName), 120),
            TextColumn("阈值", nameof(UnitRow.HealthThreshold), 76),
            TextColumn("阈值字段", nameof(UnitRow.HealthThresholdField), 110),
            ComboColumn<UnitRow>(
                "职责过滤",
                row => row.RoleFilter,
                (row, value) => row.RoleFilter = value,
                new[] { string.Empty }.Concat(Enum.GetNames<UnitRoleFilterKind>()).ToArray(),
                100),
            TextColumn("职责", nameof(UnitRow.Role), 64),
            CheckColumn("逆序", nameof(UnitRow.Reverse), 62),
            TextColumn("光环（逗号分隔）", nameof(UnitRow.AuraNames), 180),
            TextColumn("层数", nameof(UnitRow.AuraCount), 64),
            TextColumn("驱散", nameof(UnitRow.DispelType), 64));
        _countsGrid = CreateGrid(_counts,
            TextColumn("名称", nameof(CountRow.Name), 150),
            ComboColumn<CountRow>(
                "类型",
                row => row.Kind,
                (row, value) => row.Kind = value,
                Enum.GetNames<CountKind>(),
                220),
            TextColumn("阈值", nameof(CountRow.HealthThreshold), 84),
            TextColumn("阈值字段", nameof(CountRow.HealthThresholdField), 130),
            TextColumn("光环", nameof(CountRow.AuraName), 160));
        _adjustmentsGrid = CreateGrid(_adjustments,
            CheckColumn("启用", nameof(AdjustmentRow.Enabled), 62),
            TextColumn("数值名称", nameof(AdjustmentRow.Field), 150),
            TextColumn("条件", nameof(AdjustmentRow.Condition), 220),
            TextColumn("增量", nameof(AdjustmentRow.Delta), 76),
            TextColumn("公式", nameof(AdjustmentRow.Formula), 260));
        _rulesGrid = CreateGrid(_rules,
            CheckColumn("启用", nameof(RuleRow.Enabled), 62),
            TextColumn("备注", nameof(RuleRow.Comment), 160),
            TextColumn("技能/动作", nameof(RuleRow.Spell), 150),
            TextColumn("目标", nameof(RuleRow.Unit), 100),
            TextColumn("宏条件", nameof(RuleRow.MacroCondition), 120),
            TextColumn("主条件", nameof(RuleRow.Condition), 240),
            TextColumn("子条件（分号）", nameof(RuleRow.SubConditions), 220),
            TextColumn("规则延迟", nameof(RuleRow.DelayMs), 84),
            TextColumn("逻辑暂停", nameof(RuleRow.LogicDelayMs), 84));
        AutomationProperties.SetName(_unitsGrid, "动态单位表格");
        AutomationProperties.SetName(_countsGrid, "数量字段表格");
        AutomationProperties.SetName(_adjustmentsGrid, "数值调整表格");
        AutomationProperties.SetName(_rulesGrid, "模块规则表格");

        var newButton = CommandButton("新建", async (_, _) => await CreateModuleAsync());
        var duplicateButton = CommandButton("复制", async (_, _) => await DuplicateModuleAsync());
        var refreshButton = CommandButton("刷新", async (_, _) => await RefreshAsync());
        var downloadButton = CommandButton("下载", async (_, _) => await OpenMarketplaceAsync());
        var importDependenciesButton = CommandButton(
            "导入全部模块依赖",
            async (_, _) => await ImportDependenciesAsync());
        AutomationProperties.SetName(importDependenciesButton, "导入全部模块依赖");
        _saveButton = CommandButton("保存模块", async (_, _) => await SaveAsync());
        _deleteButton = CommandButton("删除模块", async (_, _) => await DeleteAsync());

        var sidebarActions = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowSpacing = 8,
            ColumnSpacing = 8
        };
        sidebarActions.Children.Add(newButton);
        sidebarActions.Children.Add(At(duplicateButton, 0, 1));
        sidebarActions.Children.Add(At(refreshButton, 1, 0));
        sidebarActions.Children.Add(At(downloadButton, 1, 1));
        var sidebar = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10 };
        sidebar.Children.Add(_moduleList);
        sidebar.Children.Add(At(sidebarActions, 1, 0));

        var metadata = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            ColumnDefinitions = new ColumnDefinitions("2*,*,Auto"),
            RowSpacing = 8,
            ColumnSpacing = 10
        };
        metadata.Children.Add(Field("名称", _name));
        metadata.Children.Add(At(Field("作者", _author), 0, 1));
        metadata.Children.Add(At(Field("状态", _enabled), 0, 2));
        metadata.Children.Add(At(Field("推荐天赋", _recommendedTalent), 1, 0));
        Grid.SetColumnSpan(metadata.Children[^1], 2);
        metadata.Children.Add(At(Field("文件", _path), 1, 2));

        var match = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            ColumnSpacing = 10
        };
        match.Children.Add(Field("职业 ID", _classId));
        match.Children.Add(At(Field("专精 ID", _specId), 0, 1));
        match.Children.Add(At(Field("队伍类型", _partyType), 0, 2));
        match.Children.Add(At(Field("英雄天赋 ID", _heroTalent), 0, 3));
        metadata.Children.Add(At(match, 2, 0));
        Grid.SetColumnSpan(metadata.Children[^1], 3);

        var tabs = new TabControl
        {
            ItemsSource = new object[]
            {
                EditorTab("规则", _rulesGrid, _rules,
                    () => new RuleRow { Enabled = true, Unit = "目标" }),
                EditorTab("动态单位", _unitsGrid, _units,
                    () => new UnitRow { Name = "新单位", Kind = UnitSelectorKind.LowestHealth.ToString() }),
                EditorTab("数量字段", _countsGrid, _counts,
                    () => new CountRow { Name = "新数量", Kind = CountKind.UnitsBelowHealth.ToString() }),
                EditorTab("数值调整", _adjustmentsGrid, _adjustments,
                    () => new AdjustmentRow { Enabled = true, Field = "新数值" }),
                new TabItem
                {
                    Header = "依赖",
                    Content = new ScrollViewer
                    {
                        Padding = new Thickness(12),
                        Content = new StackPanel
                        {
                            Spacing = 12,
                            Children =
                            {
                                _dependencySummary,
                                new TextBlock
                                {
                                    Text = "保存模块时自动从当前职业和专精捕获依赖。导入只补充本地缺失项，冲突保留本地内容。",
                                    Classes = { "muted" },
                                    TextWrapping = TextWrapping.Wrap
                                },
                                importDependenciesButton
                            }
                        }
                    }
                }
            }
        };

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        footer.Children.Add(_editorStatus);
        footer.Children.Add(At(ActionRow(_deleteButton, _saveButton), 0, 1));

        _editor = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 12,
            Children =
            {
                metadata,
                At(tabs, 1, 0),
                At(footer, 2, 0)
            }
        };
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("230,1,*") };
        root.Children.Add(sidebar);
        root.Children.Add(At(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#30353A")),
            Margin = new Thickness(14, 0)
        }, 0, 1));
        root.Children.Add(At(_editor, 0, 2));
        Content = root;

        _moduleList.ItemsSource = _moduleNames;
        _moduleList.SelectionChanged += HandleSelectionChanged;
        ReloadModules(reloadStore: false);
    }

    public bool HasUnsavedChanges => HasUnsavedChangesCore();

    public async Task<bool> ConfirmDiscardBeforeExitAsync() =>
        !HasUnsavedChangesCore()
        || await ConfirmAsync("当前模块有未保存修改。是否放弃修改并退出？", "放弃并退出");

    public void ReloadModulesFromStore()
    {
        var selectedId = _selected?.Id;
        ReloadModules(reloadStore: false, selectedId);
    }

    private async void HandleSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || _moduleList.SelectedIndex < 0 || _moduleList.SelectedIndex >= _loadedModules.Count)
        {
            return;
        }

        var candidate = _loadedModules[_moduleList.SelectedIndex];
        if (_selected is not null
            && string.Equals(candidate.Id, _selected.Id, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (HasUnsavedChangesCore() && !await ConfirmAsync("当前模块有未保存修改。是否放弃修改并切换？", "放弃修改"))
        {
            RestoreSelection();
            return;
        }

        LoadEditor(candidate);
    }

    private async Task CreateModuleAsync()
    {
        await RunAsync(async () =>
        {
            if (HasUnsavedChangesCore() && !await ConfirmAsync("当前模块有未保存修改。是否放弃修改并新建模块？", "放弃并新建"))
            {
                return;
            }

            var name = await PromptModuleNameAsync(_store.CreateNextModuleName());
            if (name is null)
            {
                return;
            }

            var module = ModuleDefinition.CreateDefault(name);
            module.Version = CurrentVersion();
            var saved = _store.Save(module);
            ModulesChanged($"已新建模块：{saved.Name}");
            ReloadModules(reloadStore: false, saved.Id);
        });
    }

    private async Task DuplicateModuleAsync()
    {
        if (_selected is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            if (HasUnsavedChangesCore() && !await ConfirmAsync("复制将使用上次保存的内容。是否继续？", "继续复制"))
            {
                return;
            }

            var copy = _selected.Clone();
            copy.Name = CreateCopyName(_selected.Name);
            copy.Id = ModuleStore.CreateModuleId(copy.Name);
            copy.FilePath = null;
            copy.Version = CurrentVersion();
            var saved = _store.Save(copy);
            ModulesChanged($"已复制模块：{saved.Name}");
            ReloadModules(reloadStore: false, saved.Id);
        });
    }

    private async Task SaveAsync()
    {
        if (_selected is null)
        {
            return;
        }

        await RunAsync(() =>
        {
            CommitEdits();
            var module = BuildModule(setVersion: true);
            var warning = _captureDependencies(module);
            var saved = _store.Save(module);
            ModulesChanged(warning is null
                ? $"已保存模块并捕获依赖：{saved.Name}"
                : $"已保存模块：{saved.Name}；{warning}");
            ReloadModules(reloadStore: false, saved.Id);
            return Task.CompletedTask;
        });
    }

    private async Task DeleteAsync()
    {
        if (_selected is null
            || !await ConfirmAsync($"删除模块“{_selected.Name}”？此操作会删除对应 JSON 文件。", "删除"))
        {
            return;
        }

        await RunAsync(() =>
        {
            var name = _selected.Name;
            _store.Delete(_selected);
            ModulesChanged($"已删除模块：{name}");
            ReloadModules(reloadStore: false);
            return Task.CompletedTask;
        });
    }

    private async Task RefreshAsync()
    {
        if (HasUnsavedChangesCore() && !await ConfirmAsync("刷新会放弃当前未保存修改。是否继续？", "刷新"))
        {
            return;
        }

        ReloadModulesFromStore();

        await RunAsync(async () =>
        {
            if (!await _importDependencies(true, true))
            {
                return;
            }
            _setStatus($"已加载 {_loadedModules.Count} 个本地模块");
        });
    }

    private Task ImportDependenciesAsync() => RunAsync(async () =>
    {
        await _importDependencies(false, false);
    });

    private async Task OpenMarketplaceAsync()
    {
        if (HasUnsavedChangesCore()
            && !await ConfirmAsync("打开下载窗口会刷新本地模块列表。是否放弃当前未保存修改？", "放弃并打开"))
        {
            return;
        }

        ReloadModulesFromStore();

        var installed = false;
        var window = new ModuleMarketplaceWindow(
            _store,
            _marketplace,
            () => installed = true);
        await window.ShowDialog(_owner);
        if (installed)
        {
            await RunAsync(async () =>
            {
                if (!await _importDependencies(false, true))
                {
                    ReloadModulesFromStore();
                }
            });
        }
    }

    private void ReloadModules(bool reloadStore, string? selectId = null)
    {
        if (reloadStore)
        {
            _store.Reload();
        }

        _loadedModules = _store.GetModules();
        _suppressSelection = true;
        _moduleNames.Clear();
        foreach (var module in _loadedModules)
        {
            _moduleNames.Add(module.Enabled ? module.Name : $"[停用] {module.Name}");
        }

        var selectedIndex = string.IsNullOrWhiteSpace(selectId)
            ? (_loadedModules.Count > 0 ? 0 : -1)
            : IndexOfModule(selectId);
        if (selectedIndex < 0 && _loadedModules.Count > 0)
        {
            selectedIndex = 0;
        }

        _moduleList.SelectedIndex = selectedIndex;
        _suppressSelection = false;
        if (selectedIndex >= 0)
        {
            LoadEditor(_loadedModules[selectedIndex]);
        }
        else
        {
            ClearEditor();
        }
    }

    private void LoadEditor(ModuleDefinition module)
    {
        _selected = module.Clone();
        _name.Text = module.Name;
        _author.Text = module.Author;
        _recommendedTalent.Text = module.RecommendedTalent;
        _enabled.IsChecked = module.Enabled;
        _classId.Text = NullableText(module.Match.ClassId);
        _specId.Text = NullableText(module.Match.SpecId);
        _partyType.Text = module.Match.PartyType ?? string.Empty;
        _heroTalent.Text = NullableText(module.Match.HeroTalent);
        _path.Text = string.IsNullOrWhiteSpace(module.FilePath) ? "尚未保存" : Path.GetFileName(module.FilePath);

        Replace(_units, module.Units.Select(UnitRow.FromModel));
        Replace(_counts, module.Counts.Select(CountRow.FromModel));
        Replace(_adjustments, module.ValueAdjustments.Select(AdjustmentRow.FromModel));
        Replace(_rules, module.Rules.Select(RuleRow.FromModel));
        UpdateDependencySummary(module.Dependencies);
        _baseline = Fingerprint(BuildModule(setVersion: false));
        _editorStatus.Text = $"{module.Rules.Count} 条规则 · v{module.Version}";
        SetEditorEnabled(true);
    }

    private void ClearEditor()
    {
        _selected = null;
        _baseline = null;
        _name.Text = string.Empty;
        _author.Text = string.Empty;
        _recommendedTalent.Text = string.Empty;
        _enabled.IsChecked = false;
        _classId.Text = string.Empty;
        _specId.Text = string.Empty;
        _partyType.Text = string.Empty;
        _heroTalent.Text = string.Empty;
        _path.Text = "无模块";
        _units.Clear();
        _counts.Clear();
        _adjustments.Clear();
        _rules.Clear();
        _dependencySummary.Text = "无依赖快照";
        _editorStatus.Text = "请新建或下载模块";
        SetEditorEnabled(false);
    }

    private ModuleDefinition BuildModule(bool setVersion)
    {
        if (_selected is null)
        {
            throw new InvalidOperationException("没有选中的模块。");
        }

        var name = (_name.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            throw new InvalidOperationException("模块名称不能为空。");
        }

        var module = _selected.Clone();
        module.Name = name;
        module.Author = (_author.Text ?? string.Empty).Trim();
        module.RecommendedTalent = (_recommendedTalent.Text ?? string.Empty).Trim();
        module.Enabled = _enabled.IsChecked == true;
        if (setVersion)
        {
            module.Version = CurrentVersion();
        }

        module.Match = new ModuleMatch
        {
            ClassId = ParseNullableInt(_classId.Text, "职业 ID"),
            SpecId = ParseNullableInt(_specId.Text, "专精 ID"),
            PartyType = ModuleMatch.NormalizePartyTypeValue(_partyType.Text),
            HeroTalent = ParseNullableInt(_heroTalent.Text, "英雄天赋 ID")
        };
        module.Units = BuildUnits();
        module.Counts = BuildCounts();
        module.ValueAdjustments = BuildAdjustments();
        module.Rules = BuildRules(module.Units);
        return module;
    }

    private List<ModuleUnit> BuildUnits()
    {
        var result = new List<ModuleUnit>();
        foreach (var row in _units)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            result.Add(new ModuleUnit
            {
                Name = row.Name.Trim(),
                HealthName = NullIfBlank(row.HealthName),
                Kind = ParseEnum<UnitSelectorKind>(row.Kind, "动态单位类型"),
                HealthThreshold = ParseNullableInt(row.HealthThreshold, "动态单位生命阈值"),
                HealthThresholdField = NullIfBlank(row.HealthThresholdField),
                RoleFilter = ParseNullableEnum<UnitRoleFilterKind>(row.RoleFilter, "职责过滤"),
                Role = ParseNullableInt(row.Role, "职责"),
                Reverse = row.Reverse,
                AuraNames = ParseList(row.AuraNames),
                AuraCount = ParseNullableInt(row.AuraCount, "光环层数"),
                DispelType = ParseNullableInt(row.DispelType, "驱散类型")
            });
        }

        return result;
    }

    private List<ModuleCountField> BuildCounts()
    {
        var result = new List<ModuleCountField>();
        foreach (var row in _counts)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                continue;
            }

            result.Add(new ModuleCountField
            {
                Name = row.Name.Trim(),
                Kind = ParseEnum<CountKind>(row.Kind, "数量字段类型"),
                HealthThreshold = ParseNullableInt(row.HealthThreshold, "数量字段生命阈值"),
                HealthThresholdField = NullIfBlank(row.HealthThresholdField),
                AuraName = NullIfBlank(row.AuraName)
            });
        }

        return result;
    }

    private List<ModuleValueAdjustment> BuildAdjustments()
    {
        var result = new List<ModuleValueAdjustment>();
        foreach (var row in _adjustments)
        {
            var field = (row.Field ?? string.Empty).Trim();
            if (field.Length == 0
                && string.IsNullOrWhiteSpace(row.Condition)
                && string.IsNullOrWhiteSpace(row.Formula)
                && string.IsNullOrWhiteSpace(row.Delta))
            {
                continue;
            }

            if (field.Length == 0)
            {
                throw new InvalidOperationException("数值调整缺少数值名称。");
            }

            result.Add(new ModuleValueAdjustment
            {
                Enabled = row.Enabled,
                Field = field,
                Condition = (row.Condition ?? string.Empty).Trim(),
                Delta = ParseNullableInt(row.Delta, "数值调整增量") ?? 0,
                Formula = (row.Formula ?? string.Empty).Trim()
            });
        }

        return result;
    }

    private List<ModuleRule> BuildRules(IReadOnlyList<ModuleUnit> units)
    {
        var unitNames = new HashSet<string>(units.Select(unit => unit.Name), StringComparer.Ordinal);
        var result = new List<ModuleRule>();
        foreach (var row in _rules)
        {
            if (string.IsNullOrWhiteSpace(row.Comment)
                && string.IsNullOrWhiteSpace(row.Spell)
                && string.IsNullOrWhiteSpace(row.Condition)
                && string.IsNullOrWhiteSpace(row.Unit)
                && string.IsNullOrWhiteSpace(row.MacroCondition)
                && string.IsNullOrWhiteSpace(row.SubConditions))
            {
                continue;
            }

            var unitText = (row.Unit ?? string.Empty).Trim();
            var dynamicUnit = unitNames.Contains(unitText);
            var unit = dynamicUnit || unitText.Length == 0 ? null : ReservedUnit.ParseDisplayText(unitText);
            if (!dynamicUnit && unitText.Length > 0 && unit is null)
            {
                throw new InvalidOperationException($"规则目标“{unitText}”不是动态单位、保留目标或数字槽位。");
            }

            var subConditions = ParseSubConditions(row.SubConditions);
            result.Add(new ModuleRule
            {
                Enabled = row.Enabled,
                Comment = (row.Comment ?? string.Empty).Trim(),
                Spell = (row.Spell ?? string.Empty).Trim(),
                Unit = unit,
                UnitName = dynamicUnit ? unitText : null,
                MacroCondition = MacroConditionText.ParseDisplayText(row.MacroCondition),
                Condition = (row.Condition ?? string.Empty).Trim(),
                SubConditions = subConditions.Count == 0 ? null : subConditions,
                DelayMs = ParseNullableInt(row.DelayMs, "规则延迟"),
                LogicDelayMs = ParseNullableInt(row.LogicDelayMs, "逻辑暂停"),
                Hotkey = string.Empty,
                Step = string.Empty
            });
        }

        return result;
    }

    private bool HasUnsavedChangesCore()
    {
        if (_selected is null || _baseline is null)
        {
            return false;
        }

        try
        {
            CommitEdits();
            return !string.Equals(_baseline, Fingerprint(BuildModule(setVersion: false)), StringComparison.Ordinal);
        }
        catch
        {
            return true;
        }
    }

    private void CommitEdits()
    {
        foreach (var grid in new[] { _unitsGrid, _countsGrid, _adjustmentsGrid, _rulesGrid })
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, exitEditingMode: true);
            grid.CommitEdit(DataGridEditingUnit.Row, exitEditingMode: true);
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            await ShowMessageAsync(exception.Message);
            _setStatus($"模块操作失败：{exception.Message}");
        }
        finally
        {
            _busy = false;
            SetEditorEnabled(_selected is not null);
        }
    }

    private void ModulesChanged(string status)
    {
        _modulesChanged();
        _setStatus(status);
    }

    private void RestoreSelection()
    {
        _suppressSelection = true;
        _moduleList.SelectedIndex = _selected is null ? -1 : IndexOfModule(_selected.Id);
        _suppressSelection = false;
    }

    private int IndexOfModule(string id)
    {
        for (var index = 0; index < _loadedModules.Count; index++)
        {
            if (string.Equals(_loadedModules[index].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private string CreateCopyName(string sourceName)
    {
        var names = new HashSet<string>(_loadedModules.Select(module => module.Name), StringComparer.CurrentCultureIgnoreCase);
        var baseName = $"{sourceName} 副本";
        if (!names.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{baseName} {index}";
            if (!names.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void UpdateDependencySummary(ModuleDependencySnapshot? dependencies)
    {
        if (dependencies is null)
        {
            _dependencySummary.Text = "无依赖快照";
            return;
        }

        var spec = dependencies.Config?.Spec;
        var macros = dependencies.Macros;
        var stateCount = (spec?.FlatStates?.Count ?? 0)
            + (spec?.CategorizedStates?.Values.Sum(values => values?.Count ?? 0) ?? 0);
        var auraCount = (spec?.PlayerAuras?.Count ?? 0)
            + (spec?.TargetHarmfulAuras?.Count ?? 0)
            + (spec?.TargetHelpfulAuras?.Count ?? 0)
            + (spec?.FocusHarmfulAuras?.Count ?? 0)
            + (spec?.FocusHelpfulAuras?.Count ?? 0);
        var macroCount = (macros?.DynamicCommon?.Count ?? 0)
            + (macros?.DynamicForSpec?.Count ?? 0)
            + (macros?.StaticSpells?.Count ?? 0)
            + (macros?.SpecialSpells?.Count ?? 0);
        _dependencySummary.Text =
            $"Schema {dependencies.SchemaVersion}\n"
            + $"职业/专精：{dependencies.ClassId}/{dependencies.SpecId}\n"
            + $"状态：{stateCount} · 光环：{auraCount} · 技能：{spec?.Spells?.Count ?? 0}\n"
            + $"宏：{macroCount} · spellsList：{dependencies.Config?.SpellsList?.Count ?? 0}\n\n"
            + "保存模块会重新捕获当前依赖；导入时只补充本地缺失项。";
    }

    private void SetEditorEnabled(bool enabled)
    {
        _editor.IsEnabled = enabled && !_busy;
        _saveButton.IsEnabled = enabled && !_busy;
        _deleteButton.IsEnabled = enabled && !_busy;
    }

    private async Task<bool> ConfirmAsync(string message, string confirmText)
    {
        var confirmed = false;
        var dialog = DialogWindow("确认", 430, 185);
        var cancel = DialogButton("取消", (_, _) => dialog.Close());
        var confirm = DialogButton(confirmText, (_, _) =>
        {
            confirmed = true;
            dialog.Close();
        });
        dialog.Content = DialogContent(message, cancel, confirm);
        await dialog.ShowDialog(_owner);
        return confirmed;
    }

    private async Task<string?> PromptModuleNameAsync(string initialName)
    {
        string? result = null;
        var dialog = DialogWindow("新建模块", 430, 230);
        var input = new TextBox
        {
            Text = initialName,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(input, "新模块名称");
        var error = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#FF8A80")),
            TextWrapping = TextWrapping.Wrap
        };
        var cancel = DialogButton("取消", (_, _) => dialog.Close());
        var create = DialogButton("创建", (_, _) => Commit());

        void Commit()
        {
            var name = (input.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                error.Text = "模块名称不能为空。";
                return;
            }

            if (_loadedModules.Any(module =>
                    string.Equals(module.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            {
                error.Text = "已存在同名模块。";
                return;
            }

            result = name;
            dialog.Close();
        }

        input.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            Commit();
            e.Handled = true;
        };
        input.TextChanged += (_, _) => error.Text = string.Empty;
        dialog.Opened += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "模块名称" },
                input,
                error
            }
        };
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(24)
        };
        root.Children.Add(body);
        root.Children.Add(At(ActionRow(cancel, create), 1, 0));
        dialog.Content = root;
        await dialog.ShowDialog(_owner);
        return result;
    }

    private async Task ShowMessageAsync(string message)
    {
        var dialog = DialogWindow("模块操作失败", 450, 190);
        var close = DialogButton("关闭", (_, _) => dialog.Close());
        dialog.Content = DialogContent(message, close);
        await dialog.ShowDialog(_owner);
    }

    private static Window DialogWindow(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner
    };

    private static Control DialogContent(string message, params Button[] buttons)
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        root.Children.Add(At(ActionRow(buttons), 1, 0));
        return root;
    }

    private static TabItem EditorTab<T>(
        string title,
        DataGrid grid,
        ObservableCollection<T> rows,
        Func<T> createRow)
    {
        var add = CommandButton("新增", (_, _) => rows.Add(createRow()));
        var remove = CommandButton("删除", (_, _) => RemoveSelected(grid, rows));
        var up = CommandButton("上移", (_, _) => MoveSelected(grid, rows, -1));
        var down = CommandButton("下移", (_, _) => MoveSelected(grid, rows, 1));
        var content = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 8 };
        content.Children.Add(grid);
        content.Children.Add(At(ActionRow(add, remove, up, down), 1, 0));
        return new TabItem { Header = title, Content = content };
    }

    private static DataGrid CreateGrid<T>(ObservableCollection<T> rows, params DataGridColumn[] columns)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            SelectionMode = DataGridSelectionMode.Single,
            CanUserResizeColumns = true,
            CanUserReorderColumns = true,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal
        };
        foreach (var column in columns)
        {
            grid.Columns.Add(column);
        }

        return grid;
    }

    private static DataGridTextColumn TextColumn(string title, string property, double width) => new()
    {
        Header = title,
        Binding = new Binding(property) { Mode = BindingMode.TwoWay },
        Width = new DataGridLength(width)
    };

    private static DataGridCheckBoxColumn CheckColumn(string title, string property, double width) => new()
    {
        Header = title,
        Binding = new Binding(property) { Mode = BindingMode.TwoWay },
        Width = new DataGridLength(width)
    };

    private static DataGridTemplateColumn ComboColumn<T>(
        string title,
        Func<T, string> getValue,
        Action<T, string> setValue,
        IReadOnlyList<string> options,
        double width) => new()
    {
        Header = title,
        CellTemplate = new FuncDataTemplate<T>((item, _) => new TextBlock
        {
            Text = getValue(item),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0)
        }),
        CellEditingTemplate = new FuncDataTemplate<T>((item, _) =>
        {
            var combo = new ComboBox
            {
                ItemsSource = options,
                SelectedItem = getValue(item),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            combo.SelectionChanged += (_, _) => setValue(item, combo.SelectedItem as string ?? string.Empty);
            return combo;
        }),
        Width = new DataGridLength(width)
    };

    private static StackPanel Field(string label, Control control) => new()
    {
        Spacing = 3,
        Children =
        {
            new TextBlock { Text = label, Classes = { "muted" }, FontSize = 11 },
            control
        }
    };

    private static Button CommandButton(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button = new Button { Content = text, Classes = { "command" } };
        button.Click += handler;
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static Button DialogButton(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> handler)
    {
        var button = new Button { Content = text, MinWidth = 88, MinHeight = 34 };
        button.Click += handler;
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static StackPanel ActionRow(params Control[] controls)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        foreach (var control in controls)
        {
            panel.Children.Add(control);
        }

        return panel;
    }

    private static T At<T>(T control, int row, int column) where T : Control
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        return control;
    }

    private static void RemoveSelected<T>(DataGrid grid, ObservableCollection<T> rows)
    {
        if (grid.SelectedItem is T selected)
        {
            rows.Remove(selected);
        }
    }

    private static void MoveSelected<T>(DataGrid grid, ObservableCollection<T> rows, int delta)
    {
        if (grid.SelectedItem is not T selected)
        {
            return;
        }

        var current = rows.IndexOf(selected);
        var target = current + delta;
        if (target >= 0 && target < rows.Count)
        {
            rows.Move(current, target);
            grid.SelectedItem = selected;
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private static string Fingerprint(ModuleDefinition module) => JsonSerializer.Serialize(module);

    private static string CurrentVersion() =>
        typeof(ModuleEditorView).Assembly.GetName().Version?.ToString(4) ?? string.Empty;

    private static string NullableText(int? value) => value?.ToString() ?? string.Empty;

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? ParseNullableInt(string? value, string label)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (int.TryParse(text, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"{label}“{text}”不是整数。");
    }

    private static T ParseEnum<T>(string? value, string label) where T : struct, Enum
    {
        if (Enum.TryParse<T>(value?.Trim(), ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"{label}“{value}”无效。");
    }

    private static T? ParseNullableEnum<T>(string? value, string label) where T : struct, Enum
    {
        return string.IsNullOrWhiteSpace(value) ? null : ParseEnum<T>(value, label);
    }

    private static List<string>? ParseList(string? value)
    {
        var values = (value ?? string.Empty)
            .Split([',', '，', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToList();
        return values.Count == 0 ? null : values;
    }

    private static List<string> ParseSubConditions(string? value) =>
        (value ?? string.Empty)
            .Split([';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToList();

    public sealed class UnitRow
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string HealthName { get; set; } = string.Empty;
        public string HealthThreshold { get; set; } = string.Empty;
        public string HealthThresholdField { get; set; } = string.Empty;
        public string RoleFilter { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool Reverse { get; set; }
        public string AuraNames { get; set; } = string.Empty;
        public string AuraCount { get; set; } = string.Empty;
        public string DispelType { get; set; } = string.Empty;

        public static UnitRow FromModel(ModuleUnit model) => new()
        {
            Name = model.Name,
            Kind = model.Kind.ToString(),
            HealthName = model.HealthName ?? string.Empty,
            HealthThreshold = NullableText(model.HealthThreshold),
            HealthThresholdField = model.HealthThresholdField ?? string.Empty,
            RoleFilter = model.RoleFilter?.ToString() ?? string.Empty,
            Role = NullableText(model.Role),
            Reverse = model.Reverse,
            AuraNames = string.Join(", ", model.AuraNames ?? []),
            AuraCount = NullableText(model.AuraCount),
            DispelType = NullableText(model.DispelType)
        };
    }

    public sealed class CountRow
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string HealthThreshold { get; set; } = string.Empty;
        public string HealthThresholdField { get; set; } = string.Empty;
        public string AuraName { get; set; } = string.Empty;

        public static CountRow FromModel(ModuleCountField model) => new()
        {
            Name = model.Name,
            Kind = model.Kind.ToString(),
            HealthThreshold = NullableText(model.HealthThreshold),
            HealthThresholdField = model.HealthThresholdField ?? string.Empty,
            AuraName = model.AuraName ?? string.Empty
        };
    }

    public sealed class AdjustmentRow
    {
        public bool Enabled { get; set; } = true;
        public string Field { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty;
        public string Delta { get; set; } = string.Empty;
        public string Formula { get; set; } = string.Empty;

        public static AdjustmentRow FromModel(ModuleValueAdjustment model) => new()
        {
            Enabled = model.Enabled,
            Field = model.Field,
            Condition = model.Condition,
            Delta = model.Delta.ToString(),
            Formula = model.Formula
        };
    }

    public sealed class RuleRow
    {
        public bool Enabled { get; set; } = true;
        public string Comment { get; set; } = string.Empty;
        public string Spell { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public string MacroCondition { get; set; } = string.Empty;
        public string Condition { get; set; } = string.Empty;
        public string SubConditions { get; set; } = string.Empty;
        public string DelayMs { get; set; } = string.Empty;
        public string LogicDelayMs { get; set; } = string.Empty;

        public static RuleRow FromModel(ModuleRule model) => new()
        {
            Enabled = model.Enabled,
            Comment = model.Comment,
            Spell = model.Spell,
            Unit = !string.IsNullOrWhiteSpace(model.UnitName)
                ? model.UnitName
                : model.Unit is { } unit ? ReservedUnit.ToDisplayText(unit) : string.Empty,
            MacroCondition = MacroConditionText.ToDisplayText(model.MacroCondition),
            Condition = model.Condition,
            SubConditions = string.Join("; ", model.SubConditions ?? []),
            DelayMs = NullableText(model.DelayMs),
            LogicDelayMs = NullableText(model.LogicDelayMs)
        };
    }
}
