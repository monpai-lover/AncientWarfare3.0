# AW3 War Notice, Temporary Levy, And Frontier Deployment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Xia-rites deliberate wars announce their intent, give defenders one to three years to raise bounded temporary levies, and durably deploy all non-guard armies to threatened frontier cities.

**Architecture:** Notice data is part of the current decision and its encoded queue row, so preemption cannot duplicate or reset deadlines. A runtime notice index projects persisted decision state; a kingdom-year mobilization service performs bounded recruitment, while a dedicated actor job maintains deployment orders until war creation or cancellation. War-start/end hooks transfer or release temporary service atomically.

**Tech Stack:** C# 10, Harmony, WorldBox decision/war/AI APIs, Cultiway-backed AW3 streaming movement, CSV localization, AW3 tests.

---

### Task 1: Define notice, levy, and deployment rules with TDD

**Files:**
- Create: `Code/core/lineage/WarNoticeRules.cs`
- Create: `Code/core/lineage/TemporaryLevyRules.cs`
- Create: `Code/core/lineage/ArmyDeploymentRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing assertions**

Cover Xia/Xia-rites applicability, excluded independence/rebellion/system/joined wars, `issue+1` earliest year, `issue+3` forced year, full-progress hold behavior, four-work-item/16-candidate-per-item/eight-recruit-per-item and 64-candidate/32-recruit annual limits, enlistment age `<65`, 70-percent ordinary-army readiness, empty-army non-blocking, and guard exclusion.

- [ ] **Step 2: Run rule tests and verify RED**

- [ ] **Step 3: Implement pure deterministic rules and verify GREEN**

The declaration gate returns `Wait` before earliest year, `Ready` when all non-guard populated armies reached their assignment, and `Forced` at the deadline; it never reports completion while political progress is below cost.

- [ ] **Step 4: Commit**

```powershell
git add Code/core/lineage/*Rules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define war notice and levy rules"
```

### Task 2: Persist notice state through decision preemption

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/policy/KingdomDecisionQueueCodec.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Create: `Code/core/lineage/WarNoticeService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing codec round-trip and source-structure tests**

Extend the 15-field row with `notice_signature`, `notice_year`, `earliest_war_year`, `forced_war_year`, and `notice_recorded`. Assert encode/decode round-trips all fields and rejects truncated rows rather than silently resetting deadlines.

- [ ] **Step 2: Run tests and verify RED**

- [ ] **Step 3: Issue notices only when the declaration becomes current**

`WarNoticeService.EnsureCurrentNotice(Kingdom)` checks `XiaizationService.GetLevel`, Xia race, and the deliberate-war exclusion rules. It writes a stable attacker/defender/goal/city/year signature once, records one log/history event, indexes both kingdoms, and queues defender mobilization.

- [ ] **Step 4: Carry notice fields through all decision paths**

Update `CaptureCurrentDecision`, `CreateWarDecisionItem`, `ApplyQueuedDecision`, and `ClearDecisionTarget`. A queued-but-never-current declaration has empty notice fields. A preempted current declaration retains its original issue year and signature.

- [ ] **Step 5: Hold completed declarations without extra spending**

At the start of `AdvanceCurrent`, before the `points<=0` and `remaining<=0` exits, detect a full-progress war declaration and ask `WarNoticeService.CanCompleteCurrentDeclaration`. Only call `Complete` when ready or forced. Refactor `Complete` so decision state is cleared after `ApplyEffect` succeeds; a failed final revalidation cancels cleanly without consuming source claims.

- [ ] **Step 6: Verify and commit**

```powershell
git add Code/core/policy Code/core/lineage/WarNotice* Tests
git commit -m "feat: persist Xia rites war notices"
```

### Task 3: Raise and demobilize temporary levies in bounded batches

**Files:**
- Create: `Code/core/lineage/TemporaryLevyService.cs`
- Modify: `Code/core/lineage/MilitaryRecruitmentScope.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/patch/AW_RetirementPatch.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing source guards for bounded work and retirement isolation**

Require four deferred work items, 16 resident checks and eight enlistments per item, and 64 resident checks and 32 enlistments per year. Require `Actor.updateAge` to exit before benchmark/database retirement work for a levy; forbid whole-kingdom actor scans and levy calls to permanent enlistment/retirement history.

- [ ] **Step 2: Run source guards and verify RED**

- [ ] **Step 3: Implement kingdom-year mobilization**

The kingdom-year hook only schedules work. Process at most one city per shared runtime-queue item, prioritize cached threatened-frontier cities, then resume stable city and resident cursors. Each item scans at most 16 candidates and recruits at most eight actors; all four items together scan at most 64 and recruit at most 32. Persist consumed work items, candidate checks, recruits, and the frontier cursor so same-year emergency changes and save/load resume the remaining budget. Use original eligibility plus age below 65 and AW3 identity exclusions. Stop at each city's full `warrior_slots`, and persist flag, mobilizing kingdom, notice signature, original city, and eventual war ID.

- [ ] **Step 4: Isolate retirement and permanent service side effects**

Use `MilitaryRecruitmentScope.TemporaryLevy` for enlistment. Make actor retirement and fallback retirement scans reject levies before service-time, state, trait, benchmark, and database work.

- [ ] **Step 5: Implement final-emergency cleanup**

After the kingdom has no incoming/outgoing notice or real war, enqueue a coalesced runtime cleanup that processes at most eight levy records. Demote surviving same-kingdom actors into their original/current/capital civilian context without teleport or nationality change. For dead, captured, or naturalized actors, only clear stale AW3 levy fields.

- [ ] **Step 6: Verify and commit**

```powershell
git add Code/core/lineage Code/patch/AW_RetirementPatch.cs Tests
git commit -m "feat: add bounded temporary levies"
```

### Task 4: Add durable pre-war frontier deployment

**Files:**
- Create: `Code/content/WarDeploymentContent.cs`
- Create: `Code/ai/behaviours/actor/BehWarDeploymentMove.cs`
- Create: `Code/core/lineage/ArmyDeploymentService.cs`
- Modify: `Code/content/XiaContent.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Locales/aw3_war_decisions.csv`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing content/source guards**

Require a unique job/task locale key, `BehGoToTileTarget`, Cultiway-backed AW3 path request use, guard exclusion, stable city assignment, dispersed in-city target tiles, and cancellation restoration.

- [ ] **Step 2: Run source guards and verify RED**

- [ ] **Step 3: Register `aw_war_deployment` job and task**

The task repeatedly resolves its persisted assigned city/tile, requests movement through the existing AW3 streaming path adapter, marks arrival within the accepted radius, waits briefly, and yields. It never scans enemy actors or overwrites royal-guard jobs.

- [ ] **Step 4: Assign every ready non-guard army**

Ordinary armies use original `City.isOkToSendArmy()` readiness. Non-guard special armies require a living captain and at least one living warrior. Map armies across threatened frontier cities with stable ordering and per-city dispersed points; keep the assignment until notice completion/cancellation.

- [ ] **Step 5: Verify task text and cancellation**

Add Simplified Chinese, English, and Traditional Chinese rows for deployment, notice issued, mobilization, and demobilization. Confirm actors leave the job before normal war AI chooses combat targets.

- [ ] **Step 6: Commit**

```powershell
git add Code/content Code/ai Code/core/lineage Locales Tests
git commit -m "feat: deploy notified defenders to the frontier"
```

### Task 5: Integrate real-war lifecycle, load rebuilding, and UI summary

**Files:**
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/core/lineage/WarDecisionService.cs`
- Modify: `Code/core/lineage/WarTerritoryService.cs`
- Modify: `Code/ui/windows/KingdomPolicyWindow.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing lifecycle guards**

Require notice-to-war levy transfer before deployment cancellation, activation for unannounced system/joined wars, end-war cleanup only after the final emergency, and runtime-index rebuild on load/archive switch.

- [ ] **Step 2: Implement start/end/cancel ordering**

On war start: transfer notice levy records to war ID, activate sudden-war mobilization for threatened participants, cancel pre-war deployment, then expose actors to normal combat AI. On cancellation: close notice, deployment, and notice-only work. On war end: release only when no remaining notice/war exists.

- [ ] **Step 3: Rebuild runtime state after load**

Clear transient indexes on new map/archive switch and rebuild notices, levy pools, annual levy plans, and deployment assignments from current persisted fields. Load-time world-unit and kingdom passes are permitted; normal runtime work remains indexed and bounded. Old row compatibility is deliberately omitted.

- [ ] **Step 4: Show current preparation state**

The policy window displays notice target, earliest/forced year, levy count, and deployment readiness using localized labels. It reads cached summaries and performs no actor scan during redraw.

- [ ] **Step 5: Run the complete matrix and commit**

```powershell
& '.\Tests\SourceGuardTests.ps1'
dotnet run --project '.\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj' -c Release
dotnet build '.\AncientWarfare3.csproj' -c Debug --no-restore
dotnet build '.\AncientWarfare3.csproj' -c Release --no-restore
git add Code Locales Tests
git commit -m "feat: integrate war notice mobilization lifecycle"
```
