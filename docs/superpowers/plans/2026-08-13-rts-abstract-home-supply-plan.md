# RTS Abstract Home Supply Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Feed active RTS army members from the real food inventory of their own anchor city without interrupting their mission.

**Architecture:** A pure rule determines supply eligibility. A runtime service resolves only `AWArmyService.FindAnchorCity(actor.army)`, consumes one matching item through vanilla city and actor APIs, and returns false for every invalid or empty-supply case. A Harmony prefix suppresses the vanilla city-food task only after a successful ration.

**Tech Stack:** C#, Harmony, WorldBox Actor and City APIs, net9 rules tests, PowerShell source guards.

---

### Task 1: Supply Rules

**Files:**
- Create: `Code/core/lineage/ArmyRtsAbstractSupplyRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsAbstractSupplyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Write tests for a live RTS-owned actor with a same-kingdom anchor city being eligible, and ordinary actor, dead actor, missing anchor, foreign anchor being ineligible. Also assert `ShouldSuppressVanillaFoodTask(false)` is false and true is true.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`; expect compile failure because `ArmyRtsAbstractSupplyRules` is absent.
- [ ] Implement `CanAttempt(bool actorAlive, bool ownsLiveRtsActor, bool hasAnchorCity, bool anchorInActorKingdom)` as the conjunction of those inputs, and `ShouldSuppressVanillaFoodTask(bool supplied)` as `return supplied;`.
- [ ] Link the source and tests in the rules project and run the same command; expect exit code 0.
- [ ] Commit with `git commit -m 'test: define RTS abstract home supply rules'`.

### Task 2: Home-City Ration Service

**Files:**
- Create: `Code/core/lineage/ArmyRtsAbstractSupplyService.cs`
- Modify: `Code/core/lineage/ArmyRtsAbstractSupplyRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyRtsAbstractSupplyRulesTests.cs.txt`

- [ ] Add failing rules tests for `CanConsume(eligible, hasSuitableFood, hasFoodItem)`: only the all-true case succeeds.
- [ ] Run the rules project; expect failure because `CanConsume` is absent.
- [ ] Add `CanConsume` as the conjunction. Implement `TryConsumeHomeRation(Actor actor)` to resolve `AWArmyService.FindAnchorCity(actor.army)`, validate that actor is alive and `ArmyRtsControllerService.OwnsLiveActor(actor)` is true, validate that the non-rekt anchor city belongs to `actor.kingdom`, choose `home.getFoodItem(actor.subspecies, actor.data.favorite_food)`, then call `home.eatFoodItem(food.id)` and `actor.consumeFoodResource(food)`. Wrap engine access in a narrow `try/catch` that returns false. Do not scan cities, change tasks, create resources, or charge actor money.
- [ ] Run the rules project; expect exit code 0.
- [ ] Commit with `git commit -m 'feat: consume RTS rations from army anchor city'`.

### Task 3: Hunger-Task Hook

**Files:**
- Create: `Code/patch/AW_RtsAbstractSupplyPatch.cs`
- Create: `Tests/ArmyRtsAbstractSupplySourceGuard.ps1`

- [ ] Write a guard requiring the patch to target `AiSystemActor` task assignment, compare exactly `try_to_eat_city_food`, call `TryConsumeHomeRation`, suppress only success, and return to vanilla behavior after failure.
- [ ] Run `pwsh -File Tests/ArmyRtsAbstractSupplySourceGuard.ps1`; expect it to fail because the patch is absent.
- [ ] Add a Harmony prefix on `AiSystemActor.setTask`: for all task IDs other than `try_to_eat_city_food`, return true. On a successful `TryConsumeHomeRation`, return false; otherwise return true. Catch all hook errors and return true.
- [ ] Run `pwsh -File Tests/ArmyRtsAbstractSupplySourceGuard.ps1`, `pwsh -File Tests/ArmyRtsIndependentPathSourceGuard.ps1`, and `pwsh -File Tests/ArmyRtsContinuitySourceGuard.ps1`; expect all pass.
- [ ] Commit with `git commit -m 'fix: keep RTS missions supplied during hunger checks'`.

### Task 4: Full Verification

**Files:**
- Verify only

- [ ] Run `dotnet build AncientWarfare3.csproj --no-restore -p:TargetFramework=net481`; expect 0 warnings and 0 errors.
- [ ] Run the rules, movement-priority, mission-lock, watchdog-lock, and source-guard suites; expect all exit code 0.
- [ ] Run `dotnet run --project Tests/ArmyRtsAdversarialSimulation/ArmyRtsAdversarialSimulation.csproj -- --all --first-seed 17 --seeds 32 --ticks 10000`; expect 192 scenarios pass.
- [ ] Run `git diff HEAD~3..HEAD --check` and `git status --short`; expect no whitespace errors or unintended changes.
