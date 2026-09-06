# Shigure macOS 协作规则

## 执行原则

- 范围明确的任务默认自主完成读取、修改、验证和报告；不要把本文件当作逐条确认清单。
- 只有目标、范围、权限、外部目标、不可逆结果或多个合理解释会改变方案时才澄清；低风险不确定性记录假设后继续。
- 本地读取、诊断、编辑和验证不重复确认。证书、依赖、外部写入、提交、推送、合并、发布、部署和删除必须分别获得明确授权。
- 遇到环境或权限失败，先做安全的只读验证或可逆替代；无法确认时报告影响，不把环境失败当作代码失败，也不停止可独立完成的工作。
- 只完成用户请求的生命周期阶段；最终分别报告已分析、已修改、已验证和未执行阶段。

## 项目边界

- 本仓库只交付原生 macOS；正式 UI 在 `Apps/Shigure.MacUI/`，业务入口在 `Apps/Shigure.MacApp/`。
- 原生实现只进入 `Platforms/Shigure.Platform.Mac/`，平台合同进入 `Platforms/Shigure.Platform.Abstractions/`。
- `Fuyutsui/` 是插件权威源；`config/`、`keymap/` 不得复制生成逻辑。上游变化按文件移植，不合并完整 Windows 树。
- 使用入口为 `README.md`，macOS 架构说明为 `Documentation/macOS/README.md`，上游基线只记录在 `upstream.json`；共享源清单以 `Core/Shigure.Core.csproj` 为准。
- 不提交构建产物、日志、权限数据库、屏幕导出、本地模块、签名材料或凭据。

## 验证与打包

- 验证按风险选择最小充分集合：普通改动做定向回归；契约、跨层或配置改动追加 `Tests/Shigure.Core.ContractTests/` 契约/生成一致性检查；Lua 改动执行语法检查；打包改动检查 `Packaging/macOS/` 脚本和签名。
- 全量恢复、全量契约、双架构构建、公证、Gatekeeper、Sparkle 和 `prepare-release.sh` 仅在用户明确要求交付前验证、正式发行、双架构发行或 release staging 时执行。
- 普通本地打包默认是本机架构单架构包，入口为 `Packaging/macOS/build-app.sh`，不覆盖既有 `.app`，不静默创建证书、不使用 ad-hoc 签名；签名身份检查失败不得继续打包。
- 本地包必须核对固定签名身份、标识符、designated requirement、嵌套签名和 `Info.plist`；缺少完整 Sparkle 配置时必须报告应用内更新不可用。
- 正式 release staging 要求工作区干净；脏工作区只能做只读检查并报告阻塞点。

## 领域门禁

- 修改目标死亡、血 DK 门控、死神印记或 Fuyutsui 同步时，覆盖目标类型、生命值、死亡状态和目标身份契约。
- 修改吸奶盾、美德、DiGua 桥接、AOE 阶段或相关同步/打包逻辑前，先读 `Documentation/holy-paladin-virtue-implementation.md`，并执行生产 Lua 回放与契约测试。
- DiGua 行为以 `../../World of Warcraft/_retail_/Interface/AddOns/DiGuaTimelineAudioHelper/` 为事实源；桥接必须保留单位条件、触发来源、`11.7`/`23` 秒时序、单位级取消和多单位并行语义，不做跨单位去重。
- 日志只用于本机诊断；报告仅记录脱敏路径、时间、版本和关键证据，不复制或提交运行时数据。
