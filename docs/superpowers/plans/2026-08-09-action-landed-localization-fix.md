# Action-Landed And Localization Recovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent stale or disposed actors from crashing `update_action_landed`, and provide the exact localization keys requested by WorldBox.

**Architecture:** Keep the Cultiway perf post-job order intact, but route only `update_action_landed` through an AW3-owned serial runner. Refresh the original container, remove disposed or re-batched entries, preserve native callbacks for actors that still own the batch, and let all unrelated exceptions propagate. Add exact `_description` localization aliases without removing legacy ` Description` keys.

**Tech Stack:** C#/.NET Framework 4.8, WorldBox `BatchActors` and `ObjectContainer<Actor>`, PowerShell source guards, CSV localization.

---

### Task 1: Capture Both Regressions

**Files:**
- Create: `Tests/ActionLandedPostJobSafetySourceGuard.ps1`
- Modify: `Tests/ReportedLocalizationCoverageSourceGuard.ps1`

- [x] **Step 1: Require a dedicated action-landed job route**

The guard checks the exact job ID, dedicated runner call, container refresh, actor validity checks, original callback, and absence of a broad `NullReferenceException` catch.

- [x] **Step 2: Require exact localization keys**

The localization guard imports both CSV files and requires `aw_royal_enfeoffment_description` and `aw_diplomacy_ai_description` as exact ordinal keys.

- [x] **Step 3: Verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests\ActionLandedPostJobSafetySourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests\ReportedLocalizationCoverageSourceGuard.ps1
```

Expected before implementation: the first reports the missing job route and callback; the second reports both missing keys.

### Task 2: Implement The Narrow Runtime Fix

**Files:**
- Modify: `Code/core/performance/AWCooperativeActorPostRunner.cs`
- Test: `Tests/ActionLandedPostJobSafetySourceGuard.ps1`

- [x] **Step 1: Add `ActionLandedJobId` and dispatch**

Route only `update_action_landed` to `RunActionLandedJob(batch, job.container)` before the generic updater.

- [x] **Step 2: Filter stale actors without swallowing errors**

Call `checkAddRemove()`, iterate the stable container array, explicitly remove entries whose data is gone or whose batch changed, and call `actor.actionLanded()` for actors that still own the current batch. Do not replace original dead-but-not-disposed cleanup semantics.

- [x] **Step 3: Verify GREEN**

Expected: `Action-landed post-job safety guard passed.`

### Task 3: Add Exact Localization Aliases

**Files:**
- Modify: `Locales/aw3_ancestry_mapmode.csv`
- Modify: `Locales/others.csv`
- Test: `Tests/ReportedLocalizationCoverageSourceGuard.ps1`

- [x] **Step 1: Add underscore-description aliases**

Copy the existing three-language descriptions to the exact `_description` keys while retaining the legacy rows.

- [x] **Step 2: Verify GREEN**

Expected: `Reported localization coverage guard passed.`

### Task 4: Verify And Publish

**Files:**
- Verify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Verify: `AncientWarfare3.csproj`

- [ ] **Step 1: Run focused and scheduler guards**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests\ActionLandedPostJobSafetySourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests\ReportedLocalizationCoverageSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Tests\CultiwayPerfSchedulerNonRegressionSourceGuard.ps1
```

- [ ] **Step 2: Run full rules and Release build**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: `Rule tests passed.` and zero build errors.

- [ ] **Step 3: Commit scoped files and push**

Stage only the files listed in this plan, push the feature branch, merge it into `master`, rerun verification, push `master`, and confirm the remote hash equals the local hash.
