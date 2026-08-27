# macOS 架构与发行边界

Shigure for macOS 由四层组成：

1. `Core` 保存配置、模块、协议解码、运行循环和用户数据规则。
2. `Platforms/Shigure.Platform.Abstractions` 定义目标窗口、权限、捕获、输入和按键输出合同。
3. `Platforms/Shigure.Platform.Mac` 使用 CoreGraphics、ApplicationServices 和 macOS 进程/窗口 API 实现平台合同。
4. `Apps/Shigure.MacUI` 通过 `Apps/Shigure.MacApp` 与 `Presentation` 组合业务能力和 Avalonia UI。

`Fuyutsui/`、`config/`、`keymap/` 与 `wow_process.txt` 随版本作为只读基线进入应用包。运行时会在 `~/Library/Application Support/Shigure/runtime` 建立可升级工作副本，用户修改与版本基线分离。

## Fuyutsui 协议升级

- 当前基线使用 Fuyutsui 1.2.1.11：`施法` 已拆分为 `施法(倒计时)` 与 `施法(正计时)`，施法时间以 `1 秒 = 10` 编码，并增加鼠标指向及首领 1-5 的单位状态。
- 工作副本中的旧 `施法` 字段会通过 Lua 结构化模型迁移为 `施法(倒计时)`，随后从 `Fuyutsui/class` 重新生成 `config`；模块中依赖旧时间尺度的阈值仍需人工复核。
- 用户职业配置和生成配置可以保留本地差异；若 `Fuyutsui/main.lua` 或协议核心 Lua 存在本地冲突，Mac UI 与命令行都会禁止插件同步和运行时启动，并报告具体路径，避免混用新旧协议。
- Mac 快捷控件固定忽略游戏 UI 缩放，位置按默认缩放基准迁移；鼠标中键通过全局 `GLOBAL_MOUSE_UP` 切换无限爆发，不能在局部按钮事件中重复触发。

## 捕获与权限

- 生产扫描使用 CoreGraphics 捕获目标顶部协议窄带，并保留物理像素 RGB。
- 屏幕录制用于读取目标窗口；辅助功能用于输入监听和定向按键发送。
- 触发键同时读取当前按下状态并消费 CoreGraphics 按下脉冲，避免短促键盘或侧键点击落在扫描间隔之间；键盘自动重复不会生成新脉冲。
- 权限请求只能由用户显式触发；状态检查不得弹出系统提示。
- 捕获与诊断默认不保存画面，显式导出只允许 PPM 诊断文件。

## 签名与发行

- `ensure-local-signing-identity.sh` 只创建本机开发身份，用于保持本地 TCC 主体稳定。
- 对外发布必须使用 Developer ID Application、Hardened Runtime、最小 entitlement、公证、staple 和 Gatekeeper 验证。
- Sparkle 私钥只能由钥匙串读取；仓库只保存版本、公开下载地址入口和公开密钥配置位置。
- `prepare-release.sh` 只准备本地产物，不创建 GitHub Release，也不上传文件。

当前公共仓库只发布源码，没有 Developer ID 签名、公证或 Apple 审核的二进制版本。
