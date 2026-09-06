# Shigure for macOS

Shigure 的原生 macOS 版本。应用读取 Fuyutsui 在目标游戏窗口绘制的像素状态，按职业 keymap 和模块规则选择按键，并通过 Avalonia 界面展示状态、队伍、逻辑结果和运行日志。

本仓库只发布 macOS 应用及其必需的共享核心，不包含 Windows WinForms/Win32 项目。Windows 原版与上游源码位于 [waynebian01/Shigure](https://github.com/waynebian01/Shigure)。本项目基于其 MIT 许可源码继续开发，基线记录在 [`upstream.json`](upstream.json)，许可证与原作者声明见 [LICENSE](LICENSE)。

当前版本的用户可见变化见 [更新日志](CHANGELOG.md)。

## 文档导航

- 使用、构建和权限：本文档。
- macOS 架构与发行边界：[Documentation/macOS/README.md](Documentation/macOS/README.md)。
- 版本变化：[CHANGELOG.md](CHANGELOG.md)。
- 模块与协议实现：[Documentation/](Documentation/)。

## 快速开始

1. 确认 macOS 13+、`.NET 10 SDK` 和 Xcode Command Line Tools 已安装。
2. 在“系统设置 → 隐私与安全性”中，为实际运行的 Shigure 应用授予屏幕录制和辅助功能权限。
3. 启动应用，按提示同步游戏插件；同步完成后在 WoW 中执行一次 `/reload`。
4. 首次构建或权限主体变化时，按[本地构建](#本地构建)流程生成并启动固定路径的应用。

## 主要功能

- 原生 macOS 13+ Avalonia 桌面界面，支持 Apple Silicon 与 Intel 构建。
- CoreGraphics 物理像素窄带捕获，保留 Retina 环境下的精确 RGB 协议。
- 按职业、专精、队伍类型和英雄天赋自动或手动选择模块。
- 编辑 Fuyutsui 职业配置、宏和模块，并同步到游戏 AddOns 目录。
- 支持共享单位目标路由：一个技能选择键配合 30 个复用目标键，减少治疗模块的宏槽位占用。
- 独立解码治疗吸收、真实生命值和治疗缺口，并在运行日志中提供去重诊断。
- 支持 `switch`、`click`、`hold` 触发模式及键盘、侧键、滚轮输入。
- 在 WoW 窗口任意位置使用鼠标中键切换爆发状态。
- 色块扫描异常时在屏幕中央持续提示，恢复后自动通知。
- 长时间运行日志使用虚拟化逐行显示，保留最近 2000 行及完整复制能力。
- 独立的 macOS 数据目录、窗口状态、诊断、签名、公证和 Sparkle 更新流程。

## Fuyutsui 同步

当前基线为 Fuyutsui 1.2.1.15。应用会迁移工作副本、生成配置并检查协议冲突；协议、模块和奶骑美德的详细变更见[架构与发行说明](Documentation/macOS/README.md)、[更新日志](CHANGELOG.md)及[美德实现文档](Documentation/holy-paladin-virtue-implementation.md)。

## 环境要求

- macOS 13 或更高版本。
- .NET 10 SDK。
- Xcode Command Line Tools。

运行捕获与按键功能时，需要在“系统设置 → 隐私与安全性”中向当前 Shigure 应用授予屏幕录制和辅助功能权限。

## 本地构建

先创建仅供本机开发使用的持久代码签名身份，再构建应用：

```bash
Packaging/macOS/ensure-local-signing-identity.sh
Packaging/macOS/build-app.sh artifacts/macos/Shigure.app
ditto artifacts/macos/Shigure.app /Applications/Shigure.app
open -n /Applications/Shigure.app
```

构建脚本默认生成当前 Mac 架构的 self-contained 应用。交叉构建时设置：

```bash
SHIGURE_RUNTIME_IDENTIFIER=osx-arm64 Packaging/macOS/build-app.sh artifacts/macos/Shigure-arm64.app
SHIGURE_RUNTIME_IDENTIFIER=osx-x64 Packaging/macOS/build-app.sh artifacts/macos/Shigure-x64.app
```

## 构建与测试

```bash
dotnet restore Shigure.slnx
dotnet build Shigure.slnx --configuration Release --no-restore
dotnet run --project Tests/Shigure.Core.ContractTests/Shigure.Core.ContractTests.csproj --configuration Release --no-build
dotnet build Apps/Shigure.MacUI/Shigure.MacUI.csproj --configuration Release --runtime osx-arm64
dotnet build Apps/Shigure.MacUI/Shigure.MacUI.csproj --configuration Release --runtime osx-x64
bash -n Packaging/macOS/*.sh
```

```bash
codesign --verify --deep --strict artifacts/macos/Shigure.app
codesign --display --requirements - artifacts/macos/Shigure.app
codesign --display --verbose=4 artifacts/macos/Shigure.app/Contents/MacOS/Shigure.MacApp
```

## 数据位置

```text
artifacts/macos/*.app
~/Library/Application Support/Shigure/logs/runtime-detailed.log
~/Library/Application Support/Shigure/logs/runtime-ui-errors.log
```

## 目录结构

```text
Apps/                         macOS 命令入口与 Avalonia UI
Core/                         共享业务核心和用户数据服务
Platforms/                    平台抽象与 macOS 原生实现
Presentation/                 UI 无关的会话与展示投影
App/ Infrastructure/         Core 编译使用的共享源文件
Input/ Modules/ Runtime/      Keymap、模块规则和运行时共享源文件
Fuyutsui/ FuyutsuiDiGuaBridge/ config/ keymap/ 插件权威源、DiGua 兼容桥及生成数据
Packaging/macOS/              构建、签名、公证和发布脚本
Tests/                        macOS 与共享核心契约测试
Tools/Shigure.MacDiagnostics/ 低副作用诊断入口
```

## 使用风险

本软件仅供技术研究、学习交流和个人实验。窗口读取、按键发送或自动化辅助可能违反目标游戏或服务的使用条款，并可能导致账号处罚。使用者应自行确认合规性并承担风险。本软件按 MIT License “原样”提供，不附带任何担保。
