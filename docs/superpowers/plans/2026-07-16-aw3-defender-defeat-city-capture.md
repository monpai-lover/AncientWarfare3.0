# AW3 Defender Defeat City Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transfer a city immediately after its active military defenders are defeated while preserving contested-city safety.

**Architecture:** Add one pure completion decision to the existing occupation rules and evaluate it in the existing `City.updateCapture` prefix. Reuse vanilla `finishCapture` and skip the rest of that update only when ownership actually changes.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox `City` APIs, .NET 9 rule executable, PowerShell source guards.

---

### Task 1: Define Immediate Completion

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Code/core/lineage/CityOccupationAccelerationRules.cs`

- [ ] Add failing tests for the complete condition and each rejected edge:

```csharp
True(CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
    true, true, false, false, false, false),
    "uncontested enemy control completes occupation immediately");
Equal(false, CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
    true, true, true, false, false, false), "active defenders block transfer");
Equal(false, CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
    true, true, false, true, false, false), "hostile rival blocks transfer");
Equal(false, CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
    false, true, false, false, false, false), "non-enemy cannot capture");
Equal(false, CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
    true, false, false, false, false, false), "absent attackers cannot capture");
Equal(false, CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
    true, true, false, false, true, false), "changed ownership is not repeated");
Equal(false, CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
    true, true, false, false, false, true), "manager lock defers transfer");
```

- [ ] Run the rule executable and require a missing-method failure.
- [ ] Add the six-boolean pure rule and re-run to `Rule tests passed.`.

### Task 2: Complete Through Vanilla City Transfer

**Files:**
- Modify: `Code/core/lineage/CityOccupationAccelerationService.cs`
- Modify: `Code/patch/AW_CityOccupationAccelerationPatch.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add source guards requiring `TryCompleteAfterDefenderDefeat` and a boolean `UpdateCapture_Prefix`; run guards and require failure.
- [ ] Resolve the dominant capturer, active capture presence, active defenders, hostile rivals, ownership state, and `World.world.cities.isLocked()`.
- [ ] If the pure rule accepts, snapshot the old owner, call `pCity.finishCapture(capturer)`, and return whether `pCity.kingdom != oldOwner`.
- [ ] Change the Harmony prefix to return `false` only after the service reports a successful transfer; otherwise run current acceleration and vanilla capture logic.
- [ ] Run rule tests, source guards, Debug build, and Release build.
- [ ] Commit as `fix: complete city capture after defender defeat`.

### Task 3: Runtime Verification

**Files:**
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] Deploy while preserving `.runtime/aw3_lineage_archive.db` and compare assembly hashes.
- [ ] Verify one attacker captures immediately after the last defender leaves or dies.
- [ ] Verify two hostile attackers keep the city contested.
- [ ] Confirm civilians are no longer attacked after transfer and inspect `Player.log` for new exceptions.
