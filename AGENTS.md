# Shigure macOS 协作规则

## 验证与打包

- 专门的 macOS 打包流程、快速路径与完整验证边界见 `Documentation/macOS/packaging.md`；执行打包前先按该文档判断适用流程。
- C#、Core、Runtime、config 或跨层改动：执行 `dotnet restore Shigure.slnx`、`dotnet build Shigure.slnx --configuration Release --no-restore` 和 `dotnet run --project Tests/Shigure.Core.ContractTests/Shigure.Core.ContractTests.csproj --configuration Release --no-build`。
- Lua 改动：对每个改动文件执行 `luajit -b <文件> /dev/null`。
- 修改 `Packaging/macOS/*.sh`：执行 `bash -n Packaging/macOS/*.sh`。
- 本地打包必须在能够读取登录钥匙串的系统权限终端中按 `Documentation/macOS/packaging.md` 执行；日常优先使用 `Packaging/macOS/repackage-local.sh`，使用新的 `.app` 输出路径、自动发现 `Shigure Local Code Signing`，不要设置 `SHIGURE_CODESIGN_IDENTITY` 直接传入证书指纹。生成后执行 `codesign --verify --deep --strict <.app 路径>`，并从 Launch Services 启动主程序确认运行时可加载。
- 正式发布：工作区必须干净；使用 `Packaging/macOS/notarize-app.sh` 公证，使用 `Packaging/macOS/prepare-release.sh` 准备发行产物。
- 修改 Fuyutsui 状态协议、事件刷新、目标识别或生成配置时，依次检查 `Fuyutsui/`、`config/`/`keymap/`、`Runtime/`、契约测试和包内资源；只有协议版本、状态心跳、目标类型、战斗时间和宏绑定状态全部有效时才标记“逻辑已开启”，否则保持 fail-closed 并记录具体等待原因。

## 事实来源与交付

- 当前源码、运行包和待部署状态只在 `Documentation/macOS/README.md` 汇总；奶骑共享协议见 `Documentation/holy-paladin-virtue-implementation.md`，团本差异见 `Documentation/holy-paladin-raid-virtue-implementation.md`。实际加载必须核对规则内容、App 和插件资源，不能仅看模块编辑器的版本标签。
- 涉及玩家自身与团队槽位时复用 `UnitSelector.ResolvePlayerSlot`；团队的 1 号成员不固定等于玩家本人，自动治疗仍遵守现有目标槽位容量。

## 本地诊断

- 日志只用于本机诊断；报告仅记录脱敏路径、时间、版本和关键证据，不复制或提交运行时数据。
- 运行时详细日志固定路径为 `~/Library/Application Support/Shigure/logs/runtime-detailed.log*`，需要诊断时直接使用该路径。
