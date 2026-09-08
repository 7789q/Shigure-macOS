# macOS 打包说明

本文说明 Shigure macOS 本地 `.app` 打包、验收和正式发行的边界。构建产物位于 `artifacts/macos/`，不提交到 Git。

## 1. 日常入口

从能够访问登录钥匙串的 macOS 系统 Terminal 执行这一条命令即可：

```bash
Packaging/macOS/repackage-local.sh
```

脚本会自动选择当天未占用的 `artifacts/macos/Shigure-YYYYMMDD-local-N.app` 路径，预检工具链和钥匙串，根据工作区改动选择验证级别，生成包后执行签名校验并从 Launch Services 启动验收。

需要明确跳过完整验证时使用：

```bash
Packaging/macOS/repackage-local.sh --fast
```

`--fast` 只适合确认打包链路本身；它不代表源码、配置或 Lua 已通过测试。默认模式发现相关改动时自动走完整验证。契约测试失败时仍会生成本地诊断包，但脚本返回非零状态并显示失败原因；restore、build、shell 或 Lua 检查失败时会在打包前停止。

不要从受限沙箱终端执行。脚本会在任何 `dotnet restore` 之前检查登录钥匙串访问，环境不正确时立即退出，不再无输出等待。

统一入口对 restore、Release build 和 publish 分别设置 60 秒、180 秒和 300 秒上限；超过上限会停止当前命令并报告环境问题。

## 2. 先判断流程

| 场景 | 必须执行 |
| --- | --- |
| 仅重新打包，且上次验证后 `C#`、`Core`、`Runtime`、`config`、`keymap`、`Fuyutsui` 和 `Packaging/macOS` 未变化 | 本地签名检查、打包、签名校验、Launch Services 启动验证 |
| 上述任一源码、配置、Lua 或打包脚本发生变化 | 先执行完整验证，再执行本地打包和启动验证 |
| 对外发行或生成 release staging | 工作区干净、Developer ID 签名、公证、staple、Gatekeeper 验证和 release staging |

“仅重新打包”只表示重新生成应用包，不代表源码变更已经通过测试。契约测试失败时脚本仍会在明确记录失败项后生成本地诊断包，但不能报告为全量验证通过；其他完整验证步骤失败时不会进入打包阶段。

## 3. 环境与签名身份

需要 macOS 13+、`.NET 10 SDK`、Xcode Command Line Tools，以及能够读取当前用户登录钥匙串的系统权限终端。先检查本机开发签名身份：

```bash
Packaging/macOS/ensure-local-signing-identity.sh
```

该脚本在身份不存在时创建 `Shigure Local Code Signing`，并把 SHA-1 指纹固定到 `~/Library/Application Support/Shigure/local-signing-identity.sha1`；已有身份时只复用并校验，不应静默更换证书。`build-app.sh` 也会自动执行 `ensure-local-signing-identity.sh --find`，因此单独的检查是可重复的前置确认，不会创建第二个身份。

不要设置 `SHIGURE_CODESIGN_IDENTITY` 传入证书指纹。当前本地权限主体依赖固定的证书根、Bundle ID 和 `Shigure` 子程序标识符，错误的签名分支可能导致已有屏幕录制或辅助功能权限失效。

## 4. 底层打包脚本（调试入口）

日常不需要直接调用这个脚本；只有在需要绕过统一入口、单独调试 Bundle 生成时才使用 `build-app.sh`。它不会自动执行完整验证，也不会启动生成的应用。

输出路径必须是新的 `.app`，`build-app.sh` 拒绝覆盖已有产物。建议使用当天日期和递增序号，例如：

```bash
Packaging/macOS/build-app.sh artifacts/macos/Shigure-20260907-local-9.app
```

未设置 `SHIGURE_RUNTIME_IDENTIFIER` 时，脚本按当前主机架构构建；Apple Silicon 为 `osx-arm64`，Intel 为 `osx-x64`。需要明确指定时：

```bash
SHIGURE_RUNTIME_IDENTIFIER=osx-arm64 Packaging/macOS/build-app.sh artifacts/macos/Shigure-arm64.app
```

打包脚本会执行以下工作：

1. 发布 self-contained .NET 应用。
2. 编译 `ShigureCapture.swift` 原生库。
3. 放置 Fuyutsui、配置、keymap 和随包模块。
4. 生成应用图标和 `Info.plist`。
5. 对运行时原生库、子程序和应用 Bundle 逐层签名。
6. 校验版本、签名 requirement、嵌套签名和 Bundle 资源。

脚本内部使用 `dotnet publish`，会再次执行 publish 所需的还原和编译；因此即使前面已经执行过 Release build，完整验证后的打包仍会有一轮发布构建。这是当前流程的预期耗时来源，不是重复执行错误。

## 5. 打包后验收

使用生成的实际路径执行签名校验，并从 Launch Services 启动应用：

```bash
codesign --verify --deep --strict artifacts/macos/Shigure-20260907-local-9.app
open -n artifacts/macos/Shigure-20260907-local-9.app
```

启动后确认 `Shigure.MacUI` 进程保持运行；不能只用命令行权限探针代替 GUI 启动验证。若需要保持 TCC 权限主体和路径稳定，应在用户确认后将已验收包安装为固定路径 `/Applications/Shigure.app`，再从该固定路径启动。

同时检查：

```bash
codesign --display --requirements - artifacts/macos/Shigure-20260907-local-9.app
codesign --display --verbose=4 artifacts/macos/Shigure-20260907-local-9.app/Contents/MacOS/Shigure.MacApp
plutil -p artifacts/macos/Shigure-20260907-local-9.app/Contents/Info.plist
```

重点确认 `CFBundleShortVersionString`、`CFBundleVersion`、`CFBundleIdentifier`、arm64/x64 架构，以及本地签名的 designated requirement。未配置完整 Sparkle 归档、feed URL 和公钥时，构建会提示应用内更新不可用；这不影响本地包生成，但不能作为正式发行包。

## 6. 完整验证（调试参考）

以下命令是拆分排查时的参考，不是日常打包入口：

当源码、配置、Lua 或打包脚本发生变化时，统一入口的 `--full` 模式按以下顺序执行：

```bash
dotnet restore Shigure.slnx
dotnet build Shigure.slnx --configuration Release --no-restore
dotnet run --project Tests/Shigure.Core.ContractTests/Shigure.Core.ContractTests.csproj --configuration Release --no-build
bash -n Packaging/macOS/*.sh
```

修改 Lua 文件时，对每个改动文件执行：

```bash
luajit -b <改动文件.lua> /dev/null
```

修改 `Packaging/macOS/*.sh` 时，统一入口会自动执行以下检查；直接调用底层 `build-app.sh` 时仍需手动执行：

```bash
bash -n Packaging/macOS/*.sh
```

测试失败不应被隐藏。报告中分别记录构建结果、测试通过/失败数量、失败测试名称和是否仍生成了本地包。

## 7. 为什么本地打包可能较慢

- `dotnet restore` 需要访问依赖缓存或 NuGet；受限沙箱可能长时间无输出后失败，应该改用能够访问项目依赖和登录钥匙串的系统权限终端重试。
- self-contained `dotnet publish` 会把 .NET runtime 和原生库复制进应用，不能等同于只编译一个 DLL。
- Swift 原生库、图标和包内资源需要在每次新 Bundle 中重新生成或复制。
- macOS 代码签名按嵌套文件、动态库、子程序和外层 Bundle 分层执行。
- 完整验证与实际 publish 都包含构建步骤；前者提供验证证据，后者生成可运行的 self-contained 包。

## 8. 正式发行

正式发行不使用本机开发证书。必须在干净工作区使用 Developer ID Application、Hardened Runtime 和最小 entitlement，依次执行公证、staple、Gatekeeper 验证以及：

```bash
Packaging/macOS/notarize-app.sh <已签名的 .app>
Packaging/macOS/prepare-release.sh <已签名且已公证的 .app> <发行输出目录>
```

正式发行还需要有效的 Sparkle 锁定归档、HTTPS appcast URL 和 Ed25519 公钥。`prepare-release.sh` 只准备本地产物，不负责上传或创建 GitHub Release。
