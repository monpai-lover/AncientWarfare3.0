# War Force Elimination Settlement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** End every war when an entire side has no active warriors and no available AW3 reserves, using full surrender for one-sided exhaustion and score-maximized peace for mutual exhaustion.

**Architecture:** Vanilla `War` aggregation remains authoritative for active warriors; AW3 adds only participant reserve pools. A pure rule confirms zero potential on two distinct months, while a bounded monthly runtime service routes the decision to ordinary, Zhulu, or rebellion settlement adapters.

**Tech Stack:** C# 9, WorldBox runtime APIs, NCMS source mod, AW3 deferred authority work, .NET 9 rules tests.

---

## File Map

- Create `Code/core/lineage/WarForceEliminationRules.cs`: pure streak and outcome rules.
- Create `Code/core/lineage/WarForceEliminationSettlementService.cs`: bounded monthly observation and dispatch.
- Create `Code/core/lineage/WarForceSpecialSettlementService.cs`: Zhulu/rebellion routing boundary.
- Modify `Code/core/lineage/WartimeMilitaryPotentialService.cs`: replace Army-index scans with vanilla totals.
- Modify `Code/core/lineage/WarScoreRuntimeBridge.cs`: give force elimination first settlement priority.
- Modify `Code/core/performance/AWAuthorityCycleService.cs`: run and reset monthly observations.
- Modify `Code/core/lineage/WarPeaceSettlementService.cs`: execute ordinary forced settlements.
- Modify `Code/core/lineage/ZhuluWarSettlementService.cs`: execute full or score-limited Zhulu outcomes.
- Modify `Code/core/lineage/RebellionCollapseSettlementService.cs`: support exhaustion of either side.
- Modify `Code/core/lineage/DiplomacyProposalService.cs`: clear runtime observation state.
- Create `Tests/AncientWarfare3.Rules.Tests/WarForceEliminationRulesTests.cs.txt` and register it in the test project and runner.

### Task 1: Define Force-Elimination Decisions

**Files:**
- Create: `Code/core/lineage/WarForceEliminationRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/WarForceEliminationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing tests for the public rule contract**

```csharp
Equal(1, WarForceEliminationRules.NextZeroStreak(0, 0));
Equal(2, WarForceEliminationRules.NextZeroStreak(0, 1));
Equal(0, WarForceEliminationRules.NextZeroStreak(1, 2));
Equal(WarForceEliminationDecisionKind.None,
    WarForceEliminationRules.Resolve(0, 0, 1, 1, 40).Kind);
Equal(WarForceEliminationDecisionKind.DefendersSurrender,
    WarForceEliminationRules.Resolve(3, 0, 0, 2, -20).Kind);
Equal(WarForceEliminationDecisionKind.AttackersSurrender,
    WarForceEliminationRules.Resolve(0, 4, 2, 0, 20).Kind);
Equal(WarScoreSide.Attackers,
    WarForceEliminationRules.Resolve(0, 0, 2, 2, 35).Beneficiary);
Equal(WarScoreSide.Defenders,
    WarForceEliminationRules.Resolve(0, 0, 2, 2, -35).Beneficiary);
Equal(WarForceEliminationDecisionKind.WhitePeace,
    WarForceEliminationRules.Resolve(0, 0, 2, 2, 0).Kind);
Equal(int.MaxValue,
    WarForceEliminationRules.AddPotential(int.MaxValue, 10));
```

Register both production and test files in the `.csproj`, then add `WarForceEliminationRulesTests.Run();` beside the existing war settlement suites in `Program.cs.txt`.

- [ ] **Step 2: Run tests and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: compilation fails because the new rule and decision types do not exist.

- [ ] **Step 3: Implement the minimal pure rule**

Create `WarForceEliminationDecisionKind` with `None`, `AttackersSurrender`, `DefendersSurrender`, `ScoreSettlement`, and `WhitePeace`. Create an immutable `WarForceEliminationDecision` containing `Kind`, `Beneficiary`, and a score clamped to `-100..100`.

`NextZeroStreak` increments only when potential is exactly zero, caps at `2`, and otherwise resets. `Resolve` requires streak `2`; one exhausted side surrenders regardless of score, while mutual exhaustion selects the score-sign beneficiary or white peace at zero. `AddPotential` treats negative inputs as zero and clamps overflow to `int.MaxValue`.

- [ ] **Step 4: Run tests and verify GREEN**

Run the same command. Expected: the executable exits `0` with the normal all-tests-passed output.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/WarForceEliminationRules.cs Tests/AncientWarfare3.Rules.Tests/WarForceEliminationRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: define exhausted war settlement rules"
```

### Task 2: Reuse Vanilla Military Statistics

**Files:**
- Modify: `Code/core/lineage/WartimeMilitaryPotentialService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarForceEliminationRulesTests.cs.txt`

- [ ] **Step 1: Add a failing source-boundary test**

```csharp
True(source.Contains("pKingdom.countTotalWarriors()"),
    "kingdom potential reuses vanilla warrior aggregation");
False(source.Contains("ArmyFieldIndexService.CreateSnapshotCursor"),
    "kingdom potential does not rescan Army indexes");
True(source.Contains("CityReservePoolService.CountAvailable"),
    "AW3 reserves remain additive");
```

- [ ] **Step 2: Run the rules executable and verify RED**

Expected: the source-boundary assertion reports the old Army-index scan.

- [ ] **Step 3: Replace the duplicate scan**

```csharp
public static int CountPotentialWarriors(Kingdom pKingdom)
{
    if (pKingdom?.data == null || pKingdom.isRekt()) return 0;
    int active = 0;
    try { active = Math.Max(0, pKingdom.countTotalWarriors()); }
    catch { }
    return WarForceEliminationRules.AddPotential(active,
        CityReservePoolService.CountAvailable(pKingdom));
}
```

Delete the private Army-index scan and duplicate overflow helper. Preserve `CountPotentialWarriorsBounded`, `ClearRuntime`, and `RemoveKingdom` for caller compatibility.

- [ ] **Step 4: Verify tests and production build**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release
```

Expected: both exit `0`; production reports `0 errors`.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/WartimeMilitaryPotentialService.cs Tests/AncientWarfare3.Rules.Tests/WarForceEliminationRulesTests.cs.txt
git commit -m "perf: reuse vanilla military totals"
```

### Task 3: Add Bounded Monthly Side Observation

**Files:**
- Create: `Code/core/lineage/WarForceEliminationSettlementService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/core/lineage/WarScoreRuntimeBridge.cs`
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarForceEliminationRulesTests.cs.txt`

- [ ] **Step 1: Add failing month-gate and wiring tests**

Add a pure `WarForceObservationState.Observe(monthKey, attackerPotential, defenderPotential)` test proving duplicate observations in one month do not advance streaks. Add source assertions for `WarForceEliminationSettlementService.ProcessAuthorityCycle`, `ClearRuntime`, and this first line in `QueueSettlementChecks`:

```csharp
if (WarForceEliminationSettlementService.QueueIfReady(pWar)) return;
```

- [ ] **Step 2: Run tests and verify RED**

Expected: observation state and runtime wiring are absent.

- [ ] **Step 3: Implement bounded monthly observation**

Use `MonthlyAuthorityWorkQueue<long>` and a dictionary keyed by war ID. On each new `year/month` key, snapshot live war IDs once and drain at most two per authority cycle. Calculate:

```csharp
int attackers = WarForceEliminationRules.AddPotential(
    Math.Max(0, war.countAttackersWarriors()),
    CountSideReserves(war.getAttackers()));
int defenders = WarForceEliminationRules.AddPotential(
    Math.Max(0, war.countDefendersWarriors()),
    CountSideReserves(war.getDefenders()));
```

`CountSideReserves` iterates only current participant kingdoms and sums `CityReservePoolService.CountAvailable`. `QueueIfReady` observes no more than once per war per month, reads signed score through `WarScoreService.TryGetSnapshot`, and enqueues one coalesced job only for a non-`None` decision.

Fail closed for ended wars, unreadable warrior totals, missing main participants,
loading/reset state, paused authority execution, and multiplayer replicas. Remove
streak records for ended or missing wars; `ClearRuntime` drops both the monthly
queue and every streak record so save changes cannot inherit confirmation.

Call `ProcessAuthorityCycle` under the diplomacy benchmark. Call `ClearRuntime` from `AWAuthorityCycleService.Reset` and `DiplomacyProposalService.ClearRuntime`. Run force elimination before decisive score, war goals, exhaustion, and rebellion collapse in `QueueSettlementChecks`.

- [ ] **Step 4: Verify tests and build**

Run the two Task 2 verification commands. Expected: both exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/WarForceEliminationRules.cs Code/core/lineage/WarForceEliminationSettlementService.cs Code/core/performance/AWAuthorityCycleService.cs Code/core/lineage/WarScoreRuntimeBridge.cs Code/core/lineage/DiplomacyProposalService.cs Tests/AncientWarfare3.Rules.Tests/WarForceEliminationRulesTests.cs.txt
git commit -m "feat: observe exhausted war sides monthly"
```

### Task 4: Execute Ordinary-War Outcomes

**Files:**
- Modify: `Code/core/lineage/WarPeaceSettlementService.cs`
- Modify: `Code/core/lineage/WarForceEliminationSettlementService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarForceEliminationRulesTests.cs.txt`

- [ ] **Step 1: Add failing offer-mode and entry-point tests**

Test that surrender decisions map to `WarPeaceDefaultOfferMode.Surrender`, mutual non-zero decisions map to `ExhaustionMaximumBenefit`, and zero maps to `WhitePeace`. Assert the service source contains `ForceMilitaryEliminationSettlement`.

- [ ] **Step 2: Run tests and verify RED**

Expected: `OfferMode` and `ForceMilitaryEliminationSettlement` are missing.

- [ ] **Step 3: Add the authoritative ordinary entry point**

`ForceMilitaryEliminationSettlement(draft, decision)` must reject replicas/null drafts, reload the live war, recount both side potentials, and reject if the relevant side is no longer exhausted. It builds one-sided surrender at effective score `100`; mutual exhaustion uses current signed score and `ExhaustionMaximumBenefit`; exact zero builds white peace. It calls `Prepare` and `AcceptAndExecuteOrResume` without requiring exhaustion `100`, while retaining term-cost and live-score validation.

The runtime resolves requester/responder from `decision.Beneficiary`, sets `PlayerInitiated = false`, retries at most twice through coalesced deferred work, and logs one terminal warning.

- [ ] **Step 4: Verify tests and build**

Run the two Task 2 verification commands. Expected: both exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/WarForceEliminationRules.cs Code/core/lineage/WarForceEliminationSettlementService.cs Code/core/lineage/WarPeaceSettlementService.cs Tests/AncientWarfare3.Rules.Tests/WarForceEliminationRulesTests.cs.txt
git commit -m "feat: settle ordinary wars after force exhaustion"
```

### Task 5: Adapt Zhulu And Rebellion Wars

**Files:**
- Create: `Code/core/lineage/WarForceSpecialSettlementService.cs`
- Modify: `Code/core/lineage/ZhuluWarSettlementService.cs`
- Modify: `Code/core/lineage/RebellionCollapseSettlementService.cs`
- Modify: `Code/core/lineage/WarForceEliminationSettlementService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/RebellionForceCollapseRulesTests.cs.txt`

- [ ] **Step 1: Add failing special-route tests**

Test `SpecialKind(isZhulu, isRebellion)` for Zhulu, rebellion, and ordinary wars. Add source assertions that the central runtime calls `WarForceSpecialSettlementService.TrySettle` before ordinary settlement and both special services expose a force-elimination entry accepting `WarForceEliminationDecision`.

- [ ] **Step 2: Run tests and verify RED**

Expected: the router and dedicated entry points are absent.

- [ ] **Step 3: Implement explicit adapters**

```csharp
if (ZhuluWarService.IsZhuluWar(war, requireActive: false))
    return ZhuluWarSettlementService.QueueForceElimination(war, decision);
if (war.getAsset()?.rebellion == true)
    return RebellionCollapseSettlementService.QueueForceElimination(
        war, decision);
return WarForceSpecialSettlementResult.NotSpecial;
```

One-sided exhaustion uses each type's decisive transfer. Mutual exhaustion uses current score: zero white peace; non-zero grants the largest legal benefit not exceeding `abs(score)`. Zhulu filters ceded cities to a connected block beginning with occupied/adjacent cities before applying dedicated settlement depth. Rebellion preserves cities already directly transferred and applies only additional score-affordable transfers. Neither adapter enters ordinary settlement while its guard is active.

Replace attacker-only rebellion collapse expectations with unified tests for attacker exhaustion, defender exhaustion, and mutual exhaustion. Keep the old method only as a compatibility wrapper if `rg` finds a remaining caller.

- [ ] **Step 4: Verify tests and build**

Run the two Task 2 verification commands. Expected: both exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/WarForceSpecialSettlementService.cs Code/core/lineage/ZhuluWarSettlementService.cs Code/core/lineage/RebellionCollapseSettlementService.cs Code/core/lineage/WarForceEliminationSettlementService.cs Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/RebellionForceCollapseRulesTests.cs.txt
git commit -m "feat: settle exhausted special wars"
```

### Task 6: Verify, Audit Statistics, And Deploy Source

**Files:**
- Deploy changed source paths under `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run full verification**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release
```

Expected: both exit `0`; report the observed warning and error counts exactly.

- [ ] **Step 2: Audit remaining duplicate statistics**

```powershell
rg -n --glob '*.cs' "ArmyFieldIndexService.CreateSnapshotCursor|foreach \(.*Actor|foreach \(.*City" Code/core/lineage Code/core/policy
```

Classify hits as vanilla-semantic duplicates or intentional AW3 subsets. Do not change ordinary-army-only, royal-guard exclusion, reserves, occupied control, synthetic levies, titles, or war-goal calculations. Report non-blocking candidates instead of broadening this change.

- [ ] **Step 3: Verify task scope**

```powershell
git status --short
git diff --check
git log -6 --oneline
```

Expected: no uncommitted changes from this task; unrelated dirty files remain untouched.

- [ ] **Step 4: Deploy source only**

Copy only this task's changed `.cs` files to matching paths under `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`. Do not deploy `bin`, `obj`, DLLs, tests, or docs. Compare each deployed file with repository source using `Get-FileHash`.

- [ ] **Step 5: Run in-game scenarios**

Verify: a participant ally's reserves prevent surrender; a one-month zero that rebuilds does not surrender; either side at zero for two months surrenders; mutual zero resolves at positive, negative, and zero score; ordinary, Zhulu, and rebellion wars all resolve; save/load resets a one-month streak; multiplayer replicas never execute settlement.
