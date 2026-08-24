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
