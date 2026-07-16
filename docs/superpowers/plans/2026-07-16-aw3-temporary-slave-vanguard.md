# AW3 Temporary Slave Vanguard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove all peacetime slave-army work and replace per-city persistent formations with at most one bounded, temporary, 80-percent-slave vanguard per kingdom during a military emergency.

**Architecture:** `SLAVE_ARMY_ENABLED` remains policy capability only. Notice/war lifecycle hooks enqueue one kingdom-scoped deferred state machine; initial formation publishes an atomic captain-plus-four-slave roster, later work changes at most four actors, and a persisted stable city assignment drives the army through the deployment job without enemy-actor scans. Final-emergency cleanup restores actors and deletes the army object in bounded batches.

**Tech Stack:** C# 10, Harmony, AW3 role armies, deferred runtime queue, temporary recruitment scope, CSV localization, AW3 rule/source tests.

---

### Task 1: Replace legacy formation rules with emergency composition rules

**Files:**
- Modify: `Code/core/lineage/SlaveArmyFormationRules.cs`
- Modify: `Code/core/lineage/SlaveArmyMaintenanceRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing assertions**

Assert: peace never permits formation; capability alone does not imply an army; one kingdom permits one army; maximum roster is 25; initial roster is exactly one non-slave captain plus four slaves; every published roster has `slaveCount * 5 >= totalCount * 4`; a cadre addition that breaks the ratio is rejected; formation work scans one city/32 residents and changes at most four actors after initial creation.

- [ ] **Step 2: Run tests and verify RED**

- [ ] **Step 3: Implement the approved pure rules and verify GREEN**

Casualties may temporarily make an existing army invalid; rules must request repair or disband it, never add a cadre that further reduces the slave ratio.

- [ ] **Step 4: Commit**

```powershell
git add Code/core/lineage/SlaveArmy*Rules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "test: define temporary slave vanguard rules"
```

### Task 2: Remove every peacetime and per-city maintenance path

**Files:**
- Modify: `Code/patch/AW_RetirementPatch.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/core/policy/CityMaintenanceBenchmarkRules.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing absence guards**

Forbid `SlaveService.EnsureSlaveArmy(pCity)` from city maintenance, forbid city-keyed slave army cursors, forbid `DriveSlaveArmyFrontline`, forbid iteration over enemy kingdom actors, and forbid periodic name/identity repair for peaceful slave armies.

- [ ] **Step 2: Run source guards and verify RED**

- [ ] **Step 3: Delete the legacy recurring path**

Remove slave formation/fill/frontline calls and obsolete benchmark labels from `AW_RetirementPatch`. Keep unrelated slave labor, retirement, capture, and ordinary army captain safety.

- [ ] **Step 4: Replace city-scoped runtime state**

Use one kingdom-scoped state record containing army ID, anchor city, target city, stable city/actor cursors, cached slave/cadre counts, emergency token, and cleanup phase. `SLAVE_ARMY_ENABLED` is read only as a capability gate.

- [ ] **Step 5: Run source guards and commit**

```powershell
git add Code/patch/AW_RetirementPatch.cs Code/core/lineage Tests/SourceGuardTests.ps1
git commit -m "perf: remove peacetime slave army maintenance"
```

### Task 3: Add event-driven atomic formation and bounded repair

**Files:**
- Create: `Code/core/lineage/TemporarySlaveVanguardService.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/core/lineage/MilitaryRecruitmentScope.cs`
- Modify: `Code/core/lineage/DeferredRuntimeWorkService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing source guards**

Require one coalesced kingdom key, one deferred item per frame, one-city/32-resident scan bounds, an atomic five-actor initial attach, at most four later mutations, batched persistence, and one aggregated history record.

- [ ] **Step 2: Run source guards and verify RED**

- [ ] **Step 3: Enqueue formation from military emergencies**

Notice issue/receipt and real-war start call `TemporarySlaveVanguardService.OnEmergencyChanged(kingdom)`. It coalesces by kingdom ID and revalidates slavery, policy capability, living kingdom, and active notice/war inside every work item.

- [ ] **Step 4: Form the first valid roster atomically**

Scan at most one city and 32 residents per work item. Do not create an army until one eligible non-slave captain and four eligible slaves are all resolved. Under one `MilitaryRecruitmentScope.SlaveVanguard`, create/attach all five, write flags and cached counts, then publish the kingdom state and aggregated history. If any attach fails, roll back all five and remove the unexposed army.

- [ ] **Step 5: Fill and repair in bounded batches**

Cap total membership at 25. Later work changes at most four actors and keeps at least 80 percent slaves. Lifecycle hooks for death, capture, manumission, or nationality change update cached counts and enqueue repair without scanning the kingdom.

- [ ] **Step 6: Verify and commit**

```powershell
git add Code/core/lineage Tests
git commit -m "feat: form temporary slave vanguards"
```

### Task 4: Assign the vanguard without actor-target scans

**Files:**
- Modify: `Code/core/lineage/ArmyDeploymentService.cs`
- Modify: `Code/core/lineage/TemporarySlaveVanguardService.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing guards for stable city targeting**

Require the slave vanguard role to receive the highest-priority facing city or coastal/capital/lowest-ID fallback, persist that city, and use the shared deployment job. Forbid nearest-enemy actor queries and per-member `goTo` loops.

- [ ] **Step 2: Run source guards and verify RED**

- [ ] **Step 3: Integrate with shared deployment**

The vanguard becomes deployment-ready only after its captain and four slaves produce a valid 80-percent roster. Assign one stable city target per emergency token; individual members follow the army's shared task and dispersed positions. Reassign only when the target city becomes invalid.

- [ ] **Step 4: Verify and commit**

```powershell
git add Code/core/lineage Tests/SourceGuardTests.ps1
git commit -m "perf: route slave vanguards by city target"
```

### Task 5: Demobilize completely after the final emergency

**Files:**
- Modify: `Code/core/lineage/TemporarySlaveVanguardService.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Locales/aw3_war_decisions.csv`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing cleanup tests/guards**

Assert cleanup is deferred while any notice/war remains, processes at most four actors per frame item, restores surviving members without teleport or nationality change, clears dead/captured/naturalized stale flags, removes the empty army object, and clears all kingdom runtime state.

- [ ] **Step 2: Run tests and verify RED**

- [ ] **Step 3: Implement phased cleanup**

End-war/cancel events enqueue cleanup only after `WarNoticeService.HasMilitaryEmergency` becomes false. Each item restores up to four actors to civilian/slave labor state and requeues itself. Once empty, detach captain, cancel deployment, remove the army from world/kingdom/city indexes through the existing AW3 army-removal helper, clear fields, and emit one localized history event.

- [ ] **Step 4: Rebuild or remove on load**

On load/archive switch, rebuild a valid active vanguard from persisted kingdom/actor fields only when an emergency exists. Otherwise enqueue cleanup immediately. Old per-city armies are not migrated because old-save compatibility is out of scope.

- [ ] **Step 5: Run complete verification and commit**

```powershell
& '.\Tests\SourceGuardTests.ps1'
dotnet run --project '.\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj' -c Release
dotnet build '.\AncientWarfare3.csproj' -c Debug --no-restore
dotnet build '.\AncientWarfare3.csproj' -c Release --no-restore
git add Code Locales Tests
git commit -m "feat: complete temporary slave vanguard lifecycle"
```
