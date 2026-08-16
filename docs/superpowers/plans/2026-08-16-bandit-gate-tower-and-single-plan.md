# Bandit Gate Tower And Single Plan Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Place four gate towers inside valid 2 by 2 strongholds and eliminate the post-kingdom second planning pass.

**Architecture:** A pure rule ranks gate-center-to-interior candidate points. Runtime filters those points by selected-zone footprint and the exact native cityless placement check, while a shared planned-commit method consumes the preflight plan without recomputing it.

**Tech Stack:** C# 9, WorldBox native building APIs, net9 rule tests, PowerShell source guards, net48 production build.

---

### Task 1: Inward Tower Candidates

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditZoneWallRulesTests.cs.txt`
- Modify: `Code/core/lineage/PeasantRebelBanditZoneWallRules.cs`

- [ ] Add a failing test asserting that north, east, south, and west gates produce gate-first candidates followed by points moving toward the center.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --bandit-stronghold` and confirm the missing `RankInwardTowerCandidates` failure.
- [ ] Implement `RankInwardTowerCandidates(gate, center, 6)` using the cardinal sign from gate to center.
- [ ] Re-run the focused test and require a pass.

### Task 2: Runtime Tower Preflight

**Files:**
- Modify: `Tests/BanditStrongholdHistoryAndTowerSourceGuard.ps1`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Locales/others.csv`

- [ ] Add failing source assertions for `RankInwardTowerCandidates`, complete-footprint selected-zone containment, `canBuildFrom(tile, asset, null, BuildPlacingType.Load)`, four distinct tiles, and `aw_bandit_stronghold_tower_failed`.
- [ ] Run the tower guard and confirm it fails on the current gate-center-only code.
- [ ] Scan inward candidates per gate, accept the first native-valid footprint, and keep wall versus tower failure keys distinct.
- [ ] Run the tower guard, focused rules, and localization validation.

### Task 3: Single Authoritative Plan

**Files:**
- Modify: `Tests/BanditStrongholdTransactionSourceGuard.ps1`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`

- [ ] Add a failing source assertion that the `TryCreateDirect` body contains one `TryPlan`, ordered before `makeNewCivKingdom`, and calls `TryCreatePlanned` instead of `TryCreate`.
- [ ] Run the transaction guard and confirm the current second-plan path fails.
- [ ] Extract `TryCreatePlanned`; make ordinary `TryCreate` plan once and direct creation update the preflight plan context before committing it.
- [ ] Re-run transaction, tower, wall, and history guards.

### Task 4: Verify And Deploy

**Files:**
- Verify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] Run the complete rule suite and all bandit stronghold guards.
- [ ] Run `dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore` and require zero errors and warnings.
- [ ] Run `git diff --check`, deploy through `deploy-local.ps1`, and verify all production SHA-256 hashes with `Tests/VerifySourceDeployment.ps1`.
- [ ] Restart WorldBox visibly and inspect the new log session for compilation, stronghold planning, or null-reference errors.
