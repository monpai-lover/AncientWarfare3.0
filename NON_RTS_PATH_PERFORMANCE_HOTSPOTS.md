# 非调度/寻路真实内容性能热点审查

审查范围：`AncientWarfare3.0/Code` 中与战争、外交、官场、法理、人口、经济、学派、世家、叛乱和地图内容有关的代码。已排除调度器本身、寻路算法和寻路诊断；但记录这些入口触发的真实内容工作。结论来自静态源码调用链，实际耗时应结合 `RecentFeatureBenchmark` 与 `RuntimePerformanceDiagnostic` 日志确认。

## 风险摘要

| 风险 | 主要位置 | 触发频率 | 主要成本 |
|---|---|---|---|
| 极高 | 法理存储读路径 | 任何法理查询/UI/地图读取 | 读操作重复世界维护、全局修复和迁移 |
| 高 | 层级法理地图与逐 zone Meta | 地图激活、刷新、hover、绘制 | 全部州/城市/zones 构建，临时集合和快照读取 |
| 高 | 运输、抽象战斗、战争结算 | 战时每帧或每 authority cycle | 航运成员、mission 分组、占领与 DB 写入 |
| 高 | 战争 AI、动员、补员 | 年度/authority cycle，积压时持续 | 候选国家/城市/actor 全量扫描与排序 |
| 中高 | 学派/世家 snapshot | 地图激活或 dirty 时 | 城市居民/actor 重建和动态颜色计算 |
| 中 | 官场空缺、地方官署、经济 | 年度批次、事件触发、UI 刷新 | 重复官员索引、城市/居民扫描、SQLite 往返 |
| 受控 | 学校旅行、讲学、客卿活动 | 每帧入口但每次有预算 | 队列通常单步推进，长期 backlog 仍会累积 |

## 1. 法理和层级地图

### 1.1 `EnsureInitialized` 读路径重复世界维护（极高）

- 位置：`Code/core/court/DeJureRegionStore.cs:707-746`。
- `EnsureInitialized()` 在 store 已存在且世界已加载时，仍可能执行空法理修复、首府修复、全法理名称同步、历史元数据迁移，并调用 `DeJureNewCityAssignmentService.RepairUnassignedCities()`。这些操作位于 `ActiveRegions()`、`TryGetForCity()`、`TryGetBySeat()` 等读路径之前。
- 影响：地图、法理战争目标、地方官署和统计面板只要读取法理，就可能重复触发 `O(K + R + C)` 级维护；大地图上容易形成 UI 长帧和重复分配。
- 建议：将维护拆到 world load、城市创建、首府变化、名称变化和显式法理变更事件；使用 world generation/store revision/dirty flag，读函数只返回快照。`RepairUnassignedCities` 应只在受控初始化队列运行。

### 1.2 全图法理源和州区域构建（高）

- 位置：`Code/core/policy/HierarchicalVassalMapModeService.cs:810-840`。
- 每次重建遍历全部 active regions，再按成员城市查找城市并收集所有 zones；国家视图还会按国家、城市和 zones 多层分组。
- 复杂度约为 `O(R * M + Z)`，其中 `M` 为州成员城市数，`Z` 为涉及 zones 数。法理 dirty、地图切换或强制刷新时会产生大量 `List`、`Dictionary`、`HashSet`。
- 建议：维护不可变的 `cityId -> regionId`、`cityId -> zones` 和 `region revision` 快照；只有法理/城市拓扑 revision 变化才重建。

### 1.3 hover/tooltip 重复收集整国 zones（中高）

- 位置：`HierarchicalVassalMapModeService.cs:1050-1110`。
- `TryGetDisplayedRealm()` 在鼠标 hover 路径收集代表国全部成员城市和 zones，再构造临时列表/集合。
- 每次 hover 可能是 `O(C_rep + Z_rep)`，连续移动鼠标时会持续产生 GC。
- 建议：缓存代表国可见 zone 集合和 hierarchy revision；hover 只做 tile 到 region 的 O(1) 查询，并将 tooltip 刷新节流到约 100-200ms。

### 1.4 标签几何重复计算（中高）

- 位置：`HierarchicalVassalMapLabelRuntime.cs:341-430`、`HierarchicalVassalMapModeGeometry.cs:38-180`。
- 增量标签运行时每帧推进 cache/discovery/source 提交；geometry 对 land tile 多次遍历并用 HashSet 去重，质心/可见质心又重复扫描。
- 建议：缓存 `zone geometry revision -> metrics`，合并为单次 pass；州名已经绑定首府时直接使用首府锚点，避免无意义的质心计算。

### 1.5 每帧地图模式服务及逐 zone Meta（高，地图激活时）

- 位置：`Code/patch/AW_DeferredRuntimeWorkPatch.cs:17-36`、`AWMapModeMetaLibrary.cs:299-309,414-438`。
- `MapBox.Update` 每帧驱动本地化、学派地图、世家地图；Meta 回调按每个 zone 查询 city snapshot/影响和颜色。学派/世家 getter 不应触发 DB、Demand 或重建。
- 建议：地图未激活时直接短路；使用 `cityId/zoneId -> compact meta/color` 缓存，按 snapshot generation 批量失效；避免在 getter 内进行排队或数据库查询。

## 2. 官场、法理官署和经济

### 2.1 地方官署重复读取官员全集（中）

- 位置：`Code/core/court/CityBureauAnnualWorkService.cs:147-182`。
- 每个城市处理时调用 `CourtService.GetActiveOfficers(pKingdom, int.MaxValue)`，再筛选 city layer；同一国家当年会重复读取并复制完整官员集合。
- 建议：年度开始按 `kingdom/city -> officer IDs` 建一次索引；任命、死亡、迁城时增量失效，切片直接消费索引。

### 2.2 空缺刷新和候选选择重复扫描（中高）

- 位置：`Code/core/court/CourtVacancyReconciliationService.cs:72-99,191-240`。
- 刷新先读全量官员，再遍历中央/地方职位和城市空缺；每个 vacancy 又重新取 registry snapshot 并做候选选择。
- 建议：维护 office occupancy counter 与 dirty vacancy set；候选池按 office/layer/qualification 缓存，避免每个职位复制同一 snapshot。

### 2.3 官场读模型/UI重复构建（中）

- 位置：`CourtReadModelService.cs:380-450,210-225,987-999` 及 CourtWindow 调用链。
- 打开或刷新窗口时可能重复构建中央官场、地方官署、官员卡片，并逐个通过 `getCities()` 查城市名。
- 建议：一次 refresh 构建共享 `CourtReadContext`（city/name/officer dictionaries），按 revision 缓存；滚动列表只物化可见页。

### 2.4 城市经济、奴隶人口和邻接扫描（中）

- 位置：`Code/core/policy/CityEconomyService.cs:63-130,617-687`。
- 每国每年遍历全部城市；每城计算技术、人口、奴隶人口和外国邻接。`CountSlavePopulation` 仍遍历 `pCity.units`，而项目已有 `SlavePopulationIndexService` 增量索引可复用。
- `OnRealmSupplyChanged`（约 `524-545`）还会立即写入零值贡献行，事件密集时会放大 SQLite 写入。
- 建议：使用人口/奴隶/ownership revision 增量计数；邻接关系缓存；把 supply refresh 合并到年度/dirty 批次。

### 2.5 技术地图首次全世界范围扫描（中）

- 位置：`Code/core/policy/TechMapModeService.cs:126-177`。
- `EnsureVisibleRange()` 首次绘制遍历全部国家和城市并计算技术分数；地图切换或 cache dirty 时重复。
- 建议：年度技术更新时维护全局 min/max 快照，地图直接读取快照，不在绘制路径重新计算。

## 3. 战争、军队、运输和结算

> 下列条目排除了 RTS 调度器/寻路实现本身，但保留它们调用的真实战争内容成本。

### 3.1 运输成员状态每帧/P0 更新（高）

- 位置：`Code/core/lineage/ArmyRtsTransportService.cs:275-285,307-320,356-473,548-637,657-765`。
- 普通模式每 Unity frame、Large 模式每 P0 处理 active voyages；刷新 roster census、路线、船阶段和每个成员的登船/卸船状态。船和成员都可能再次执行原版 AI/平滑移动。
- 复杂度为 `O(voyages + embarked members)`，路线未改变时仍可能重复解析。
- 建议：路线按拓扑/目标 revision 缓存；成员 census 使用 dirty bit；航行中降低轮询频率，只有船/阶段/成员状态变化才执行昂贵检查。

### 3.2 抽象战斗每帧重建 mission 分组（高）

- 位置：`Code/core/lineage/ArmyAbstractBattleService.cs:39-112`。
- 每次处理复制 missions，按 `(WarId, TargetCityId)` 构建字典、查重、排序 keys/groups，再构造双方 participant 和士气字典。
- 上限为每帧 4 场，但没有可靠的“输入 revision 未变即跳过”；战争 mission 数量大时仍有明显 GC。
- 建议：按目标维护增量 battle index，只处理 mission/city/war revision 变化的组，并复用临时容器。

### 3.3 RTS controller 战斗 roster/优先级重复遍历（高）

- 位置：`ArmyRtsControllerService.cs:1469-1515,2454-2477,4876-4905`。
- 每个控制阶段先处理补员/普通运输，再消费 controller；backfill 可能遍历全部 `_records.Keys` 重新入队。优先级刷新又复制/排序全部军队并枚举每个成员；进入野战战斗时再次遍历整支 roster。
- 建议：只在 mission/roster/actor priority revision 变化时 backfill 和刷新；维护 dirty controller/dirty actor 集合。

### 3.4 watchdog 和补员（中高）

- 位置：`ArmyStallWatchdogService.ProcessFrame`；`ArmyReplenishmentOperationService.cs:206-320`。
- watchdog 在 Large 模式强制采样，补员恢复阶段可遍历 `World.world.armies` 构建 snapshot，随后逐个 active operation 处理。
- 建议：常规轮询仅采样超时/进度变化军队；补员只由 casualty、war-end、arrival 事件入 dirty queue。

### 3.5 合成动员与临时征召全量扫描（高，战争/备战积压时）

- 位置：`SyntheticMobilizationLedgerService.cs:240-320,535+ ,1114-1125`；`TemporaryLevyService.cs:785-834,952+`。
- 动员 ledger 会处理记录和结束记录，actor batch 使用 `World.world.units.getSimpleList()`；war enrollment 会扫描 wars。临时征召流程多处遍历 kingdoms、城市和全 actor 列表。
- 备战状态卡住时这些阶段会反复进行候选过滤、列表分配，形成 FPS 和 GC 热点。
- 建议：war start/end 事件维护 participant index；按 kingdom/city actor cursor 分页，禁止常态从 world actor 全量重建。

### 3.6 战争结算与占领事件重复 DB/城市计算（高）

- 位置：`WarScoreRuntimeBridge.cs:429-517,693-760`；`WarGoalSettlementRuntimeService.cs:35-189`。
- 每次城市冻结占领可能写 DB、记录 goal、重算 total occupation；`CountLiveCities` 多次遍历 kingdom city roster。参与者 revaluation 也可重复入队。
- `ResolveFrozenCityObjectId` 在索引缺失时遍历 `World.world.cities`，是明确的全世界 fallback。
- 建议：按 war/city revision 合并占领事件；缓存 live-city count 和 frozen city ID index；正常路径禁止全世界 cities fallback，只允许受控读档修复。

### 3.7 terminal settlement 恢复扫描（中高）

- 位置：`WarTerminalSettlementCoordinator.cs:39-74`。
- 每约 4 个 authority cycle 调 `wars.checkLists()`，最多检查 8 场战争并入 deferred queue；战争多时仍持续扫描，后续 DB/规则回调会放大成本。
- 建议：war/capture/death 事件直接标记 dirty war；恢复扫描仅在 world load 或 pending 不一致时启用，并按 backlog 自适应间隔。

### 3.8 战争 AI 目标/邻接/附庸实力重复计算（高）

- 位置：`WarDecisionAI.cs:185-223,384-425,691-842`。
- 先枚举本国各城市邻国，必要时再枚举全部 kingdoms；候选目标重复构建 facts、排序和 trace。`AreNeighbors` 每次查询又逐城市遍历邻接；`GetWarPowerScore(includeVassals:true)` 可能递归多层附庸。
- 建议：维护 kingdom-neighbor bitset、首府距离和附庸实力 revision；候选只在年度/外交 dirty 时生成，复用异步快照。

### 3.9 战斗 episode 记录（中）

- 位置：`WarBattleEpisodeService.cs:34-50,66-78,137-165`，由 `BattleKeeperManager.update` 每帧触发过期结算。
- 单帧维护通常轻，但击杀事件会查 killer wars，并在 3x3 空间桶内逐 episode 计算距离；高击杀量会叠加 actor death 路径和记录写入。
- 建议：保持空间索引，合并同一 frame/war 的 episode 更新，DB 写入批处理。

## 4. 外交、难民和叛乱

### 4.1 外交提案/操作（中高）

- 位置：`DiplomacyProposalService.cs:1930-1978,2213-2270`；`DiplomaticOperationService.cs:256-310,626-663`。
- authority frame 即使无 due proposal 也会轮询 SQLite；年度维护对每国校准战争分数、赔款、结算恢复并读取 wars。外交操作候选按城市/邻国构建。
- 建议：SQL due index + 批量读取；无 due 时不查询；proposal/operation 失败采用指数退避；按 kingdom dirty 唤醒。

### 4.2 难民（中）

- 位置：`WarRefugeeService.cs:34-46,239-338,970-1090`。
- 已按月且有预算（最多 16 个受威胁城市、32 条 journey），整体受控；但目的地选择会遍历 owner 的全部城市，首次建立目的地 snapshot 会扫全 `World.world.cities`。
- 建议：缓存安全目的地/食物/容量 revision，批量加载 journey 成员；全世界目的地扫描仅在读档或索引失效时做。

### 4.3 起义/土匪（中高）

- `MassUprisingClusterService.cs:94-139,322-349`：年度触发且最多处理 4 个 rebel，但城市忠诚、文化和邻接仍需按城构建，多个国家同年触发时为 `O(sum cities)`。
- `PeasantRebelBanditRaidService.cs:83-105`：每 authority cycle 复制 `World.world.kingdoms.ToList()`，实际只推进一个 active raid，active raid 少时浪费。
- `PeasantRebelBanditAmnestyService.ProcessAuthorityCycle` 每轮读取少量 pending；可改为 pending queue 变更时唤醒。
- 建议：维护 active raid queue/round-robin ID；忠诚/文化 revision 脏标记；重建只在 world load 或事件触发。

## 5. 学派、世家和官员资格

### 5.1 地图 snapshot 重建（中高）

- 位置：`CitySchoolSnapshotService.cs:105-175`、`CityShiInfluenceSnapshotService.cs:137-160`。
- dirty 处理虽每帧最多 4 城，但一次重建会遍历城市居民/actor；整国 dirty 会形成持续 backlog。地图 getter 还可能需求 snapshot。
- 建议：出生、死亡、入城、离城、学校/世家变更时维护增量计数；整城 actor scan 只用于 load/recovery。

### 5.2 学校运行时入口（受控但需监控）

- 位置：`HistoricalSchoolRuntime.cs:77-167`。
- `ProcessFrame/ProcessVanillaFrame` 每帧入口，内部固定调用年度入队、scheduler、descent、guest、action、write、activity、travel。旅行/客卿/活动当前有界：旅行每帧最多一步，guest 有上限，activity 每帧处理一个过期/验证/讲学动作。
- 风险：`ProcessVanillaFrame` 在 `LoadState()` 失败时下一帧重试，可能反复 DB/索引加载；持续 deferred backlog 会使每帧固定诊断和队列操作累积。
- 建议：失败使用退避和一次性世界代重试；无 pending demand/active activity 时跳过子服务；保持现有预算。

### 5.3 学派名册与资格重建（中）

- 位置：`SchoolRosterReadModelService.cs:106-126,213-225`；`SchoolMembershipService.cs:1375-1410`。
- 名册构建会加载全部 membership/lecture seniority，actor ID miss 时遍历全 `World.world.units`；资格/索引加载也有全 actor pass。
- 建议：actor ID 建稳定字典/增量索引，名册按学校 revision 缓存，避免 UI miss fallback 全局枚举。

## 6. 高频小调用与分配模式

- `AW_ActorVisualRolePatch.cs:15` 的贴图/头部路径是每 actor 渲染调用；应只做 asset/role 缓存和无分配字符串比较。
- `AW_EnlistPatch.cs:266` 的 `Actor.getNextJob` 是每 actor AI 高频 hook；必须保持 O(1)，不得加入世界枚举、LINQ 或日志。
- `AWLocalizedNameRefreshService.cs:25-95` 每帧预算处理多个全局集合；请求合并和 world generation 去重，否则 refresh 请求会从头重复投影。
- `AWMapModeMetaLibrary.GetMeta` 的动态 key 清理/重建会产生临时 key 列表；应按 map revision 批量更新并复用对象。
- 运行时大量 `ToList/ToArray/new Dictionary/HashSet` 出现在 controller P0、运输 roster、抽象战斗、战争候选、结算和土匪 raid；这是高倍速下 GC 下降的共同来源。

## 7. 已确认的受控项与误报排除

- `HistoricalSchoolTravelService`：按 frame/actor budget 推进，季度城市索引扫描不是每 actor 每帧。
- `SchoolGuestOfficeService`：pending drain 有上限，年度 service sweep 有上限。
- `HistoricalSchoolActivityQueue`：每帧单步处理，过期 lease 有 retry 规则。
- 当前 `HistoricalSchoolStore.SaveAffiliation` 已写入 home kingdom 字段，不应把旧的继承归属持久化问题作为当前性能/正确性缺陷。

## 8. 建议整改顺序

1. 先移出 `DeJureRegionStore.EnsureInitialized` 的读路径维护，并建立 region/store revision。
2. 为全图法理源、hover、逐 zone Meta、学派/世家 snapshot 建立 revisioned immutable cache。
3. 合并占领/战争结算事件，缓存 live-city 和 frozen-city 索引，去除正常路径全世界 city fallback。
4. 限制运输、抽象战斗、RTS roster 优先级和 watchdog 的重复遍历，改用 dirty queue。
5. 重构战争 AI 邻接/附庸实力缓存，避免每候选递归。
6. 官场空缺、地方官署和经济改为共享城市/官员索引，复用 `SlavePopulationIndexService`。
7. 对学校失败加载、外交 due 查询、土匪 raid 和本地化刷新增加退避/事件唤醒。

