# Historical School Descent, Roster, and Runtime Stability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make school masters descend atomically, eliminate invalid actors that freeze simulation, and add a complete living-member hierarchy window for every fixed school.

**Architecture:** Keep `HistoricalSchoolDescentService` as the spawn owner, but commit the membership, master, and affiliation rows in one SQLite transaction before adopting runtime state. Resolve ambiguous commit exceptions through strict three-row readback into `Committed`, `CleanFailure`, or `Unknown`; only clean or pre-persistence failures enter the original scheduled ActorManager destruction lifecycle. Preserve and reserve unknown actors without announcements. Add pure standing/layout rules plus a runtime read model over `SchoolMembershipService`, then render a separate pooled, draggable school roster window without changing the existing school/city browser.

**Tech Stack:** C#/.NET Framework 4.8, Harmony, Unity UI, NeoModLoader, System.Data.SQLite, existing AW3 rule harnesses.

---

### Task 1: Atomic descent persistence and actor lifecycle regression

**Files:**
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`
- Modify: `Code/core/schools/HistoricalSchoolStore.cs`
- Modify: `Code/core/schools/HistoricalSchoolDescentService.cs`

- [ ] **Step 1: Add failing pure-rule and source-contract assertions**

Exhaust the `Missing`/`Exact`/`Conflict` truth table for all three rows with successful and
failed queries. Require only `CleanFailure` to pass `CanDestroy`. Require a dedicated commit
API whose transaction starts inside `try`, inserts membership/master/affiliation with exact
row-count checks, binds affiliation `@year`, and uses three dedicated strict readback queries.
Reject boolean `TryRecordDescent`, compensating `RollbackDescent`, ordinary historical
`TryJoin`/`RollbackJoin`, direct `removeObject`, and direct `Actor.Dispose()`.

- [ ] **Step 2: Run the historical-school harness and confirm RED**

Run:

```powershell
dotnet run --project F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj --no-restore
```

Expected: failure reporting missing persistence types, prepare/adopt APIs, or atomic commit
contracts.

- [ ] **Step 3: Implement prepare, atomic commit, and committed adopt**

Prepare an immutable historical membership without persistence or runtime mutation. Insert all
three rows in one transaction and return `SchoolPersistenceOutcome`. After an ambiguous exception,
strictly compare the expected membership, master, and affiliation rows; all exact means committed,
all missing means clean failure, and every other state means unknown. Adopt only committed
membership state, reloading indexes and strictly verifying the same record on conflict.

- [ ] **Step 4: Preserve unknown actors and schedule only proven failures**

Guard the removal helper with pre-persistence state or the pure `CanDestroy` rule. The helper
checks that the manager still owns the actor by ID and calls:

```csharp
pActor.setAlive(pValue: false);
pActor.skipUpdates();
World.world.units.scheduleDestroyOnPlay(pActor);
```

Do not call low-level `removeObject` or `Actor.Dispose()` directly because both bypass
`ActorManager.destroyObject` job-batch cleanup. For `Unknown` and post-commit adopt errors,
keep the actor alive, suppress announcements, and reserve its master/actor/home/affiliation
runtime state. On load, scan living valid `SCHOOL_MASTER_ID` actors and re-reserve unknown
survivors without fabricating memberships.

- [ ] **Step 5: Run the historical-school harness and confirm GREEN**

Run the Task 1 command again. Expected: `AW3 historical school rules passed`.

### Task 2: Pure school standing and hierarchy rules

**Files:**
- Create: `Code/core/schools/SchoolRosterRules.cs`
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj`
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Add failing standing/order/link tests**

Create test candidates for one canonical master, two teachers, a direct disciple, a later
disciple, and a converted member. Assert tier order, reputation/follower tie-breaking,
stable actor-ID fallback, inclusion of every valid living member, and teacher links only
when both endpoints share the selected school.

- [ ] **Step 2: Run the harness and confirm RED**

Expected: compile failure because `SchoolRosterRules` and its value models do not exist.

- [ ] **Step 3: Implement minimal pure rules**

Implement `SchoolRosterStanding`, `SchoolRosterCandidate`, `SchoolRosterNode`, and
`SchoolRosterRules.Build` with deterministic tier grouping, row coordinates, and validated
teacher-link pairs. Keep Unity and WorldBox types out of this file.

- [ ] **Step 4: Run the harness and confirm GREEN**

Expected: all standing, ordering, inclusion, and link tests pass.

### Task 3: Runtime roster read model and dirty version

**Files:**
- Create: `Code/core/schools/SchoolRosterReadModelService.cs`
- Modify: `Code/core/schools/SchoolMembershipService.cs`
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Add failing source-contract tests**

Require the read model to enumerate `SchoolMembershipService.Members`, resolve
`GetActive`, filter live actors, use `HistoricalAffiliationService` for display residence
and service kingdom, and never infer membership from actor traits or city snapshots.
Require a public read-only membership `Version` that changes on join, convert, rollback,
death, load, and clear paths.

- [ ] **Step 2: Confirm RED in the harness**

Expected: missing read model and membership version contracts.

- [ ] **Step 3: Implement the runtime projection**

Build one candidate list per selected school, compute direct-disciple counts once, map
canonical and qualified-teacher state, and pass candidates through `SchoolRosterRules`.
Increment the membership version only when authoritative in-memory state changes.

- [ ] **Step 4: Confirm GREEN in the harness**

Expected: historical-school rules pass without database or Unity-frame work in pure rules.

### Task 4: Dedicated school roster window

**Files:**
- Create: `Code/ui/windows/SchoolRosterWindow.cs`
- Create: `Code/ui/items/SchoolRosterNodeView.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`
- Modify: `Code/ui/AW_LineageTab.cs`
- Modify: `Locales/aw3_school.csv`
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Add failing UI source-contract tests**

Require a unique window ID and tab button, all 14 registered schools, pooled nodes and
links, `TreeDragPanHandler`, batched portrait rendering, `UiUnitAvatarElement`, actor
display-kingdom colors, standing labels, unit click-through, and existing-school-detail
click-through.

- [ ] **Step 2: Run the harness and confirm RED**

Expected: missing roster window, node view, and localization keys.

- [ ] **Step 3: Implement the node view**

Create a pooled 132x104 card with a real live portrait, name in actor kingdom color,
translated standing, generation/reputation text, teacher/residence tooltip, and unit
window click action. A missing portrait must leave the text card usable.

- [ ] **Step 4: Implement the roster window and entry**

Create a resizable window with a fixed-school selector, summary strip, pan/zoom canvas,
pooled orthogonal links, and at most eight portrait binds per frame. Refresh only on open,
school selection, or membership-version change.

- [ ] **Step 5: Add English and Simplified Chinese text**

Add exact Simplified Chinese, English, and Traditional Chinese columns for the roster
button/title, all standing tiers, member/teacher/excluded counts, reputation, generation,
teacher, residence, and empty-school state.

- [ ] **Step 6: Run the harness and confirm GREEN**

Expected: source contracts and pure rules pass.

### Task 5: Historical-school runtime and performance audit

**Files:**
- Inspect and modify only when a failing regression is added: `Code/core/schools/*.cs`
- Inspect and modify only when a failing regression is added: `Code/core/court/CitySchoolSnapshotService.cs`
- Inspect and modify only when a failing regression is added: `Code/core/policy/SchoolMapModeService.cs`
- Inspect and modify only when a failing regression is added: `Code/ui/windows/SchoolWindow.cs`
- Modify: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Audit bounded annual work and lifecycle transitions**

Search for direct actor disposal, `createNewUnit`, world-wide actor scans, database calls
inside frame/hover paths, incomplete parameter sets, repeated active-membership queries,
and membership/affiliation rollback asymmetry. Trace every finding to a concrete runtime
path before changing code.

- [ ] **Step 2: Add one failing regression per confirmed defect**

Use pure rule tests where possible and narrow source-contract assertions for Unity/SQLite
integration boundaries. Run the harness after each assertion and confirm the expected
failure.

- [ ] **Step 3: Apply the smallest root-cause fix for each confirmed defect**

Preserve annual budgets, transaction ownership, and cache invalidation semantics. Do not
add exception-swallowing Harmony patches to original simulation loops.

- [ ] **Step 4: Run the historical-school harness after every fix**

Expected: `AW3 historical school rules passed` after each green step.

### Task 6: Full verification and review

**Files:**
- Verify: all changed files

- [ ] **Step 1: Run Debug rebuild**

```powershell
$env:DOTNET_ROLL_FORWARD='Major'
dotnet build AncientWarfare3.csproj -c Debug -t:Rebuild --no-incremental -p:TargetFrameworkRootPath='C:\Users\24908\.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3\build'
```

Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Run Release rebuild**

Run the same command with `-c Release`. Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Run both rule harnesses**

```powershell
dotnet run --project F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj --no-restore
dotnet run --project F:/tmp/AW3PathfindingRuleTests/AW3PathfindingRuleTests.csproj --no-restore
```

Expected: both print their pass messages.

- [ ] **Step 4: Review diffs and runtime log**

Run `git diff --check`, inspect every changed file, and review the latest `Player.log` for
historical-school errors, actor-container null references, Harmony patch failures, and UI
exceptions. If a fresh game run is unavailable, state that runtime verification remains
manual rather than claiming the log is fresh.

- [ ] **Step 5: Clean generated build output**

Remove only `AncientWarfare3.0/bin` and `AncientWarfare3.0/obj` after verifying their
resolved paths are inside the repository.

- [ ] **Step 6: Request focused code review**

Review the complete diff against the design, fix every critical or important finding,
then rerun Steps 1-4 before reporting completion.
