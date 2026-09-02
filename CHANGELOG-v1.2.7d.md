# 古代战争 3.0 v1.2.7d

v1.2.7c 之后的维护与接口版本。本版加入供其他历史 AI 模组读取的公开历史 API，
并修复继承、官职历史、外交历史事件发布以及年度调度尖峰问题。

## 历史 API

- 新增 `AncientWarfare3.api.history` 公开只读 API，覆盖人物传记、族谱、编年史、
  外交历史和官职任职历史。
- 新增统一历史事件流、游标分页和 `RuntimeDatabaseEpoch` 世界隔离，防止读档或切换
  世界后继续使用旧游标和旧缓存。
- 新增提交成功后的历史事件订阅。外交对话、提案与回应、婚姻、联盟、秘密行动、
  和约、和平结算以及官员任职/离任都会在事务提交成功后通知订阅者，不发布失败事务
  的假事件。
- 修复不同历史 ledger 使用相同数字 ID 时被错误去重的问题，并修复统一历史流在没有
  actor、kingdom 或 city 过滤条件时返回空页的问题。
- API 的 DLL 引用方式、查询示例、分页、订阅、epoch 处理和线程约束见：
  `docs/api/history-api-usage.md`。

## 继承与官职

- 修复女性继承法未开启时仍可能注册或保留女性继承人的问题。继承人刷新、存档读取、
  小地图读取和手动登记现在统一检查继承性别资格。
- 女性继承法变更后立即刷新继承人，避免旧继承人继续留在顺位池。
- 继承人不能被错误纳入官员候选或任职流程。
- 官员任职、离任、县令等地方官的记录继续写入个人传记与官职历史，并接入统一历史
  事件流，外部 AI 可以按人物、官职、地区和任职状态查询。

## 稳定性与性能

- 拆分年度国家治理阶段，降低单帧官府、官员和贡赋处理的峰值，同时保持原有处理顺序。
- 历史读查询使用独立只读连接；回调在游戏主线程排队执行，单个订阅者异常不会阻断
  其他订阅者。
- 运行时历史查询不读取外交 modifier 或活动缓存作为历史事件，避免把当前状态误报
  为历史记录。

## API 快速示例

```csharp
using AncientWarfare3.api.history;

long epoch = AW3HistoryApi.RuntimeDatabaseEpoch;
var biography = AW3BiographyApi.GetEntries(actorId,
    AW3HistoryQuery.ForActor(actorId));
var family = AW3GenealogyApi.GetAncestors(actorId, 8);
var career = AW3OfficialCareerApi.GetHistory(actorId,
    AW3HistoryQuery.ForActor(actorId));
var events = AW3ChronicleApi.GetKingdomEvents(kingdomId,
    AW3HistoryQuery.ForKingdom(kingdomId));

var subscription = AW3HistoryApi.Subscribe(
    AW3HistorySubscription.ForKingdom(kingdomId),
    item => ConsumeHistoryEvent(item));

// 世界切换后应丢弃缓存和 cursor，并重新读取。
if (epoch != AW3HistoryApi.RuntimeDatabaseEpoch)
    subscription.Dispose();
```

完整接口说明和约束以 `docs/api/history-api-usage.md` 为准。
