# 单城相邻州法理合并决策实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 增加一个立即执行、可回滚、可持久化的国家决策，把同一国家内两个相邻且各只有一个成员城市的州法理合并为一个活动州法理。

**Architecture:** 以纯规则类负责资格、相邻关系、主州排序和冷却判断；以 `DeJureRegionStore` 内的事务服务负责快照、成员迁移、退休和历史写入；以 `KingdomPolicyService` 负责决策点消耗和完成记录。玩家 UI 和 AI 都调用同一个候选解析器与事务入口，玩家通过权威多人命令提交两个 `RegionId`。

**Tech Stack:** C#、Unity UI、Newtonsoft.Json、现有 `KingdomPolicy*` 决策系统、`DeJureRegionStore` 持久化、现有 `.cs.txt` 规则测试和 `dotnet` 测试项目。

---

## 文件地图

- Create: `Code/core/court/DeJureRegionMergeRules.cs` - 纯资格、失败原因、主州排序和冷却规则。
- Create: `Code/core/court/DeJureRegionMergeService.cs` - 活动州候选解析和调用州法理事务入口。
- Modify: `Code/core/court/DeJureRegionStore.cs` - 锁内快照事务、城市迁移、退休和历史记录。
- Modify: `Code/core/court/DeJureRegionModels.cs` - 增加非持久化的候选/结果模型；持久化字段继续复用现有模型。
- Modify: `Code/core/lineage/LineageKeys.cs` - 增加国家合并冷却和上次失败月份键。
- Modify: `Code/content/policies/KingdomPolicyDefs.cs` - 增加决策定义。
- Modify: `Code/core/policy/KingdomPolicyService.cs` - 决策可用性、立即执行、点数扣除、完成记录和冷却。
- Modify: `Code/core/policy/KingdomPolicyAI.cs` - 选择和执行 AI 合并候选。
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalogModels.cs` - 增加权威命令类型和请求工厂。
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalog.cs` - 注册命令描述。
- Modify: `Code/core/multiplayer/commands/AW3PolicyCommandHandler.cs` - 校验国家和 RegionId 并执行命令。
- Modify: `Code/core/multiplayer/commands/AW3AuthoritativeCommandRouter.cs` - 路由新命令。
- Create: `Code/ui/windows/DeJureRegionMergeWindow.cs` - 两级州法理选择和确认窗口。
- Create: `Code/ui/items/DeJureRegionMergeListItem.cs` - 复用现有列表视觉规范的候选项。
- Modify: `Code/ui/windows/KingdomPolicyWindow.cs` - 点击合并决策时打开选择窗口，避免直接启动普通进度决策。
- Modify: `Locales/aw3_policy_decisions.csv` - 决策名称、描述、候选、失败和按钮本地化。
- Create: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeRulesTests.cs.txt` - 纯规则测试。
- Create: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt` - 存储、政策、命令和本地化接线源代码守卫。
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj` - 注册新增测试和纯规则生产文件。

### Task 1: 建立纯合并规则和失败原因

**Files:**
- Create: `Code/core/court/DeJureRegionMergeRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [x] **Step 1: 写失败测试**

在测试文件中加入以下可执行断言：

```csharp
True(DeJureRegionMergeRules.CanMerge(
    primaryActive: true, primaryMemberCount: 1,
    secondaryActive: true, secondaryMemberCount: 1,
    sameKingdom: true, adjacent: true,
    primaryEligible: true, secondaryEligible: true),
    "two adjacent same-kingdom single-city regions can merge");
False(DeJureRegionMergeRules.CanMerge(
    true, 2, true, 1, true, true, true, true),
    "multi-city primary is rejected");
False(DeJureRegionMergeRules.CanMerge(
    true, 1, true, 1, false, true, true, true),
    "cross-kingdom regions are rejected");
False(DeJureRegionMergeRules.CanMerge(
    true, 1, true, 1, true, false, true, true),
    "non-adjacent regions are rejected");
False(DeJureRegionMergeRules.CanMerge(
    true, 1, false, 1, true, true, true, true),
    "retired region is rejected");
Equal(-1, DeJureRegionMergeRules.ComparePrimary(100, 20, 50, 90, 4, 7),
    "higher population wins primary tie-break");
Equal(1, DeJureRegionMergeRules.ComparePrimary(50, 50, 90, 20, 8, 3),
    "higher economy wins equal-population tie-break");
Equal(-1, DeJureRegionMergeRules.ComparePrimary(50, 50, 20, 20, 4, 7),
    "lower region id wins final tie-break");
True(DeJureRegionMergeRules.CooldownAllows(-1, 100),
    "unset cooldown allows execution");
False(DeJureRegionMergeRules.CooldownAllows(100, 105),
    "same short cooldown window rejects execution");
```

- [x] **Step 2: 运行测试确认失败**

运行：`dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

预期：编译失败，提示 `DeJureRegionMergeRules` 尚不存在。

- [x] **Step 3: 实现最小纯规则**

实现公开的无 Unity 依赖 API：

```csharp
namespace AncientWarfare3.core.court
{
    internal static class DeJureRegionMergeRules
    {
        internal const int CooldownYears = 3;

        internal static bool CanMerge(bool primaryActive, int primaryMemberCount,
            bool secondaryActive, int secondaryMemberCount, bool sameKingdom,
            bool adjacent, bool primaryEligible, bool secondaryEligible)
            => primaryActive && secondaryActive &&
               primaryMemberCount == 1 && secondaryMemberCount == 1 &&
               sameKingdom && adjacent && primaryEligible && secondaryEligible;

        internal static int ComparePrimary(int leftPopulation, int rightPopulation,
            int leftEconomy, int rightEconomy, long leftRegionId,
            long rightRegionId)
        {
            int population = rightPopulation.CompareTo(leftPopulation);
            if (population != 0) return population;
            int economy = rightEconomy.CompareTo(leftEconomy);
            if (economy != 0) return economy;
            return leftRegionId.CompareTo(rightRegionId);
        }

        internal static bool CooldownAllows(int lastYear, int currentYear)
            => lastYear < 0 || currentYear - lastYear >= CooldownYears;
    }
}
```

- [x] **Step 4: 注册生产文件和测试文件**

在 `.csproj` 中加入两个 `Compile Include`，生产规则使用 `Link="Production\DeJureRegionMergeRules.cs"`。

- [x] **Step 5: 运行测试确认通过并提交**

运行同一 `dotnet run` 命令，预期新增规则测试通过；提交：

```bash
git add Code/core/court/DeJureRegionMergeRules.cs Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "test: define de jure region merge rules"
```

### Task 2: 实现候选解析和州法理事务合并

**Files:**
- Create: `Code/core/court/DeJureRegionMergeService.cs`
- Modify: `Code/core/court/DeJureRegionModels.cs`
- Modify: `Code/core/court/DeJureRegionStore.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [x] **Step 1: 写源代码守卫测试**

测试必须读取上述三个生产文件并断言存在 `GetMergeCandidates`、`TryMergeSingleCityRegions`、`DeJureRegionMerged`、快照恢复和 `WarGoalPersistence.InvalidateOpenDeJureRegionGoals`，同时断言没有直接删除 `DeJureRegion` 对象的代码路径。

将 `DeJureRegionMergeSourceGuardTests.cs.txt` 及其引用的纯规则文件加入测试项目，确保守卫在每次 `dotnet run` 中执行。

- [x] **Step 2: 定义候选和结果模型**

在 `DeJureRegionModels.cs` 增加运行时模型，不序列化到存档：

```csharp
internal sealed class DeJureRegionMergeCandidate
{
    public long PrimaryRegionId { get; set; }
    public long SecondaryRegionId { get; set; }
    public long PrimaryCityId { get; set; }
    public long SecondaryCityId { get; set; }
    public string PrimaryName { get; set; } = string.Empty;
    public string SecondaryName { get; set; } = string.Empty;
}
```

- [x] **Step 3: 实现实际城市边界相接判断**

在 `DeJureRegionMergeService` 中读取两个唯一成员城市，使用原版 `City.neighbours_cities` 作为城市区域边界邻接索引，并做双向检查；禁止使用中心点距离或寻路可达性。城市无效、死亡、匪巢或邻接集合读取异常时返回不可用。

- [x] **Step 4: 实现候选解析器**

实现 `GetMergeCandidates(Kingdom pKingdom)`：只遍历 `DeJureRegionStore.ActiveRegions()`，按成员数、城市国家、匪巢资格和邻接关系筛选；同一对区域只产生一次，使用 `DeJureRegionMergeRules.ComparePrimary` 稳定决定主州，并按主州/次州 `RegionId` 排序。

- [x] **Step 5: 实现锁内快照事务**

在 `DeJureRegionStore` 增加 `TryMergeSingleCityRegions(Kingdom pKingdom, long pPrimaryRegionId, long pSecondaryRegionId, out string pError)`：

```csharp
DeJureAdministrationStore snapshot = CloneStore(_store);
try
{
    // 在 Gate 锁内重新获取并验证两个活动单城州和同国相邻城市。
    // secondary.MemberCityIds[0] 追加到 primary.MemberCityIds，并去重。
    // secondary.MemberCityIds.Clear(); secondary.Active = false;
    // primary.Version++; secondary.Version++;
    // AddChange(primaryId, secondaryCityId, secondaryId, primaryId,
    //           "DeJureRegionMerged");
    // AddChange(secondaryId, secondaryCityId, secondaryId, -1L,
    //           "DeJureRegionRetired");
    // StoreRevision++，清理地方官署聚合并失效法理战争目标。
    return true;
}
catch (Exception error)
{
    _store = snapshot;
    pError = error.Message;
    ModClass.LogError("De jure single-city region merge failed: " + error.Message);
    return false;
}
```

保留主州的 `RegionId`、`RegionName`、`SeatCityId`、创建元数据和颜色来源；绝不修改城市国家归属、人口、官员或官职历史。退休州保留对象和历史但不再出现在 `ActiveRegions()`。

- [x] **Step 6: 实现服务门面和失败原因**

在 `DeJureRegionMergeService` 提供 `TryMerge(Kingdom, long, long, out string)`，负责国家/冷却/候选检查后调用 Store 事务；UI、命令和 AI 只能调用这个门面，不直接修改 Store。

- [x] **Step 7: 运行源代码守卫和规则测试并提交**

运行：`dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`。预期新增源守卫和现有法理退休/新城分配测试通过；提交：

```bash
git add Code/core/court/DeJureRegionModels.cs Code/core/court/DeJureRegionStore.cs Code/core/court/DeJureRegionMergeService.cs Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "feat: add transactional de jure region merge service"
```

### Task 3: 接入国家决策和政治点消耗

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/content/policies/KingdomPolicyDefs.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Code/core/policy/KingdomPolicyAI.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt`

- [ ] **Step 1: 增加状态键和决策定义**

增加 `POLICY_AI_LAST_DEJURE_MERGE_YEAR` 和 `POLICY_AI_LAST_DEJURE_MERGE_MONTH`；在 `KingdomPolicyDefs` 增加：

```csharp
Id = "aw_decision_merge_single_city_de_jure",
Kind = PolicyNodeKind.Decision,
ProfileIds = CommonProfiles,
NameKey = "aw_decision_merge_single_city_de_jure",
DescKey = "aw_decision_merge_single_city_de_jure_desc",
FallbackName = "合并单城州法理",
FallbackDesc = "将同一国家内相邻且各只有一座城市的两个州法理合并，保留主州首府。",
IconPath = "ui/icons/iconMap",
Cost = 60f,
Repeatable = true,
Column = 7,
Row = 1
```

- [ ] **Step 2: 增加决策可用性和特殊提示**

让 `GetStatus` 对该 ID 额外要求政治点不少于 `Cost`、冷却通过且 `DeJureRegionMergeService.GetMergeCandidates(pKingdom).Count > 0`；在 `BuildNodeTooltip` 的特殊要求分支显示“没有相邻单城州法理”“政治点不足”或“冷却未结束”。

- [ ] **Step 3: 实现立即执行入口**

在 `KingdomPolicyService` 增加：

```csharp
internal static bool TryExecuteDeJureMergeDecision(Kingdom pKingdom,
    long pPrimaryRegionId, long pSecondaryRegionId, out string pError)
```

该方法按顺序读取决策定义、检查可访问性/完成状态/节点锁/政治点和冷却，保存原政治点；先调用 `DeJureRegionMergeService.TryMerge`，成功后再扣除 `Cost`、`AddCompleted`、记录通用完成历史、写入年度/月度冷却并 `UpsertSnapshot`。任何失败不得扣点或写完成记录。该决策不进入普通长期 `DECISION_CURRENT` 进度，确认后立即完成。

同时增加 `TryExecuteBestDeJureMergeDecision(Kingdom pKingdom, out string pError)`，读取候选列表的第一项并转调上面的双 RegionId 入口；没有候选时直接返回失败，不扣政治点。

- [ ] **Step 4: 接入 AI**

在 `KingdomPolicyAI.ShouldAutoStartDecision` 对新 ID 检查冷却和候选；在 `TryStartIfEmpty` 对新 ID 直接调用 `KingdomPolicyService.TryExecuteBestDeJureMergeDecision`（内部使用 `DeJureRegionMergeService.GetMergeCandidates` 的相同排序规则），成功后写入两个冷却键。AI 每月只尝试一次，失败写失败月份键，避免重复扫描。

- [ ] **Step 5: 运行政策规则测试并提交**

补充源守卫断言：决策 ID、`Cost`、候选门禁、政治点扣除发生在成功事务之后、失败不扣点、AI 使用统一服务。运行规则测试后提交：

```bash
git add Code/core/lineage/LineageKeys.cs Code/content/policies/KingdomPolicyDefs.cs Code/core/policy/KingdomPolicyService.cs Code/core/policy/KingdomPolicyAI.cs Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt
git commit -m "feat: expose de jure region merge as policy decision"
```

### Task 4: 增加权威多人命令链路

**Files:**
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalogModels.cs`
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalog.cs`
- Modify: `Code/core/multiplayer/commands/AW3PolicyCommandHandler.cs`
- Modify: `Code/core/multiplayer/commands/AW3AuthoritativeCommandRouter.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt`

- [ ] **Step 1: 增加命令类型和请求工厂**

新增 `MergeDeJureRegions` 命令；请求工厂签名为：

```csharp
public static AW3CommandRequest MergeDeJureRegions(
    long countryId, long primaryRegionId, long secondaryRegionId)
```

将两个 RegionId 放入现有 `CityId` 和 `SecondaryId` 数值槽，保持 JSON schema 简单并在命令描述中声明国家上下文。

- [ ] **Step 2: 注册命令描述和权威路由**

在 `AW3MultiplayerCatalog` 注册 Domestic/Country 命令描述；在 `AW3AuthoritativeCommandRouter` 将命令转发到 `AW3PolicyCommandHandler`。

- [ ] **Step 3: 实现命令处理**

处理器必须验证国家有效、两个 RegionId 为正数、区域属于该国候选集合，然后调用 `KingdomPolicyService.TryExecuteDeJureMergeDecision`；成功返回受影响主州 ID，失败返回 `IllegalTarget` 和具体本地化错误键。

- [ ] **Step 4: 运行命令源守卫和规则测试并提交**

断言请求字段、命令枚举、描述、路由和处理器均存在，且处理器不直接写 `MemberCityIds`。运行测试后提交：

```bash
git add Code/api/multiplayer/AW3MultiplayerCatalogModels.cs Code/api/multiplayer/AW3MultiplayerCatalog.cs Code/core/multiplayer/commands/AW3PolicyCommandHandler.cs Code/core/multiplayer/commands/AW3AuthoritativeCommandRouter.cs Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt
git commit -m "feat: authorize de jure merge command"
```

### Task 5: 实现玩家选择窗口并接入政策树

**Files:**
- Create: `Code/ui/windows/DeJureRegionMergeWindow.cs`
- Create: `Code/ui/items/DeJureRegionMergeListItem.cs`
- Modify: `Code/ui/windows/KingdomPolicyWindow.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt`

- [ ] **Step 1: 建立窗口骨架和状态**

复用 `NameDecisionWindow` 的 `AbstractWindow`、`WideWindowChrome`、滚动区域和按钮样式，窗口状态只包含国家 ID、已选主州 ID、已选候选州 ID、实时候选列表、pending 标记和错误文本。不要复制法理数据，也不要在 UI 内计算城市邻接。

- [ ] **Step 2: 实现两级选择交互**

第一次点击设置主州并刷新右侧候选；第二次点击设置被合并州。候选项显示两州名、唯一城市和成员数；确认按钮文案明确“保留主州并合并”。确认前再次调用 `GetMergeCandidates` 验证所选 pair 仍存在。

- [ ] **Step 3: 接入权威命令和状态刷新**

确认时调用 `AW3CommandRequest.MergeDeJureRegions`；`Accepted` 关闭窗口并刷新政策树、法理地图和地方官署；`Pending` 保持 pending；拒绝则显示错误且不清空选择。取消只关闭窗口，不改变政策状态。

- [ ] **Step 4: 从政策节点打开窗口**

在 `KingdomPolicyWindow.BuildNode` 对 `aw_decision_merge_single_city_de_jure` 特判 `DeJureRegionMergeWindow.Open(pKingdom.id)`，不调用普通 `StartPolicyNode`。普通政策节点行为保持不变。

- [ ] **Step 5: 运行 UI 源守卫并提交**

源守卫断言窗口复用现有 `AW_UIStyle`/`WideWindowChrome`/`AW3MultiplayerCommandFacade`、确认使用新命令、没有直接修改 Store。运行规则测试后提交：

```bash
git add Code/ui/windows/DeJureRegionMergeWindow.cs Code/ui/items/DeJureRegionMergeListItem.cs Code/ui/windows/KingdomPolicyWindow.cs Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt
git commit -m "feat: add de jure merge selection window"
```

### Task 6: 完成本地化和候选错误展示

**Files:**
- Modify: `Locales/aw3_policy_decisions.csv`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt`

- [ ] **Step 1: 添加完整 CSV 键**

添加以下键的简体中文、英文、繁体中文列：

```text
aw_decision_merge_single_city_de_jure
aw_decision_merge_single_city_de_jure_desc
aw_de_jure_merge_select_primary
aw_de_jure_merge_select_secondary
aw_de_jure_merge_confirm
aw_de_jure_merge_cancel
aw_de_jure_merge_no_candidates
aw_de_jure_merge_invalid_target
aw_de_jure_merge_cross_kingdom
aw_de_jure_merge_not_adjacent
aw_de_jure_merge_not_single_city
aw_de_jure_merge_retired
aw_de_jure_merge_cooldown
aw_de_jure_merge_insufficient_points
aw_de_jure_merge_committing
```

- [ ] **Step 2: 加入 CSV 源守卫并验证重复键**

测试读取 CSV，断言每个键存在且每行至少有三列；使用现有本地化检查脚本或 `rg` 检查同一文件内没有重复键。

- [ ] **Step 3: 提交本地化**

运行规则测试后提交：

```bash
git add Locales/aw3_policy_decisions.csv Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt
git commit -m "feat: localize de jure merge decision"
```

### Task 7: 完成集成刷新、持久化和失败路径验证

**Files:**
- Modify: `Code/core/court/DeJureRegionStore.cs`
- Modify: `Code/core/court/DeJureRegionMergeService.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt`

- [ ] **Step 1: 验证成功刷新链路**

成功事务必须依次清理 `RegionalGovernmentAggregationService`、调用 `WarGoalPersistence.InvalidateOpenDeJureRegionGoals`，并触发法理地图的现有 dirty/refresh 入口；政策窗口在命令回调后重新读取候选。

- [ ] **Step 2: 增加结构化日志和单次失败抑制**

日志字段固定包含国家 ID、主州 ID、被合并州 ID、两个城市 ID、失败原因、耗时和刷新结果；AI 使用月份键抑制同月重复失败日志，不输出逐帧日志。

- [ ] **Step 3: 运行全量规则测试和构建**

运行：

```bash
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
dotnet build AncientWarfare3.0.sln --no-restore
```

预期：规则测试进程退出码为 0；源码构建无新增错误。

- [ ] **Step 4: 做持久化回归检查并提交**

用测试世界创建两个同国相邻单城州，执行合并、保存、重新加载，确认主州有两个成员、次州 `Active=false` 且历史包含 `DeJureRegionMerged`/`DeJureRegionRetired`；提交：

```bash
git add Code/core/court/DeJureRegionStore.cs Code/core/court/DeJureRegionMergeService.cs Code/core/policy/KingdomPolicyService.cs Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeSourceGuardTests.cs.txt
git commit -m "test: verify de jure merge persistence and refresh"
```

### Task 8: 最终审查和交付检查

**Files:**
- Review only: all files listed above
- Test: `Tests/AncientWarfare3.Rules.Tests/DeJureRegionMergeRulesTests.cs.txt`, `DeJureRegionMergeSourceGuardTests.cs.txt`

- [ ] **Step 1: 对照设计逐项检查**

确认所有设计要求均有实现位置：单城、同国、边界相接、匪巢排除、主州保留、次州退休、历史、回滚、刷新、政治点、冷却、AI、UI 和本地化。

- [ ] **Step 2: 检查未授权副作用**

使用 `rg` 确认合并代码没有删除城市、改变 `city.kingdom`、清理人口、重置官员或覆盖官职历史。

- [ ] **Step 3: 检查工作区和提交历史**

运行：`git status --short`、`git log -8 --oneline`、`git diff origin/master...HEAD --stat`。确认只有本功能相关提交，工作区干净，再进入发布/部署流程。
