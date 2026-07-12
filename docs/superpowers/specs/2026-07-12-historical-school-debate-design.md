# Historical School Debate and Influence Design

**Status:** Proposed for review  
**Scope:** Historical-school debate, influence transmission, and the first institution hook  
**Existing base:** Historical master descent, real-member discipleship, cross-state residence, `SchoolDebate`/`CitySchoolLedger` tables, and `CitySchoolSnapshotService`

## Goal

让历史先师在同一城市相遇时进行有历史语境的辩论。辩论结果必须留下可查询的事件，并改变城市的学派传统、当前存在感和短期势头；国家朝廷方向继续通过已有城市/官场快照读取这些变化。学派影响力来自真实人物、弟子、游说、辩论和机构，不再随机给城市塞入学派。

## Rules

1. 每个世界年、每座城市最多结算一场辩论；只选择在场、存活、未旅行/服役、属于不同学派的合格教师或历史先师。
2. 配对按城市、学派影响力和稳定 actor id 排序，保证无随机数、可复现，并限制年度总预算，避免大地图性能尖峰。
3. 议题优先选择双方共同支持的 `HistoricalDebateTopicId`；没有共同议题时，从城市当前问题与双方议题的交集选择；仍无交集则跳过，不强行制造不合理辩论。
4. 辩论分数由议题专长、`HistoricalSchoolAbilityProfile`、成员声望、直接弟子数、城市问题匹配和近期势头组成。结果只产生 Draw/Narrow/Decisive 五种已有结算类型。
5. 胜者获得城市 `active_presence` 和 `momentum`，败者保留少量 `tradition`；平局双方获得小幅传统。所有值有上下界和年度衰减，防止单场滚雪球。
6. 辩论写入 `SchoolDebate` 和 `SchoolEvent`，并写双方人物传记/城市历史；失败写入不会改变账本，账本更新与辩论记录必须在同一 SQLite 事务中完成。
7. 辩论结束后标记 `DebateStatusId`，不能重复结算同一 actor/city/year；死亡、离城或旅行中的人物不能成为辩论对手。

## Influence Flow

```text
真实先师/教师
      |
      +-- lecture/recruitment --+--> CitySchoolLedger
      +-- debate result --------+
      +-- preserved work/institution
                                      |
                                      v
                         CitySchoolSnapshotService
                                      |
                                      v
                          CourtDirectionService / UI
```

`CitySchoolSnapshotService` 继续只把国王、世子、城主、官员和将领作为直接官场贡献者；辩论与游说通过账本影响这些贡献的基础分，不伪造一个城市官员或直接改变国籍。

## Institution Hook

本阶段只实现最小的“创设/维护入口”：历史先师在同一城市完成足够讲学或辩论后，可一次性创建其定义中的 `InstitutionId`；机构记录城市、学派、创始人、等级和状态，账本获得有限 institutions 加成。机构的完整 UI、升级树和破坏事件另列后续子项目，避免把辩论结算和 UI 绑定。

## Failure and Persistence

- 所有候选均需真实 actor；不创建合成弟子或合成辩手。
- 无 DB、无城市、无合法议题、无第二学派或事务失败时跳过并记录 warning，不改变内存账本。
- 事件、辩论和账本使用显式 source/event id，重载后由数据库恢复；运行时缓存只在持久化成功后更新。
- 任何单城/年度预算都必须在扫描前过滤，避免全量排序后再截断。

## Verification Targets

- 规则测试覆盖议题选择、配对稳定性、比分/结果边界、账本增减和年度预算。
- 源码断言确认运行时入口、真实 actor 限制、事务写入、状态效果和历史记录均存在。
- `dotnet build` Debug/Release、历史学派规则测试、路径规则测试和 `git diff --check` 全部通过。
- 实机烟测确认同城两学派先师会辩论，城市学派条目变化，朝廷方向/地图 tooltip 随账本刷新，跨国先师国籍不变。

