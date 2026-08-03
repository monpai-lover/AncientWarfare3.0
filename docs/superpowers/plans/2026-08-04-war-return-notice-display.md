# War Return And Runtime Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give armies bound to an ended war a safe-city return intent without disrupting another active-war mission, and render declaration, war, and settlement text from the real `War.name` with localized fallback.

**Architecture:** Add a bounded army-level return queue that reuses the existing RTS transport service and never mutates composition. Hook it only after exact ended-war mission invalidation. Centralize war-name resolution, persist the resolved name in automatic truce `DETAIL_ID`, and keep legacy empty values on the existing generic display path.

**Tech Stack:** C# 9/.NET 9 rules executable, Harmony runtime patches, WorldBox RTS/taxi services, SQLite diplomacy proposal storage.

---

### Task 1: Define Ended-War Return Contracts

**Files:**
- Create: `Code/core/lineage/WarArmyReturnRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing rules test**

Add assertions that only a live current mission whose `WarId` equals the ended id is eligible, a replacement mission for another active war is preserved, arrival clears ordinary return intent, and temporary demobilization is not authorized away from a friendly safe city. Add source-contract assertions for a bounded queue, `ArmyRtsTransportService.TryHandleActor`, and no call to `RestoreCivilian` in the army queue.

- [ ] **Step 2: Run the test to verify RED**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: build failure because `WarArmyReturnRules` does not exist.

- [ ] **Step 3: Add the minimal pure rules**

Implement eligibility and completion decisions without WorldBox object dependencies:

```csharp
public static bool ShouldBeginReturn(bool armyAlive,
    long currentMissionWarId, long endedWarId)
{
    return armyAlive && endedWarId >= 0L &&
           currentMissionWarId == endedWarId;
}

public static bool HasArrived(bool armyAlive, bool insideFriendlySafeCity)
{
    return !armyAlive || insideFriendlySafeCity;
}
```

- [ ] **Step 4: Re-run and confirm the remaining source-contract RED**

Expected: rules compile, but the queue/source assertions fail because `WarArmyReturnService.cs` is absent.

### Task 2: Implement The Bounded Return Queue And End-War Hook

**Files:**
- Create: `Code/core/lineage/WarArmyReturnService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/WarArmyReturnRulesTests.cs.txt`

- [ ] **Step 1: Implement minimal queue behavior**

Store army id, kingdom id, and target city id. `TryBegin` resolves the army's intended kingdom and a live friendly safe city. `ProcessFrame` handles a bounded number of orders, re-resolves captured/destroyed targets, calls `ArmyRtsTransportService.TryHandleActor(captain, target, true)` across islands, and calls `captain.goTo` on land. Arrival or invalid ownership removes the order. Do not call demobilization APIs.

- [ ] **Step 2: Hook exact mission invalidation**

In `InvalidateWar`, snapshot the ended-war mission ids, confirm the controller still has a mission whose `WarId` matches the ended id, retain any replacement mission for another war, invalidate the matching mission, then queue that same live army for return. This ordering ensures stale transport state is released before the return transport request is created.

- [ ] **Step 3: Wire lifecycle processing**

Call `WarArmyReturnService.ProcessFrame()` from the existing authority frame path and `ClearRuntime()` from world reset.

- [ ] **Step 4: Run the focused executable and verify GREEN**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: all return-rule and source-contract assertions pass.

### Task 3: Resolve And Persist Real War Names

**Files:**
- Create: `Code/core/lineage/WarRuntimeDisplayRules.cs`
- Create: `Code/core/lineage/WarRuntimeDisplayService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/WarRuntimeDisplayRulesTests.cs.txt`
- Modify: `Code/core/lineage/WarTypeAssetRules.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/DiplomacyConversationService.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `locales/aw3_diplomacy.csv`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing display tests**

Assert that a non-placeholder live name wins; empty, `war_name_*`, `war_type_*`, and format-token values fall back to a localized value; invalid localized values fall back to a localized generic name; `war_tributary` is accepted; chronicle calls pass the resolved name; named war-ended events use winner data; automatic settlement stores and renders `DETAIL_ID`; and legacy empty `DETAIL_ID` retains the old generic summary.

- [ ] **Step 2: Run and verify RED**

Expected: build failure because `WarRuntimeDisplayRules` does not exist.

- [ ] **Step 3: Implement name resolution and runtime adapter**

Implement a pure selector plus a WorldBox adapter that reads `pWar.name`, localizes `pWar.getAsset()?.localized_war_name`, rejects unresolved keys/tokens, and finally uses `aw_diplomacy_unnamed_war`.

- [ ] **Step 4: Route chronicles and conversation events through the resolved name**

Pass the resolved name to chronicle start and end records. Record a new named war-ended event whose outcome comes from `WarWinner` and the real winning kingdom. Preserve the old event renderer for legacy rows.

- [ ] **Step 5: Persist settlement names compatibly**

Add an optional resolved-war-name argument to `RegisterTrucePair`, write it to `DETAIL_ID`, and pass it from live `War` entry points. When recovering from a settlement proposal, resolve the still-live war by id if available. Render the named settlement summary only when `DETAIL_ID` is a valid display name; otherwise execute the unchanged legacy generic summary and unchanged term lookup/pricing logic.

- [ ] **Step 6: Add localization rows and tributary template**

Add the generic unnamed-war and named settlement/war-ended strings in all locale columns. Add `war_tributary` to `WarTypeAssetRules` without changing any war mechanics.

- [ ] **Step 7: Run the rules executable and verify GREEN**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: all display rules and existing settlement tests pass.

### Task 4: Manual Declaration Regression And Verification

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarRuntimeDisplayRulesTests.cs.txt`

- [ ] **Step 1: Add the declaration-chain regression assertion**

Assert the UI dispatches `AW3CommandRequest.DeclareWar`, the authority handler calls `DiplomaticWarDeclarationService.TryIssue`, `TryIssue` calls `WarNoticeService.EnsureCurrentNotice`, and the notice service records `DiplomacyConversationService.RecordWarNotice`. Do not modify the production chain when this test passes.

- [ ] **Step 2: Run the focused rules slice**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --filter WarArmyReturnRulesTests,WarRuntimeDisplayRulesTests`

Expected: focused assertions pass if the executable supports filters; otherwise the executable safely runs the complete suite.

- [ ] **Step 3: Run complete Rules.Tests**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: `Rule tests passed.`

- [ ] **Step 4: Verify diff hygiene and self-review**

Run: `git diff --check`

Inspect: `git diff --stat`, `git diff`, and `git status --short`.

- [ ] **Step 5: Commit the implementation**

```powershell
git add Code Tests locales
git commit -m "fix: return armies and show real war outcomes"
```
