# Shigure for macOS

Shigure 的原生 macOS 版本。应用读取 Fuyutsui 在目标游戏窗口绘制的像素状态，按职业 keymap 和模块规则选择按键，并通过 Avalonia 界面展示状态、队伍、逻辑结果和运行日志。

本仓库只发布 macOS 应用及其必需的共享核心，不包含 Windows WinForms/Win32 项目。Windows 原版与上游源码位于 [waynebian01/Shigure](https://github.com/waynebian01/Shigure)。本项目基于其 MIT 许可源码继续开发，基线记录在 [`upstream.json`](upstream.json)，许可证与原作者声明见 [LICENSE](LICENSE)。

当前版本的用户可见变化见 [更新日志](CHANGELOG.md)。

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

## Fuyutsui 1.2.1.15 同步内容

- 同步新版像素协议：施法状态拆分为正计时与倒计时，时间编码调整为 `1 秒 = 10`，并增加鼠标指向、首领 1-5 等单位状态。
- 自动迁移工作副本中的旧 `施法` 字段并重新生成职业配置；协议核心文件存在本地冲突时会阻止同步和运行，避免新旧协议混用。
- 补齐模块依赖的法术合并、规则备注和新增单位选择器，并在 Mac 模块编辑器中提供对应字段。
- 圣骑士治疗宏使用安全的技能选择键与共享目标键序列；路由模式随模块依赖保存并参与冲突检查。
- 治疗吸收使用独立像素条传输，不覆盖真实生命值；Retina 缩放下按物理像素对齐解码。
- 键盘与鼠标侧键同时使用实时按下状态和短按脉冲，减少点击落在扫描间隔之间而漏触发的情况。
- 保留 Mac 专属行为：快捷控件固定忽略 WoW UI 缩放，全局鼠标中键继续切换无限爆发。
- 奶骑增加 DiGua 桥接握手、玩家动作确认和真实 GCD 剩余字段；桥接由既有 Fuyutsui 本体加载，运行时只在握手成功后发键，并以 WoW 施法事件确认动作。
- 奶骑模块事实来源为 `BundledModules/holy-paladin-virtue-12.1.json`（当前 `1.2.1.22`、41 条规则）。玩家低于 30% 时先圣盾术，再进入圣疗和治疗链；非玩家队友低于 40% 时，在玩家高于 95% 或圣盾术剩余超过 6 秒的条件下优先施放牺牲祝福。治疗石优先于浓缩银月城生命药水，且圣盾术可用或已激活时两者都不消耗。正义盾击只对 5 码内的当前敌对目标释放，不依赖副本内受限的正面 API。真实群伤要求至少 3 人低于 85% 且总生命缺口至少 50，并保持 2 秒；无美德黎明之光使用严格 H95 边界。美德窗口内技能按当前优先级逐级判断，不再依赖单独的“群疗爆发保持”状态。圣洁鸣钟要求至少 3 人治疗缺口达到 15% 且当前总缺口大于 50，光环掌握要求至少 3 人达到 30% 且总缺口至少 120。轻伤阈值为 90%，灌注和神圣震击不可用时由 3 圣能或神圣意志荣耀圣令兜底；重伤和治疗吸收分支分别选择对应的最低血量/最高治疗缺口目标。脱战灌注转换使用圣光闪现，AOE 日志同时记录“鸣钟预计可用”状态。
- 吸奶盾美德采用 DiGua 实时姓名板信号，按 `11.7 秒倒计时 + 2 秒后置延迟` 进入执行窗口；实现约束与回归验收见[美德道标与吸奶盾现役实现](Documentation/holy-paladin-virtue-implementation.md)。

升级后应确认 Shigure 日志显示“游戏插件已同步”，并按应用提示执行一次 `/reload`。桥接已经内置于 Fuyutsui，不依赖 WoW 在启动时发现新的插件目录；如果启动 Shigure 时 WoW 尚未运行，启动运行时前会再次同步。旧模块中依赖原施法时间尺度的阈值仍需人工复核。

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

首次运行签名身份脚本后，证书指纹会固定在 `~/Library/Application Support/Shigure/local-signing-identity.sha1`。后续打包若发现证书丢失、替换或存在多个同名身份会直接停止，且正式打包入口拒绝 ad-hoc 签名，避免静默更换 TCC 主体。日常使用应始终替换并启动 `/Applications/Shigure.app`，不要从不同名称的临时候选包运行。

本地 TCC 主体由签名证书根和 designated requirement 共同决定：外层应用与 `Shigure.MacUI` 使用 `com.arasaka.shigure.mac`，运行时子进程 `Shigure.MacApp` 必须保持 `Identifier=Shigure`。`build-app.sh` 会为三者固定证书根、拒绝 `cdhash` 绑定并在打包末尾验证嵌套签名；不要把运行时标识改成 Bundle ID，也不要用临时或 ad-hoc 签名替代固定身份。

本地签名身份不是 Apple Developer ID，不能替代正式分发签名和公证。`Packaging/macOS/` 中的发行脚本不会保存 Apple 密码、私钥或公证凭据；这些信息必须由钥匙串和显式环境变量提供。

## 构建与测试

```bash
dotnet restore Shigure.slnx
dotnet build Shigure.slnx --configuration Release --no-restore
dotnet run --project Tests/Shigure.Core.ContractTests/Shigure.Core.ContractTests.csproj --configuration Release --no-build
dotnet build Apps/Shigure.MacUI/Shigure.MacUI.csproj --configuration Release --runtime osx-arm64
dotnet build Apps/Shigure.MacUI/Shigure.MacUI.csproj --configuration Release --runtime osx-x64
bash -n Packaging/macOS/*.sh
```

生成应用后，应确认签名主体和嵌套运行时仍符合上述合同：

```bash
codesign --verify --deep --strict artifacts/macos/Shigure.app
codesign --display --requirements - artifacts/macos/Shigure.app
codesign --display --verbose=4 artifacts/macos/Shigure.app/Contents/MacOS/Shigure.MacApp
```

## 数据位置

版本内置的 `Fuyutsui/FuyutsuiDiGuaBridge/config/keymap/wow_process.txt` 只作为只读基线。首次运行会将工作副本初始化到：

```text
~/Library/Application Support/Shigure/runtime
```

模块、日志和界面状态位于同一 `Application Support/Shigure` 数据根。启动时 APP 从包内 `BundledModules/` 补装模块；已知官方旧哈希会先备份再升级，未知同 ID/同名模块保留。仓库中的 `module/`、`cache/`、日志、屏幕导出、签名材料和构建产物均被忽略，不应提交。

## 目录结构

```text
Apps/                         macOS 命令入口与 Avalonia UI
Core/                         共享业务核心和用户数据服务
Platforms/                    平台抽象与 macOS 原生实现
Presentation/                 UI 无关的会话与展示投影
App/ Infrastructure/         Core 编译使用的共享源文件
Input/ Modules/ Runtime/      Keymap、模块规则和运行时共享源文件
Fuyutsui/ FuyutsuiDiGuaBridge/ config/ keymap/  插件权威源、DiGua 兼容桥及生成数据
Packaging/macOS/              构建、签名、公证和发布脚本
Tests/                        macOS 与共享核心契约测试
Tools/Shigure.MacDiagnostics/ 低副作用诊断入口
```

## 使用风险

本软件仅供技术研究、学习交流和个人实验。窗口读取、按键发送或自动化辅助可能违反目标游戏或服务的使用条款，并可能导致账号处罚。使用者应自行确认合规性并承担风险。本软件按 MIT License “原样”提供，不附带任何担保。
