# AW3 Defender-Defeat Capture Balance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Require evidence of real attacker/defender combat before AW3 immediately transfers a city whose defenders disappear.

**Architecture:** Extend the existing per-city military-presence cache with a bounded set of engaged attacker IDs tied to the current owner. Pure rules decide whether evidence is sufficient and whether it remains valid; Harmony supplies engine state and lifecycle events.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, the existing net9 pure-rule executable, PowerShell source guards.

---

### Task 1: Define engagement evidence in pure rules

**Files:**
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
- Modify: Code/core/lineage/CityOccupationAccelerationRules.cs

- [ ] **Step 1: Write failing empty-city and defeated-defender tests**

Add a required defenderEngagementObserved argument to every completion-rule call. Add these wished-for assertions:

    True(CityOccupationAccelerationRules.ShouldLatchDefenderEngagement(
        ownerWarriorPresent: true,
        attackerWarriorPresent: true,
        attackerIsEnemy: true),
        "matched hostile warriors prove a real city battle");
    Equal(false, CityOccupationAccelerationRules.ShouldLatchDefenderEngagement(
        true, false, true),
        "an empty city cannot manufacture defender engagement");
    Equal(false, CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
        true, true, false, false, false, false,
        defenderEngagementObserved: false),
        "an initially empty city uses capture progress");
    True(CityOccupationAccelerationRules.ShouldCompleteAfterDefenderDefeat(
        true, true, false, false, false, false,
        defenderEngagementObserved: true),
        "a defeated real garrison completes occupation");

- [ ] **Step 2: Run the rule executable and confirm RED**

Run:

    dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj

Expected: compilation fails because the latch rule and completion argument do not exist.

- [ ] **Step 3: Implement the minimal pure rules**

Add:

    public static bool ShouldLatchDefenderEngagement(
        bool ownerWarriorPresent,
        bool attackerWarriorPresent,
        bool attackerIsEnemy)
    {
        return ownerWarriorPresent && attackerWarriorPresent && attackerIsEnemy;
    }

Extend ShouldCompleteAfterDefenderDefeat with the required boolean and include it in the returned conjunction.

- [ ] **Step 4: Run the rule executable and confirm GREEN**

Expected: Rule tests passed.

- [ ] **Step 5: Commit the rule contract**

    git add -- Code/core/lineage/CityOccupationAccelerationRules.cs Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
    git commit -m "test: require real combat for instant capture"

### Task 2: Track exact city-owner-attacker engagements

**Files:**
- Modify: Code/core/lineage/CityOccupationAccelerationService.cs
- Modify: Code/core/lineage/CityOccupationAccelerationRules.cs
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt

- [ ] **Step 1: Write failing lifecycle tests**

    True(CityOccupationAccelerationRules.ShouldRetainDefenderEngagement(
        ownerMatches: true, attackerMatches: true,
        attackerStillEnemy: true, attackerPresentInCompletedCycle: true),
        "a continuing assault retains its combat evidence");
    Equal(false, CityOccupationAccelerationRules.ShouldRetainDefenderEngagement(
        true, false, true, true),
        "a replacement attacker cannot inherit combat evidence");
    Equal(false, CityOccupationAccelerationRules.ShouldRetainDefenderEngagement(
        true, true, true, false),
        "withdrawal clears combat evidence");

- [ ] **Step 2: Run tests and confirm RED**

Expected: compilation fails on ShouldRetainDefenderEngagement.

- [ ] **Step 3: Add the lifecycle rule and runtime state**

Add the tested pure conjunction. Add a bounded EngagementByCity dictionary whose value stores OwnerKingdomId and a HashSet of attacker IDs. After inserting each living warrior into ActiveMilitaryKingdomsByCity, latch every hostile non-owner warrior when the owner warrior is also present. Before clearing a completed presence cycle, remove engaged attackers absent from that completed set.

Add:

    private static bool HasDefenderEngagement(
        City pCity,
        Kingdom pOwner,
        Kingdom pAttacker)

Pass its exact result into the completion rule. Remove the city entry after successful transfer or owner mismatch.

- [ ] **Step 4: Run tests and Debug build**

    dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
    dotnet build AncientWarfare3.csproj -c Debug

Expected: tests pass and build has 0 errors.

- [ ] **Step 5: Commit runtime tracking**

    git add -- Code/core/lineage/CityOccupationAccelerationRules.cs Code/core/lineage/CityOccupationAccelerationService.cs Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
    git commit -m "fix: require defender engagement before instant capture"

### Task 3: Invalidate evidence at engine boundaries

**Files:**
- Modify: Code/core/lineage/CityOccupationAccelerationService.cs
- Modify: Code/patch/AW_CityOccupationAccelerationPatch.cs
- Modify: Code/patch/AW_WarPatch.cs
- Modify: Code/patch/AW_SavePatch.cs
- Modify: Tests/SourceGuardTests.ps1

- [ ] **Step 1: Add failing source guards**

Require HasDefenderEngagement, OnWarEnded, and ClearRuntime in the service; require the war-end and archive-switch calls; require the completion call to pass defenderEngagementObserved.

- [ ] **Step 2: Run source guards and confirm RED**

    powershell -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1

Expected: the new lifecycle guards fail.

- [ ] **Step 3: Implement lifecycle hooks**

OnWarEnded removes only owner/attacker pairs that were on opposite sides of that war. ClearRuntime clears presence, engagement, and goal caches. Call them from AW_WarPatch.EndWar_Postfix and AW_SavePatch.ResetHistoryWindowsAfterArchiveSwitch. Keep clearCurrentCaptureAmounts as the completed-cycle boundary.

- [ ] **Step 4: Run source guards and full builds**

    powershell -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1
    dotnet build AncientWarfare3.csproj -c Debug
    dotnet build AncientWarfare3.csproj -c Release

Expected: guards pass and both builds have 0 errors.

- [ ] **Step 5: Commit lifecycle integration**

    git add -- Code/core/lineage/CityOccupationAccelerationService.cs Code/patch/AW_CityOccupationAccelerationPatch.cs Code/patch/AW_WarPatch.cs Code/patch/AW_SavePatch.cs Tests/SourceGuardTests.ps1
    git commit -m "fix: clear stale city combat evidence"

### Task 4: Deploy and runtime-verify

**Files:**
- Deploy: bin/Debug/net48/AncientWarfare3.dll
- Deploy: bin/Release/net48/AncientWarfare3.dll

- [ ] **Step 1: Run all automated verification fresh**

Run the rule executable, source guards, Debug build, and Release build.

- [ ] **Step 2: Deploy without replacing the runtime lineage archive**

Copy the mod to D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0 while preserving .runtime/aw3_lineage_archive.db.

- [ ] **Step 3: Verify deployed DLL hashes**

Compare SHA-256 hashes for source and deployed Debug/Release DLLs. Expected: each pair matches.

- [ ] **Step 4: Runtime acceptance**

Confirm an empty border city no longer changes owner immediately, a genuinely defended city changes owner when its last defending warrior is defeated, and Player.log contains no new AW3 exception.

