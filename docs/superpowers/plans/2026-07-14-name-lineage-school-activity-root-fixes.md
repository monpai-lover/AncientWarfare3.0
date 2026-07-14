# Name, Lineage, and School Activity Root-Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore creation-time name/lineage invariants and implement bounded visible school lecture/debate activities.

**Architecture:** Remove display-time repairs and make object creation authoritative. Freeze school activity requests during the annual snapshot, then execute one state transition per frame through dedicated actor tasks, deterministic venue claims, and idempotent persistence.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox AI behaviours, SQLite, NeoModLoader, PowerShell source guards, .NET 9 pure-rule test harness.

---

### Task 1: Establish failing regression guards

**Files:**
- Create: `Tests/SourceGuardTests.ps1`
- Create: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Create: `Tests/AncientWarfare3.Rules.Tests/Program.cs`

- [ ] Write source assertions that reject `WindowMetaGeneric<War`, Kingdom `nameInput.setText`,
  lifecycle `EnsureWorldNames()`, and `ApplyNativeTabSprites`/`tab_main.image_`.
- [ ] Add assertions requiring the four dedicated school task IDs and their locale rows.
- [ ] Run `pwsh -File Tests/SourceGuardTests.ps1`; verify it fails on the current sources.
- [ ] Add pure-rule assertions for queue capacity, school master slots, and royal lineage
  candidate priority; verify the harness fails because those production types do not exist.

### Task 2: Restore vanilla meta-window binding and tab skins

**Files:**
- Modify: `Code/patch/AW_WorldLogGuardPatch.cs`
- Modify: `Code/patch/AW_KingdomWindowPatch.cs`
- Delete: `Code/core/lineage/MetaWindowSafetyRules.cs`
- Modify: `Code/ui/AW_LineageTab.cs`

- [ ] Leave only the `WorldLog.logNewKing` Prefix in its patch class.
- [ ] Delete the Kingdom name Postfix while retaining unrelated title/heir UI patches.
- [ ] Delete `ApplyNativeTabSprites()` and its call.
- [ ] Run the source guards; verify these four guards pass while later task guards still fail.

### Task 3: Enforce creation-time Xia names and royal clan identity

**Files:**
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/content/XiaNaming.cs`
- Modify: `Code/content/XiaNameRepairRules.cs`
- Modify: `Code/patch/AW_XiaNamingPatch.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Create: `Code/core/lineage/RoyalLineageResolutionRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs`

- [ ] Test priority `self -> father -> current royal -> full sibling -> create` and invalid
  placeholder rejection.
- [ ] Remove both lifecycle `EnsureWorldNames()` calls; retain targeted full-Xiaization rename.
- [ ] Make the clan parameter getter refuse anonymous placeholders and provide a deterministic
  valid shi before clan creation.
- [ ] Resolve and persist king branch identity before `newClan()`, then let the normal Clan
  creation callback assign its first final name.
- [ ] Run pure rules and source guards.

### Task 4: Register dedicated school tasks and localization

**Files:**
- Modify: `Code/content/schools/HistoricalSchoolContent.cs`
- Create: `Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs`
- Create: `Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs`
- Modify: `Locales/others.csv`

- [ ] Register lecture, debate-travel, debate, and debate-receiving task assets with explicit
  locale keys, icons, movement/wait/finalization behaviours, and reproduction/socialization
  cancellation disabled.
- [ ] Add all four task labels in Simplified Chinese, English, and Traditional Chinese.
- [ ] Keep the scholar job's ordinary travel/wander tasks; activity requests force the
  dedicated task only while active.
- [ ] Run localization/source guards and build Debug.

### Task 5: Add deterministic venues and bounded activity queue

**Files:**
- Create: `Code/core/schools/HistoricalSchoolVenueRules.cs`
- Create: `Code/core/schools/HistoricalSchoolVenueService.cs`
- Create: `Code/core/schools/HistoricalSchoolActivityQueueRules.cs`
- Create: `Code/core/schools/HistoricalSchoolActivityQueue.cs`
- Modify: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs`

- [ ] Test stable distinct venue selection, release, maximum eight pending lectures, one
  transition per frame, duplicate operation rejection, and cancellation cleanup.
- [ ] Freeze annual lecture/debate requests without DB writes or `City.units` scans.
- [ ] Drain at most one request transition from `HistoricalSchoolRuntime.ProcessFrame()` and
  enforce a stopwatch time budget.
- [ ] Clear venue and queue runtime state during load/new-map reset.
- [ ] Run pure rules and Debug build.

### Task 6: Move lecture and debate settlement behind actor-task completion

**Files:**
- Modify: `Code/core/schools/HistoricalSchoolActionService.cs`
- Modify: `Code/core/schools/HistoricalSchoolDebateService.cs`
- Modify: `Code/core/schools/HistoricalSchoolAnnualMemberSnapshot.cs`
- Modify: `Code/core/schools/HistoricalSchoolAnnualMemberSnapshotBuilder.cs`
- Modify: `Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs`
- Modify: `Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs`

- [ ] Cache bounded recruitment candidate IDs in the annual snapshot.
- [ ] Replace annual `RecordTeaching`/`CandidateResidents` execution with immutable queue
  requests; recruit only frozen valid IDs after physical lecture completion.
- [ ] Replace immediate annual debate commits with paired actor requests; retain the existing
  atomic debate/ledger transaction as the completion commit.
- [ ] Release tasks and venues on success, death, city change, invalid membership, or failure.
- [ ] Run source guards, pure rules, and Debug build.

### Task 7: Enforce one active canonical master per school

**Files:**
- Create: `Code/core/schools/HistoricalSchoolActiveMasterSlots.cs`
- Modify: `Code/core/schools/HistoricalSchoolRules.cs`
- Modify: `Code/core/schools/HistoricalSchoolDescentService.cs`
- Modify: `Code/core/schools/SchoolMembershipService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs`

- [ ] Test pending reservation, activation, duplicate-school rejection, clean-failure release,
  committed-death release, and deterministic load reconstruction.
- [ ] Reserve by `SchoolId` before actor creation and activate only after descent commit.
- [ ] Release only after the matching committed death; stale actor callbacks cannot release a
  replacement slot.
- [ ] Rebuild from persisted master rows plus targeted actor IDs, not an annual world scan.
- [ ] Run all rule tests and Debug build.

### Task 8: Full verification

**Files:**
- Modify only files needed to correct verification failures.

- [ ] Run `pwsh -File Tests/SourceGuardTests.ps1` and require all assertions to pass.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests` and require all assertions to pass.
- [ ] Run `dotnet build AncientWarfare3.csproj -c Debug --no-restore -p:AutomaticallyUseReferenceAssemblyPackages=true`.
- [ ] Run `dotnet build AncientWarfare3.csproj -c Release --no-restore -p:AutomaticallyUseReferenceAssemblyPackages=true`.
- [ ] Run `git diff --check` and inspect `git status --short` for only intentional changes.
- [ ] Report any live-game checks that still require the user's running WorldBox session; do
  not claim those symptoms verified from compilation alone.
