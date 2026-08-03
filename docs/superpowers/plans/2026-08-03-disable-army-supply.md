# Disable Army Supply Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Temporarily make RTS Army supply inert while preserving organization, casualties, regrouping, recruitment, movement, and combat.

**Architecture:** Add one pure authoritative supply feature rule in `ArmyLogisticsRules.cs`. Normalize supply both in pure rule consumers and at the runtime operational-state boundary so stale values from existing sessions cannot affect the director or RTS state machine, while retaining all existing fields and persistence-compatible APIs.

**Tech Stack:** C# 8, .NET Framework 4.8, WorldBox/NML runtime, rule-slice console tests, adversarial RTS simulation.

---

### Task 1: Lock Disabled-Supply Semantics With Failing Tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ArmyLogisticsSustainmentRulesTests.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Add tests for normalization, no drain, regroup completion, and retained organization behavior**

Add these cases to `Run()` and implement them in the same test class:

```csharp
DisabledSupplyAlwaysNormalizesToFull();
DisabledSupplyDoesNotBlockRegroupCompletion();
OrganizationStillRespondsToCasualtiesAndRegrouping();
```

The assertions must include:

```csharp
Equal(100, ArmyLogisticsRules.EffectiveSupply(0),
    "disabled supply ignores a stale zero value");
Equal(true, ArmyLogisticsRules.EffectiveSupplyConnection(false),
    "disabled supply ignores a stale disconnected corridor");
Equal(100, ArmyLogisticsRules.UpdateSupply(17,
        ArmyRtsState.Assault, connectedSupply: false,
        inCorridor: false),
    "disabled supply neither drains nor applies isolation penalties");
Equal(true, ArmyLogisticsRules.CanCompleteRegroup(
        organization: 60, supply: 0, minimumForceReady: true),
    "stale supply cannot trap an otherwise-ready Army in regroup");
Equal(55, ArmyLogisticsRules.UpdateOrganization(
        new ArmyOrganizationFacts
        {
            CurrentOrganization = 60,
            RecentCasualties = 1,
            Supply = 0
        }),
    "casualties still reduce organization while supply is disabled");
Equal(72, ArmyLogisticsRules.UpdateOrganization(
        new ArmyOrganizationFacts
        {
            CurrentOrganization = 60,
            Supply = 0,
            Regrouping = true
        }),
    "regrouping still restores organization while supply is disabled");
```

In the existing corridor and movement tests, change only the expected supply
values from `99`, `97`, `71`, and `70` to `100`; retain the connectivity and
scheduler assertions. Rename `AStalledMarchDoesNotConsumeSupply` to
`DisabledSupplyNeverDrainsDuringMarch` so the test describes the new contract.

- [ ] **Step 2: Run the logistics slice and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-logistics-sustainment-slice
```

Expected: compilation fails because `EffectiveSupply` does not exist, and the old supply behavior would return drained values.

- [ ] **Step 3: Commit the failing tests**

```powershell
git add -- Tests/AncientWarfare3.Rules.Tests/ArmyLogisticsSustainmentRulesTests.cs.txt
git commit -m "test: define disabled army supply behavior"
```

### Task 2: Implement the Authoritative Disabled-Supply Rule

**Files:**
- Modify: `Code/core/lineage/ArmyLogisticsRules.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/ArmyLogisticsSustainmentRulesTests.cs.txt`

- [ ] **Step 1: Add the feature rule and effective-value helper**

Add to `ArmyLogisticsRules`:

```csharp
public const bool SupplySimulationEnabled = false;

public static int EffectiveSupply(int observedSupply)
{
    return SupplySimulationEnabled
        ? Math.Max(MinimumSupply, Math.Min(MaximumSupply, observedSupply))
        : MaximumSupply;
}

public static bool EffectiveSupplyConnection(bool observedConnection)
{
    return !SupplySimulationEnabled || observedConnection;
}
```

- [ ] **Step 2: Normalize every pure logistics decision**

Make `UpdateSupply` immediately return `MaximumSupply` while disabled. In
`UpdateOrganization`, calculate the critical-supply penalty and regroup
recovery from `EffectiveSupply(pFacts.Supply)`. Apply the same helper in
`CanCompleteRegroup`, `ArmyOperationalDirectorRules.Project`, and
`ResolvePursuit` before comparing supply thresholds.

The organization delta remains:

```csharp
long delta = (long)Math.Max(0, pFacts.RecentCasualties) *
             OrganizationPerCasualty;
if (pFacts.CaptainLost) delta += CaptainLossOrganization;
int effectiveSupply = EffectiveSupply(pFacts.Supply);
if (effectiveSupply <= CriticalSupply)
    delta += CriticalSupplyOrganization;
if (pFacts.Regrouping)
    delta += ArmyOperationalDirectorRules.
        RegroupRecoveryForSupply(effectiveSupply);
```

- [ ] **Step 3: Run the logistics slice and verify GREEN**

Run the Task 1 command.

Expected: `AW3 RTS logistics sustainment rules passed.`

- [ ] **Step 4: Commit the pure rule implementation**

```powershell
git add -- Code/core/lineage/ArmyLogisticsRules.cs
git commit -m "fix: temporarily disable army supply simulation"
```

### Task 3: Normalize Runtime Operational State

**Files:**
- Modify: `Code/core/lineage/ArmyLogisticsService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/ArmyLogisticsSustainmentRulesTests.cs.txt`

- [ ] **Step 1: Normalize supply at the runtime read boundary**

In `ArmyLogisticsService.GetOperationalState`, replace the raw supply value
with the pure helper:

```csharp
return new ArmyOperationalStateView(
    ArmyLogisticsRules.EffectiveSupply(state.Supply),
    state.Organization,
    ArmyLogisticsRules.EffectiveSupplyConnection(state.ConnectedSupply),
    ArmyLogisticsRules.EffectiveSupplyConnection(state.InCorridor));
```

This ensures `ArmyRtsControllerService` and `KingdomWarDirectorService` see
full effective supply even when an existing runtime index contains zero.

- [ ] **Step 2: Store normalized supply during periodic updates**

After `UpdateSupply`, pass the normalized value to both organization
calculation and `OperationalIndex.SetValues`. Do not remove connectivity or
snapshot fields.

In `IsTileInMissionCorridor`, return `true` immediately while supply
simulation is disabled. Terrain validity, island checks, pursuit distance,
and route-arrival logic remain active in their existing owners; only the
supply-corridor veto is removed.

- [ ] **Step 3: Run focused and broad RTS tests**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --rts-logistics-sustainment-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --army-rts
```

Expected: both commands exit `0` and report their respective slice passed.

- [ ] **Step 4: Commit the runtime boundary change**

```powershell
git add -- Code/core/lineage/ArmyLogisticsService.cs
git commit -m "fix: normalize disabled supply at runtime boundary"
```

### Task 4: Verify Simulation, Build, and Source Deployment

**Files:**
- Deploy: `Code/core/lineage/ArmyLogisticsRules.cs`
- Deploy: `Code/core/lineage/ArmyLogisticsService.cs`

- [ ] **Step 1: Run the adversarial RTS simulation**

```powershell
dotnet run --project Tests/ArmyRtsAdversarialSimulation/ArmyRtsAdversarialSimulation.csproj -c Release -- --all
```

Expected: all 32 seeds pass land, ownership, route, transport, war, and
rally-recruitment scenarios without a progress-oracle failure.

- [ ] **Step 2: Build the mod**

```powershell
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: `0` warnings and `0` errors.

- [ ] **Step 3: Check the scoped diff**

```powershell
git diff --check -- Code/core/lineage/ArmyLogisticsRules.cs Code/core/lineage/ArmyLogisticsService.cs Tests/AncientWarfare3.Rules.Tests/ArmyLogisticsSustainmentRulesTests.cs.txt
```

Expected: no whitespace errors.

- [ ] **Step 4: Deploy source only**

Copy the two changed production `.cs` files to the same relative paths under:

```text
D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0
```

Do not copy `bin`, `obj`, or any DLL.

- [ ] **Step 5: Verify deployed SHA-256 hashes**

Run `Get-FileHash -Algorithm SHA256` for each source and deployed file pair.

Expected: both pairs match exactly.
