# Historical Minister Pool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand all six historical minister crates with accurate civil officials and military generals, and deploy military-general cards immediately as captains of ordinary armies without renaming the host kingdom.

**Architecture:** Keep `Monarch` and `Minister` as top-level roles. Add an explicit minister subtype and collection metadata to card definitions, centralize subtype decisions in `HistoricalFigureCardRoleRules`, and keep deployment orchestration in `HistoricalFigureCardDeploymentService`. Reuse `GeneralService`, `AWArmyService`, and existing army lifecycle hooks for promotion, membership, captain assignment, and cleanup.

**Tech Stack:** C#, Unity/WorldBox runtime APIs, existing Harmony lifecycle patches, the inline rules test executable, and the existing card collection persistence format.

---

## Files and Responsibilities

- Modify `Code/content/figures/HistoricalFigureCardModels.cs`: subtype enum and card metadata.
- Modify `Code/content/figures/HistoricalFigureCardCatalog.cs`: legacy classification, six-period seeds, collection IDs, and validation.
- Modify `Code/content/figures/HistoricalFigureCardCrates.cs`: minister-pool count accessors without changing crate boundaries.
- Modify `Code/core/lineage/HistoricalFigureCardRoleRules.cs`: pure subtype and deployment predicates.
- Modify `Code/core/lineage/HistoricalFigureCardDeploymentRules.cs`: military-general preconditions.
- Modify `Code/core/lineage/HistoricalFigureCardDeploymentService.cs`: civil and military deployment branches plus rollback state.
- Modify `Code/core/lineage/GeneralService.cs` only if existing promotion visibility requires a narrow wrapper.
- Modify `Code/core/lineage/AWArmyService.cs` only if existing ordinary army wrappers require a narrow overload.
- Modify the existing card history helper used by `RecordHistory`: deployment and military appointment events.
- Modify `Code/core/lineage/HistoricalFigureCardIdentityService.cs` and `HistoricalFigureCardRuntimeService.cs` only for persistence migration.
- Modify `Code/ui/windows/HistoricalFigureDrawWindow.cs`, `Code/ui/items/HistoricalFigureCardListItem.cs`, and `Locales/aw3_historical_cards.csv` only for subtype/source labels.
- Create `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardMinisterPoolTests.cs.txt`: catalogue and pure-rule tests.
- Modify `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardAcceptanceSourceGuardTests.cs.txt`: deployment source guards.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: register the new suite.
- Modify `docs/api/historical-figure-cards.md`: public subtype and deployment API documentation.

## Task 1: Add Failing Pure Rules Tests

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardMinisterPoolTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Test subtype classification and crate coverage**

Add assertions equivalent to:

```csharp
Equal(HistoricalFigureCardMinisterType.MilitaryGeneral,
    HistoricalFigureCardCatalog.Get("han_han_xin").MinisterType,
    "Han Xin is a military minister");
Equal(HistoricalFigureCardMinisterType.CivilOfficial,
    HistoricalFigureCardCatalog.Get("han_xiao_he").MinisterType,
    "Xiao He is a civil minister");
True(HistoricalFigureCardCrates.All.All(crate =>
    HistoricalFigureCardCatalog.GetCards(crate.Id,
        HistoricalFigureCardRole.Minister).Count >= 40),
    "each period has at least forty ministers");
True(HistoricalFigureCardCrates.All.All(crate =>
    HistoricalFigureCardCatalog.GetCards(crate.Id,
        HistoricalFigureCardRole.Minister).Any(card =>
        card.MinisterType == HistoricalFigureCardMinisterType.CivilOfficial) &&
    HistoricalFigureCardCatalog.GetCards(crate.Id,
        HistoricalFigureCardRole.Minister).Any(card =>
        card.MinisterType == HistoricalFigureCardMinisterType.MilitaryGeneral)),
    "each period contains both minister types");
```

- [ ] **Step 2: Test metadata and host-kingdom semantics**

Assert every minister has non-empty `CollectionId`, historical kingdom, era, background, and detailed biography. Assert `HistoricalFigureCardRoleRules.MinisterChangesKingdomName` is false and `IsKingdomFoundingRole(Minister)` is false.

- [ ] **Step 3: Register and run the suite**

Register `HistoricalFigureCardMinisterPoolTests.Run()` in the existing runner, then run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected result before implementation: the new tests fail because subtype metadata and the expanded pools do not yet exist.

## Task 2: Extend the Card Model Safely

**Files:**
- Modify: `Code/content/figures/HistoricalFigureCardModels.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardMinisterPoolTests.cs.txt`

- [ ] **Step 1: Add a safe subtype default**

Define:

```csharp
public enum HistoricalFigureCardMinisterType
{
    None,
    CivilOfficial,
    MilitaryGeneral
}
```

Append optional constructor parameters to `HistoricalFigureCardDefinition` so old call sites remain source-compatible:

```csharp
HistoricalFigureCardMinisterType pMinisterType =
    HistoricalFigureCardMinisterType.None,
string pCollectionId = ""
```

Expose `MinisterType`, `CollectionId`, and:

```csharp
public bool IsMilitaryGeneral =>
    Role == HistoricalFigureCardRole.Minister &&
    MinisterType == HistoricalFigureCardMinisterType.MilitaryGeneral;
```

Monarch cards use `None`.

- [ ] **Step 2: Run the rules executable**

Run the same `dotnet run` command. Expected result: model compilation succeeds while pool coverage tests still fail.

- [ ] **Step 3: Commit the model change**

```powershell
git add -- Code/content/figures/HistoricalFigureCardModels.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardMinisterPoolTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "Add historical minister subtypes"
```

## Task 3: Build the Six-Period Minister Catalogue

**Files:**
- Modify: `Code/content/figures/HistoricalFigureCardCatalog.cs`
- Modify: `Code/content/figures/HistoricalFigureCardCrates.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardMinisterPoolTests.cs.txt`

- [ ] **Step 1: Centralize role and subtype lookup**

Keep the existing six minister IDs and classify Han Xin, Huo Qubing, and Ban Chao as military generals; classify Xiao He, Zhang Liang, Sima Qian, and Huo Guang as civil officials. Replace scattered ID-only checks with one `MinisterTypeForCard(string pCardId)` lookup.

- [ ] **Step 2: Add a card-only minister seed path**

Add a focused seed helper accepting stable ID, display name, name parts, dynasty, historical kingdom, era, dates, fame, sex, reliable parent fields, biography, background, detailed biography, combat fields, subtype, and collection ID. Do not add card-only ministers to `HistoricalFigureDef`; they must not create ordinary spawn slots.

- [ ] **Step 3: Populate every period**

Add at least forty unique ministers to each existing crate, with both subtypes in each crate. Use stable ASCII IDs and historically appropriate years. Anchor coverage with:

```text
pre_qin_qin: Guan Zhong, Yan Ying, Shang Yang, Zhang Yi, Su Qin,
Fan Ju, Bai Qi, Wang Jian, Meng Tian, Li Si, Wu Qi, Sun Wu, Sun Bin,
Lian Po, Li Mu, Le Yi, Lue Buwei, Wei Liao

han: Xiao He, Zhang Liang, Han Xin, Chen Ping, Cao Can, Zhou Bo,
Wei Qing, Huo Qubing, Li Guang, Huo Guang, Sang Hongyang, Zhang Qian,
Ban Chao, Ban Gu, Dong Zhongshu, Jia Yi, Chao Cuo, Deng Yu

three_six_dynasties: Xun Yu, Xun You, Guo Jia, Jia Xu, Cheng Yu,
Sima Yi, Zhang Liao, Deng Ai, Zhuge Liang, Pang Tong, Fa Zheng, Zhao Yun,
Jiang Wei, Zhou Yu, Lu Su, Lu Meng, Lu Xun, Wang Dao, Xie An, Zu Ti

sui_tang: Gao Jiong, Yang Su, Changsun Wuji, Fang Xuanling, Du Ruhui,
Wei Zheng, Li Jing, Qin Qiong, Yuchi Jingde, Hou Junji, Xu Shiji,
Di Renjie, Yao Chong, Song Jing, Zhang Jiuling, Guo Ziyi, Li Guangbi,
Yan Zhenqing, Li Mi, Pei Du

five_song: Zhao Pu, Kou Zhun, Fan Zhongyan, Han Qi, Fu Bi, Wang Anshi,
Sima Guang, Su Shi, Wen Yanbo, Bao Zheng, Yue Fei, Han Shizhong, Zong Ze,
Li Gang, Di Qing, Yu Yunwen, Xin Qiji, Yelu Xiuge, Han Derang, Wanyan Zongbi

yuan_ming_qing: Yelu Chucai, Shi Tianze, Bayan, Tuotuo, Liu Bowen,
Li Shanchang, Xu Da, Chang Yuchun, Lan Yu, Yu Qian, Wang Yangming,
Zhang Juzheng, Qi Jiguang, Yu Dayou, Yuan Chonghuan, Sun Chengzong,
Hong Chengchou, Dorgon, Fan Wencheng, Zeng Guofan, Li Hongzhang, Zuo Zongtang
```

Use the project’s existing naming conventions. Do not fabricate uncertain parents. Normalize every historical kingdom name through the existing geographic-prefix validation.

- [ ] **Step 4: Extend catalogue validation**

Reject minister cards with `MinisterType == None`, empty `CollectionId`, invalid crate source, or missing biography fields. Verify every crate has both subtypes and at least forty ministers while preserving duplicate ID, parent, lifespan, rarity, fame, and geographic-prefix checks.

- [ ] **Step 5: Run tests and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git add -- Code/content/figures/HistoricalFigureCardCatalog.cs Code/content/figures/HistoricalFigureCardCrates.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardMinisterPoolTests.cs.txt
git commit -m "Expand historical minister crate pools"
```

The new catalogue tests must pass. Existing unrelated baseline failures, if still present, must be reported separately.

## Task 4: Add Pure Deployment Role Rules

**Files:**
- Modify: `Code/core/lineage/HistoricalFigureCardRoleRules.cs`
- Modify: `Code/core/lineage/HistoricalFigureCardDeploymentRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardMinisterPoolTests.cs.txt`

- [ ] **Step 1: Add pure predicates**

Implement:

```csharp
public static bool IsCivilOfficial(HistoricalFigureCardDefinition pCard);
public static bool IsMilitaryGeneral(HistoricalFigureCardDefinition pCard);
public static bool MinisterChangesKingdomName => false;
public static bool CanDeployMilitaryGeneral(bool pHasValidCity,
    bool pHasLivingKingdom, bool pTargetIsCivilKingdom);
```

The military predicate requires a live city and a live civil kingdom. Monarch kingdom founding remains separate.

- [ ] **Step 2: Test the predicates**

Verify `None` is not a minister subtype, the two minister subtypes are mutually exclusive, monarchs are neither subtype, and military deployment rejects missing city, missing kingdom, and non-civil hosts.

- [ ] **Step 3: Run and commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git add -- Code/core/lineage/HistoricalFigureCardRoleRules.cs Code/core/lineage/HistoricalFigureCardDeploymentRules.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardMinisterPoolTests.cs.txt
git commit -m "Add minister deployment role rules"
```

## Task 5: Implement Military-General Deployment

**Files:**
- Modify: `Code/core/lineage/HistoricalFigureCardDeploymentService.cs`
- Modify: `Code/core/lineage/GeneralService.cs` only if compiler visibility requires it
- Modify: `Code/core/lineage/AWArmyService.cs` only if compiler visibility requires it
- Modify: the existing card history helper used by `RecordHistory`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardAcceptanceSourceGuardTests.cs.txt`

- [ ] **Step 1: Track army transaction state**

Track the pre-deployment army, whether a new army was created, whether the actor was promoted, whether membership succeeded, and whether captain assignment succeeded. Consume the card only after every required state is valid.

- [ ] **Step 2: Preserve minister host-kingdom semantics**

For every minister, keep `newKingdom = oldKingdom` and never call kingdom `setName`. The historical kingdom field is used only for source labels and history metadata.

- [ ] **Step 3: Keep the civil-official branch separate**

Ensure the actor is in the city and call `OfficerCandidateCatalog.GetOrBuild` plus `EnsurePresent`. Do not promote the actor, create an army, set a captain, or rename the host kingdom.

- [ ] **Step 4: Add the military-general branch**

After the actor joins the city, use the existing services in this order:

```csharp
if (!GeneralService.PromoteToGeneral(actor))
    throw new InvalidOperationException("military_general_promotion_failed");

Army army = city.hasArmy() ? city.getArmy() : null;
if (army == null || !army.isAlive() || AWArmyService.IsSpecialArmy(army) ||
    AWArmyService.GetIntendedKingdom(army) != oldKingdom)
    army = World.world.armies.newArmy(actor, city);
if (army?.data == null)
    throw new InvalidOperationException("military_general_army_creation_failed");

AWArmyService.AddToArmy(actor, army);
AWArmyService.SetCaptainIfChanged(army, actor);
if (actor.army != army || army.getCaptain() != actor)
    throw new InvalidOperationException("military_general_captain_assignment_failed");
```

Use the existing recruitment/mutation scope around `newArmy` where required. Do not use special-role `EnsureArmy` for this ordinary army. Invoke the existing standing-army replenishment path once after captain assignment and verify the army remains alive with initial soldiers.

- [ ] **Step 5: Record military history**

Record deployment, general appointment, army creation or joining, captain assignment, and initial force establishment through the existing history path. Use the current kingdom and city as targets; use `HistoricalKingdomName` only for the source label.

- [ ] **Step 6: Roll back army mutations**

On failure, clear captain and membership through existing army APIs, end the general state through the existing general retirement path, and schedule or remove only an army created by this deployment. Leave pre-existing city armies untouched, then run the existing actor rollback and preserve the card.

- [ ] **Step 7: Add source guards and build**

Require source guards for `GeneralService.PromoteToGeneral`, `AWArmyService.AddToArmy`, `AWArmyService.SetCaptainIfChanged`, `newArmy(actor, city)`, and the absence of minister-side kingdom renaming. Run:

```powershell
dotnet build AncientWarfare3.csproj
```

Expected result: 0 errors and 0 warnings in the main project.

- [ ] **Step 8: Commit deployment behavior**

```powershell
git add -- Code/core/lineage/HistoricalFigureCardDeploymentService.cs Code/core/lineage/GeneralService.cs Code/core/lineage/AWArmyService.cs Code/core/lineage/HistoryWriter.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardAcceptanceSourceGuardTests.cs.txt
git commit -m "Deploy historical generals with armies"
```

## Task 6: Persist and Display Subtypes and Sources

**Files:**
- Modify: `Code/core/lineage/HistoricalFigureCardIdentityService.cs`
- Modify: `Code/core/lineage/HistoricalFigureCardRuntimeService.cs`
- Modify: `Code/ui/windows/HistoricalFigureDrawWindow.cs`
- Modify: `Code/ui/items/HistoricalFigureCardListItem.cs`
- Modify: `Locales/aw3_historical_cards.csv`
- Modify: `docs/api/historical-figure-cards.md`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardAcceptanceSourceGuardTests.cs.txt`

- [ ] **Step 1: Add migration defaults**

Map old minister IDs through the central subtype lookup and derive missing `CollectionId` from `HistoricalFigureCardCrates.ForYear`. Treat unknown subtype data as `None` for monarch records and `CivilOfficial` only for minister records.

- [ ] **Step 2: Display subtype and source**

Add localized civil-official and military-general labels. Keep the existing window dimensions and card layout. Detail views show the source label and collection name while gold mystery cards remain concealed before reveal.

- [ ] **Step 3: Update API documentation**

Document `MinisterType`, `CollectionId`, `HistoricalKingdomName`, and `IsMilitaryGeneral`, including examples showing that minister deployment keeps the host kingdom name and military generals immediately own an army and captain relationship.

- [ ] **Step 4: Build and commit**

```powershell
dotnet build AncientWarfare3.csproj
git add -- Code/core/lineage/HistoricalFigureCardIdentityService.cs Code/core/lineage/HistoricalFigureCardRuntimeService.cs Code/ui/windows/HistoricalFigureDrawWindow.cs Code/ui/items/HistoricalFigureCardListItem.cs Locales/aw3_historical_cards.csv docs/api/historical-figure-cards.md Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardAcceptanceSourceGuardTests.cs.txt
git commit -m "Persist and display minister card origins"
```

## Task 7: Full Verification and Worktree Review

**Files:**
- Modify only files identified by failing tests or compiler diagnostics; do not reformat unrelated files.

- [ ] **Step 1: Run source and pure rules tests**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Minister-pool and source-guard failures must be resolved. Known unrelated baseline failures are `AW3HistoryEventPublisher` and `KingdomAnnualWorkStage.StateGovernment`.

- [ ] **Step 2: Run whitespace and main build checks**

```powershell
git diff --check
dotnet build AncientWarfare3.csproj
```

Expected result: no whitespace errors and a successful main build.

- [ ] **Step 3: Review acceptance behavior**

Verify each period has at least forty ministers and both subtypes; no minister path renames a kingdom; civil officials enter only the candidate pool; military generals are promoted, captain an ordinary army immediately, and have initial soldiers; card consumption follows success; rollback preserves the card.

- [ ] **Step 4: Review the final diff**

```powershell
git status --short
git diff --stat
git diff -- Code/content/figures/HistoricalFigureCardModels.cs Code/content/figures/HistoricalFigureCardCatalog.cs Code/core/lineage/HistoricalFigureCardDeploymentService.cs
```

Keep unrelated pre-existing worktree changes intact and report them separately.

- [ ] **Step 5: Commit verification fixes**

```powershell
git add -- Code/content/figures/HistoricalFigureCardModels.cs Code/content/figures/HistoricalFigureCardCatalog.cs Code/core/lineage/HistoricalFigureCardRoleRules.cs Code/core/lineage/HistoricalFigureCardDeploymentRules.cs Code/core/lineage/HistoricalFigureCardDeploymentService.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardMinisterPoolTests.cs.txt Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardAcceptanceSourceGuardTests.cs.txt
git commit -m "Verify historical minister deployment"
```
