# Shadow Save Reliability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make shadow diagnostics coexist with real batched historical writes, keep saves fail-closed on genuine persistence faults, explain the shadow switch, and suppress duplicate policy-inheritance logs.

**Architecture:** Database enablement alone controls the historical SQLite worker. Shadow mode remains an opt-in comparison layer and never changes persistence success or save eligibility. Death archives continue through the existing queue and batch sink; policy diagnostics use a small pure de-duplication rule backed by a runtime hash set.

**Tech Stack:** C#/.NET Framework 4.8 mod, Unity/NeoModLoader, System.Data.SQLite, Harmony, net9 rule-test harness, JSON locale files.

---

### Task 1: Add pure rules and failing tests for the new contracts

**Files:**
- Create: `Code/core/db/HistoricalWriteModeRules.cs`
- Create: `Code/core/policy/KingdomPolicyInheritanceDiagnosticRules.cs`
- Modify: `Code/core/lineage/ActorDeathArchiveRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HistoricalWriteModeRulesTests.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/KingdomPolicyInheritanceDiagnosticRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorDeathArchiveRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add the mode and diagnostic rules.**

```csharp
namespace AncientWarfare3.core.db
{
    public static class HistoricalWriteModeRules
    {
        public static bool ShouldStartWorker(bool pDatabaseEnabled)
        {
            return pDatabaseEnabled;
        }

        public static bool ShouldAttemptAsyncWrite(bool pDatabaseEnabled,
            bool pWorkerAvailable)
        {
            return pDatabaseEnabled && pWorkerAvailable;
        }

        public static bool ShouldCompareShadow(bool pShadowEnabled,
            bool pWriteAccepted)
        {
            return pShadowEnabled && pWriteAccepted;
        }
    }
}
```

```csharp
using System.Collections.Generic;

namespace AncientWarfare3.core.policy
{
    public static class KingdomPolicyInheritanceDiagnosticRules
    {
        public static string BuildKey(long pWorldGeneration, long pChildId,
            long pSourceId, string pState)
        {
            return pWorldGeneration + ":" + pChildId + ":" + pSourceId + ":" +
                   (pState ?? string.Empty);
        }

        public static bool ShouldLog(ISet<string> pEmitted, string pKey)
        {
            return pEmitted != null && !string.IsNullOrEmpty(pKey) &&
                   pEmitted.Add(pKey);
        }
    }
}
```

Add `ActorDeathArchiveRules.DescribePendingForSave(int pPendingCount, long pFirstActorId, int pFirstAttempts)` that returns a bounded string containing `pending actor death archives`, `first_actor_id`, and `first_attempts`; use `-1` when there is no first actor.

- [ ] **Step 2: Add tests before production wiring.**

The mode tests must assert that `(database=true, shadow=true)` starts and attempts the worker, while `(database=false, shadow=true)` does not; shadow comparison must be false until a write is accepted. The diagnostic tests must assert stable keys, different keys for different states/generations, first insertion returning true, and duplicate insertion returning false. Extend `ActorDeathArchiveRulesTests.Run()` with exact assertions that the save diagnostic contains all three field names and the actor ID/attempt count.

- [ ] **Step 3: Link the new production/test files and invoke the new test suites from `Program.cs.txt`.**

- [ ] **Step 4: Run the focused test command and confirm it fails only because the new APIs are not yet wired.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --historical-shadow`

Expected: compile/test failure identifying the not-yet-used or missing production contracts, with no unrelated test changes.

### Task 2: Make shadow mode execute the real historical worker

**Files:**
- Modify: `Code/core/db/HistoricalWriteService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalWriteRulesTests.cs.txt`

- [ ] **Step 1: Change worker startup gating.**

Replace the `!AWAsyncRuntime.DatabaseEnabled || AWAsyncRuntime.ShadowEnabled` early return with the pure rule:

```csharp
if (!HistoricalWriteModeRules.ShouldStartWorker(
        AWAsyncRuntime.DatabaseEnabled)) return;
```

The worker must therefore start when database writes and shadow checks are both enabled.

- [ ] **Step 2: Rewrite `TryAppendHistory` and `TryUpsertState` shadow branches.**

Require `HistoricalWriteModeRules.ShouldAttemptAsyncWrite` before enqueueing. Build the envelope once, calculate the expected summary only when shadow is enabled, enqueue it through the normal worker path, return `true` on acceptance, and call `CompareShadow` only when `ShouldCompareShadow` is true. If no worker exists, return `historical async writer is disabled` when database writes are disabled or `historical async writer is unavailable` when they are enabled but unavailable. Remove every successful-path assignment of `historical async writer is shadow-only`.

- [ ] **Step 3: Preserve synchronous fallback behavior.**

Do not add a shadow-specific fallback. A failed async enqueue must continue to return false so `LineageArchiveWriter` can use its existing bounded synchronous path; a death archive remains pending if that fallback fails.

- [ ] **Step 4: Add source assertions to `HistoricalWriteRulesTests`.**

Assert that startup checks `DatabaseEnabled` without `ShadowEnabled`, that both state and append APIs call the normal `TryEnqueue`/callback path in shadow mode, and that the literal `historical async writer is shadow-only` is absent from `HistoricalWriteService.cs`.

- [ ] **Step 5: Run the focused historical worker tests.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --historical-shadow`

Expected: PASS, including existing worker failure/completion tests.

### Task 3: Improve save diagnostics without weakening the save barrier

**Files:**
- Modify: `Code/core/lineage/ActorDeathArchiveService.cs`
- Modify: `Code/core/db/HistoricalWriteService.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorDeathArchiveRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Add bounded pending-death diagnostics.**

Add an internal read-only helper in `ActorDeathArchiveService` that scans `Order` without mutating it, finds the first live pending item, and formats `ActorDeathArchiveRules.DescribePendingForSave`. Update `FlushForSave` to return that detail when the queue remains non-empty; do not remove or acknowledge the item on failure.

- [ ] **Step 2: Add elapsed and worker state details to historical flush failures.**

Measure `Stopwatch.GetTimestamp()` around `worker.Flush`. On failure, append elapsed milliseconds, `worker.PendingCount`, and `worker.EarliestUncommittedSequence` to the existing error. Keep the successful error string empty.

- [ ] **Step 3: Keep save preparation fail-closed and surface both details.**

Retain the existing `HistoricalSchoolSavePreparation` ordering in `AW_SavePatch`. Ensure the final error includes the enriched `death_archive_error` and `async_error`; do not increase the timeout beyond the existing bounded rule or bypass `AllResolved`.

- [ ] **Step 4: Add tests for diagnostic formatting and barrier behavior.**

Assert that non-empty death backlog diagnostics identify the first actor and attempts, and that a historical worker terminal fault still reports failure rather than being treated as shadow-only success.

- [ ] **Step 5: Run save-preparation and worker fault tests.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --historical-save`

Expected: PASS; terminal/open/retry failures remain blocking and include an earliest sequence.

### Task 4: De-duplicate policy-inheritance diagnostics

**Files:**
- Modify: `Code/core/policy/KingdomPolicyInheritanceService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/KingdomPolicyInheritanceDiagnosticRulesTests.cs.txt`

- [ ] **Step 1: Add a runtime emitted-key set.**

Add `HashSet<string> InheritanceDiagnosticKeys` and clear it in `ClearRuntime()` alongside the existing pending/inherited caches.

- [ ] **Step 2: Gate the success log with the pure rule.**

After `ApplySnapshot` and `SynchronizeInheritedNameIntegration`, build a key from `AWAsyncRuntime.WorldGeneration`, `pNewKingdom.id`, `source.id`, and `childProfileId + "|" + sourceProfileId`. Log only when `KingdomPolicyInheritanceDiagnosticRules.ShouldLog` returns true. Preserve the existing message text for the first occurrence.

- [ ] **Step 3: Verify source-level placement.**

The log gate must run after a successful policy application, never before validation, and `ClearRuntime` must clear the diagnostic set. Add source assertions for both conditions.

- [ ] **Step 4: Run the diagnostic rule tests.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --policy-inheritance-diagnostics`

Expected: PASS; repeated identical keys are suppressed and changed state/world generation logs again.

### Task 5: Add the shadow tooltip and localized status text

**Files:**
- Modify: `Locales/cz.json`
- Modify: `Locales/en.json`
- Modify: `Locales/ch.json`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ShadowSettingsSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Replace the shadow description in all supported locales.**

Use the simplified-Chinese text in `cz.json`:

`Shadow \\u8bca\\u65ad\\uff1a\\u4ec5\\u7528\\u4e8e\\u5f00\\u53d1/\\u590d\\u73b0\\u5f02\\u6b65\\u5dee\\u5f02\\u3002\\u5f00\\u542f\\u540e\\u4f1a\\u989d\\u5916\\u6821\\u9a8c\\u5f02\\u6b65\\u7ed3\\u679c\\uff0c\\u53ef\\u80fd\\u589e\\u52a0\\u65e5\\u5fd7\\u548c\\u5c11\\u91cf\\u5f00\\u9500\\uff1b\\u4e0d\\u4f1a\\u66ff\\u4ee3\\u6570\\u636e\\u5e93\\u5199\\u5165\\u3002\\u6b63\\u5e38\\u6e38\\u73a9\\u8bf7\\u5173\\u95ed\\u3002`

Use equivalent English and Traditional-Chinese translations in their locale files. Keep the setting default `false` and the callback unchanged.

- [ ] **Step 2: Add a source guard.**

The guard must parse the three JSON files, assert the shadow description contains diagnostic-only wording, a performance/logging warning, and a normal-play-off recommendation, and assert `default_config.json` still defaults the switch to false.

- [ ] **Step 3: Run the settings guard.**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/AncientWarfare3.Rules.Tests/ShadowSettingsSourceGuard.ps1`

Expected: `Shadow settings source guard passed.`

### Task 6: Full verification and focused commits

**Files:**
- Modify only the files listed in Tasks 1-5; do not stage unrelated worktree changes.

- [ ] **Step 1: Run all rule tests.**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: `Rule tests passed.`

- [ ] **Step 2: Build the release assembly.**

Run: `dotnet build AncientWarfare3.csproj -c Release`

Expected: exit code 0 with zero warnings and zero errors.

- [ ] **Step 3: Review the diff and verify no save bypass exists.**

Run: `git diff --check`; `rg -n "shadow-only|FlushForSave|InheritanceDiagnosticKeys|AW3_ENABLE_ASYNC_SHADOW_CHECKS Description" Code Locales Tests`

Expected: no whitespace errors; `shadow-only` appears only in historical tests/spec text, not as a runtime failure assignment; save preparation still requires both death archives and async writes resolved.

- [ ] **Step 4: Commit only the implementation batch.**

```bash
git add Code/core/db/HistoricalWriteModeRules.cs Code/core/db/HistoricalWriteService.cs Code/core/lineage/ActorDeathArchiveRules.cs Code/core/lineage/ActorDeathArchiveService.cs Code/core/policy/KingdomPolicyInheritanceDiagnosticRules.cs Code/core/policy/KingdomPolicyInheritanceService.cs Code/patch/AW_SavePatch.cs Locales/cz.json Locales/en.json Locales/ch.json Tests
git commit -m "fix: make shadow diagnostics safe for historical saves"
```
