# 同族战争、逐鹿总战争与夏化显示 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现同族 AI 稳定 80% 兼并路线、Chaos 阶段的逐鹿总战争，以及国家窗口中的非 Xia 国家夏化等级。

**Architecture:** 纯规则负责稳定桶、逐鹿资格、胜负与 UI 文本，WorldBox 运行时服务只负责采集事实和执行副作用。同族路线同时门控 `WarDecisionAI` 与 `VassalAIService`；逐鹿通过独立战争类型、共享禁和谈守卫、直接城市移交和幂等灭国结算形成完整生命周期。

**Tech Stack:** C#、Harmony、NeoModLoader、Unity UI、SQLite、AW3 规则测试控制台、PowerShell。

---

## 执行约束

当前主工作树包含其他会话的未提交改动，且 `WarDecisionAI.cs`、`VassalAIService.cs`、`WarTerritoryService.cs`、`DiplomacyProposalService.cs`、`KingdomWindowAddition.cs` 等目标文件已有重叠修改。执行每个任务前必须先运行：

```powershell
git diff -- Code/core/lineage/WarDecisionAI.cs Code/core/lineage/VassalAIService.cs Code/core/lineage/WarTerritoryService.cs Code/ui/windows/KingdomWindowAddition.cs
```

不得回滚、覆盖或格式化掉既有改动。由于这些共享文件无法安全按文件独立提交，只有完全由本计划新建的文件可单独提交；共享文件的提交推迟到最终人工检查后处理。

## 文件结构

- 新建 `Code/core/lineage/SamePeopleWarIntentRules.cs`：稳定 80/20 路线纯规则。
- 新建 `Code/core/lineage/WarClaimPreparationService.cs`：共享弱宣称筹备与意图锁定。
- 新建 `Code/core/lineage/ZhuluWarRules.cs`：逐鹿资格、评分、和平门控和胜负纯规则。
- 新建 `Code/core/lineage/ZhuluWarService.cs`：逐鹿候选、宣战、直接占领收件方和活动战争查询。
- 新建 `Code/core/lineage/ZhuluPeaceGuard.cs`：统一识别不可普通和谈的逐鹿战争。
- 新建 `Code/core/lineage/ZhuluWarSettlementService.cs`：截获普通结束、幂等吞并和专用结束。
- 新建 `Code/core/lineage/XiaizationStatusDisplayRules.cs`：国家窗口显示文本纯规则。
- 修改 `Code/core/lineage/WarDecisionAI.cs`、`VassalAIService.cs`：接入同族路线和逐鹿异步候选。
- 修改 `Code/core/lineage/AsyncDiplomacyPlanModels.cs`：增加逐鹿候选类型和评分事实。
- 修改 `Code/core/lineage/WarDecisionService.cs`、`WarTerritoryService.cs`：注册逐鹿固有宣战理由和持久化目标。
- 修改 `Code/content/DiplomacyContent.cs`、`WarTypeAssetRules.cs`、`WarIconPathRules.cs`：注册逐鹿战争资产。
- 修改 `Code/core/lineage/DiplomacyProposalService.cs`、`WarPeaceSettlementRuntime.cs`、`WarGoalSettlementRuntimeService.cs`、`WarExhaustionSettlementRuntimeService.cs`、`WarScoreDecisiveSettlementService.cs`、`Code/ui/windows/WarPeaceNegotiationController.cs`：统一阻断普通和谈。
- 修改 `Code/patch/AW_CityOccupationAccelerationPatch.cs`、`Code/patch/AW_WarPatch.cs`：直接占领与结束拦截。
- 修改 `Code/core/lineage/MandatePhaseService.cs`、`MandateService.cs`：活动逐鹿保持 Chaos 并阻止提前建立新天命。
- 修改 `Code/ui/windows/KingdomWindowAddition.cs`、`Code/core/lineage/XiaizationService.cs`：显示夏化窄行和 0 级 Tooltip。
- 修改 `Locales/war.csv`、`Locales/aw3_xiaization_generals.csv`、`Code/core/lineage/HistoryLocalizationRules.cs`、`WarDisplayLabelRules.cs`：本地化和编年史标签。

### Task 1: 同族稳定 80/20 纯规则

**Files:**
- Create: `Code/core/lineage/SamePeopleWarIntentRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/SamePeopleWarIntentRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: 写失败测试**

新增测试类并把它接入 `--war-ai-slice` 和完整测试入口：

```csharp
using AncientWarfare3.core.lineage;

internal static class SamePeopleWarIntentRulesTests
{
    public static void Run()
    {
        int territorial = 0;
        for (int bucket = 0; bucket < 100; bucket++)
            if (SamePeopleWarIntentRules.RouteFromBucket(
                    WarAiPeopleRelation.SameSpecies, bucket,
                    territorialIntentLocked: false) ==
                SamePeopleWarRoute.Territorial)
                territorial++;
        Equal(80, territorial, "exactly eighty buckets prefer annexation");

        int first = SamePeopleWarIntentRules.StableBucket(11L, 22L, 99);
        int second = SamePeopleWarIntentRules.StableBucket(11L, 22L, 99);
        Equal(first, second, "same realm pair and period stays stable");

        Equal(SamePeopleWarRoute.NotApplicable,
            SamePeopleWarIntentRules.RouteFromBucket(
                WarAiPeopleRelation.Foreign, 0, false),
            "foreign wars bypass same-people routing");
        Equal(SamePeopleWarRoute.Territorial,
            SamePeopleWarIntentRules.RouteFromBucket(
                WarAiPeopleRelation.SameCulture, 99, true),
            "an active claim plan remains territorial");
        True(SamePeopleWarIntentRules.ShouldSuppressSubjugation(
                SamePeopleWarRoute.Territorial, "force_vassal"),
            "the eighty-percent route blocks the vassal side entrance");
        False(SamePeopleWarIntentRules.ShouldSuppressSubjugation(
                SamePeopleWarRoute.Territorial, "take_mandate"),
            "special war goals bypass ordinary routing");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new System.Exception(message);
    }

    private static void False(bool value, string message)
    {
        if (value) throw new System.Exception(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(
                expected, actual))
            throw new System.Exception(message + ": expected=" + expected +
                                       " actual=" + actual);
    }
}
```

在测试项目中链接新生产文件，并在 `WarAiGoalSelectionRulesTests.Run()` 后调用 `SamePeopleWarIntentRulesTests.Run()`。

- [ ] **Step 2: 运行测试并确认失败**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --war-ai-slice
```

Expected: 编译失败，提示 `SamePeopleWarIntentRules` 和 `SamePeopleWarRoute` 不存在。

- [ ] **Step 3: 写最小纯规则实现**

```csharp
namespace AncientWarfare3.core.lineage
{
    public enum SamePeopleWarRoute
    {
        NotApplicable,
        Territorial,
        SubjugationCompetition
    }

    public static class SamePeopleWarIntentRules
    {
        public const int TerritorialPercent = 80;

        public static int StableBucket(long attackerId, long targetId,
            int decisionPeriod)
        {
            unchecked
            {
                ulong value = (ulong)attackerId;
                value ^= (ulong)targetId + 0x9E3779B97F4A7C15UL +
                         (value << 6) + (value >> 2);
                value ^= (uint)decisionPeriod + 0xBF58476D1CE4E5B9UL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return (int)(value % 100UL);
            }
        }

        public static SamePeopleWarRoute Resolve(
            WarAiPeopleRelation relation, long attackerId, long targetId,
            int decisionPeriod, bool territorialIntentLocked)
        {
            return RouteFromBucket(relation,
                StableBucket(attackerId, targetId, decisionPeriod),
                territorialIntentLocked);
        }

        public static SamePeopleWarRoute RouteFromBucket(
            WarAiPeopleRelation relation, int bucket,
            bool territorialIntentLocked)
        {
            if (relation != WarAiPeopleRelation.SameCulture &&
                relation != WarAiPeopleRelation.SameSpecies)
                return SamePeopleWarRoute.NotApplicable;
            if (territorialIntentLocked)
                return SamePeopleWarRoute.Territorial;
            int normalized = ((bucket % 100) + 100) % 100;
            return normalized < TerritorialPercent
                ? SamePeopleWarRoute.Territorial
                : SamePeopleWarRoute.SubjugationCompetition;
        }

        public static bool ShouldSuppressSubjugation(
            SamePeopleWarRoute route, string goalType)
        {
            if (route != SamePeopleWarRoute.Territorial) return false;
            return goalType == "force_vassal" ||
                   goalType == "force_tributary";
        }
    }
}
```

- [ ] **Step 4: 运行切片测试**

Run 同 Step 2。

Expected: `War AI goal selection rules passed.`

- [ ] **Step 5: 提交仅新增纯规则文件**

```powershell
git add Code/core/lineage/SamePeopleWarIntentRules.cs
git commit -m "feat: add stable same-people war routing rules" -- Code/core/lineage/SamePeopleWarIntentRules.cs
```

测试项目共享文件已有用户改动，不在此提交中包含。

### Task 2: 两条战争 AI 入口共用兼并筹备

**Files:**
- Create: `Code/core/lineage/WarClaimPreparationService.cs`
- Modify: `Code/core/lineage/WarDecisionAI.cs`
- Modify: `Code/core/lineage/VassalAIService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarAiGoalSelectionRulesTests.cs.txt`

- [ ] **Step 1: 扩充失败测试**

在 `SamePeopleWarIntentRulesTests.Run()` 中增加：

```csharp
Equal(SamePeopleWarDirective.PrepareClaim,
    SamePeopleWarIntentRules.ResolveDirective(
        SamePeopleWarRoute.Territorial, "force_vassal",
        hasTerritorialOption: false, canFabricate: true),
    "the annexation route prepares a claim instead of vassalizing");
Equal(SamePeopleWarDirective.SuppressSelection,
    SamePeopleWarIntentRules.ResolveDirective(
        SamePeopleWarRoute.Territorial, "force_tributary",
        hasTerritorialOption: false, canFabricate: false),
    "an impossible claim does not fall through to tributary war");
Equal(SamePeopleWarDirective.KeepSelection,
    SamePeopleWarIntentRules.ResolveDirective(
        SamePeopleWarRoute.SubjugationCompetition, "force_vassal",
        hasTerritorialOption: false, canFabricate: true),
    "the twenty-percent route keeps existing eligibility competition");
```

- [ ] **Step 2: 运行切片测试并确认新断言先失败**

Run Task 1 Step 2 的命令。

Expected: 编译失败，提示 `SamePeopleWarDirective` 或 `ResolveDirective` 不存在。

- [ ] **Step 3: 提取共享弱宣称筹备服务**

先在 `SamePeopleWarIntentRules.cs` 增加运行时可直接使用的纯指令：

```csharp
public enum SamePeopleWarDirective
{
    KeepSelection,
    SuppressSelection,
    PrepareClaim
}

public static SamePeopleWarDirective ResolveDirective(
    SamePeopleWarRoute route, string selectedGoal,
    bool hasTerritorialOption, bool canFabricate)
{
    if (route != SamePeopleWarRoute.Territorial ||
        !ShouldSuppressSubjugation(route, selectedGoal))
        return SamePeopleWarDirective.KeepSelection;
    if (!hasTerritorialOption && canFabricate)
        return SamePeopleWarDirective.PrepareClaim;
    return SamePeopleWarDirective.SuppressSelection;
}
```

`WarClaimPreparationService` 使用与 `WarDecisionAI.CLAIM_TARGET_ID` 相同的 key `aw_war_ai_claim_target_id`，并复用现有间谍网/伪造文书链：

```csharp
internal static class WarClaimPreparationService
{
    internal const string TargetKey = "aw_war_ai_claim_target_id";

    public static bool IsLockedTo(Kingdom source, Kingdom target)
    {
        if (source?.data == null || target?.data == null) return false;
        source.data.get(TargetKey, out long targetId, -1L);
        return targetId == target.id &&
               (WarTerritoryService.HasActiveProjectAgainst(source, target) ||
                DiplomaticOperationService.HasActiveSpyNetwork(source,
                    target, out _, out _));
    }

    public static bool TryBeginWeakClaim(Kingdom source, Kingdom target)
    {
        City city = WarTerritoryService.FindFirstFabricationTargetCity(
            source, target);
        if (city?.data == null) return false;
        bool started = DiplomaticOperationService.HasActiveSpyNetwork(
            source, target, out _, out _)
            ? DiplomaticOperationService.TryStartForgeDocuments(source,
                target, city, WarTerritoryService.PROJECT_WEAK_CLAIM,
                pPlayerInitiated: false, out _, out _)
            : DiplomaticOperationService.TryStartSpyNetwork(source, target,
                pPlayerInitiated: false, out _, out _);
        if (started) source.data.set(TargetKey, target.id);
        return started;
    }
}
```

- [ ] **Step 4: 接入 `WarDecisionAI`**

在 `PickBestImmediateOption` 构造路线，并在候选循环中仅压制普通附庸/叩关：

```csharp
SamePeopleWarRoute route = SamePeopleWarIntentRules.Resolve(
    relation, pKingdom.id, pTarget.id, Date.getCurrentYear(),
    WarClaimPreparationService.IsLockedTo(pKingdom, pTarget));

if (SamePeopleWarIntentRules.ShouldSuppressSubjugation(
        route, option.goal_type))
    continue;
```

若路线为 `Territorial`、不存在合法核心/宣称选项且可制造弱宣称，则调用 `WarClaimPreparationService.TryBeginWeakClaim` 并返回 `null`。把原私有 `TryAcquireWeakClaim` 的调用改为共享服务。

- [ ] **Step 5: 接入 `VassalAIService`**

在 `StartSubjugationWar` 评分前执行同一计算：

```csharp
SamePeopleWarRoute route = SamePeopleWarIntentRules.Resolve(
    relation, pAttacker.id, pDefender.id, Date.getCurrentYear(),
    WarClaimPreparationService.IsLockedTo(pAttacker, pDefender));
if (route == SamePeopleWarRoute.Territorial)
{
    WarClaimPreparationService.TryBeginWeakClaim(pAttacker, pDefender);
    return false;
}
```

20% 路线继续执行现有爵位差、接壤、军力、独立和臣属容量判断。

- [ ] **Step 6: 验证**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --war-ai-slice
dotnet build AncientWarfare3.csproj -c Debug
```

Expected: 切片通过；主项目 `0 Error(s)`。

### Task 3: 逐鹿资格、和平门控和胜负纯规则

**Files:**
- Create: `Code/core/lineage/ZhuluWarRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: 写失败测试**

```csharp
using AncientWarfare3.core.lineage;

internal static class ZhuluWarRulesTests
{
    public static void Run()
    {
        var valid = new ZhuluEligibilityFacts(MandatePhase.Chaos,
            attackerValid: true, defenderValid: true,
            attackerMandateEligible: true, defenderMandateEligible: true,
            attackerIsSubject: false, sameSubjectTree: false,
            diplomaticBlocked: false, sameAlliance: false,
            alreadyAtWar: false);
        True(ZhuluWarRules.CanStart(valid),
            "eligible Xia-system realms can contest during chaos");
        var renewal = new ZhuluEligibilityFacts(MandatePhase.Renewal,
            true, true, true, true, false, false, false, false, false);
        False(ZhuluWarRules.CanStart(renewal),
            "renewal closes new zhulu wars");
        var lowXiaization = new ZhuluEligibilityFacts(MandatePhase.Chaos,
            true, true, true, false, false, false, false, false, false);
        False(ZhuluWarRules.CanStart(lowXiaization),
            "low-Xiaization targets are ineligible");
        True(ZhuluWarRules.BlocksOrdinarySettlement(
                ZhuluWarRules.WarTypeId, active: true),
            "zhulu blocks ordinary peace");
        Equal(ZhuluWarOutcome.Attackers,
            ZhuluWarRules.ResolveOutcome(attackerValid: true,
                attackerCities: 3, defenderValid: false, defenderCities: 0),
            "the surviving principal wins");
        Equal(ZhuluWarOutcome.Ambiguous,
            ZhuluWarRules.ResolveOutcome(false, 0, false, 0),
            "simultaneous extinction does not invent a winner");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new System.Exception(message);
    }

    private static void False(bool value, string message)
    {
        if (value) throw new System.Exception(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!System.Collections.Generic.EqualityComparer<T>.Default.Equals(
                expected, actual))
            throw new System.Exception(message);
    }
}
```

- [ ] **Step 2: 运行并确认失败**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --zhulu-war-slice
```

Expected: 编译失败，提示逐鹿规则类型不存在。

- [ ] **Step 3: 实现纯规则**

创建完整的规则数据结构；构造函数参数顺序必须与 Step 1 的位置参数调用一致：

```csharp
namespace AncientWarfare3.core.lineage
{
public readonly struct ZhuluEligibilityFacts
{
    public ZhuluEligibilityFacts(MandatePhase phase,
        bool attackerValid, bool defenderValid,
        bool attackerMandateEligible, bool defenderMandateEligible,
        bool attackerIsSubject, bool sameSubjectTree,
        bool diplomaticBlocked, bool sameAlliance, bool alreadyAtWar)
    {
        Phase = phase;
        AttackerValid = attackerValid;
        DefenderValid = defenderValid;
        AttackerMandateEligible = attackerMandateEligible;
        DefenderMandateEligible = defenderMandateEligible;
        AttackerIsSubject = attackerIsSubject;
        SameSubjectTree = sameSubjectTree;
        DiplomaticBlocked = diplomaticBlocked;
        SameAlliance = sameAlliance;
        AlreadyAtWar = alreadyAtWar;
    }

    public MandatePhase Phase { get; }
    public bool AttackerValid { get; }
    public bool DefenderValid { get; }
    public bool AttackerMandateEligible { get; }
    public bool DefenderMandateEligible { get; }
    public bool AttackerIsSubject { get; }
    public bool SameSubjectTree { get; }
    public bool DiplomaticBlocked { get; }
    public bool SameAlliance { get; }
    public bool AlreadyAtWar { get; }
}

public enum ZhuluWarOutcome
{
    None,
    Attackers,
    Defenders,
    Ambiguous
}

public static class ZhuluWarRules
{
public const string WarTypeId = "zhulu_war";
public const string GoalTypeId = "zhulu_annexation";
public const string SettlementBlockedReason =
    "zhulu_requires_total_annexation";

public static bool CanStart(ZhuluEligibilityFacts facts)
{
    return facts.Phase == MandatePhase.Chaos &&
           facts.AttackerValid && facts.DefenderValid &&
           facts.AttackerMandateEligible && facts.DefenderMandateEligible &&
           !facts.AttackerIsSubject && !facts.SameSubjectTree &&
           !facts.DiplomaticBlocked && !facts.SameAlliance &&
           !facts.AlreadyAtWar;
}

public static bool BlocksOrdinarySettlement(string warType, bool active)
{
    return active && warType == WarTypeId;
}

public static ZhuluWarOutcome ResolveOutcome(bool attackerValid,
    int attackerCities, bool defenderValid, int defenderCities)
{
    bool attackerAlive = attackerValid && attackerCities > 0;
    bool defenderAlive = defenderValid && defenderCities > 0;
    if (attackerAlive == defenderAlive)
        return attackerAlive ? ZhuluWarOutcome.None :
            ZhuluWarOutcome.Ambiguous;
    return attackerAlive ? ZhuluWarOutcome.Attackers :
        ZhuluWarOutcome.Defenders;
}
}
}
```

- [ ] **Step 4: 运行逐鹿切片并提交新规则文件**

Run Step 2，Expected: `Zhulu war rules passed.`

```powershell
git add Code/core/lineage/ZhuluWarRules.cs
git commit -m "feat: define zhulu total-war rules" -- Code/core/lineage/ZhuluWarRules.cs
```

### Task 4: 注册逐鹿资产、宣战理由和持久化目标

**Files:**
- Create: `Code/core/lineage/ZhuluWarService.cs`
- Modify: `Code/content/DiplomacyContent.cs`
- Modify: `Code/core/lineage/WarTypeAssetRules.cs`
- Modify: `Code/core/lineage/WarIconPathRules.cs`
- Modify: `Code/core/lineage/WarGoalSettlementRules.cs`
- Modify: `Code/core/lineage/WarDecisionService.cs`
- Modify: `Code/core/lineage/WarTerritoryService.cs`
- Modify: `Locales/war.csv`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Modify: `Code/core/lineage/WarDisplayLabelRules.cs`

- [ ] **Step 1: 先增加规则测试断言**

在 `ZhuluWarRulesTests.Run()` 增加：

```csharp
double adjacent = ZhuluWarRules.ScoreTarget(120f, 100f,
    directlyAdjacent: true, capitalDistance: 20f);
double near = ZhuluWarRules.ScoreTarget(120f, 100f,
    directlyAdjacent: false, capitalDistance: 30f);
double far = ZhuluWarRules.ScoreTarget(120f, 100f,
    directlyAdjacent: false, capitalDistance: 120f);
True(adjacent > near, "border rivals are preferred");
True(near > far, "nearer eligible rivals are preferred");
Equal(double.MinValue,
    ZhuluWarRules.ScoreTarget(20f, 100f, false, 20f),
    "an unaffordable target is rejected");
```

- [ ] **Step 2: 运行逐鹿切片并确认失败**

Run Task 3 Step 2。

Expected: 新评分断言失败。

- [ ] **Step 3: 注册战争资产与本地化**

先给 `ZhuluWarRules` 增加本步骤测试要求的评分：

```csharp
public static double ScoreTarget(float attackerPower,
    float defenderPower, bool directlyAdjacent, float capitalDistance)
{
    float defender = System.Math.Max(1f, defenderPower);
    if (attackerPower < defender * .55f) return double.MinValue;
    double ratio = System.Math.Min(3d, attackerPower / defender);
    double distancePenalty = System.Math.Min(300d,
        System.Math.Max(0f, capitalDistance) * 2d);
    return 600d + (directlyAdjacent ? 200d : 0d) +
           ratio * 100d - distancePenalty;
}
```

在 `DiplomacyContent.Init()` 增加 `war_zhulu` 名称模板，并让 `AddWarType` 接受 `pTotalWar`，再把现有固定赋值改为参数赋值：

```csharp
AddWarNameTemplate("war_zhulu", "逐鹿之战,问鼎之战,天下争衡");
AddWarType(ZhuluWarRules.WarTypeId, "war_zhulu",
    "war_type_zhulu_war", "ui/Icons/traits/iconTianming",
    pAllianceJoin: true, pRebellion: false, pTotalWar: true);
```

把现有方法签名精确替换为：

```csharp
private static void AddWarType(string pId, string pNameTemplate,
    string pLocalizedType, string pIcon, bool pAllianceJoin,
    bool pRebellion = false, bool pTotalWar = false)
```

并把方法内现有 `asset.total_war = false;` 精确替换为：

```csharp
asset.total_war = pTotalWar;
```

`WarGoalTypeIds` 增加 `ZhuluAnnexation`，并给持久化规则增加只用于快照的专用效果。普通自动结算仍由 Task 7 的守卫阻断：

```csharp
public const string ZhuluAnnexation = "zhulu_annexation";

// WarGoalAutomaticSettlementEffect
ZhuluAnnexation

case WarGoalTypeIds.ZhuluAnnexation:
    pProfile = new WarGoalAutomaticSettlementProfile(
        WarGoalAutomaticSettlementEffect.ZhuluAnnexation,
        "principal_extinction", DecisiveVictoryScore,
        pUsesDynamicCityCost: false);
    return true;
```

`WarTypeAssetRules` 允许 `war_zhulu`，`WarIconPathRules` 为逐鹿复用天命图标。`Locales/war.csv` 增加：

```csv
war_type_zhulu_war,逐鹿战争,Zhulu War,逐鹿戰爭
war_name_zhulu_war,逐鹿战争,Zhulu War,逐鹿戰爭
aw_war_goal_zhulu_annexation,逐鹿天下,Contest All Under Heaven,逐鹿天下
aw_hist_label_zhulu_war,逐鹿战争,Zhulu War,逐鹿戰爭
aw_zhulu_peace_blocked,逐鹿之战必须打到一方灭亡,Zhulu wars continue until one principal realm is destroyed,逐鹿之戰必須打到一方滅亡
```

- [ ] **Step 4: 加入宣战资格运行时事实**

`ZhuluWarService.CanDeclare` 构造 `ZhuluEligibilityFacts`：

```csharp
public static bool CanDeclare(Kingdom attacker, Kingdom defender,
    out string reason)
{
    reason = "";
    bool sameRoot = VassalService.GetRootSuzerain(attacker) ==
                    VassalService.GetRootSuzerain(defender);
    bool allowed = ZhuluWarRules.CanStart(new ZhuluEligibilityFacts(
    MandatePhaseService.CurrentPhase,
    ValidRealm(attacker), ValidRealm(defender),
    XiaizationService.CanUseMandateSystem(attacker),
    XiaizationService.CanUseMandateSystem(defender),
    VassalService.GetDiplomaticSuzerain(attacker)?.data != null,
    sameRoot,
    DiplomacyProposalService.HasActiveWarBlocker(attacker, defender),
    WarTerritoryService.AreInSameAlliance(attacker, defender),
    World.world.wars.getWar(attacker, defender, pOnlyMain: false) != null));
    if (!allowed) reason = "zhulu_ineligible";
    return allowed;
}

private static bool ValidRealm(Kingdom kingdom)
{
    return kingdom?.data != null && !kingdom.isRekt() &&
           kingdom.isCiv() && !kingdom.isNeutral();
}
```

- [ ] **Step 5: 接入战争启动与目标持久化**

在 `WarDecisionService` 增加 `WAR_ZHULU`，在 `StartWar` 和 `HasIntrinsicCasusBelli` 中调用 `ZhuluWarService.CanDeclare`。在 `WarGoalTypeIds` 和 `WarTerritoryService` 增加逐鹿目标常量与入口：

```csharp
public static bool TryDeclare(Kingdom attacker, Kingdom defender,
    out string reason)
{
    if (!CanDeclare(attacker, defender, out reason)) return false;
    var goal = new WarGoalRequest
    {
        goal_type = ZhuluWarRules.GoalTypeId,
        target_kingdom = defender,
        target_city = defender.capital ?? FindFirstTargetCity(defender)
    };
    War war = WarDecisionService.TryStartWarWithResult(attacker, defender,
        ZhuluWarRules.WarTypeId, ZhuluWarRules.GoalTypeId);
    if (war?.data == null)
    {
        reason = "zhulu_war_start_failed";
        return false;
    }
    WarGoalCreateResult persisted =
        WarTerritoryService.TryPersistGoalOrEndWar(war, goal);
    reason = persisted.Success ? "" : persisted.Reason;
    return persisted.Success;
}
```

`TryGetGoalSettlementSnapshot` 对逐鹿写入 `completion_kind = "principal_extinction"`、`required_score = 100`；它只用于持久化与显示，不能进入普通自动和谈。

- [ ] **Step 6: 验证构建与本地化**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --zhulu-war-slice
dotnet build AncientWarfare3.csproj -c Debug
rg -n "zhulu_war|zhulu_annexation" Code Locales
```

Expected: 测试通过；构建 `0 Error(s)`；逐鹿类型、目标和四语言键均可检索。

### Task 5: 将逐鹿接入同步/异步战争 AI

**Files:**
- Modify: `Code/core/lineage/AsyncDiplomacyPlanModels.cs`
- Modify: `Code/core/lineage/WarDecisionAI.cs`
- Modify: `Code/core/lineage/AsyncKingdomStrategyService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`

- [ ] **Step 1: 写失败的候选排序测试**

在 `ZhuluWarRulesTests.Run()` 增加：

```csharp
var source = new KingdomStrategyFacts(1L, 150f, .7f, .2f, .7f, 1L);
var zhulu = new StrategyTargetFacts(2L, 100f, -10,
    neighbor: true, atWar: false, warBlocked: false,
    preferredKind: WarStrategyCandidateKind.Zhulu,
    sameRoot: false, vassalBlocked: false,
    fabricationAvailable: false, sameAlliance: false,
    sourceAlliancePower: 150f, targetAlliancePower: 100f,
    mandateValue: 0, mandateCoreControl: 0f,
    zhuluEligible: true, capitalDistance: 20f);
True(WarStrategyCandidateRules.TryEvaluate(source, zhulu,
        out WarStrategyCandidate candidate),
    "eligible zhulu facts produce a candidate");
Equal(WarStrategyCandidateKind.Zhulu, candidate.Kind,
    "the candidate retains the zhulu kind");

var renewalBlocked = new StrategyTargetFacts(2L, 100f, -10,
    true, false, false, WarStrategyCandidateKind.Zhulu,
    false, false, false, false, 150f, 100f, 0, 0f,
    zhuluEligible: false, capitalDistance: 20f);
False(WarStrategyCandidateRules.TryEvaluate(source, renewalBlocked,
        out _), "renewal-captured facts reject zhulu");
```

- [ ] **Step 2: 运行测试并确认失败**

Run Task 3 Step 2。

Expected: `WarStrategyCandidateKind.Zhulu` 不存在或候选断言失败。

- [ ] **Step 3: 扩展异步只读事实**

给 `WarStrategyCandidateKind` 增加 `Zhulu = 4`。把 `StrategyTargetFacts` 现有构造函数签名末尾：

```csharp
float targetAlliancePower = 0f, int mandateValue = 0,
float mandateCoreControl = 1f)
```

精确替换为：

```csharp
float targetAlliancePower = 0f, int mandateValue = 0,
float mandateCoreControl = 1f,
bool zhuluEligible = false, float capitalDistance = 0f)
```

在现有 `MandateCoreControl = Clamp01(mandateCoreControl);` 后追加：

```csharp
ZhuluEligible = zhuluEligible;
CapitalDistance = FiniteNonNegative(capitalDistance);
```

并在 `MandateCoreControl` 属性后追加：

```csharp
public bool ZhuluEligible { get; }
public float CapitalDistance { get; }
```

随后在 `WarStrategyCandidateRules.TryEvaluate` 的 `MandateConquest` 分支前处理：

```csharp
if (pTarget.PreferredKind == WarStrategyCandidateKind.Zhulu)
{
    if (!pTarget.ZhuluEligible || pTarget.AtWar) return false;
    pCandidate = new WarStrategyCandidate(pTarget.TargetId,
        WarStrategyCandidateKind.Zhulu,
        ZhuluWarRules.ScoreTarget(pSource.Power, pTarget.Power,
            pTarget.Neighbor, pTarget.CapitalDistance));
    return pCandidate.Score > double.MinValue;
}
```

- [ ] **Step 4: 捕获逐鹿候选并在主线程重新验证**

`WarDecisionAI.BuildTargetFacts` 仅在 Chaos 且双方 `CanUseMandateSystem` 时标记 `Zhulu`。候选集合在现有接壤国后，补入按首都距离排序的合格 Xia/夏化国家，总数仍受 `MaximumCandidateKingdoms` 限制。

`TryCommitAsyncPlan` 对 `Zhulu` 单独执行：

```csharp
bool started = pPlan.WarKind == WarStrategyCandidateKind.Zhulu
    ? ZhuluWarService.TryDeclare(source, target, out _)
    : TryCommitOrdinaryWar(source, target, pPlan, court);
```

提交前再次调用 `CanDeclare`，防止异步快照产生后阶段、臣属关系或外交状态发生变化。

- [ ] **Step 5: 验证同步与异步路径**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --zhulu-war-slice
dotnet build AncientWarfare3.csproj -c Debug
```

Expected: 逐鹿候选测试通过；构建 `0 Error(s)`。

### Task 6: 逐鹿直接占领和幂等灭国结算

**Files:**
- Create: `Code/core/lineage/ZhuluWarSettlementService.cs`
- Modify: `Code/core/lineage/ZhuluWarService.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`

- [ ] **Step 1: 写失败的胜负与重入规则测试**

在 `ZhuluWarRulesTests.Run()` 增加：

```csharp
Equal(ZhuluWarOutcome.None,
    ZhuluWarRules.ResolveOutcome(true, 2, true, 3),
    "two living principals keep fighting");
Equal(ZhuluWarOutcome.Defenders,
    ZhuluWarRules.ResolveOutcome(false, 0, true, 1),
    "the surviving defender wins");
False(ZhuluWarRules.CanQueueSettlement(warValid: true,
        active: true, alreadyQueued: true),
    "one war cannot enter the settlement queue twice");
Equal(10L, ZhuluWarRules.ResolveCaptureRecipient(
        capturerOnAttackerSide: true, mainAttackerId: 10L,
        mainDefenderId: 20L),
    "an attacking ally awards the city to its principal");
Equal(20L, ZhuluWarRules.ResolveCaptureRecipient(
        capturerOnAttackerSide: false, mainAttackerId: 10L,
        mainDefenderId: 20L),
    "a defending ally awards the city to its principal");
```

- [ ] **Step 2: 运行逐鹿切片并确认失败**

Run Task 3 Step 2。

Expected: 新增结算规则断言失败。

- [ ] **Step 3: 让占领城市直接归主战国**

先在 `ZhuluWarRules` 实现纯重入与收件方规则：

```csharp
public static bool CanQueueSettlement(bool warValid, bool active,
    bool alreadyQueued)
{
    return warValid && active && !alreadyQueued;
}

public static long ResolveCaptureRecipient(bool capturerOnAttackerSide,
    long mainAttackerId, long mainDefenderId)
{
    return capturerOnAttackerSide ? mainAttackerId : mainDefenderId;
}
```

`ZhuluWarService.TryResolveCaptureRecipient` 遍历占领者参与的活动逐鹿战争，确认原城主和占领者分属两侧，然后返回对应侧主战国。`AW_CityOccupationAccelerationPatch.FinishCapture_Prefix` 在叛乱判断前应用：

```csharp
if (ZhuluWarService.TryResolveCaptureRecipient(__instance,
        pNewKingdom, out War zhuluWar, out Kingdom principal))
{
    pNewKingdom = principal;
    __state = new RebellionDirectCaptureState(oldOwner, principal,
        zhuluWar.data.id, pDirect: true);
    return true;
}
```

`JoinCapturedCity_Prefix` 同样把 `pNewSetKingdom` 改成主战国，保证盟友只协战、不分城。

- [ ] **Step 4: 拦截普通战争结束**

把 `AW_WarPatch.EndWar_Prefix` 改为返回 `bool`。若是逐鹿且当前不在专用结束深度中，则入队 `ZhuluWarSettlementService.Queue` 并返回 `false`；Postfix 使用 `__runOriginal`，原方法未运行时不执行普通清理。

- [ ] **Step 5: 实现幂等结算**

`ZhuluWarSettlementService` 使用战争 ID 合并延迟队列，并暴露只读专用结束深度供 Harmony 旁路判断：

```csharp
private const string QueuePrefix = "zhulu_settlement:";
private const int MaximumAttempts = 2;
[System.ThreadStatic] private static int _dedicatedSettlementDepth;
private static readonly System.Collections.Generic.HashSet<long>
    QueuedWarIds = new System.Collections.Generic.HashSet<long>();

public static bool IsDedicatedSettlementActive =>
    _dedicatedSettlementDepth > 0;

public static bool Queue(War war)
{
    long warId = war?.data?.id ?? -1L;
    bool alreadyQueued = QueuedWarIds.Contains(warId);
    if (!ZhuluWarRules.CanQueueSettlement(war?.data != null,
            war?.data != null && !war.hasEnded(), alreadyQueued))
        return false;
    QueuedWarIds.Add(warId);
    DeferredRuntimeWorkService.EnqueueCoalesced(
        QueuePrefix + warId, DeferredWorkClass.Runtime,
        () => Process(warId, 0));
    return true;
}
```

`Process` 重试时直接用相同 key 重新 `EnqueueCoalesced`，不要再次调用 `Queue`；成功、战争已不存在、结果仍不明确或最终失败时都执行 `QueuedWarIds.Remove(warId)`。世界重置时由以下方法清空状态：

```csharp
public static void ClearRuntime()
{
    QueuedWarIds.Clear();
    _dedicatedSettlementDepth = 0;
}
```

在 `AW3RuntimeRestorePipeline.ResetRuntimeCaches` 的 stage 列表中加入：

```csharp
new AW3RestoreStage("zhulu_settlement",
    ZhuluWarSettlementService.ClearRuntime),
```

`Process` 按 `ZhuluWarRules.ResolveOutcome` 判断胜者。对败方仍登记的城市逐一执行：

```csharp
City[] remaining = loser.getCities().Where(city =>
    city?.data != null && !city.isRekt()).ToArray();
foreach (City city in remaining)
    if (city.kingdom != winner)
        city.joinAnotherKingdom(winner, pCaptured: false,
            pRebellion: false);
```

全部验证成功后，在线程静态专用结束深度内调用 `World.world.wars.endWar`。失败保留活动战争并最多重试两次；重复调用看到战争已结束或城市已归胜方时直接成功返回。

- [ ] **Step 6: 验证**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --zhulu-war-slice
dotnet build AncientWarfare3.csproj -c Debug
```

Expected: 胜负/重入测试通过；构建 `0 Error(s)`。

### Task 7: 统一禁和谈并保持 Chaos

**Files:**
- Create: `Code/core/lineage/ZhuluPeaceGuard.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Code/core/lineage/WarPeaceSettlementRuntime.cs`
- Modify: `Code/core/lineage/WarGoalSettlementRuntimeService.cs`
- Modify: `Code/core/lineage/WarExhaustionSettlementRuntimeService.cs`
- Modify: `Code/core/lineage/WarScoreDecisiveSettlementService.cs`
- Modify: `Code/ui/windows/WarPeaceNegotiationController.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/core/lineage/MandatePhaseService.cs`
- Modify: `Code/core/lineage/MandateService.cs`

- [ ] **Step 1: 写失败测试**

在 `ZhuluWarRulesTests.Run()` 增加：

```csharp
True(ZhuluWarRules.BlocksOrdinarySettlement(
        ZhuluWarRules.WarTypeId, active: true),
    "active zhulu blocks peace");
False(ZhuluWarRules.BlocksOrdinarySettlement(
        "aw_normal_war", active: true),
    "ordinary wars keep normal peace");
True(ZhuluWarRules.HasActiveClaimants(
        activeRebels: false, activeZhulu: true),
    "active zhulu keeps the realm in chaos");
True(ZhuluWarRules.HasActiveClaimants(
        activeRebels: true, activeZhulu: false),
    "existing rebel claimants still keep chaos");
False(ZhuluWarRules.HasActiveClaimants(
        activeRebels: false, activeZhulu: false),
    "renewal can resume after all claimants finish");
Equal("zhulu_requires_total_annexation",
    ZhuluWarRules.SettlementBlockedReason,
    "all peace paths return one stable reason");
```

- [ ] **Step 2: 运行测试并确认失败**

Run Task 3 Step 2。

Expected: 活跃争夺者组合或和平原因断言失败。

- [ ] **Step 3: 实现共享守卫**

先在 `ZhuluWarRules` 增加活跃争夺者组合：

```csharp
public static bool HasActiveClaimants(bool activeRebels,
    bool activeZhulu)
{
    return activeRebels || activeZhulu;
}
```

```csharp
internal static class ZhuluPeaceGuard
{
    public static bool BlocksOrdinarySettlement(War war)
    {
        bool active = war?.data != null && !war.hasEnded();
        return ZhuluWarRules.BlocksOrdinarySettlement(
            war?.getAsset()?.id ?? "", active);
    }

    public static string Reason(War war)
    {
        return BlocksOrdinarySettlement(war)
            ? ZhuluWarRules.SettlementBlockedReason
            : "";
    }
}
```

- [ ] **Step 4: 覆盖所有普通结束入口**

在以下现有 `RebellionDirectTerritoryTransferService.BlocksOrdinarySettlement` 检查旁加入逐鹿守卫，并返回逐鹿专用原因：

- `DiplomacyProposalService.TryResolvePeaceScope` 与 AI 战争和谈选择；
- `WarPeaceNegotiationController` 的打开与提交校验；
- `WarPeaceSettlementRuntime` 的准备、验证和执行前复核；
- `WarGoalSettlementRuntimeService.QueueIfReady/Process`；
- `WarExhaustionSettlementRuntimeService.QueueIfReady/Process`；
- `WarScoreDecisiveSettlementService.QueueIfDecisive/Process`。

任一入口命中后只返回 `zhulu_requires_total_annexation`，不得创建可执行和谈草案。

同时把 `AW_WarPatch.RemoveFromWar_Prefix` 改成返回 `bool`，保留原有参与方快照，并在逐鹿活动期间阻止底层单独退出旁路：

```csharp
private static bool RemoveFromWar_Prefix(War __instance,
    Kingdom pKingdom, out bool __state)
{
    __state = false;
    try
    {
        __state = __instance?.data != null &&
                  pKingdom?.data != null &&
                  __instance.hasKingdom(pKingdom);
    }
    catch { }
    return !__state ||
           !ZhuluPeaceGuard.BlocksOrdinarySettlement(__instance) ||
           ZhuluWarSettlementService.IsDedicatedSettlementActive;
}
```

这样玩家单独退出、AI 单独退出和直接调用 `War.removeFromWar` 都不能绕过逐鹿专用结算；专用结算深度内仍允许原版清理参与方。

- [ ] **Step 5: 活动逐鹿保持 Chaos**

`MandatePhaseService` 使用：

```csharp
bool activeClaimants = MandateRebelService.HasActiveRebelClaimants() ||
                       ZhuluWarService.HasActivePrincipalWars();
```

`MandateService.OnKingdomYear` 的自动天命建立，以及 `TryDeclareMandate` 的所有来源，都在活动逐鹿存在时返回 `zhulu_unresolved`。逐鹿专用结算结束后，下一次正常天命检查才可建立新天命并进入 Renewal。

- [ ] **Step 6: 验证**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --zhulu-war-slice
dotnet build AncientWarfare3.csproj -c Debug
rg -n "ZhuluPeaceGuard" Code/core/lineage Code/ui/windows
```

Expected: 测试通过；构建 `0 Error(s)`；六类和谈/自动结算入口全部调用共享守卫。

### Task 8: 国家窗口显示非 Xia 夏化等级

**Files:**
- Create: `Code/core/lineage/XiaizationStatusDisplayRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/XiaizationStatusDisplayRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Code/ui/windows/KingdomWindowAddition.cs`
- Modify: `Code/core/lineage/XiaizationService.cs`
- Modify: `Locales/aw3_xiaization_generals.csv`

- [ ] **Step 1: 写失败测试**

```csharp
using AncientWarfare3.core.lineage;

internal static class XiaizationStatusDisplayRulesTests
{
    public static void Run()
    {
        False(XiaizationStatusDisplayRules.ShouldShow(nativeXia: true),
            "native Xia hides the redundant status row");
        True(XiaizationStatusDisplayRules.ShouldShow(nativeXia: false),
            "every non-Xia realm shows the status row");
        Equal("夏化：0级 · 未入夏",
            XiaizationStatusDisplayRules.Format("夏化", 0, "未入夏"),
            "zero level remains visible");
        Equal("夏化：4级 · 行夏制",
            XiaizationStatusDisplayRules.Format("夏化", 4, "行夏制"),
            "institution level is compact");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new System.Exception(message);
    }

    private static void False(bool value, string message)
    {
        if (value) throw new System.Exception(message);
    }

    private static void Equal(string expected, string actual,
        string message)
    {
        if (expected != actual) throw new System.Exception(message);
    }
}
```

- [ ] **Step 2: 运行并确认失败**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --xiaization-ui-slice
```

Expected: 编译失败，提示 `XiaizationStatusDisplayRules` 不存在。

- [ ] **Step 3: 实现显示纯规则**

```csharp
namespace AncientWarfare3.core.lineage
{
    public static class XiaizationStatusDisplayRules
    {
        public static bool ShouldShow(bool nativeXia) => !nativeXia;

        public static string Format(string prefix, int level,
            string levelLabel)
        {
            return (prefix ?? "") + "：" +
                   System.Math.Max(0, level) + "级 · " +
                   (levelLabel ?? "");
        }
    }
}
```

- [ ] **Step 4: 在现有中段下方创建独立窄行**

`KingdomWindowAddition` 新增 `_xiaizationRow`、`_xiaizationText`、`_xiaizationTip`。在 `AW_KingdomMiddle` 后创建宽 `206`、高 `14` 的 `AW_XiaizationStatus`，使用 `BuildTextButton` 和现有 `SetPolicyTip`，不要塞入已满的头像/国策中列。

刷新逻辑：

```csharp
bool nativeXia = XiaizationService.IsNativePolicyKingdom(kingdom);
bool show = XiaizationStatusDisplayRules.ShouldShow(nativeXia);
_xiaizationRow.SetActive(show);
if (show)
{
    int level = XiaizationService.GetLevel(kingdom);
    _xiaizationText.text = XiaizationStatusDisplayRules.Format(
        AW_L10n.Text("aw_xiaization_status", "夏化"), level,
        XiaizationService.GetLevelLabel(kingdom));
    SetPolicyTip(_xiaizationTip,
        AW_L10n.Text("aw_xiaization_level", "入夏等级"),
        XiaizationService.BuildTooltip(kingdom));
}
```

`XiaizationService.BuildTooltip` 不再对非 Xia 0 级提前返回空字符串，至少返回等级行。

- [ ] **Step 5: 添加本地化并验证**

`Locales/aw3_xiaization_generals.csv` 增加：

```csv
aw_xiaization_status,夏化,Xiaization,夏化
```

运行 Step 2 和：

```powershell
dotnet build AncientWarfare3.csproj -c Debug
```

Expected: UI 切片通过；构建 `0 Error(s)`。

### Task 9: 全量回归、源码部署与实机验收

**Files:**
- Verify: `AncientWarfare3.csproj`
- Verify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Deploy: `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`

- [ ] **Step 1: 运行完整规则测试**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
```

Expected: 进程退出码 `0`，末尾无未处理异常。

- [ ] **Step 2: 构建主项目并检查差异**

```powershell
dotnet build AncientWarfare3.csproj -c Debug
git diff --check
git status --short
```

Expected: 构建 `0 Error(s)`；`git diff --check` 无输出；状态中不存在意外回滚或无关格式化文件。

- [ ] **Step 3: 覆盖部署源码和资源，不部署 DLL**

```powershell
$src = 'F:\WorldBox New Mod\AncientWarfare3.0'
$dst = 'D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0'
Copy-Item "$src\Code" $dst -Recurse -Force
Copy-Item "$src\Locales" $dst -Recurse -Force
Copy-Item "$src\GameResources" $dst -Recurse -Force
Copy-Item "$src\EmbededResources" $dst -Recurse -Force
Copy-Item "$src\mod.json","$src\default_config.json","$src\icon.png" $dst -Force
```

随后验证：

```powershell
Get-ChildItem $dst -Recurse -Include *.dll,*.pdb | Select-Object FullName
```

Expected: 本次复制没有新增 DLL/PDB；模组运行内容来自源码目录和资源。

- [ ] **Step 4: 游戏内验收场景**

创建至少六个国家：两个原生 Xia、两个夏化 4 级、一个夏化 3 级、一个异族 0 级。验证：

1. 非 Xia 国家窗口显示 0-4 级，原生 Xia 隐藏；
2. 普通阶段同族 AI 没宣称时先筹备弱宣称，附庸/叩关不再压倒性出现；
3. 天命崩溃进入 Chaos 后，只有 Xia、夏化 4 级和伪朝产生逐鹿候选；
4. 逐鹿城市由协战方攻下时仍归对应主战国；
5. 外交窗口、AI、战争分数 100 和双方厌战度 100 都不能结束逐鹿；
6. 一方失去最后城市后，剩余城市全部归胜方且战争只结束一次；
7. 仍有逐鹿战争时不能建立新天命，最后一场结束后恢复正常天命建立；
8. 保存并重载后，逐鹿仍不可和谈且能够继续专用结算。

- [ ] **Step 5: 检查日志**

```powershell
$log = 'C:\Users\24908\AppData\LocalLow\mkarpenko\WorldBox\Player.log'
Select-String -Path $log -Pattern 'zhulu|逐鹿|Exception|NullReference|failed' |
    Select-Object -Last 200
```

Expected: 有逐鹿开始/结算诊断；无重复结算、空引用、无限和谈重试或每帧刷屏。
