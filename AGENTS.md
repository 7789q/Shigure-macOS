# Shigure macOS 协作规则

## 项目定位

- 本仓库只交付原生 macOS 版本，不包含 Windows WinForms/Win32 项目。
- Windows 上游为 `waynebian01/Shigure`；来源基线只在 `upstream.json` 维护。
- `README.md` 是公开使用与构建入口，`Documentation/macOS/README.md` 说明架构和发行边界。

## 实施边界

- `Apps/Shigure.MacUI` 是正式 UI，`Apps/Shigure.MacApp` 提供 Mac 业务组合与命令入口。
- `Core/Shigure.Core.csproj` 显式编译 `App/Infrastructure/Input/Modules/Runtime` 中的共享源；这些目录不是 Windows 项目残留。
- 原生能力只进入 `Platforms/Shigure.Platform.Mac`，平台合同进入 `Platforms/Shigure.Platform.Abstractions`。
- `Fuyutsui/` 是插件权威源；`config/` 和 `keymap/` 不得复制第二套生成逻辑。
- 上游变化必须按文件审查并移植，不得把完整 Windows 树直接合并进本仓库。
- 不提交 `bin/`、`obj/`、`artifacts/`、`.vs/`、权限数据库、日志、屏幕导出、本地模块、签名材料或凭据。

## 本地门禁

```bash
dotnet restore Shigure.slnx
dotnet build Shigure.slnx --configuration Release --no-restore
dotnet run --project Tests/Shigure.Core.ContractTests/Shigure.Core.ContractTests.csproj --configuration Release --no-build
dotnet build Apps/Shigure.MacUI/Shigure.MacUI.csproj --configuration Release --runtime osx-arm64
dotnet build Apps/Shigure.MacUI/Shigure.MacUI.csproj --configuration Release --runtime osx-x64
```

修改打包脚本时追加 `bash -n Packaging/macOS/*.sh`，修改 workflow 时解析 YAML。真实 Developer ID、公证、更新和游戏输入输出必须单独验收，不能由本地构建推导。
本地打包还必须保持 TCC 签名合同：外层/UI 标识符为 `com.arasaka.shigure.mac`，运行时子进程 `Shigure.MacApp` 标识符为 `Shigure`，三者 designated requirement 固定到同一证书根且不得绑定 `cdhash`；变更后必须检查实际 `.app` 的嵌套签名。

## 奶骑美德回归门禁

- 修改吸奶盾、美德、DiGua 桥接、AOE 阶段或相关同步/打包逻辑前，必须先阅读 `Documentation/holy-paladin-virtue-implementation.md`。
- 现役吸奶盾入口是兼容桥复刻 DiGua 的实时姓名板条件并调用 `ObserveAOEDiGuaBar(132334, 11.7, "准备吸奶盾", unit)`；不得改回钩取兼容桥自身 `addonTable.CustomEncounterBar` 的无效方案。
- 必须保持 `11.7 秒倒计时 -> 再等待 2 秒 -> 类型 2/阶段 3 -> 模块规则 3 -> WoW 动作确认` 的链路。普通 AOE 仍走真实读条，不得与吸奶盾一并改成倒计时提交。
- 修改该链路时不得顺带调整其他技能优先级；必须运行生产 Lua 回放和契约测试，并以实战日志中的美德最终确认作为游戏内验收依据。
