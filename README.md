# Shigure for macOS

Shigure 的原生 macOS 版本。应用读取 Fuyutsui 在目标游戏窗口绘制的像素状态，按职业 keymap 和模块规则选择按键，并通过 Avalonia 界面展示状态、队伍、逻辑结果和运行日志。

本仓库只发布 macOS 应用及其必需的共享核心，不包含 Windows WinForms/Win32 项目。Windows 原版与上游源码位于 [waynebian01/Shigure](https://github.com/waynebian01/Shigure)。本项目基于其 MIT 许可源码继续开发，基线记录在 [`upstream.json`](upstream.json)，许可证与原作者声明见 [LICENSE](LICENSE)。

## 主要功能

- 原生 macOS 13+ Avalonia 桌面界面，支持 Apple Silicon 与 Intel 构建。
- CoreGraphics 物理像素窄带捕获，保留 Retina 环境下的精确 RGB 协议。
- 按职业、专精、队伍类型和英雄天赋自动或手动选择模块。
- 编辑 Fuyutsui 职业配置、宏和模块，并同步到游戏 AddOns 目录。
- 支持 `switch`、`click`、`hold` 触发模式及键盘、侧键、滚轮输入。
- 在 WoW 窗口任意位置使用鼠标中键切换爆发状态。
- 独立的 macOS 数据目录、窗口状态、诊断、签名、公证和 Sparkle 更新流程。

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
open artifacts/macos/Shigure.app
```

构建脚本默认生成当前 Mac 架构的 self-contained 应用。交叉构建时设置：

```bash
SHIGURE_RUNTIME_IDENTIFIER=osx-arm64 Packaging/macOS/build-app.sh artifacts/macos/Shigure-arm64.app
SHIGURE_RUNTIME_IDENTIFIER=osx-x64 Packaging/macOS/build-app.sh artifacts/macos/Shigure-x64.app
```

本地签名身份不是 Apple Developer ID，不能替代正式分发签名和公证。`Packaging/macOS/` 中的发行脚本不会保存 Apple 密码、私钥或公证凭据；这些信息必须由钥匙串和显式环境变量提供。

## 构建与测试

```bash
dotnet restore Shigure.slnx
dotnet build Shigure.slnx --configuration Release --no-restore
dotnet run --project Tests/Shigure.Core.ContractTests/Shigure.Core.ContractTests.csproj --configuration Release --no-build
dotnet build Apps/Shigure.MacUI/Shigure.MacUI.csproj --configuration Release --runtime osx-arm64
dotnet build Apps/Shigure.MacUI/Shigure.MacUI.csproj --configuration Release --runtime osx-x64
```

## 数据位置

版本内置的 `Fuyutsui/config/keymap/wow_process.txt` 只作为只读基线。首次运行会将工作副本初始化到：

```text
~/Library/Application Support/Shigure/runtime
```

模块、日志和界面状态位于同一 `Application Support/Shigure` 数据根。仓库中的 `module/`、`cache/`、日志、屏幕导出、签名材料和构建产物均被忽略，不应提交。

## 目录结构

```text
Apps/                         macOS 命令入口与 Avalonia UI
Core/                         共享业务核心和用户数据服务
Platforms/                    平台抽象与 macOS 原生实现
Presentation/                 UI 无关的会话与展示投影
App/ Infrastructure/         Core 编译使用的共享源文件
Input/ Modules/ Runtime/      Keymap、模块规则和运行时共享源文件
Fuyutsui/ config/ keymap/     插件权威源及生成数据
Packaging/macOS/              构建、签名、公证和发布脚本
Tests/                        macOS 与共享核心契约测试
Tools/Shigure.MacDiagnostics/ 低副作用诊断入口
```

## 使用风险

本软件仅供技术研究、学习交流和个人实验。窗口读取、按键发送或自动化辅助可能违反目标游戏或服务的使用条款，并可能导致账号处罚。使用者应自行确认合规性并承担风险。本软件按 MIT License “原样”提供，不附带任何担保。
