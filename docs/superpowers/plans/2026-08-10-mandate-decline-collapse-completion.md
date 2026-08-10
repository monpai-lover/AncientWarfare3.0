# 天命衰亡与崩溃闭环实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将已确认的城市、战争、Chaos 倒计时、历史人物宽限和运行态同步规则接入现有天命系统，形成可测试的衰亡与崩溃闭环。

**Architecture:** 新增无 Unity/World 依赖的纯规则类，分别计算城市损失、战争损失和 Chaos 年度状态；`MandateService` 只负责现有事件边界上的状态读写与调用；`MandatePhaseService` 持久化 Chaos 未解决年数、恢复连续年数和历史人物宽限状态。城市与战争补丁只转发已有对象，绝不添加 Actor 全量扫描。

**Tech Stack:** C#/.NET 4.8, Harmony, SQLite, existing AW3 rule-test harness, PowerShell source guards.

---

### Task 1: Add Pure Mandate Decline Rules

**Files:**
- Create: `Code/core/lineage/MandateDeclineRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing tests for city loss and annual cap**

Add assertions to the existing mandate rules section for:

```csharp
Equal(-2, MandateDeclineRules.CityTransferDelta(false), "legal core loss costs two mandate");
Equal(-8, MandateDeclineRules.CityTransferDelta(true), "capital legal core loss costs eight mandate");
Equal(-12, MandateDeclineRules.ClampAnnualCityLoss(-20), "city loss is capped per year");
Equal(0, MandateDeclineRules.ClampAnnualCityLoss(4), "positive city loss input is normalized to zero");
```

- [ ] **Step 2: Run the focused rules harness and verify the expected compile failure**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: FAIL because `MandateDeclineRules` does not exist.

- [ ] **Step 3: Write failing tests for war loss and Chaos progression**

Add assertions for:

```csharp
Equal(-4, MandateDeclineRules.WarDefeatDelta(false, false), "ordinary defeat costs four mandate");
Equal(-7, MandateDeclineRules.WarDefeatDelta(true, false), "half army loss adds three");
Equal(-9, MandateDeclineRules.WarDefeatDelta(true, true), "total elimination replaces half-loss penalty");
True(MandateDeclineRules.ShouldCollapseChaos(8, true), "eight unresolved chaos years collapse");
False(MandateDeclineRules.ShouldCollapseChaos(7, true), "seven unresolved chaos years do not collapse");
True(MandateDeclineRules.ShouldRecoverChaos(40, 40, 0.70f, false, false, 3), "stable chaos recovers after three years");
```

- [ ] **Step 4: Run the harness and verify the new assertions fail for missing methods**

Run the same command. Expected: FAIL only on the new `MandateDeclineRules` references.

- [ ] **Step 5: Implement the minimal pure rules**

Implement constants and methods with no Unity or game-object references:

```csharp
public const int ChaosCollapseYears = 8;
public const int ChaosRecoveryYears = 3;
public const int MaximumAnnualCityLoss = 12;

public static int CityTransferDelta(bool pCapital) => pCapital ? -8 : -2;

public static int ClampAnnualCityLoss(int pAccumulatedLoss) =>
    Math.Max(-MaximumAnnualCityLoss, Math.Min(0, pAccumulatedLoss));

public static int WarDefeatDelta(bool pHalfLoss, bool pTotalLoss) =>
    pTotalLoss ? -9 : pHalfLoss ? -7 : -4;
```

`ShouldCollapseChaos` must require both `unresolvedYears >= 8` and an unresolved flag. `ShouldRecoverChaos` must require all five recovery conditions and `stableYears >= 3`.

- [ ] **Step 6: Run the focused rules harness and verify it passes**

Expected output: `Rule tests passed.`

- [ ] **Step 7: Commit the pure rules slice**

```powershell
git add Code/core/lineage/MandateDeclineRules.cs Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: define mandate decline and chaos rules"
```

### Task 2: Persist Chaos Counters And One-Time Protection

**Files:**
- Modify: `Code/core/db/LineageKeys.cs`
- Modify: `Code/core/lineage/MandatePhaseService.cs`
- Modify: `Code/core/lineage/MandatePhaseRules.cs`
- Modify: `Code/core/lineage/MandateService.cs`
- Modify: `Code/core/db/MandateStateTableItem.cs`
- Modify: `Code/core/lineage/MandatePhaseService.cs`
- Modify: `Code/core/lineage/MandateService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing tests for counter transitions**

Cover incrementing unresolved Chaos years, resetting recovery years when any condition fails, the eight-year collapse threshold, and a protection budget of exactly one use per mandate period with a four-year grace interval.

- [ ] **Step 2: Run tests and verify they fail before production changes**

Expected: FAIL on the missing counter/protection rule API.

- [ ] **Step 3: Add persisted fields and pure transition helpers**

Add period-scoped fields for `chaos_unresolved_years`, `chaos_recovery_years`, `mandate_protection_used`, and `mandate_protection_until_year`. Extend the existing SQLite migration and load/save paths rather than adding a second table.

- [ ] **Step 4: Implement annual Chaos state update**

In `MandatePhaseService.EvaluateActiveMandateYear`, derive the unresolved flag from the already available report, phase, catalyst, and active-claimant values. Increment or reset counters once per world year. Attempt recovery before collapse; if recovery succeeds, call the existing phase setter to enter `Decline`. If the unresolved counter reaches eight, call `MandateService.TryApplyMandateProtection` and only defer collapse when it returns true.

- [ ] **Step 5: Replace permanent `first/figure` protection**

Remove the direct permanent return from `HasMandateProtection`. Keep the trait check as the eligibility predicate for consuming the period-scoped protection budget. Record the protection event once and persist the four-year deadline.

- [ ] **Step 6: Run all mandate rule tests**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected output: `Rule tests passed.`

- [ ] **Step 7: Commit the phase-state slice**

```powershell
git add Code/core/db Code/core/lineage Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: complete mandate chaos lifecycle"
```

### Task 3: Wire Immediate City Loss With Idempotent Annual Cap

**Files:**
- Modify: `Code/core/lineage/MandateService.cs:1157-1170`
- Modify: `Code/core/lineage/MandateCoreTransferRules.cs`
- Modify: `Code/patch/AW_ChroniclePatch.cs` only if the existing callback cannot provide the pre-transfer owner
- Test: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Test: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add failing rule tests for transfer eligibility**

Verify that only a current-period legal core transfer is eligible, capital loss is distinguished, same-owner refreshes are ignored, and annual loss is capped at `-12`.

- [ ] **Step 2: Run the rules harness and observe the expected failures**

- [ ] **Step 3: Add a period/year loss marker and call `ChangeMandate` once per accepted transfer**

Use the pre-transfer owner supplied by the existing Harmony callback. Reject load transfers, non-core cities, mismatched mandate kingdoms, and already-cleared mandate state. Store only year and accumulated negative amount; do not store a city pool.

- [ ] **Step 4: Re-read the report after the change and dirty the existing maps/UI**

Keep the existing cache invalidation behavior after the mutation. Do not add city or Actor iteration.

- [ ] **Step 5: Run rules and source guards**

Expected: no new `World.world.actors`, `kingdom.getUnits()`, or whole-city scans in the transfer path.

- [ ] **Step 6: Commit the city-loss slice**

```powershell
git add Code/core/lineage/MandateService.cs Code/core/lineage/MandateCoreTransferRules.cs Code/patch/AW_ChroniclePatch.cs Tests
git commit -m "feat: apply mandate loss on legal core capture"
```

### Task 4: Wire Ordinary War Defeat From Existing Military Facts

**Files:**
- Modify: `Code/core/lineage/MandateService.cs:1410-1440`
- Read: `Code/core/lineage/WarMilitaryFactsService.cs`
- Read: `Code/core/lineage/WarScoreService.cs`
- Modify: `Code/core/lineage/WarMilitaryFactsService.cs` only if its existing persisted snapshot lacks the required defeat flags
- Test: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Test: existing war military facts tests or a focused source guard

- [ ] **Step 1: Add failing tests for defeat classification**

Test ordinary defeat, half-loss defeat, total elimination, missing facts fallback, attacker victory, and mandate-war exclusion.

- [ ] **Step 2: Run the relevant test project and verify the failures**

- [ ] **Step 3: Read only the existing end-of-war military snapshot**

Add a small adapter that maps existing `WarMilitaryFactsService`/`War` values to `WarDefeatDelta`. Do not introduce a new army scan or recompute casualties at settlement time.

- [ ] **Step 4: Apply the delta only when the current mandate is the defeated main defender**

Mark mandate-war types before ordinary-war handling so they cannot receive both paths. Preserve current transfer/clear behavior for a mandate-war defeat.

- [ ] **Step 5: Run war and mandate tests**

Expected: all existing tests pass and ordinary war loss is recorded exactly once.

- [ ] **Step 6: Commit the war-loss slice**

```powershell
git add Code/core/lineage/MandateService.cs Code/core/lineage/WarMilitaryFactsService.cs Code/core/lineage/WarScoreService.cs Tests
git commit -m "feat: connect mandate loss to ordinary war defeat"
```

### Task 5: Synchronize Runtime Mirrors

**Files:**
- Modify: `Code/core/lineage/MandateService.cs:1942-1970`
- Test: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Test: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Add a failing source/rule assertion for event mirror writes**

Require `ChangeMandate` to update `LineageKeys.MANDATE_VALUE`, `LineageKeys.MANDATE_AUTHORITY`, and `LineageKeys.MANDATE_PRESTIGE` after the authoritative state mutation.

- [ ] **Step 2: Run the guard and verify the missing-write failure**

- [ ] **Step 3: Add one private mirror helper and call it from `ChangeMandate`**

The helper must no-op for null kingdom data and write only the active mandate kingdom. Keep `UpdateState` as the database/cache authority and retain `DirtyAllMaps`.

- [ ] **Step 4: Run the focused source guards and full rules harness**

- [ ] **Step 5: Commit the synchronization slice**

```powershell
git add Code/core/lineage/MandateService.cs Tests
git commit -m "fix: synchronize mandate event runtime mirrors"
```

### Task 6: Verify Collapse Fallback And Runtime Boundaries

**Files:**
- Read: `Code/core/lineage/MandateRebelService.cs`
- Modify: `Tests/SourceGuardTests.ps1`
- Add focused test files only if the existing harness cannot express the boundary

- [ ] **Step 1: Add failing tests for no-candidate collapse behavior**

Assert that the collapse call still clears the mandate when no valid non-capital rebel candidate exists and does not create a random kingdom or actor.

- [ ] **Step 2: Run the test and verify the expected failure**

- [ ] **Step 3: Add only the minimum guard or fallback call needed**

Keep `MandateRebelService.OnMandateCollapse` as the existing candidate selector. The fallback is a control-flow guarantee, not a forced random rebel spawn.

- [ ] **Step 4: Add source guards for no full-world scan**

Check city transfer, war-end, and Chaos annual methods for absence of Actor enumeration and presence of existing authority guards.

- [ ] **Step 5: Run the complete verification set**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
pwsh -File Tests/SourceGuardTests.ps1
dotnet build AncientWarfare3.csproj --no-restore
git diff --check
git status --short --branch
```

Expected: rule tests pass, source guards pass, build exits 0, diff check is empty, and only intended commits/files are present.

- [ ] **Step 6: Commit verification-only guard changes**

```powershell
git add Tests
git commit -m "test: guard mandate collapse runtime boundaries"
```

### Task 7: Final Review And Deployment Handoff

**Files:**
- Review all commits on `fix/mandate-decline-collapse`
- Do not modify the clean `master` worktree during implementation

- [ ] **Step 1: Review the complete diff against `master`**

Confirm that no unrelated UI, naming, RTS, deployment, or JSON files changed.

- [ ] **Step 2: Re-run the complete verification set after the final commit**

Use the commands from Task 6 and capture their exit codes/output.

- [ ] **Step 3: Report the isolated worktree path, commit list, test output, and any game-only verification still required**

Do not merge or push until the user explicitly requests integration.
