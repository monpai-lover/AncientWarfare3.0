# Zhulu Vanilla Total War Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Return Zhulu combat and city transfer to native WorldBox total war while retaining one zero-force liveness fallback and restricting AI declarations to the Zhulu age.

**Architecture:** `ZhuluWarRules` defines the pure boundary and fallback decision. `ZhuluWarService` starts the native war without an AW3 goal. The existing monthly force observer calls a small Zhulu fallback executor, while all ordinary AW3 settlement and occupation paths exclude Zhulu wars.

**Tech Stack:** C#, Harmony, WorldBox runtime API, .NET rules tests, PowerShell source guards.

---

### Task 1: Lock the desired boundary in tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`

- [ ] Replace the old dedicated-settlement assertions with assertions for native total war, no AW3 goal creation, no capture redirection, no war lifecycle interception, and no restore registration.
- [ ] Add pure cases for both-zero peace, attacker-zero defender victory, defender-zero attacker victory, and neither-zero no-op.
- [ ] Add `age_zhulu` AI eligibility and non-Zhulu-age rejection cases.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --zhulu-war-slice` and confirm the assertions fail for the old production behavior.

### Task 2: Restore native declaration and capture behavior

**Files:**
- Modify: `Code/core/lineage/ZhuluWarRules.cs`
- Modify: `Code/core/lineage/ZhuluWarService.cs`
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`
- Modify: `Code/patch/AW_WarPatch.cs`
- Modify: `Code/content/DiplomacyContent.cs`

- [ ] Return `true` from `ShouldUseVanillaTotalWar`.
- [ ] Start `zhulu_war` without constructing or persisting an AW3 goal.
- [ ] Remove Zhulu capture-recipient rewrites.
- [ ] Let native `removeFromWar` and `endWar` always execute for Zhulu.
- [ ] Run the focused test and confirm the native-boundary assertions pass.

### Task 3: Replace dedicated settlement with zero-force fallback

**Files:**
- Modify: `Code/core/lineage/ZhuluWarRules.cs`
- Modify: `Code/core/lineage/WarForceSpecialSettlementService.cs`
- Modify: `Code/core/lineage/WarForceEliminationSettlementService.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`

- [ ] Add a pure `ResolveZeroForceFallback` decision based only on attacker and defender warrior counts.
- [ ] Execute full principal-city transfer only for the single-zero winner; use native `endWar` only if transfer did not already end it.
- [ ] End both-zero as native peace without city transfer.
- [ ] Remove old Zhulu deferred settlement clear/rebuild/queue production calls.
- [ ] Keep rebellion special settlement unchanged.

### Task 4: Restrict AI Zhulu declarations to the Zhulu age

**Files:**
- Modify: `Code/core/lineage/ZhuluWarRules.cs`
- Modify: `Code/core/lineage/WarDecisionAI.cs`

- [ ] Add `CanAiDeclare(currentAgeId)` using exact ordinal comparison with `ZhuluAgeRules.AgeId`.
- [ ] Gate both candidate construction and final synchronous/asynchronous issue boundaries.
- [ ] Keep manual/player declaration rules unchanged.

### Task 5: Verify and deploy source

**Files:**
- Deploy source tree to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] Run the focused Zhulu slice.
- [ ] Run the complete rules suite.
- [ ] Run relevant source guards and `git diff --check`.
- [ ] Copy changed source/content files without compiling or replacing DLLs; preserve `.runtime` and `Assemblies`.
