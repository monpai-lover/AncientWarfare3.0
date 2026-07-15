# AW3 School Academy Building Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Register the supplied academy art as a real one-per-city Xia building and require lectures and debates to enter that building.

**Architecture:** Clone the existing Xia library and reuse `order_library` for normal AI construction. Extend venue claims with a concrete academy building, route academic activity only through an indexed academy source, and use vanilla building-target behaviors with exact terminal cleanup.

**Tech Stack:** C# 11, .NET Framework 4.8, WorldBox publicized API, NeoModLoader resources/locales, PowerShell source guards, .NET 9 pure rule harness.

---

### Task 1: Academy Routing Rules

**Files:**
- Modify: `Code/core/schools/HistoricalSchoolVenueRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs`

- [ ] Add failing rules for lecture/debate requiring an academy, public/local
  fallback remaining legal for idle/travel, same-building debate validity, and
  academy lifecycle requirements.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
  and verify the new assertions fail because the academy APIs do not exist.
- [ ] Add the minimal pure enums and predicates to satisfy the assertions.
- [ ] Rerun the rule harness and verify it passes.

### Task 2: Building Registration and Art

**Files:**
- Create: `Code/content/schools/SchoolAcademyBuildingContent.cs`
- Modify: `Code/content/XiaArchitecture.cs`
- Create: `GameResources/buildings/civ_main/Xia/academy_Xia/construction_0.png`
- Create: `GameResources/buildings/civ_main/Xia/academy_Xia/main_0.png`
- Create: `GameResources/buildings/civ_main/Xia/academy_Xia/mini_0.png`
- Create: `GameResources/buildings/civ_main/Xia/academy_Xia/ruin_0.png`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add failing source guards for `academy_Xia`, its unique type, post-generation
  `order_library` mapping, inherited book slots, `0.07975` X/Y scale with `0.25`
  Z scale, footprint, and four art
  states.
- [ ] Run `Tests/SourceGuardTests.ps1` and verify the academy guards fail.
- [ ] Clone `library_Xia`, assign `type_aw_school_academy`,
  `new Vector3(0.07975f, 0.07975f, 0.25f)` scale,
  `BuildingFundament(3, 3, 2, 0)`, and bind `order_library` to it.
- [ ] Copy the four supplied images without modifying their pixels.
- [ ] Rerun source guards and verify the building slice passes.

### Task 3: Indexed Academy Venue Source

**Files:**
- Create: `Code/core/schools/HistoricalSchoolAcademyService.cs`
- Modify: `Code/core/schools/HistoricalSchoolVenueProvider.cs`
- Modify: `Code/core/schools/HistoricalSchoolVenueService.cs`
- Modify: `Code/content/schools/HistoricalSchoolContent.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add failing guards proving academy lookup uses
  `City.getBuildingOfType(type_aw_school_academy)` and lecture/debate do not
  fall back to public or local tiles.
- [ ] Run source guards and verify those checks fail.
- [ ] Inject the academy source during school content initialization.
- [ ] Carry `Building Academy` in venue selections and claims. For debates,
  accept `Primary == Secondary` only when both refer to one valid academy.
- [ ] Reserve the academy main tile once so lectures and debates are mutually
  exclusive per city.
- [ ] Rerun pure rules and source guards.

### Task 4: Lecture Building Task and Cleanup

**Files:**
- Modify: `Code/ai/behaviours/actor/BehHistoricalSchoolLecture.cs`
- Modify: `Code/content/schools/HistoricalSchoolContent.cs`
- Modify: `Code/core/schools/HistoricalSchoolActivityQueue.cs`
- Modify: `Code/core/schools/HistoricalSchoolTaskLeaseService.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add failing guards requiring lecture preparation to set a building target,
  `BehGoToBuildingTarget`, `BehStayInBuildingTarget`, actual inside-building
  completion, and terminal `exitBuilding()` cleanup.
- [ ] Run source guards and verify the lecture guards fail.
- [ ] Replace lecture tile movement with vanilla building movement and stay.
- [ ] Validate the claimed academy every bounded validation turn and exit the
  exact academy on success, interruption, expiry, city change, or clear.
- [ ] Rerun rule tests and source guards.

### Task 5: Debate Building Task and Cleanup

**Files:**
- Modify: `Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs`
- Modify: `Code/content/schools/HistoricalSchoolContent.cs`
- Modify: `Code/core/schools/HistoricalSchoolDebateActivityService.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add failing guards requiring both debaters to share the claimed academy,
  enter it, remain inside during their visible task, and exit on every terminal
  path.
- [ ] Run source guards and verify the debate guards fail.
- [ ] Change both travel/receiving tasks to building-target movement. Enter the
  first actor before switching to the debate task and keep the second actor
  inside during the receiving wait.
- [ ] Require both actors to be inside the same valid academy before queueing
  persistence and exit both actors in `Finish` and runtime clear.
- [ ] Rerun rules and source guards.

### Task 6: Localization and Full Verification

**Files:**
- Modify: `Locales/others.csv`

- [ ] Add simplified Chinese, English, and traditional Chinese academy labels
  and activity wording where a new key is needed.
- [ ] Run the rule harness and `Tests/SourceGuardTests.ps1`.
- [ ] Run Debug and Release builds with `--no-restore`.
- [ ] Run `git diff --check` and inspect the complete diff without reverting
  unrelated dirty-tree work.

### Task 7: Deployment and Fresh-World Runtime Check

**Files:**
- Deploy changed mod files to the installed Ancient Warfare 3 mod directory.

- [ ] Synchronize the repository mod payload to
  `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`.
- [ ] Start WorldBox and verify AW3 loads without an exception.
- [ ] On a fresh map, verify the AI constructs at most one academy in an eligible
  Xia city, the four building states render, and lecture/debate actors enter and
  leave the academy.
- [ ] Destroy an active academy and verify both leases and inside-building state
  clear without a city-center fallback.
- [ ] Inspect `Player.log`, stop the game process, and report any live check that
  could not be completed automatically.

### Task 8: Early Academy Construction Event

**Files:**
- Create: `Code/core/schools/SchoolAcademyConstructionRules.cs`
- Create: `Code/core/schools/HistoricalSchoolAcademyConstructionService.cs`
- Modify: `Code/core/schools/HistoricalSchoolDescentService.cs`
- Modify: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/SourceGuardTests.ps1`

- [x] Add failing construction eligibility and rotated-zone-window rule tests.
- [x] Add failing source guards for committed-descent hookup, duplicate checks,
  bounded placement, original footprint validation, unfinished-site creation,
  and runtime cleanup.
- [x] Start one free construction site after a committed descent while retaining
  vanilla `order_library` for later rebuilding.
- [x] Reject overlap through `BuildingManager.canBuildFrom`, inspect no more than
  24 zones and 8 sampled tiles per zone, and rotate later attempts.
- [x] Remove temporary academy registration and city diagnostics.
- [ ] Verify a fresh world creates and completes the first academy after a
  historical master descends.
