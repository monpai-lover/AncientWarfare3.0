# Restoration Protection And Atomic Launch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with verification checkpoints.

**Goal:** Prevent autonomous restoration from deleting a newly created kingdom when accession changes supporter eligibility, and protect every restored kingdom from external declarations for ten game years.

**Architecture:** Keep the existing shared `RestoreFromCity` creation path as the single place that records restoration protection. Add a pure protection-rule layer plus a runtime service that reads the kingdom deadline, and call it from both AW3 and vanilla war-entry gates. Align autonomous supporter preflight with post-accession roles using a claimant exclusion, one-candidate reserve, and post-creation revalidation without adding world scans.

**Tech Stack:** C#/.NET, Harmony patches, WorldBox runtime APIs, SQLite-backed kingdom data, source-based rule tests in `Tests/AncientWarfare3.Rules.Tests`.

---

### Task 1: Add Pure Protection Rules And Regression Tests

**Files:**
- Create: `Code/core/lineage/RestorationProtectionRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Create: `Tests/AncientWarfare3.Rules.Tests/RestorationProtectionRulesTests.cs.txt`

- [ ] **Step 1: Write failing rule tests**

Add tests for `currentYear < protectionUntilYear`, the exact year-10 expiry,
incoming versus outgoing direction, and the explicit internal-war type allowlist.
Include external system wars as blocked and independence, general rebellion,
fief independence, succession, Jingnan, coup restoration, Mandate rebellion,
and restoration wars as allowed when the restored kingdom is the defender.

- [ ] **Step 2: Run the targeted test and verify the expected failure**

Run the repository's rules test command with the new selector or compile the
test source through the existing `Program.cs.txt` harness. Expected failure:
the new `RestorationProtectionRules` type and methods are not defined.

- [ ] **Step 3: Implement the minimal pure rule API**

Provide methods equivalent to:

```csharp
public static bool IsActive(int currentYear, int protectionUntilYear);
public static int ProtectionUntil(int restorationYear, int durationYears);
public static bool IsInternalWarType(string warType);
public static bool ShouldBlockIncoming(bool active, bool protectedDefender,
    bool internalWar, bool attackerIsDefender);
```

Use ordinal string comparisons and keep the method free of WorldBox objects.

- [ ] **Step 4: Run the tests and verify they pass**

Run the same targeted command. Expected output: all new protection rule cases
pass and no existing rule case regresses.

- [ ] **Step 5: Commit the isolated rule change**

```bash
git add Code/core/lineage/RestorationProtectionRules.cs Tests/AncientWarfare3.Rules.Tests/Program.cs.txt Tests/AncientWarfare3.Rules.Tests/RestorationProtectionRulesTests.cs.txt
git commit -m "test: define restoration protection rules"
```

### Task 2: Fix Autonomous Initial Cohort Eligibility

**Files:**
- Modify: `Code/core/lineage/RestorationUprisingMobilizationService.cs:58-76`
- Modify: `Code/core/lineage/AutonomousRestorationService.cs:366-383`
- Modify: `Code/core/lineage/RestorationUprisingRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing supporter-selection tests**

Cover these cases in the existing restoration rule section:

```text
claimant is not counted as an initial supporter;
preflight requires minimum supporters plus one reserve;
post-creation validation uses the revalidated list count;
normal cohort enlistment still uses only the minimum threshold.
```

- [ ] **Step 2: Run the tests and verify the failure**

Run the targeted restoration rules selector. Expected failure: the current
selection and threshold helpers count the claimant and have no reserve-aware
decision.

- [ ] **Step 3: Implement bounded preflight alignment**

Extend the pure restoration rules with a reserve-aware required count. Update
`CollectInitialSupporterIds` and its caller so the claimant ID is skipped while
the existing resident inspection cap remains unchanged. Require the minimum
plus one during seed selection and revalidation. Do not enumerate actors beyond
the current bounded seed-city inspection.

- [ ] **Step 4: Revalidate after identity creation**

In `AutonomousRestorationService`, call
`RevalidateInitialSupporterIds` after `RestoreFromCity`, use the returned count
for `postCreationSeedValid`, and pass the returned IDs to
`TryStartWithInitialCohort` with the normal minimum requirement. Preserve the
existing rollback only for a genuinely invalid seed or failed actual cohort.

- [ ] **Step 5: Run the restoration tests and build**

Run the targeted rules tests, then the Debug build. Expected result: the
claimant/future-heir regression passes and the build has zero errors.

- [ ] **Step 6: Commit the cohort fix**

```bash
git add Code/core/lineage/RestorationUprisingMobilizationService.cs Code/core/lineage/AutonomousRestorationService.cs Code/core/lineage/RestorationUprisingRules.cs Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "fix: align restoration cohort eligibility after accession"
```

### Task 3: Persist The Ten-Year Deadline For All Restoration Routes

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs:185-193`
- Modify: `Code/core/lineage/KingdomIdentityContinuityService.cs:269-288`
- Create or modify: `Code/core/lineage/RestorationProtectionService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing persistence/source tests**

Assert that the shared `RestoreFromCity` success path writes the protection
deadline and that all three callers still use that shared path. Assert that
missing or negative deadlines are treated as inactive.

- [ ] **Step 2: Run the tests and verify failure**

Run the targeted source-guard and persistence tests. Expected failure: the
constant, runtime helper, and write are absent.

- [ ] **Step 3: Add the key and runtime service**

Add `RESTORATION_PROTECTION_UNTIL_YEAR` and implement the service to read the
kingdom data field safely, calculate `currentYear + 10`, and expose a bounded
runtime predicate. Use the existing date service and no scheduler.

- [ ] **Step 4: Write the deadline at the shared restoration commit**

After identity application succeeds in `RestoreFromCity`, set the deadline for
the current game year plus ten. Keep rollback behavior unchanged so a removed
provisional kingdom cannot retain a live protection state.

- [ ] **Step 5: Run tests and verify**

Run persistence/source tests and Debug build. Expected result: all three routes
are covered by one write path and old-save behavior remains inactive.

- [ ] **Step 6: Commit the persistence change**

```bash
git add Code/core/lineage/LineageKeys.cs Code/core/lineage/KingdomIdentityContinuityService.cs Code/core/lineage/RestorationProtectionService.cs Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: persist restoration protection deadline"
```

### Task 4: Enforce Protection At AW3 And Vanilla War Entry

**Files:**
- Modify: `Code/core/lineage/WarDecisionService.cs:361-420`
- Modify: `Code/patch/AW_WarPatch.cs:64-102`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing war-gate tests**

Add cases for active year 0 and year 9 external incoming declarations, year 10
expiry, outgoing declarations, internal system wars, pending declaration
execution, and direct vanilla entry-point source guards.

- [ ] **Step 2: Run the tests and verify failure**

Run the targeted war-gate tests. Expected failure: `StartWar` and the vanilla
patch do not consult restoration protection.

- [ ] **Step 3: Add the authoritative AW3 gate**

In `StartWar`, after participant validation and before treaty/allowed-war
bypasses, call the runtime protection service. Pass the explicit internal
system-war flag and war type. Return `restoration_protection` when the
protected kingdom is the external defender.

- [ ] **Step 4: Add the vanilla gate**

Update `ShouldBlockWarStart` so the same protection check runs before
`IsAw3AllowedWarStart` can bypass the normal vanilla isolation. Keep the
existing peasant-origin suppression and internal-war exceptions intact.

- [ ] **Step 5: Verify pending diplomatic declarations use the authority gate**

Do not add a second protection implementation to
`DiplomaticWarDeclarationService`. Verify that pending declarations are
revalidated by `WarDecisionService.StartWar` at execution time, and add a
source-guard assertion that the final execution path reaches that authority.

- [ ] **Step 6: Run targeted tests and build**

Run war-gate tests, the full rules suite, Debug build, and Release build.
Expected output: all tests pass with zero build warnings/errors attributable to
the change.

- [ ] **Step 7: Commit the war-gate change**

```bash
git add Code/core/lineage/WarDecisionService.cs Code/patch/AW_WarPatch.cs Code/core/lineage/DiplomaticWarDeclarationService.cs Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: protect restored kingdoms from external wars"
```

### Task 5: Final Verification And Handoff

**Files:**
- Test only: `Tests/AncientWarfare3.Rules.Tests`
- Review only: all files changed in Tasks 1-4

- [ ] **Step 1: Run the complete rules suite**

Run the repository's full rules test command and confirm the existing
restoration, diplomacy, tributary, and war-entry cases remain green.

- [ ] **Step 2: Run Debug and Release builds**

Build both configurations and confirm no new warnings/errors.

- [ ] **Step 3: Review the final diff**

Run `git diff --check`, inspect the staged diff for unrelated changes, and
confirm the original dirty worktree changes were not included.

- [ ] **Step 4: Commit only if verification changes are required**

Do not create a metadata-only commit. If verification reveals a real code
correction, commit it with a focused message and rerun the affected tests.

- [ ] **Step 5: Report deployment status**

Report the commits, tests/builds run, the exact protection year semantics, and
whether a game-side remote save test is still required.
