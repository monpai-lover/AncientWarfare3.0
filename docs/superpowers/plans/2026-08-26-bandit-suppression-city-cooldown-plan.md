# Bandit Suppression City Cooldown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent automatic bandit stronghold creation in a city for 50 in-game years after suppression while allowing the player god power to bypass the restriction.

**Architecture:** Persist an absolute cooldown expiry year in the restored mother city's data. Evaluate the cooldown through pure spawn rules at the shared stronghold planning boundary, record it only after hostile suppression cleanup succeeds, and pass an explicit bypass only from the manual god power.

**Tech Stack:** C#/.NET Framework 4.8 mod code, WorldBox city data persistence, rule-test console project, CSV localization.

---

### Task 1: Define the cooldown rules with failing tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditSpawnRulesTests.cs.txt`
- Modify: `Code/core/lineage/PeasantRebelBanditSpawnRules.cs`

- [ ] **Step 1: Add failing rule tests**

Add assertions to `PeasantRebelBanditSpawnRulesTests.Run()` for:

```csharp
False(PeasantRebelBanditSpawnRules.CanCreateInCity(
    currentYear: 149, suppressionUntilYear: 150,
    manualBypass: false), "automatic spawning remains blocked before expiry");
True(PeasantRebelBanditSpawnRules.CanCreateInCity(
    currentYear: 150, suppressionUntilYear: 150,
    manualBypass: false), "automatic spawning resumes at the expiry year");
True(PeasantRebelBanditSpawnRules.CanCreateInCity(
    currentYear: 101, suppressionUntilYear: 150,
    manualBypass: true), "manual spawning bypasses suppression cooldown");
Equal(150, PeasantRebelBanditSpawnRules.ResolveSuppressionExpiryYear(
    100, suppressionCompleted: true),
    "suppression creates a fifty-year cooldown");
Equal(int.MinValue,
    PeasantRebelBanditSpawnRules.ResolveSuppressionExpiryYear(
        100, suppressionCompleted: false),
    "ordinary government cleanup creates no cooldown");
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --bandit-stronghold
```

Expected: compilation fails because `CanCreateInCity` and
`ResolveSuppressionExpiryYear` do not exist.

- [ ] **Step 3: Add the minimal pure rules**

Add to `PeasantRebelBanditSpawnRules`:

```csharp
internal const int SuppressionCooldownYears = 50;

internal static bool CanCreateInCity(int pCurrentYear,
    int pSuppressionUntilYear, bool pManualBypass)
{
    return pManualBypass || pSuppressionUntilYear <= pCurrentYear;
}

internal static int ResolveSuppressionExpiryYear(int pCurrentYear,
    bool pSuppressionCompleted)
{
    if (!pSuppressionCompleted) return int.MinValue;
    return pCurrentYear > int.MaxValue - SuppressionCooldownYears
        ? int.MaxValue
        : pCurrentYear + SuppressionCooldownYears;
}
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the same `--bandit-stronghold` command. Expected: pass.

### Task 2: Persist and enforce the city cooldown

**Files:**
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/PeasantRebelBanditStrongholdService.cs`
- Modify: `Code/content/GodPowerLibrary.cs`
- Modify: `Locales/others.csv`
- Modify: `Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditSpawnRulesTests.cs.txt`

- [ ] **Step 1: Add failing source integration guards**

Extend the spawn rule test to read the production sources and assert that:

```csharp
string strongholdService = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "Code", "core", "lineage",
    "PeasantRebelBanditStrongholdService.cs"));
string godPower = File.ReadAllText(Path.Combine(
    Directory.GetCurrentDirectory(), "Code", "content",
    "GodPowerLibrary.cs"));
Contains(strongholdService,
    "MANDATE_REBEL_BANDIT_SUPPRESSION_UNTIL_YEAR");
Contains(strongholdService,
    "pIgnoreSuppressionCooldown");
Contains(strongholdService,
    "ResolveSuppressionExpiryYear");
Contains(godPower,
    "pIgnoreSuppressionCooldown: true");
```

Add `using System.IO;` and this assertion helper:

```csharp
private static void Contains(string pText, string pExpected)
{
    if (pText == null || !pText.Contains(pExpected,
            StringComparison.Ordinal))
        throw new InvalidOperationException(
            "missing source integration: " + pExpected);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run `--bandit-stronghold`. Expected: source guard failure because the runtime
integration is absent.

- [ ] **Step 3: Add the persisted city key**

Add to `LineageKeys`:

```csharp
public const string MANDATE_REBEL_BANDIT_SUPPRESSION_UNTIL_YEAR =
    "aw_bandit_suppression_until_year";
```

- [ ] **Step 4: Gate the shared planning boundary**

Add `bool pIgnoreSuppressionCooldown = false` to `TryPlan`. Read the city expiry
year and reject before any planning mutation when `CanCreateInCity` is false,
returning `aw_bandit_stronghold_suppression_cooldown`.

Add the same optional flag to both `TryCreateDirect` overloads and forward it
into `TryPlan`. Existing automatic callers keep the default `false`.

- [ ] **Step 5: Record cooldown after successful suppression cleanup**

In `CompleteFall`, after the completed stronghold state is written, compute the
expiry through `ResolveSuppressionExpiryYear(Date.getCurrentYear(),
pRecordSuppressionChronicle)`. Store it on the mother city only when the result
is not `int.MinValue`.

- [ ] **Step 6: Let only the god power bypass**

Change `BanditStrongholdClick` to call:

```csharp
PeasantRebelBanditStrongholdService.TryCreateDirect(city,
    out Kingdom bandit, out City stronghold, out string failure,
    pIgnoreSuppressionCooldown: true)
```

- [ ] **Step 7: Add localization**

Add to `Locales/others.csv`:

```csv
aw_bandit_stronghold_suppression_cooldown,此城剿匪后五十年内不会再自行产生土匪,This city cannot automatically produce bandits for 50 years after suppression,此城剿匪後五十年內不會再自行產生土匪
```

- [ ] **Step 8: Run the focused test and verify GREEN**

Run `--bandit-stronghold`. Expected: pass.

### Task 3: Verify and commit the implementation

**Files:**
- Verify all files changed in Tasks 1-2

- [ ] **Step 1: Run focused bandit tests**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -- --bandit-stronghold
```

Expected: `Bandit stronghold and raid rules passed.`

- [ ] **Step 2: Build the mod**

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: zero errors.

- [ ] **Step 3: Check only the scoped diff**

```powershell
git diff --check -- Code/content/GodPowerLibrary.cs Code/core/lineage/LineageKeys.cs Code/core/lineage/PeasantRebelBanditSpawnRules.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Locales/others.csv Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditSpawnRulesTests.cs.txt
```

Expected: no whitespace errors.

- [ ] **Step 4: Commit only the scoped implementation**

Stage the complete scoped files, then use interactive hunk staging for
`GodPowerLibrary.cs` because it already contains unrelated user changes:

```powershell
git add -- Code/core/lineage/LineageKeys.cs Code/core/lineage/PeasantRebelBanditSpawnRules.cs Code/core/lineage/PeasantRebelBanditStrongholdService.cs Locales/others.csv Tests/AncientWarfare3.Rules.Tests/PeasantRebelBanditSpawnRulesTests.cs.txt
git add -p -- Code/content/GodPowerLibrary.cs
git commit -m "feat: add bandit suppression city cooldown"
```
