# Cancel Button Icon Override Reset Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent an AW3 GodPower cancel-button icon from remaining visible after the player selects a vanilla GodPower.

**Architecture:** Keep AW3 icon replacement limited to `xia` and `aw_*`. Add a pure rule deciding when a valid non-AW3 power must clear the cancel Image override, then apply it before the original `CancelButton.setIconFrom` updates the base sprite.

**Tech Stack:** C#, Harmony prefix patch, Unity UI Image, AW3 .NET 9 rules tests.

---

### Task 1: Define the override-reset rule

**Files:**
- Modify: `Code/ui/AWPowerButtonVisualRules.cs:13`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt:6659`

- [ ] **Step 1: Add the failing rule tests**

```csharp
True(AWPowerButtonVisualRules.ShouldClearCancelIconOverride("spawn_human"),
    "selecting a vanilla power clears a stale AW3 cancel icon override");
Equal(false,
    AWPowerButtonVisualRules.ShouldClearCancelIconOverride("aw_grant_mandate"),
    "switching between AW3 powers keeps the AW3 override path");
Equal(false, AWPowerButtonVisualRules.ShouldClearCancelIconOverride("xia"),
    "the Xia power keeps the corrected AW3 override path");
Equal(false, AWPowerButtonVisualRules.ShouldClearCancelIconOverride(null),
    "an invalid power does not mutate the current cancel icon");
```

- [ ] **Step 2: Run the rules suite and verify RED**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: compilation fails because `ShouldClearCancelIconOverride` does not exist.

- [ ] **Step 3: Implement the minimal pure rule**

```csharp
public static bool ShouldClearCancelIconOverride(string pPowerId)
{
    return !string.IsNullOrEmpty(pPowerId) &&
           !ShouldPatchCancelIcon(pPowerId);
}
```

- [ ] **Step 4: Run the rules suite and verify GREEN**

Run the Step 2 command.

Expected: `Rule tests passed.`

### Task 2: Apply the reset before vanilla icon selection

**Files:**
- Modify: `Code/patch/AW_PowerButtonVisualPatch.cs:13`

- [ ] **Step 1: Clear only stale AW3 overrides on the vanilla path**

Insert before the current `ShouldPatchCancelIcon` return:

```csharp
if (AWPowerButtonVisualRules.ShouldClearCancelIconOverride(powerId))
{
    if (__instance?.powerIcon != null)
        __instance.powerIcon.overrideSprite = null;
    return true;
}
```

The existing AW3 branch remains unchanged and continues to set both `sprite` and `overrideSprite`.

- [ ] **Step 2: Run focused source and full rules verification**

```powershell
powershell -ExecutionPolicy Bypass -File Tests/IntegratedNamingEngineSourceGuard.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git diff --check
```

Expected: both checks exit `0`, rules report `Rule tests passed.`, and no whitespace errors are reported.

- [ ] **Step 3: Commit the fix**

```powershell
git add Code/ui/AWPowerButtonVisualRules.cs Code/patch/AW_PowerButtonVisualPatch.cs Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "fix: reset stale AW3 cancel button icons"
```
