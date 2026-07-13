# 验收版仕途生命周期收尾设计

## 目标

在进入 WorldBox 功能验收前，把已经实现的官场、学派客卿、将军、城主和人物传记之间的持久化链路收敛成可判定、可重试、不会制造半状态的版本。本文只关闭当前验收阻塞，不扩展新玩法，也不兼容未发布版本的旧存档。

## 本轮范围

- 客卿开始任职：`SchoolAffiliation`、`CourtOfficer`、客卿 `SchoolEvent` 必须在同一个 SQLite 事务内提交。
- 客卿结束任职：`SchoolAffiliation` 与 `CourtOfficer` 必须在同一个 SQLite 事务内关闭，提交后才清理 Actor 官场投影和客卿状态。
- 将军任免：`GeneralState` 与军职 `CourtOfficer` 必须拥有统一事务结果，提交后才变更 `GENERAL_ACTIVE`、特质、氏族和历史。
- 城主任免：原版 `City.setLeader/removeLeader` 保持权威；`CourtOfficer` 是派生投影，失败或不确定时进入有界重试，最终 exactly-once 收敛，不能回滚原版城主。
- 人物传记：以 durable `CourtOfficer` 为仕途真相，确认中央官、客卿、将军、城主均能显示任期、国家、城市和离任原因。
- 学派系统：静态复核历史先师降临、完整成员窗口和年度任务隔离；真实自然繁殖、长时间运行和 UI 观感留给下一阶段实机验收。

## 明确不在本轮

- AW2 尚未迁移的天命成本、天命国专属政策、天下大乱/改元事件、旁系宗系回归等后续玩法。
- 玩家议和窗口、更细复国条款、城市经济扩展等路线图功能。
- UI 美术、数值阈值、AI 频率和长时间平衡调参。
- 旧存档迁移与兼容。

## 统一持久化规则

每个跨表操作只有一个 transaction owner。写入必须检查精确 affected-row 数；异常后对该操作冻结的 original/desired tuple 做严格 readback，并且只能返回：

- `Committed`：所有参与表都精确为 desired。
- `CleanFailure`：所有参与表都精确保持 original，且目标幂等记录不存在。
- `Unknown`：查询失败、混合状态、多行冲突或任何无法证明的状态。

`Unknown` 绝不做反向补偿，也绝不修改 live Actor。重试必须复用稳定 operation key、ID、年份和世界时间，不能生成第二段任期或第二条事件。

## Live 投影顺序

统一顺序为：durable commit -> 采纳内存快照 -> Actor/trait/status 投影 -> 补充人物与城市历史 -> 缓存失效。补充历史失败不允许反向撤销已经提交的权威任职，但必须可从 durable 任期重建 UI。

客卿、将军结束时顺序相反的问题必须删除：先提交 durable 关闭，再清 live。城主是例外，因为原版先完成权威变更；补丁只负责观察结果并可靠地追上派生 career。

## 恢复与预算

- 客卿年度维护按已有上限扫描，只通过 actor/layer active career 和两个候选 operation key 点查，不扫描整张事件表。
- 将军恢复按国家和 actor 的已缓存/索引状态分批执行，不在年度热路径扫描世界人口。
- 城主投影重试按去重 actor/city 队列执行；每帧/每年有固定预算，成功或权威状态已变化时移除。
- 读取失败视为 Retry，不视为“记录不存在”；只有明确证明 clean absence 才允许结束或重新创建。

## 测试策略

纯规则测试先覆盖三态判定、operation key、恢复决策和重试去重。随后使用仓库自带 `System.Data.SQLite 1.0.99.0` 建立 `net48/x64` 内存数据库故障注入：成功、event trigger abort、唯一索引冲突、stale CAS、同请求 replay、无关行保留。每一阶段都运行现有 Historical、Spawn Harmony、pathfinding harness，并执行 Debug/Release 构建和 `git diff --check`。

最终静态门禁通过后同步到游戏目录，保留 `.runtime/`。实际放置 Xia 国家、自然繁殖十年以上、学派先师降临、UI 操作和长时间卡顿检查属于下一阶段功能验收。
