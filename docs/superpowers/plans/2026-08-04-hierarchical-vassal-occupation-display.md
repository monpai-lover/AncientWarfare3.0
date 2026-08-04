# Hierarchical Vassal Occupation Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore vanilla Kingdom MapMode occupation overlays in the AW3 hierarchical vassal MapMode without adding custom city/zone traversal or full-map redraw work.

**Architecture:** Keep the existing hierarchical territory renderer unchanged. Add one narrow Harmony compatibility patch that lets vanilla `capturing_zones` pass its `Zones.showKingdomZones()` gate while the hierarchical mode is active, and preserve that vanilla QuantumSprite in the existing minimap whitelist.

**Tech Stack:** C#, Harmony, PowerShell source guards, .NET rules-test project.

---

## File Structure

- Create `Code/patch/AW_HierarchicalVassalOccupationPatch.cs`: expose vanilla Kingdom-zone visibility only while the hierarchical vassal MapMode is active.
- Modify `Code/core/policy/HierarchicalVassalMapModeRules.cs`: preserve the vanilla `capturing_zones` QuantumSprite.
- Create `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalOccupationReuseSourceGuard.ps1`: verify the compatibility wiring and forbid custom occupation rendering.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`: execute the new source guard before the rules-test build.

### Task 1: Add the failing occupation-reuse source guard

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalOccupationReuseSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Create the source guard**

Create a PowerShell guard that reads the proposed patch and rules file, then checks:

```powershell
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$patchPath = Join-Path $root 'Code\patch\AW_HierarchicalVassalOccupationPatch.cs'
$rulesPath = Join-Path $root 'Code\core\policy\HierarchicalVassalMapModeRules.cs'

if (-not (Test-Path $patchPath)) {
    throw 'Missing hierarchical vassal occupation compatibility patch.'
}

$patch = Get-Content -Raw $patchPath
$rules = Get-Content -Raw $rulesPath

if ($patch -notmatch 'HarmonyPatch\(typeof\(Zones\),\s*nameof\(Zones\.showKingdomZones\)\)') {
    throw 'Occupation compatibility must patch Zones.showKingdomZones.'
}
if ($patch -notmatch 'HierarchicalVassalMapModeService\.IsActive\(\)' -or
    $patch -notmatch '__result\s*=\s*true') {
    throw 'Hierarchical mode must expose the vanilla Kingdom-zone visibility gate.'
}
if ($rules -notmatch 'pAssetId\s*==\s*"capturing_zones"') {
    throw 'capturing_zones must be preserved by the minimap QuantumSprite whitelist.'
}

$forbidden = @(
    'World.world.cities',
    'CapturingZonesCalculator',
    'drawCapturingZones',
    'QuantumSpriteLibrary',
    'Nameplate'
)
foreach ($token in $forbidden) {
    if ($patch.Contains($token)) {
        throw "Occupation patch must reuse vanilla rendering and cannot contain: $token"
    }
}

Write-Host 'Hierarchical vassal occupation reuse source guard passed.'
```

- [ ] **Step 2: Register the guard in the rules-test project**

Add this command to an existing `BeforeTargets="Build"` source-guard target in `AncientWarfare3.Rules.Tests.csproj`:

```xml
<Exec Command="powershell -NoProfile -ExecutionPolicy Bypass -File &quot;$(MSBuildProjectDirectory)\HierarchicalVassalOccupationReuseSourceGuard.ps1&quot;" />
```

- [ ] **Step 3: Run the guard and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\AncientWarfare3.Rules.Tests\HierarchicalVassalOccupationReuseSourceGuard.ps1
```

Expected: failure with `Missing hierarchical vassal occupation compatibility patch.`

- [ ] **Step 4: Commit the failing guard**

```powershell
git add Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalOccupationReuseSourceGuard.ps1 Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git commit -m "test: guard hierarchical occupation overlay reuse"
```

### Task 2: Restore the vanilla occupation overlay gate

**Files:**
- Create: `Code/patch/AW_HierarchicalVassalOccupationPatch.cs`

- [ ] **Step 1: Add the minimal Harmony Postfix**

Create:

```csharp
using AncientWarfare3.core.policy;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch(typeof(Zones), nameof(Zones.showKingdomZones))]
    internal static class AW_HierarchicalVassalOccupationPatch
    {
        [HarmonyPostfix]
        private static void ShowKingdomZonesPostfix(ref bool __result)
        {
            if (HierarchicalVassalMapModeService.IsActive())
                __result = true;
        }
    }
}
```

This patch must not call any occupation renderer. It only exposes the vanilla visibility gate.

- [ ] **Step 2: Run the guard and verify the remaining failure**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\AncientWarfare3.Rules.Tests\HierarchicalVassalOccupationReuseSourceGuard.ps1
```

Expected: failure with `capturing_zones must be preserved by the minimap QuantumSprite whitelist.`

### Task 3: Preserve the vanilla capturing_zones sprite

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalMapModeRules.cs:89-97`

- [ ] **Step 1: Add capturing_zones to the whitelist**

Update the return expression to include:

```csharp
return pAssetId == "armies" ||
       pAssetId == "boats_big" ||
       pAssetId == "boats_small" ||
       pAssetId == "capturing_zones" ||
       pAssetId == "highlight_cursor_zones" ||
       pAssetId == "selected_kingdom";
```

- [ ] **Step 2: Run the focused guard and verify GREEN**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\AncientWarfare3.Rules.Tests\HierarchicalVassalOccupationReuseSourceGuard.ps1
```

Expected: `Hierarchical vassal occupation reuse source guard passed.`

- [ ] **Step 3: Commit the implementation**

```powershell
git add Code/patch/AW_HierarchicalVassalOccupationPatch.cs Code/core/policy/HierarchicalVassalMapModeRules.cs
git commit -m "fix: show occupation overlay in vassal map"
```

### Task 4: Run regression verification

**Files:**
- Test: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalOccupationReuseSourceGuard.ps1`
- Test: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Run the focused source guard**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\AncientWarfare3.Rules.Tests\HierarchicalVassalOccupationReuseSourceGuard.ps1
```

Expected: pass.

- [ ] **Step 2: Run the complete rules-test project**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj
```

Expected: `Rule tests passed.` The main mod DLL must not be compiled.

- [ ] **Step 3: Check patch hygiene**

```powershell
git diff --check HEAD~2..HEAD
git status --short
```

Expected: no whitespace errors; only intended MapMode/test files are committed, while unrelated user files remain untouched.

