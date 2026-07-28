# RTS Commander Escort Follow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace continuous exact Army formation enforcement with low-cost commander escort follow that releases soldiers in combat and resumes after combat.

**Architecture:** Keep the commander as the only strategic path owner. Add pure escort-state rules, use deterministic loose offsets around the commander for local follow, retain shared routes only for distant reconnect and transport, and remove full formation observation from the RTS controller hot path.

**Tech Stack:** C#/.NET 4.8 mod source, Harmony, WorldBox actor AI, .NET 9 rule-test executables, PowerShell source guards.

---

### Task 1: Lock Escort Ownership And Readiness Rules

**Files:**
- Modify: `Tests/ArmyFormationMissionGateTests.cs.txt`
- Modify: `Code/core/lineage/ArmyFormationRules.cs`

- [ ] Add failing assertions that Rally, March, Deploy, Retreat, and Regroup use escort follow; Assault/Pursue and immediate combat release it.
- [ ] Add failing assertions that operational strength, not exact slot deployment, permits departure and assault.
- [ ] Run `dotnet run --project Tests/ArmyFormationMissionGateTests.csproj -c Debug` and verify the new assertions fail for the missing rules.
- [ ] Add `ShouldOwnEscortFollow(ArmyRtsState, bool)` and proximity/readiness helpers with no runtime dependencies.
- [ ] Re-run the test and expect `ArmyFormationMissionGateTests: PASS`.

### Task 2: Replace Exact Slot Follow With Loose Commander Escort

**Files:**
- Modify: `Code/core/lineage/ArmyFormationService.cs`
- Modify: `Code/core/lineage/AWArmyMarchService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Modify: `Code/patch/AW_ArmySafetyPatch.cs`
- Test: `Tests/ArmySharedCaptainPathSourceGuardTests.ps1`

- [ ] Add a failing source guard requiring combat release and forbidding `ObserveArmy` inside `ArmyRtsControllerService.BuildFacts`.
- [ ] Run the source guard and verify it fails at the controller hot-path check.
- [ ] Make loose follow targets deterministic from Army ID, actor ID, commander tile, and coarse phase; search only a bounded radius and hold actors already near the commander.
- [ ] Let distant soldiers reconnect through the commander shared route, then use local direct correction only inside the loose radius.
- [ ] Route follower interception through escort ownership; return to vanilla combat movement during Assault/Pursue or immediate combat.
- [ ] Remove formation observation and exact deployment counters from `BuildFacts`; publish readiness from strength and route arrival.
- [ ] Run the source guard and `ArmyFormationMissionGateTests` and expect both to pass.

### Task 3: Preserve Watchdog, Transport, And Captain Continuity

**Files:**
- Modify: `Code/core/lineage/ArmyStallWatchdogService.cs`
- Modify: `Code/core/lineage/ArmyRtsControllerService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Add failing timeline assertions that strategic stalls observe the commander, transport suspends escort ownership, and post-combat follow recovery does not reset the mission.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Debug -- --rts-command-slice` and verify the new assertion fails.
- [ ] Update watchdog sampling to inspect a distant follower only in escort recovery states; keep captain sampling for strategic route failure.
- [ ] Preserve transport ownership and living-captain lease checks unchanged.
- [ ] Re-run RTS command, transport, shared-path, and captain source guards.

### Task 4: Verify Performance And Behavior

**Files:**
- Create: `Tests/ArmyRtsControllerPerformanceSourceGuard.ps1`

- [ ] Add a source guard rejecting per-controller full-roster formation scans, stable-slot rebuilds, and exact deployment geometry in `BuildFacts`.
- [ ] Run Debug and Release rule slices and the RTS deterministic simulation suite.
- [ ] Build `AncientWarfare3.csproj` in Debug and Release with zero errors.
- [ ] Deploy source only after WorldBox closes.
- [ ] Run the same save and record FPS, `aw3_army_rts_controller`, commander movement, follower movement, combat release, post-combat regroup, and next-city handoff.
