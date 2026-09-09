# 文档索引与维护边界

文档只保留能帮助使用、维护或验证项目的内容。修改代码或模块后，先更新对应的现役文档，再在根 `CHANGELOG.md` 记录用户可见变化。

## 当前事实

| 文档 | 用途 | 权威范围 |
| --- | --- | --- |
| [`../README.md`](../README.md) | 安装、构建、测试和用户可见能力 | 项目入口 |
| [`macOS/README.md`](macOS/README.md) | macOS 分层、权限、插件协议、打包和发行边界 | 平台与交付 |
| [`holy-paladin-healing-optimization-design-2026-09-06.md`](holy-paladin-healing-optimization-design-2026-09-06.md) | 奶骑治疗压力分类和优先级设计 | 治疗策略设计 |
| [`holy-paladin-virtue-implementation.md`](holy-paladin-virtue-implementation.md) | AOE、吸奶盾、美德和 DiGua/Fuyutsui 状态机 | 运行时实现契约 |
| [`action-event-protocol-follow-up.md`](action-event-protocol-follow-up.md) | 动作事件协议 v6 的部署、最小验证、20 分钟实战验收和后续根治路径 | 动作确认维护与验收 |
| [`../BundledModules/holy-paladin-virtue-12.1.json`](../BundledModules/holy-paladin-virtue-12.1.json) | 奶骑可执行规则 | 机器可读规则 |
| [`../BundledModules/blood-deathbringer-12.1.json`](../BundledModules/blood-deathbringer-12.1.json) | 血DK可执行规则 | 机器可读规则 |

## 维护入口

- [`blood-deathbringer-12.1-implementation-plan.md`](blood-deathbringer-12.1-implementation-plan.md)：血DK维护、边界和验收记录。
- [`action-event-protocol-follow-up.md`](action-event-protocol-follow-up.md)：动作码错配修复后的部署、日志核对、实战验收和 Intent ID 后续工作。

## 维护规则

1. 规则值以 `BundledModules/` 为准，插件协议以 `Fuyutsui/` 为准，`config/` 和 `keymap/` 是生成结果。
2. 运行时行为以代码和契约测试为准；文档不能把本地回放或历史日志写成 WoW 实战已验证。
3. 发生版本变化时，同时更新模块文档状态、`CHANGELOG.md` 和本索引中对应的当前事实。
4. 已完成的过程记录不另建文档；需要保留的历史只进入 `CHANGELOG.md` 或现役契约中的简短背景。
5. 状态协议改动必须同时验证生产者、生成配置、解码消费者和打包资源；启动成功不等于运行态健康，必须确认协议版本匹配且状态心跳持续变化。
