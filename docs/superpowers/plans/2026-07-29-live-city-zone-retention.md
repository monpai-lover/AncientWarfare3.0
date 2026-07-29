# Live City Zone Retention Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent WorldBox's automatic abandoned-zone cleanup from shrinking a live city's territory while preserving explicit zone transfer and city destruction.

**Architecture:** Extend the existing empty-city survival rule/service/patch stack. A pure rule defines the live-city gate, the service safely projects runtime `City` state into that rule, and a Harmony prefix skips only `CityZoneAbandon.check`; explicit `City.removeZone`, `City.addZone`, and `City.destroyCity` remain untouched.

**Tech Stack:** C#, Harmony, PowerShell source guards, NeoModLoader runtime compilation.

---

### Task 1: Add Failing Retention Tests

**Files:**
- Modify: `Tests/EmptyCitySurvivalRulesTests.ps1`
- Modify: `Tests/EmptyCitySurvivalSourceGuard.ps1`

- [ ] **Step 1: Add pure-rule expectations**

Append these assertions before the final success output in
`Tests/EmptyCitySurvivalRulesTests.ps1`:

```powershell
Assert-Equal 'a live city suppresses automatic abandoned-zone cleanup' $true `
    ($rules::ShouldSuppressAutomaticAbandonedZoneCleanup(
        $true, $false, 4))
Assert-Equal 'an invalid city permits automatic abandoned-zone cleanup' $false `
    ($rules::ShouldSuppressAutomaticAbandonedZoneCleanup(
        $false, $false, 4))
Assert-Equal 'a destroyed city permits automatic abandoned-zone cleanup' $false `
    ($rules::ShouldSuppressAutomaticAbandonedZoneCleanup(
        $true, $true, 4))
Assert-Equal 'a zoneless city permits automatic abandoned-zone cleanup' $false `
    ($rules::ShouldSuppressAutomaticAbandonedZoneCleanup(
        $true, $false, 0))
```

- [ ] **Step 2: Add Harmony source expectations**

Add these requirements to `Tests/EmptyCitySurvivalSourceGuard.ps1` while
keeping its existing `City.removeZone`, `CityManager`, and
`City.destroyCity` absence checks:

```powershell
Require-Present $patch `
    '[HarmonyPatch(typeof(CityZoneAbandon), nameof(CityZoneAbandon.check))]' `
    'Live-city retention must intercept automatic abandoned-Zone cleanup.'
Require-Present $patch `
    'ShouldSuppressAutomaticAbandonedZoneCleanup(pCity)' `
    'The cleanup Prefix must delegate to the survival service.'
```

- [ ] **Step 3: Run tests and verify RED**

Run:

```powershell
& .\Tests\EmptyCitySurvivalRulesTests.ps1
& .\Tests\EmptyCitySurvivalSourceGuard.ps1
```

Expected: the rule test fails because
`ShouldSuppressAutomaticAbandonedZoneCleanup` does not exist, and the source
guard fails because no `CityZoneAbandon.check` patch exists.

### Task 2: Suppress Automatic Cleanup for Live Cities

**Files:**
- Modify: `Code/core/lineage/EmptyCitySurvivalRules.cs`
- Modify: `Code/core/lineage/EmptyCitySurvivalService.cs`
- Modify: `Code/patch/AW_EmptyCitySurvivalPatch.cs`

- [ ] **Step 1: Add the minimal pure rule**

Add to `EmptyCitySurvivalRules`:

```csharp
public static bool ShouldSuppressAutomaticAbandonedZoneCleanup(
    bool cityValid, bool cityRekt, int zoneCount)
{
    return cityValid && !cityRekt && zoneCount > 0;
}
```

- [ ] **Step 2: Adapt runtime city state safely**

Add to `EmptyCitySurvivalService`:

```csharp
public static bool ShouldSuppressAutomaticAbandonedZoneCleanup(City pCity)
{
    try
    {
        return EmptyCitySurvivalRules.
            ShouldSuppressAutomaticAbandonedZoneCleanup(
                pCity?.data != null,
                pCity?.isRekt() == true,
                pCity?.zones?.Count ?? 0);
    }
    catch
    {
        return false;
    }
}
```

- [ ] **Step 3: Patch only the automatic cleanup entry point**

Add to `AW_EmptyCitySurvivalPatch`:

```csharp
[HarmonyPrefix]
[HarmonyPatch(typeof(CityZoneAbandon), nameof(CityZoneAbandon.check))]
private static bool AbandonedZoneCleanup_Prefix(City pCity)
{
    return !EmptyCitySurvivalService.
        ShouldSuppressAutomaticAbandonedZoneCleanup(pCity);
}
```

- [ ] **Step 4: Run focused tests and verify GREEN**

Run:

```powershell
& .\Tests\EmptyCitySurvivalRulesTests.ps1
& .\Tests\EmptyCitySurvivalSourceGuard.ps1
```

Expected: both scripts exit 0 and print their pass messages.

- [ ] **Step 5: Check the production diff**

Run:

```powershell
git diff --check -- Code/core/lineage/EmptyCitySurvivalRules.cs Code/core/lineage/EmptyCitySurvivalService.cs Code/patch/AW_EmptyCitySurvivalPatch.cs Tests/EmptyCitySurvivalRulesTests.ps1 Tests/EmptyCitySurvivalSourceGuard.ps1
```

Expected: exit 0 with no whitespace errors.

### Task 3: Build, Deploy, and Verify Runtime Compilation

**Files:**
- Deploy: `Code/core/lineage/EmptyCitySurvivalRules.cs`
- Deploy: `Code/core/lineage/EmptyCitySurvivalService.cs`
- Deploy: `Code/patch/AW_EmptyCitySurvivalPatch.cs`

- [ ] **Step 1: Compile the mod project**

Run:

```powershell
dotnet build .\AncientWarfare3.csproj --no-restore
```

Expected: exit 0 with no C# errors.

- [ ] **Step 2: Copy only the changed runtime files**

Copy the three production files to the matching paths under:

```text
D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0
```

Do not copy tests, docs, generated outputs, or delete unrelated deployed
files.

- [ ] **Step 3: Verify deployed files byte-for-byte**

Run `Get-FileHash` on each source/deployed pair and require matching hashes.

- [ ] **Step 4: Verify NeoModLoader compilation**

Inspect the new tail of:

```text
C:\Users\24908\AppData\LocalLow\mkarpenko\WorldBox\Player.log
```

Expected: AW3 recompiles without C# compiler errors, Harmony patch failures,
or exceptions mentioning `CityZoneAbandon` or
`AW_EmptyCitySurvivalPatch`.

- [ ] **Step 5: Commit only scoped implementation files**

```powershell
git add -f -- Code/core/lineage/EmptyCitySurvivalRules.cs Code/core/lineage/EmptyCitySurvivalService.cs Code/patch/AW_EmptyCitySurvivalPatch.cs Tests/EmptyCitySurvivalRulesTests.ps1 Tests/EmptyCitySurvivalSourceGuard.ps1 docs/superpowers/plans/2026-07-29-live-city-zone-retention.md
git commit -m "fix: prevent live city zone rollback"
```

Before committing, confirm no unrelated staged paths are present.
