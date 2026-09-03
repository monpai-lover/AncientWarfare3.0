# 城市 / 州名双重命名

城市名与州名（de jure region name）是**两个独立持久化字段**。本文档说明如何通过公开命令门面修改它们，以及原版重命名与州名之间的兼容规则。

## 命令用法

```csharp
AW3CommandResult result = AW3MultiplayerCommandFacade.DispatchFromUi(
    AW3CommandRequest.RenameCityState(
        kingdomId, cityId, "新城市名", "新州名"));
```

- `Text` 承载城市名，`Payload` 承载州名。
- 命令走权威路由（`AW3AuthoritativeCommandRouter` → `AW3CourtCommandHandler`
  → `CityStateRenameService.TryApply`），多人游戏下由主机裁决。
- 城市不属于任何 de jure region 时，州名必须留空，否则返回
  `InvalidRegion`。

### 返回码

| `CityStateRenameResult` | 含义 |
|---|---|
| `Success` | 两个字段按需提交 |
| `CityNotFound` | 城市或王国已不存在 |
| `Unauthorized` | 城市不属于请求方王国 |
| `EmptyCityName` / `EmptyStateName` | 规范化后为空 |
| `InvalidRegion` | 无 region 却提供了州名 |
| `NoChange` | 两个字段都与当前值相同 |
| `CommitFailed` | 提交失败，已回滚 |

## 州名的稳定性规则

`DeJureRegion.RegionName` 是**权威持久化值**。读路径
（`DeJureRegionStore.ResolveDisplayName`）只返回该字段，从不从首府城市名
推导。

自动重命名**不会**更新州名。只有同时满足三个条件时，原版
`City.setName` 才会带动州名变化：

1. 该城市是所在 region 的 `SeatCityId`
2. `pTrack: true`（玩家发起的跟踪重命名，非生成器产生）
3. `region.SeatLocked` 为真

这三条由 `CityStateRenameRules.ShouldSyncStateName(isSeat, trackedRename,
seatLocked)` 统一判定。任何一条不满足，城市改名就只改城市名。

赋值、迁移、读档修复、首府修复路径均不再调用任何州名推导逻辑，因此
存档往返与容错修复都不会意外改写玩家设定的州名。

## 给外部 mod 作者

请通过 `AW3MultiplayerCommandFacade` 派发上述命令，**不要**直接写
`City.data.name` 或修改 `aw3_de_jure_regions.json`：

- 直接写 `City.data.name` 绕过跟踪标记，州名不会同步，且不产生历史记录。
- 直接改 JSON 不会递增 `StoreRevision`，聚合缓存与地图投影不会失效。

`CityStateRenameService.Changed` 事件（`(kingdomId, cityId)`）可用于在
重命名成功后刷新自定义 UI。
