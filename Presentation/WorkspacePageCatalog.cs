namespace Shigure.Presentation;

public enum WorkspacePage
{
    General,
    Config,
    Macros,
    Modules,
    Status,
    Party,
    Logic,
    Logs,
    About
}

public sealed record WorkspacePageDescriptor(
    WorkspacePage Page,
    string Group,
    string Title,
    string Subtitle);

public static class WorkspacePageCatalog
{
    public static IReadOnlyList<WorkspacePageDescriptor> All { get; } =
    [
        new(WorkspacePage.General, "常用", "通用", "运行控制、配置同步与模块选择"),
        new(WorkspacePage.Config, "编辑", "配置", "编辑职业、专精和扫描字段"),
        new(WorkspacePage.Macros, "编辑", "宏", "维护职业动态宏、静态宏与特殊宏"),
        new(WorkspacePage.Modules, "编辑", "模块", "创建、匹配并维护运行模块"),
        new(WorkspacePage.Status, "监控", "状态", string.Empty),
        new(WorkspacePage.Party, "监控", "队伍", "当前队伍单位与扫描字段摘要"),
        new(WorkspacePage.Logic, "监控", "逻辑", "运行时推荐目标与调试值"),
        new(WorkspacePage.Logs, "监控", "日志", "运行、模块匹配与施放记录"),
        new(WorkspacePage.About, "系统", "关于", "应用信息与状态字段参考")
    ];
}
