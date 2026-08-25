# 士大夫身份与虚拟县行政实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将普通任官者与正式贵族的社会身份分流，并为每个 City 增加不改变原版 ownership 的 25-zone 虚拟县、县令官场、地图下钻和固定自定义官场层。

**Architecture:** 士大夫使用独立 social-identity trait；正式贵族判定继续由 NobleIdentityService 负责。县使用 `aw3_counties.json` sidecar，记录稳定县 ID、zone ID、名称和县令；不创建真实 City、不修改 TileZone.city，地图、官场读模型和模板仅通过 county ID 引用。

**Tech Stack:** C#、Harmony、WorldBox/Unity API、Newtonsoft.Json、SQLite、AncientWarfare3.Rules.Tests、PowerShell source guards。

---

## 文件边界

- 身份：`Code/content/XiaTraits.cs`、`Code/core/lineage/{LineageKeys,LineageService,NobleIdentityService,NobleIdentityRules,WesternLineageAdmissionService,WesternLineageMigrationService}.cs`、`Code/core/court/{CourtService,CourtOfficerRecordRules}.cs`、`Code/patch/AW_PromotionPatch.cs`、`Locales/trait.csv`。
- 县核心（新增）：`Code/core/county/{CountyModels,CountyZonePartitionRules,CountyAdministrationStore,CountyZonePartitionService,CountyNameService}.cs`。
- 县官场：`Code/core/court/CountyCourtRules.cs`、`CourtIds.cs`、`CourtOfficerTableItem.cs`、`OfficialCareerStateService.cs`、`LocalCourtAppointmentService.cs`、`LocalLowOfficeVacancyRules.cs`、`CityBureauAnnualWorkService.cs`及其 SQL/读模型/UI。
- 持久化：`Code/patch/AW_SavePatch.cs`、`Code/patch/AW_ChroniclePatch.cs`、`Code/core/multiplayer/AW3WorldLoadCoordinator.cs`。
- 地图：`Code/core/policy/CityAdministrationMapModeRules.cs`、`HierarchicalVassalMapModeService.cs`、label runtime/job、`AWMapModeMetaLibrary.cs`和对应 patch。
- 自定义官场：`CustomCourtTemplateModels.cs`、JSON codec/rules/runtime、`CustomCourtWorkflowWindow.cs`、`CourtWindow.cs`、`Locales/aw3_court.csv`。
- 测试：`Tests/AncientWarfare3.Rules.Tests/*.cs.txt`、`Program.cs.txt`、`AncientWarfare3.Rules.Tests.csproj`、source guards。

## Task 1: 测试契约（TDD 起点）

**Files:** Create `SocialIdentityRulesTests.cs.txt`, `CountyZonePartitionRulesTests.cs.txt`, `CountyCourtRulesTests.cs.txt`; modify test project and `Program.cs.txt`.

- [ ] 写失败测试：正式爵位/王族/皇亲/king/heir 为 Noble；普通正式官员为 ScholarOfficial；acting 不授予长期身份；25 zone=1 县、26 zone=2 县、每县不超过 25；县令 grade=30 且首任品级只可 8/9。
- [ ] 运行聚焦测试确认失败：
```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --social-identity --county-rules
```
- [ ] 在 `AncientWarfare3.Rules.Tests.csproj` 加入 `SocialIdentityRules.cs`、`CountyZonePartitionRules.cs`、`CountyCourtRules.cs` 的 Compile Include，并在 `Program.cs.txt` 注册三个测试入口；提交测试契约：
```powershell
git add Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define scholar official and county contracts"
```

## Task 2: 士大夫特质与贵族分流

**Files:** 新增 `SocialIdentityRules.cs`、`SocialIdentityService.cs`；修改 `XiaTraits.cs`、`LineageKeys.cs`、`LineageService.cs`、`NobleIdentity*.cs`、`CourtService.cs`、`CourtOfficerRecordRules.cs`、`AW_PromotionPatch.cs`、`Locales/trait.csv`；生成 `GameResources/ui/Icons/traits/iconshidafu.png`。

- [ ] 先让测试断言 `isRuler/isHeir/hasFormalTitle/hasRoyalKinship/isFormalAppointment` 的分流结果，运行 `--social-identity` 确认失败。
- [ ] 增加 `LineageKeys.TRAIT_SHIDAFU="shidafu"` 与 `SOCIAL_STATUS="aw_social_status"`；`SocialIdentityRules` 只将正式爵位、王族、皇亲、king/heir 判为正式贵族。
- [ ] 在 `XiaTraits.Init` 注册 `NewSocialIdentity("shidafu", "ui/Icons/traits/iconshidafu")`，与 `guizu` 互斥但不改 guizu 数值。
- [ ] 统一任官入口调用 `SocialIdentityService.ApplyOfficialIdentity(actor)`：正式贵族保留 `guizu`，普通官员移除 `guizu`、添加 `shidafu`、写入 scholar_official；不得把普通官员写成 `LINEAGE_STATUS=NOBLE`。
- [ ] 将 `RefreshNobleStatus` 和 NobleIdentity 查询收口到正式贵族证据；离职只结束 career projection。
- [ ] 通过 imagegen 生成透明 trait 图标，补 `trait_shidafu`/info CSV，并添加资源存在性 source guard。
- [ ] 运行：
```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --social-identity
dotnet build AncientWarfare3.csproj -c Release
```
- [ ] 提交：`git commit -m "feat: split scholar official and noble identities"`。

## Task 3: 旧存档身份迁移

**Files:** 新增 `Code/core/lineage/SocialIdentityMigrationService.cs` 和迁移测试；修改 `AW3WorldLoadCoordinator.cs`、测试项目。

- [ ] 测试普通活跃官员迁移为士大夫，正式爵位/王族不变，死亡 actor 不写 trait，重复迁移幂等。
- [ ] 运行 `dotnet run ... -- --social-identity-migration` 确认失败。
- [ ] 以 world generation + migrationVersion 作 guard，仅从 active officer/career 索引读取，不全图扫描；不改谱系 archive/官职历史。
- [ ] 在世界加载完成回调中迁移，再执行 `EnsureOfficialShiAndClan`；新世界跳过。
- [ ] 运行完整规则测试并提交：`git commit -m "fix: migrate ordinary officers to scholar official identity"`。

## Task 4: county sidecar 与 25-zone 分区

**Files:** 新增 `Code/core/county/CountyModels.cs`、`CountyZonePartitionRules.cs`、`CountyAdministrationStore.cs`、`CountyZonePartitionService.cs`、`CountyNameService.cs`及测试。

- [ ] 测试 0/1/25/26/50/51 zone，断言 `max(1, ceil(count/25))`、每县 <=25、增量 zone 加入相邻未满县且不重排旧县。
- [ ] 运行 `--county-partition` 确认失败。
- [ ] `CountyRecord` 固定字段：countyId、cityId、regionId、ordinal、name、manualName、zoneIds、leaderActorId、createdYear、lastRepairedYear、active、schemaVersion；ID 稳定为 cityId+ordinal。
- [ ] 初次按 `TileZone.neighbours` flood-fill；新增 zone 只加入相邻未满县，不能调用 `City.addZone` 或写 `TileZone.city`。
- [ ] Store 提供 `GetByCity`、`TryGetByZone`、`MarkCityDirty`、`FlushSnapshot`、`LoadSnapshot`、`ClearForNewWorld`，异步结果按 generation 丢弃。
- [ ] 县名从 `name_generators/lib/Xia真实城名.txt` 选历史名并追加“县”；manualName 永不覆盖，词库耗尽使用稳定 CityName+ordinal+县。
- [ ] 运行 `--county-partition`，提交 `git commit -m "feat: add persistent virtual county partition core"`。

## Task 5: 保存、读档和 dirty 生命周期

**Files:** 修改 `AW_SavePatch.cs`、`AW3WorldLoadCoordinator.cs`、`AW_ChroniclePatch.cs`；新增 persistence tests/source guard。

- [ ] 测试 JSON round-trip 包含 schema/generation/countyId/zoneIds；新世界清空；`City.addZone` postfix 只标记对应 City。
- [ ] 运行 `--county-persistence` 确认失败。
- [ ] 在 `AW_SavePatch` 接入 `FlushSnapshot(directory)`、load observer、临时文件原子替换；坏记录降级重建而不阻塞读档。
- [ ] 在 `AW3WorldLoadCoordinator` 加 `RepairAfterWorldLoaded()`，验证 generation、City ID、zone ownership 后入队修复。
- [ ] 在 `AW_ChroniclePatch.City.addZone` postfix 调 `MarkCityDirty(city.id)`，复用分片预算，不同步扫 zone/候选人。
- [ ] 运行 persistence、全规则和 Release build，提交 `git commit -m "feat: persist and repair virtual counties"`。

## Task 6: 县令官场、品级与 SQLite 历史

**Files:** 新增 `Code/core/court/CountyCourtRules.cs`；修改 `CourtIds.cs`、`CourtOfficerTableItem.cs`、SQL schema/query、`OfficialCareerStateService.cs`、`LocalCourtAppointmentService.cs`、`LocalLowOfficeVacancyRules.cs`、`CityBureauAnnualWorkService.cs`及测试。

- [ ] 测试 `CourtOfficeLayer.County`、`county_magistrate`、旧记录 county_id=-1、grade=30、首任 8/9 品、管理边为郡守/都督、续任不重复历史。
- [ ] 运行 `--county-court` 确认失败。
- [ ] 为 `CourtOfficerTableItem` 增加 nullable `county_id`，旧行读取 -1；唯一约束和索引带 kingdom_id/city_id/county_id/layer/office_id/active。
- [ ] 县令复用 `LocalOfficialCandidateRules`、`OfficerCandidateCatalog`、乡党加分、`LocalOfficialTermRules`；缺位只进入对应 City 的 coalesced repair queue。
- [ ] 扩展地方补官、任命、死亡/任期 dirty 队列和 career/history projection；县令历史携带 county scope。
- [ ] 运行 `--county-court --local-court`，提交 `git commit -m "feat: appoint county magistrates with scoped history"`。

## Task 7: 官场读模型、UI 和自定义固定层

**Files:** 修改 `LocalCourtReadModel.cs`、`CourtReadModelService.cs`、`CourtCityGovernmentCard.cs`、`CourtWindow.cs`、`CourtActorNodeView.cs`、模板 models/codec/rules/runtime、`CustomCourtWorkflowWindow.cs`、`Locales/aw3_court.csv`；新增 presentation/template tests。

- [ ] 测试 City 卡片包含县列表，县令点击复用现有人物窗口，固定县卡片 `IsFixedRole=true` 且不能删除或改管理边。
- [ ] 运行 `--county-presentation` 确认失败。
- [ ] 读模型携带 counties、县令 node、管理 edge；UI 用现有滚动容器，不复制人物窗口或新卡片体系。
- [ ] 抽象 `CustomCourtAdministrativeLayer`：县层固定指向 `RegionalGovernmentLayer.ManagementOfficeIds[0]`；缺失时使用稳定 City-chief fallback；旧 schema 自动补县层。
- [ ] codec/rules 拒绝删除固定层、改变 layer 或指向非首个管理官，只允许显示名/布局修改；补齐 CSV 本地化。
- [ ] 运行 presentation/source guards，提交 `git commit -m "feat: expose counties in court and custom templates"`。

## Task 8: 地图 county 下钻

**Files:** 修改 `Code/core/policy/CityAdministrationMapModeRules.cs`、`HierarchicalVassalMapModeService.cs`、`HierarchicalVassalMapLabelRuntime.cs`、`HierarchicalVassalLabelDiscoveryJob.cs`、`AWMapModeMetaLibrary.cs`、`Code/patch/AW_HierarchicalVassalMapClickPatch.cs`、`AW_HierarchicalVassalMapLabelPatch.cs`、`AW_HierarchicalVassalMapNameplatePatch.cs`；新增 map tests/source guard。

- [ ] 测试路径 country -> region -> city -> county，空地点击保留标签，返回逐层退出，未聚焦 City 不显示全图县。
- [ ] 运行 `--county-map` 确认失败。
- [ ] 增加 Counties level、focused county ID 和 breadcrumb，同时保持旧 state 序列化兼容。
- [ ] county layer 仅按 focused City 的 `CountyRecord.zoneIds` 着色；其它层保持原逻辑。
- [ ] label/nameplate 复用 dirty queue，县名放在稳定代表 zone；空地不清标签，不每帧扫描全图。
- [ ] 颜色从 `AssetManager.kingdom_colors_library` 派生并缓存 `(cityId, countyId)`，禁止 per-frame 创建 ColorAsset。
- [ ] 运行 `--county-map` 和 Release build，提交 `git commit -m "feat: add city county map drilldown"`。

## Task 9: 名称词库与本地化

**Files:** 修改 `name_generators/lib/Xia真实城名.txt`、`Locales/aw3_court.csv`、`CountyNameService.cs`；新增 CountyNameRulesTests。

- [ ] 测试历史名追加“县”、同 City 不重复、手动名不覆盖、词库耗尽 fallback、读档保持。
- [ ] 运行 `--county-names` 确认失败。
- [ ] 按 UTF-8 加入用户提供的历史县名；以确定性 hash 选名；不自动改 City 或州名。
- [ ] 补县名、县令、品级、任期 CSV，本地化测试通过后提交 `git commit -m "feat: add historical county names and localization"`。

## Task 10: 性能、兼容和发布门禁

**Files:** 新增 county performance/persistence guards；修改测试项目和 `Program.cs.txt`。

- [ ] Source guard 检查分区仅由 dirty City 队列驱动、地图不全图枚举、候选人复用 `OfficerCandidateCatalog`、ColorAsset 不在 per-frame 创建。
- [ ] 运行所有测试确认新 guard 先失败。
- [ ] 实现损坏/未知 schema 的回滚：保留原 JSON、清空内存县、按当前 zones 重建；模板导入失败保留上一份有效模板；county 异常不得阻塞原版 City/人口/经济/RTS。
- [ ] 运行：
```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release
Get-ChildItem Tests -Filter '*SourceGuard.ps1' -Recurse | ForEach-Object { powershell -NoProfile -ExecutionPolicy Bypass -File $_.FullName }
```
Expected：零测试失败、零 guard 失败、Release build 成功。
- [ ] WorldBox smoke：新 City、zone 25/26、县令死亡/任期、保存/读档、国家→州→City→县、空地点击、旧模板导入；记录帧率和日志。
- [ ] 所有检查通过后执行代码审查，再合并 master；提交 `git commit -m "test: enforce county persistence and performance gates"`。

## 执行门禁

1. Task 1 到 Task 10 顺序执行，每项先测后改并独立提交。
2. 身份完成后验证贵族继承、家谱、婚姻、学校读取不把士大夫当贵族。
3. sidecar 完成后用旧存档 round-trip，确认失败不影响 City ownership。
4. 官场完成后确认 county_id=-1 旧查询兼容。
5. 地图完成后必须验证空地点击、返回层级和全图性能。
6. 未通过所有测试、Release build 和代码审查不得合并 master。
