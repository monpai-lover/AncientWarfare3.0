# Invalid Actor Kingdom Crash Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop zone recalculation crashes caused by living actors that reference a Kingdom with missing runtime data, and prevent bandit rollback from creating that state.

**Architecture:** Extend the pure actor-Kingdom validity rules to require both runtime data and an asset, use those rules at every existing safety boundary, and add the missing boundary directly around vanilla `ChunkObjectContainer.addActor`. Before deleting a failed temporary bandit Kingdom, restore or detach every known actor that still references it.

**Tech Stack:** C# 10, .NET Framework 4.8.1, Harmony, PowerShell source guards, custom console rules tests.

---

### Task 1: Make Missing Kingdom Data Invalid

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/ActorKingdomSafetyRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Code/core/lineage/ActorKingdomSafetyRules.cs`

- [ ] **Step 1: Write failing rule tests**

Add `kingdomDataExists` arguments to enemy, zone, detach, and repair-source tests. Include cases where `kingdomDataExists: false` and `kingdomAssetExists: true`; assert that vanilla processing is rejected, detachment is requested, and the city is selected as the repair source.

Add a `--actor-kingdom-safety` runner branch that invokes only `ActorKingdomSafetyRulesTests.Run()` so unrelated baseline failures do not hide this regression.

```csharp
Equal(false, ActorKingdomSafetyRules.CanRunEnemyCheck(
        actorExists: true,
        actorAssetExists: true,
        kingdomDataExists: false,
        kingdomAssetExists: true),
    "a disposed kingdom cannot enter vanilla enemy checks");
Equal(true, ActorKingdomSafetyRules.ShouldDetachInvalidKingdomBeforeRepair(
        kingdomObjectExists: true,
        kingdomDataExists: false,
        kingdomAssetExists: true),
    "a kingdom with disposed data must be detached before repair");
```

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --actor-kingdom-safety
```

Expected: compilation fails because the current rule signatures do not accept `kingdomDataExists`.

- [ ] **Step 3: Implement the runtime validity invariant**

Update `CanRunEnemyCheck`, `CanEnterVanillaZoneProcessing`, `ShouldDetachInvalidKingdomBeforeRepair`, and `SelectRepairSource` to accept `kingdomDataExists`. A current Kingdom is usable only when both data and asset exist.

```csharp
private static bool HasUsableKingdom(bool kingdomDataExists,
    bool kingdomAssetExists)
{
    return kingdomDataExists && kingdomAssetExists;
}
```

Keep existing null-Kingdom and valid-Kingdom behavior unchanged.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the same focused command. Expected: `ActorKingdomSafetyRulesTests: PASS` and exit code 0.

### Task 2: Protect the Exact Vanilla Crash Boundary

**Files:**
- Modify: `Tests/ActorKingdomSafetyRuntimeSourceGuard.ps1`
- Modify: `Code/patch/AW_ActorKingdomSafetyPatch.cs`
- Modify: `Code/core/lineage/ActorKingdomSafetyService.cs`

- [ ] **Step 1: Extend the runtime source guard**

Require a Harmony prefix targeting `ChunkObjectContainer.addActor`, require it to check `pActor?.kingdom?.data != null`, and require it to queue only `pActor`. Also require the existing enemy, zone, and conquest prefixes and repair service to inspect Kingdom data.

```powershell
$chunkBlock = Get-HarmonyMethodBlock $patchSource `
    'ChunkObjectContainerAddActor_Prefix'
Require-Present 'chunk insertion validates kingdom data' `
    $chunkBlock 'pActor?.kingdom?.data != null'
Require-OnlyQueuedActor 'chunk insertion queues only its supplied actor' `
    $chunkBlock 'pActor'
```

- [ ] **Step 2: Run the source guard and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/ActorKingdomSafetyRuntimeSourceGuard.ps1
```

Expected: failure reporting the missing `ChunkObjectContainerAddActor_Prefix`.

- [ ] **Step 3: Upgrade repair and patch call sites**

Pass `kingdom?.data != null` into every upgraded rule. In `ActorKingdomSafetyService`, require both target data and asset, clear stale current references when either is missing, and only report success when the repaired Kingdom has both.

Add the exact crash-boundary prefix:

```csharp
[HarmonyPrefix]
[HarmonyPriority(Priority.First)]
[HarmonyPatch(typeof(ChunkObjectContainer),
    nameof(ChunkObjectContainer.addActor))]
private static bool ChunkObjectContainerAddActor_Prefix(Actor pActor)
{
    bool valid = pActor?.data != null && pActor.asset != null &&
                 pActor.kingdom?.data != null &&
                 pActor.kingdom.asset != null;
    if (valid) return true;
    ActorKingdomSafetyService.QueueRepair(pActor);
    return false;
}
```

- [ ] **Step 4: Run focused rules and source guard**

Run both commands from Tasks 1 and 2. Expected: both exit 0.

### Task 3: Clean Actor References Before Temporary Bandit Removal

**Files:**
- Modify: `Tests/BanditStrongholdTransactionSourceGuard.ps1`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`

- [ ] **Step 1: Write the rollback source regression**

Require a dedicated `PrepareBanditKingdomRemoval` helper and verify every `World.world.kingdoms.removeObject(...)` in `TryCreateDirect` and `Rollback` is immediately preceded by that cleanup on the same temporary Kingdom.

```powershell
foreach ($token in @('PrepareBanditKingdomRemoval(',
        'actor.kingdom = null;', 'pBandit.units.ToList()')) {
    if ($service -notmatch [regex]::Escape($token)) {
        throw "Bandit rollback cleanup is missing $token"
    }
}
```

- [ ] **Step 2: Run the transaction guard and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
```

Expected: failure reporting missing removal preparation.

- [ ] **Step 3: Implement idempotent removal preparation**

Collect distinct valid actors from the transaction snapshots and `pBandit.units`. For actors still referencing the temporary Kingdom, restore a valid snapshot/fallback city, otherwise join the valid origin Kingdom under `FormalAffiliationTransferScope`; if no valid target exists or transfer fails, detach the stale Kingdom reference. Call the helper before all three temporary-Kingdom removal sites.

The helper must not destroy actors and must not change actors that no longer reference the temporary Kingdom.

- [ ] **Step 4: Run the transaction guard and verify GREEN**

Run the command from Step 2. Expected: `Bandit stronghold transaction source guard passed.`

### Task 4: Verify, Commit, Deploy, and Launch

**Files:**
- Verify: `Tests/AncientWarfare3.Rules.Tests`
- Verify: `AncientWarfare3.csproj`
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] **Step 1: Run focused regression checks**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --actor-kingdom-safety
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/ActorKingdomSafetyRuntimeSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/BanditStrongholdTransactionSourceGuard.ps1
```

Expected: all exit 0.

- [ ] **Step 2: Run the complete rules suite and production build**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Release -f net481
```

Expected: rules runner exits 0 and production build reports zero errors.

- [ ] **Step 3: Review and commit**

```powershell
git diff --check
git status --short
git add Code/core/lineage/ActorKingdomSafetyRules.cs Code/core/lineage/ActorKingdomSafetyService.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Code/patch/AW_ActorKingdomSafetyPatch.cs Tests/AncientWarfare3.Rules.Tests/ActorKingdomSafetyRulesTests.cs.txt Tests/ActorKingdomSafetyRuntimeSourceGuard.ps1 Tests/BanditStrongholdTransactionSourceGuard.ps1
git commit -m "fix: repair disposed actor kingdom references"
```

- [ ] **Step 4: Deploy with the repository deployment script**

Inspect the existing deployment command used by the branch, create a recoverable backup, then deploy the verified source and build output to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0` without overwriting the backup directory.

- [ ] **Step 5: Launch WorldBox visibly and inspect the fresh log**

Record the current `Player.log` timestamp, start the WorldBox executable without `-WindowStyle Hidden`, and wait for a new log session. Verify that startup loads AncientWarfare3.0 and that the new session does not contain the original stack chain `ChunkObjectContainer.addActor -> SimObjectsZones.checkUnits -> SimObjectsZones.recalc`.
