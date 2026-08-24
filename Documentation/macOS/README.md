# macOS 架构与发行边界

Shigure for macOS 由四层组成：

1. `Core` 保存配置、模块、协议解码、运行循环和用户数据规则。
2. `Platforms/Shigure.Platform.Abstractions` 定义目标窗口、权限、捕获、输入和按键输出合同。
3. `Platforms/Shigure.Platform.Mac` 使用 CoreGraphics、ApplicationServices 和 macOS 进程/窗口 API 实现平台合同。
4. `Apps/Shigure.MacUI` 通过 `Apps/Shigure.MacApp` 与 `Presentation` 组合业务能力和 Avalonia UI。

`Fuyutsui/`、`config/`、`keymap/` 与 `wow_process.txt` 随版本作为只读基线进入应用包。运行时会在 `~/Library/Application Support/Shigure/runtime` 建立可升级工作副本，用户修改与版本基线分离。

## 捕获与权限

- 生产扫描使用 CoreGraphics 捕获目标顶部协议窄带，并保留物理像素 RGB。
- 屏幕录制用于读取目标窗口；辅助功能用于输入监听和定向按键发送。
- 权限请求只能由用户显式触发；状态检查不得弹出系统提示。
- 捕获与诊断默认不保存画面，显式导出只允许 PPM 诊断文件。

## 签名与发行

- `ensure-local-signing-identity.sh` 只创建本机开发身份，用于保持本地 TCC 主体稳定。
- 对外发布必须使用 Developer ID Application、Hardened Runtime、最小 entitlement、公证、staple 和 Gatekeeper 验证。
- Sparkle 私钥只能由钥匙串读取；仓库只保存版本、公开下载地址入口和公开密钥配置位置。
- `prepare-release.sh` 只准备本地产物，不创建 GitHub Release，也不上传文件。

当前公共仓库只发布源码，没有 Developer ID 签名、公证或 Apple 审核的二进制版本。
