# 士大夫身份与虚拟县行政设计

## 状态与范围

这是 `master` 当前代码的实施设计草案。本文件只定义边界、数据契约、迁移和验证顺序，不包含运行时代码。它取代旧文档中“正式任官即授予 `guizu`”的规则；正式爵位、皇亲国戚和王族仍保持贵族身份。

本计划包含两个相互依赖但可分阶段交付的子系统：

1. 社会身份分流：普通平民正式任官后获得士大夫身份，正式贵族任官仍保持贵族身份。
2. City 内虚拟县：以 City 当前 `zones` 建立不改变原版 ownership 的县级 sidecar，并把县令接入地方官场和自定义官场。

不创建真实 `City`，不修改 `TileZone.city`，不改变原版人口、经济、战争、RTS 或寻路的实体边界。

## 现状审查

### 社会身份

- [Code/content/XiaTraits.cs](../../../Code/content/XiaTraits.cs) 通过 `NewSocialIdentity` 注册 `guizu`、`zhuhou` 等互斥社会身份；新 trait 应加入同一社会身份组。
- [Code/core/lineage/LineageKeys.cs](../../../Code/core/lineage/LineageKeys.cs) 目前有 `LINEAGE_STATUS`、`NOBLE_DISTANCE`、爵位和王族标记，但没有“官员身份”字段。
- [Code/core/lineage/LineageService.cs](../../../Code/core/lineage/LineageService.cs) 的 `OnActorPromoted`、`EnsureOfficialShiAndClan`、`RefreshNobleStatus` 及其直接 `addTrait/removeTrait(TRAIT_GUIZU)` 调用混合了谱系、爵位和任官。
- [Code/core/court/CourtService.cs](../../../Code/core/court/CourtService.cs) 在正式任命提交后调用 `CourtOfficerRecordRules.ShouldGrantNobleIdentity` 和 `LineageService.EnsureOfficialShiAndClan`。
- [Code/core/lineage/WesternLineageAdmissionService.cs](../../../Code/core/lineage/WesternLineageAdmissionService.cs) 把任官 admission 的 `pOfficial` 与 `pNoble` 同时传递，需要分离；[WesternLineageMigrationService.cs](../../../Code/core/lineage/WesternLineageMigrationService.cs) 也把旧 official 视为 noble。
- [Code/core/lineage/NobleIdentityService.cs](../../../Code/core/lineage/NobleIdentityService.cs) 和 `NobleIdentityRules.cs` 是正式贵族判定的中心，应成为“是否可以移除 `guizu`”的唯一依据。

### 地方官场

- [Code/core/court/CourtIds.cs](../../../Code/core/court/CourtIds.cs) 的 `CourtOfficeLayer` 目前有 `central/city/military/censor/feudatory/regional`，没有 county。
- [Code/core/db/CourtOfficerTableItem.cs](../../../Code/core/db/CourtOfficerTableItem.cs) 以 `city_id + layer + office_id` 记录任职，没有县作用域。
- [Code/core/court/LocalCourtAppointmentService.cs](../../../Code/core/court/LocalCourtAppointmentService.cs) 负责 City 官署补缺，且第一个 City 席位绑定 City leader；[CityBureauAnnualWorkService.cs](../../../Code/core/court/CityBureauAnnualWorkService.cs) 提供分片、合并和立即补缺队列。
- [Code/core/court/LocalLowOfficeVacancyRules.cs](../../../Code/core/court/LocalLowOfficeVacancyRules.cs) 只允许 City 最低级官职使用无资格兜底；需要支持县令并保持八/九品入口。
- [Code/core/court/LocalCourtReadModel.cs](../../../Code/core/court/LocalCourtReadModel.cs)、[CourtReadModelService.cs](../../../Code/core/court/CourtReadModelService.cs)、[CourtCityGovernmentCard.cs](../../../Code/ui/components/CourtCityGovernmentCard.cs) 和 `CourtWindow` 已有地方官署/固定州级主管的展示路径，可参数化加入县列表。

### 自定义官场

- [Code/core/court/CustomCourtTemplateModels.cs](../../../Code/core/court/CustomCourtTemplateModels.cs) 当前 schema 为 3，已有 `RegionalGovernmentLayer`、`LocalTemplates` 和 `IsFixedRole` 所需的数据基础。
- [Code/core/court/CustomCourtTemplateRules.cs](../../../Code/core/court/CustomCourtTemplateRules.cs) 严格区分中央 office 与 City local office；县级固定角色必须通过 schema 迁移和受保护关系验证，不能让旧模板失效。
- [Code/core/court/CustomCourtRuntime.cs](../../../Code/core/court/CustomCourtRuntime.cs) 和 [Code/ui/windows/CustomCourtWorkflowWindow.cs](../../../Code/ui/windows/CustomCourtWorkflowWindow.cs) 已实现州牧固定动态卡片，应抽象为通用行政层，而不是复制一套逻辑。

### 地图与生命周期

- [Code/core/policy/CityAdministrationMapModeRules.cs](../../../Code/core/policy/CityAdministrationMapModeRules.cs) 的状态机只有 `Regions/Cities` 与国家、州面包屑。
- [Code/core/policy/HierarchicalVassalMapModeService.cs](../../../Code/core/policy/HierarchicalVassalMapModeService.cs) 负责 zone 元数据、层级点击、标签和增量缓存；[HierarchicalVassalMapLabelRuntime.cs](../../../Code/core/policy/HierarchicalVassalMapLabelRuntime.cs) 已提供 dirty/分片处理。
- [Code/patch/AW_ChroniclePatch.cs](../../../Code/patch/AW_ChroniclePatch.cs) 已能观察 `City.addZone`；[Code/patch/AW_SavePatch.cs](../../../Code/patch/AW_SavePatch.cs) 和 [Code/core/multiplayer/AW3WorldLoadCoordinator.cs](../../../Code/core/multiplayer/AW3WorldLoadCoordinator.cs) 是 sidecar 保存、读档和新世界清理的生命周期入口。

## 关键决策

### 士大夫与贵族分流

新增 `LineageKeys.TRAIT_SHIDAFU`（建议值 `shidafu`）和独立的社会身份服务/规则。`XiaTraits.Init` 用 `NewSocialIdentity` 注册它，使其与 `guizu` 互斥。

- **普通正式官员**：任命事务成功提交且 actor 不是正式贵族时，设置社会身份 `scholar_official`，添加士大夫 trait，移除 `guizu`（仅当 `NobleIdentityService.IsNobleActor` 为 false）。保留谱系、氏族、官职历史和仕途等级。
- **正式贵族官员**：king、heir、皇亲/王族标记、正式爵位或有效 `NOBLE_DISTANCE/NOBLE_RANK` 仍由贵族服务管理并保留 `guizu`；不因任官降为士大夫。
- **acting 任命**：沿用现有规则，不在任命尚未提交时授予任何永久社会身份；正式转正时再分流。
- **离职/免职**：不自动撤销士大夫或贵族身份。任期结束只关闭 career record。
- **旧存档迁移**：一次性处理活跃正式官员。无 king/heir/皇亲/正式爵位且只有“任官导致的贵族”证据者，转为士大夫；正式贵族的 `guizu` 不动。迁移必须幂等且不写 `LINEAGE_STATUS=NOBLE`。
- **所有写入口收口**：`LineageService`、Western admission/migration、爵位授予、王族恢复、姓氏/封爵流程统一调用 `SocialIdentityService`，禁止新增散落的 `addTrait("guizu")`。

图标由 imagegen 另行生成透明背景的小尺寸 trait 图，建议输出到 `GameResources/ui/Icons/traits/iconshidafu.png`，代码路径为 `ui/Icons/traits/iconshidafu`；同时补 `Locales/*` 中的 trait 名称和说明。图标生成不属于本设计文档的代码实现阶段。

### 虚拟县数据模型

新增 `Code/core/county/`，至少包含以下职责清晰的单元：

- `CountyModels.cs`：`CountyRecord`（稳定 ID、City ID、Region/seat ID、ordinal、name、manualName、zone IDs、leader actor ID、created/last repaired year、active、schema）。
- `CountyAdministrationStore.cs`：内存索引、原子 JSON sidecar、按 City/zone/county 的查询。
- `CountyZonePartitionRules.cs`：纯函数规则；`zoneCount <= 25` 返回一个县，`zoneCount > 25` 返回 `ceil(zoneCount / 25)` 个县，每个县最多 25 个 zone。
- `CountyZonePartitionService.cs`：初次连通分区、增量新增 zone、失效 zone 修复；不得调用 `City.addZone`。
- `CountyNameService.cs`：按 City/州的历史县名词库选择不重复名称并追加“县”；手动改名后不再覆盖，词库耗尽时使用稳定的 `CityName + ordinal + 县` 回退。

分区语义：首次建立以 zone 邻接关系做连通 flood-fill，尽量保持县连续；新增 zone 只加入相邻且未满的现有县，只有无合法容器时才新建县。普通 zone 增长不重排已有县。读档修复只移除不再属于该 City 或重复的 zone，再增量补入，不改变原版 ownership。

### 保存与读档

sidecar 文件建议为 `aw3_counties.json`，复用 `DeJureRegionStore` 的发布/观察模式：

1. `AW_SavePatch.SaveWorldToDirectory_Postfix` 在法理/谱系 sidecar 后发布县快照；保存前确保 pending county partition work 已完成或显式失败。
2. `AW_SavePatch` 的 load hooks 观察目录；`AW3WorldLoadCoordinator` 在世界加载完成后调用 `CountyAdministrationStore.RepairAfterWorldLoaded()`。
3. 新世界清理县 store；世界 generation 变化使旧异步任务失效。
4. JSON schema version 递增，未知字段忽略，写入采用临时文件 + 原子替换；坏档只丢弃无效县记录并由当前 City zones 重建。

县 ID 采用稳定的 `cityId + ordinal` 或显式生成 ID；ordinal 只在创建时分配，不因 zone 数量变化重排。City 销毁、合并、国家转移和法理撤销只更新县的 City/region 引用或标记 inactive，不创建/删除真实 City。

### 层级地图

最终交互路径为：`全国/国家 -> 州 -> City -> 县`。县层只能从一个已聚焦 City 进入；不在全图同时展开所有县。

- 扩展 `CityAdministrationMapLevel` 为 `Regions/Cities/Counties`，state 保存 kingdom、region、city、county breadcrumbs。
- `HierarchicalVassalMapModeService.GetMetaForZone` 在 county 层按县记录给 zone 着色；国家/州/City 层保持原有结果。
- `HandleZoneClick` 遵循现有 unmapped 行为：空地不清空标签；跨 City 点击返回上层或忽略，不跳到错误县。
- 县名标签放在县 zone 的稳定代表点；使用现有 label runtime/dirty queue，不在每帧计算质心。
- 县颜色从原版 `AssetManager.kingdom_colors_library` 派生，以稳定 `(kingdom color_id, cityId, county ordinal)` 偏移取色；缓存 ColorAsset，禁止每帧 `tryMakeNewColorAsset`。
- `AWMapModeMetaLibrary`、相关 mapmode/nameplate/minimap patch 只增加 county metadata 分支，不改变真实 City 全量着色和原版地图 ownership。

### 县令与地方官场

新增 `CourtOfficeLayer.County = "county"` 和 `CourtOfficeId.CountyMagistrate = "county_magistrate"`。若现有 SQL/自定义模板难以安全扩展 layer，则允许第一阶段暂用 `City` layer + `county_id` 作用域，但最终读模型和查询必须能区分县；默认推荐新增正式 County layer。

- 县令 grade 使用现有最低地方官 grade 30，入职品级限制为八/九品（复用 `NineRankRules`/`OfficialCareerRankRules`）。
- 任期复用 `LocalOfficialTermRules` 的 10-15 年默认值；续任不创建重复历史记录。
- 候选人复用 `OfficerCandidateCatalog`、`LocalOfficialCandidateRules`、科举资格和乡党加分；不对每个县重新扫描全世界。
- `LocalCourtAppointmentService.ReconcileCity` 在县 dirty 时只处理受影响 City；`CityBureauAnnualWorkService` 负责分片、合并和重试。县生成、zone 变化、县令死亡/任期结束入队立即补缺。
- 县令的管理边固定指向本 City 的郡守/都督（即现有 City chief/第一直属管理官）；当该 City 是州首府，仍遵循“州牧 = 首府 City leader = 首府郡守”的既有强绑定。
- `CourtOfficerTableItem` 增加 `county_id`，旧行默认 `-1`；所有 active/history 查询、唯一性约束、office history 和 career state 读写都携带该字段。
- `LocalCourtReadModel` 增加 counties/counties' leader/nodes/edges；`CourtReadModelService` 构造县令节点和固定管理边。`CourtCityGovernmentCard` 展示县列表，县令人物入口复用既有人物窗口调用链。

### 自定义官场固定动态卡片

不复制州牧实现，抽象通用的 `CustomCourtAdministrativeLayer`：

- 模板保存县层标题、县令标题和布局；县主管卡片 `IsFixedRole=true`，只能改中英文显示名，不能删除、换层或改变管理关系。
- 县动态卡片的管理边固定连到 `RegionalGovernmentLayer.ManagementOfficeIds[0]`（州牧直属的第一个管理官职）；若模板无有效首席管理官，codec 生成稳定的默认 City chief 边。
- `CustomCourtTemplateJsonCodec` 升 schema 并给旧模板补默认 county layer；旧模板的 central/local offices、历史和布局不得因新增层而校验失败。
- `CustomCourtTemplateRules` 增加受保护 office/edge 验证，拒绝删除固定县级角色或把其连接到非首个管理官。
- `CustomCourtRuntime`、`CustomCourtWorkflowWindow`、`CourtWindow` 和 `CourtCityGovernmentCard` 使用同一动态层模型；用户编辑只作用于相应行政层，不串改中央官场。

## 分阶段实施顺序

### Phase 0：纯规则和契约

新增 `SocialIdentityRules`、`CountyZonePartitionRules`、`CountyCourtRules`、`CountyColorRules` 和规则测试。先固定 trait 分流的正式贵族判定、25-zone 分区、县令 8/9 品入口、稳定 ID/颜色和固定管理边，避免运行时代码同时改变多个边界。

### Phase 1：士大夫 trait 分流

扩展 `LineageKeys`、`XiaTraits`、`TraitIconUsageRules` 和 localization；新增 `SocialIdentityService`。改造 `LineageService`、`CourtService`、`AW_PromotionPatch`、Western admission/migration 和所有爵位/王族恢复入口。加入一次性旧档迁移和幂等测试，确认普通官员不再写入 `LINEAGE_STATUS=NOBLE`，正式贵族不被误降级。

### Phase 2：县 store、分区和生命周期

创建 `Code/core/county/*`，接入 `AW_ChroniclePatch` 的 `City.addZone` dirty 标记、`AW_SavePatch` sidecar 和 `AW3WorldLoadCoordinator` repair。先支持新世界和读档重建，再接入增量新增 zone；任何分区失败都保留 City 可用并记录结构化错误。

### Phase 3：县级地图下钻

扩展 `CityAdministrationMapModeRules`、`HierarchicalVassalMapModeService`、label runtime/discovery、`AWMapModeMetaLibrary` 及 mapmode patches。按国家->州->City->县逐级验证点击、返回、空地和标签缓存，再开启县颜色缓存和性能预算。

### Phase 4：县令和官职历史

扩展 `CourtIds`、`CourtOfficerTableItem`、SQL schema/索引、`OfficialCareerStateService`、`CourtService`、`LocalCourtAppointmentService`、`LocalLowOfficeVacancyRules` 和 `CityBureauAnnualWorkService`。补齐县令候选、入职、任期、死亡替换、乡党/科举来源、八/九品、管理边和历史查询。

### Phase 5：读模型、UI 和自定义官场

扩展 `LocalCourtReadModel`、`CourtReadModelService`、`CourtCityGovernmentCard`、`CourtWindow`、`CourtActorNodeView`、`CourtOfficeHistoryWindow`。然后升级 custom template model/codec/rules/runtime/editor，生成固定县主管卡片并锁定关系。补齐中英文 CSV localization；不生成新的独立 UI 体系。

### Phase 6：迁移、性能和发布门禁

对旧存档执行 trait/county/court schema migration；验证 City 销毁、zone 变化、占领、法理撤销、读档和新世界。运行全部 focused rules/source-guard、Release build 和性能 smoke test 后，才合并到 `master`。

## 兼容性与性能约束

- 绝不拆真实 City 或改 `TileZone.city`，确保原版人口、经济、战争和 RTS 仍只看到真实 City。
- 县分区是 sidecar，所有 zone ID 都需在读档和异步完成回调中再次验证 generation、City ID 和 zone ownership。
- 县只在 City 聚焦时绘制；全图只保留 City/州标签。候选人使用共享 `OfficerCandidateCatalog`，县补官走 coalesced dirty queue，禁止每县/每帧全世界扫描。
- store、label cache 和 ColorAsset 使用稳定 key；世界重置清空所有 runtime cache，避免旧县泄漏到新世界。
- 新 layer/column/schema 对旧存档提供默认值；未知或坏记录降级为 vacancy/重建，而不是阻塞读档。
- 关键指标：县分区每次 dirty 的 zone budget、地图帧不执行全量候选扫描、保存前 sidecar flush 有界、县颜色/标签缓存命中率可诊断。

## 测试与验收

### 规则测试

- 普通正式官员得到士大夫且没有 `guizu`；acting 不授予；有爵位、王族/皇亲、king/heir 保留 `guizu`；任免/迁移幂等。
- 25 zones 只有一个县；26 zones 产生两个县；每县最多 25 zones；县分区连续；新增 zone 不重排旧县；名称不重复且手动名不被覆盖。
- 县令 grade=30、入职 rank 为 8/9 品；候选目录优先合格人，缺位只入队一次；任期续任不重复历史；管理边固定且不可被模板删除。
- 旧 template/旧 save 可 round-trip；缺失县 sidecar 可从 City zones 重建；坏 zone ID 不影响 City。

### 源码守卫与运行时验收

新增 `Tests/CountyRules.Isolated.Tests` 或同等规则测试项目，以及县 store、map、court、trait 的 source guards。运行：

```powershell
dotnet test Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
dotnet test Tests/LocalGovernmentRules.Isolated.Tests/LocalGovernmentRules.Isolated.Tests.csproj
dotnet test Tests/CivilServiceFocused.Tests/CivilServiceFocused.Tests.csproj
dotnet build AncientWarfare3.csproj -c Release
```

WorldBox smoke test 必须覆盖：新建 City、zone 超过 25、县令死亡/任期结束、国家/州/City/县地图下钻、空地点击和返回、保存读档、City 被销毁/占领、普通官员/贵族官员 trait、旧 custom template 导入。日志不得出现县 store、trait 分流、office history 或 map label 的未处理异常。

## 风险与回滚

1. `guizu` 既被家谱、婚姻、继承和学校读取，必须只在正式贵族判定为 false 时移除；若迁移发现证据不足，保留 `guizu` 并记录待修复，而不是误删血统。
2. `CourtOfficerTableItem` 增加 `county_id` 可能影响 SQL 索引和旧查询；先以默认 `-1` 兼容，再逐个切换调用方并加入 schema guard。
3. 地图状态机插入县层可能复发空地点击/白色标签问题；复用现有 breadcrumb/label dirty 规则，并把每个层级点击作为隔离测试。
4. 县级自定义模板若破坏旧 schema，应拒绝导入并保留上一份有效模板；绝不在运行时自动删除用户的中央官场定义。
5. 如果性能预算超标，可先关闭全图县标签，仅保留 City 聚焦后的县着色和标签；县数据与官场历史仍保留，不影响存档兼容。
