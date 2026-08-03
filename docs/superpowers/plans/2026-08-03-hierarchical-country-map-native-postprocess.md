# Hierarchical Country Map Native Post-Process Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish hierarchical country and city labels in the native map redraw, update cached TextMesh state only when it materially changes, and keep native Army flags visible.

**Architecture:** The existing kingdom-style `draw_zones` pass remains the only territory walk. A pure accumulator consumes one bounded sample per visible Zone, and the service finalizes country layout from Zone statistics or publishes cities at `city_center`; visible labels no longer enter the Tile-copy worker pipeline. The TextMesh pool keeps accepted nodes and applies only material state changes, while Army QuantumSprites remain owned by the native renderer.

**Tech Stack:** C# 9, Harmony, Unity `TextMesh`/`QuantumSprite`, WorldBox `ZoneCalculator`, .NET 9 rules tests, PowerShell source guards.

---

## File Structure

- Create `Code/core/policy/HierarchicalVassalZoneLabelAccumulator.cs`: pure weighted Zone statistics.
- Create `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalZoneLabelAccumulatorTests.cs.txt`: accumulator tests.
- Modify `Code/core/policy/AWMapModeMetaLibrary.cs`: feed native-draw Zones into the active label pass.
- Modify `Code/core/policy/HierarchicalVassalMapModeService.cs`: own native-pass accumulation, fallbacks, city anchors, and redraw invalidation.
- Modify `Code/core/policy/HierarchicalVassalMapModeLabelLayer.cs`: direct publication, visual-state caching, and immediate layer hiding.
- Modify `Code/core/policy/HierarchicalVassalMapModeRules.cs`: retain the native Army QuantumSprite asset.
- Modify `Code/patch/AW_HierarchicalVassalMapMinimapPatch.cs`: stop cancelling and re-sorting Army flags.
- Modify `Code/patch/AW_ArmyMapInformationMinimapPatch.cs`: permit Army information in the hierarchical mode.
- Modify hierarchical PowerShell source guards and create an Army visibility guard.

### Task 1: Pure Zone Statistics

**Files:**
- Create: `Code/core/policy/HierarchicalVassalZoneLabelAccumulator.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalZoneLabelAccumulatorTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing accumulator test**

```csharp
using System;
using AncientWarfare3.core.policy;

internal static class HierarchicalVassalZoneLabelAccumulatorTests
{
    internal static void Run()
    {
        var value = new HierarchicalVassalZoneLabelAccumulator();
        Equal(true, value.Add(1, 0d, 0d, 10));
        Equal(true, value.Add(2, 10d, 0d, 30));
        Equal(false, value.Add(2, 99d, 99d, 30));
        Equal(false, value.Add(3, 5d, 5d, 0));
        Equal(true, value.TryBuild(out var metrics));
        Near(7.5d, metrics.AnchorX);
        Near(0d, metrics.AnchorY);
        Equal(40, metrics.LandArea);
        Equal(11, metrics.SpanX);
        Equal(1, metrics.SpanY);
        Near(0d, metrics.Angle);

        var diagonal = new HierarchicalVassalZoneLabelAccumulator();
        diagonal.Add(1, 0d, 0d, 1);
        diagonal.Add(2, 20d, 20d, 1);
        Equal(true, diagonal.TryBuild(out metrics));
        Near(35d, metrics.Angle);
        Equal(false, new HierarchicalVassalZoneLabelAccumulator().TryBuild(out _));
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"expected {expected}, got {actual}");
    }

    private static void Near(double expected, double actual)
    {
        if (Math.Abs(expected - actual) > 0.0001d)
            throw new InvalidOperationException($"expected {expected}, got {actual}");
    }
}
```

Add both new files to the project and call `HierarchicalVassalZoneLabelAccumulatorTests.Run();` beside the existing hierarchical tests.

- [ ] **Step 2: Run the rules project and verify RED**

Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`.

Expected: compilation fails because the accumulator types do not exist.

- [ ] **Step 3: Implement duplicate-safe weighted statistics**

```csharp
internal readonly struct HierarchicalVassalZoneLabelMetrics
{
    internal readonly double AnchorX, AnchorY;
    internal readonly float Angle;
    internal readonly int LandArea, SpanX, SpanY;

    internal HierarchicalVassalZoneLabelMetrics(double x, double y,
        float angle, int area, int spanX, int spanY)
    {
        AnchorX = x; AnchorY = y; Angle = angle; LandArea = area;
        SpanX = Math.Max(1, spanX); SpanY = Math.Max(1, spanY);
    }
}

internal sealed class HierarchicalVassalZoneLabelAccumulator
{
    private readonly HashSet<int> _ids = new HashSet<int>();
    private double _w, _x, _y, _xx, _yy, _xy;
    private int _area, _minX = int.MaxValue, _maxX = int.MinValue;
    private int _minY = int.MaxValue, _maxY = int.MinValue;

    internal bool Add(int id, double x, double y, int ground)
    {
        if (id < 0 || ground <= 0 || !_ids.Add(id)) return false;
        double w = ground;
        _w += w; _x += x * w; _y += y * w;
        _xx += x * x * w; _yy += y * y * w; _xy += x * y * w;
        _area = checked(_area + ground);
        int ix = (int)Math.Round(x, MidpointRounding.AwayFromZero);
        int iy = (int)Math.Round(y, MidpointRounding.AwayFromZero);
        _minX = Math.Min(_minX, ix); _maxX = Math.Max(_maxX, ix);
        _minY = Math.Min(_minY, iy); _maxY = Math.Max(_maxY, iy);
        return true;
    }

    internal bool TryBuild(out HierarchicalVassalZoneLabelMetrics result)
    {
        result = default;
        if (_w <= 0d || _ids.Count == 0) return false;
        double cx = _x / _w, cy = _y / _w;
        double xx = _xx / _w - cx * cx, yy = _yy / _w - cy * cy;
        double xy = _xy / _w - cx * cy;
        double angle = 0.5d * Math.Atan2(2d * xy, xx - yy) * 180d / Math.PI;
        angle = Math.Max(-35d, Math.Min(35d, angle));
        result = new HierarchicalVassalZoneLabelMetrics(cx, cy, (float)angle,
            _area, _maxX - _minX + 1, _maxY - _minY + 1);
        return true;
    }
}
```

- [ ] **Step 4: Run the rules project and verify GREEN**

Expected: `All AncientWarfare3 rules tests passed.`

- [ ] **Step 5: Commit**

```powershell
git add Code/core/policy/HierarchicalVassalZoneLabelAccumulator.cs Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalZoneLabelAccumulatorTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: define hierarchical zone label statistics"
```

### Task 2: Same-Pass Country Labels

**Files:**
- Modify: `Code/core/policy/AWMapModeMetaLibrary.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeLabelLayer.cs`
- Modify: `Tests/HierarchicalVassalMapNativeRenderSourceGuard.ps1`
- Modify: `Tests/HierarchicalVassalMapTerrainLabelSourceGuardTests.ps1`

- [ ] **Step 1: Make source guards require the native label pass**

```powershell
Require $metaLibrary 'HierarchicalVassalMapModeService.BeginNativeDrawPass();' `
    'native label pass does not begin with zone rendering'
Require $metaLibrary 'HierarchicalVassalMapModeService.RecordNativeDrawZone(zone);' `
    'native zone rendering does not contribute label statistics'
Require $metaLibrary 'HierarchicalVassalMapModeService.EndNativeDrawPass();' `
    'native labels are not finalized with zone rendering'
Forbid $labels 'HierarchicalVassalMapLabelRuntime.ProcessFrame();' `
    'visible labels still depend on the multi-frame worker runtime'
```

- [ ] **Step 2: Run both guards and verify RED**

Run `pwsh -File Tests/HierarchicalVassalMapNativeRenderSourceGuard.ps1` and `pwsh -File Tests/HierarchicalVassalMapTerrainLabelSourceGuardTests.ps1`.

Expected: missing `RecordNativeDrawZone` and worker-runtime dependency failures.

- [ ] **Step 3: Feed one sample per native Zone**

Pass the `hierarchical` flag into `DrawCityZones` and record before drawing:

```csharp
TileZone zone = pCity.zones[i];
if (hierarchical)
    HierarchicalVassalMapModeService.RecordNativeDrawZone(zone);
ZoneManager.drawBegin();
ZoneManager.drawZoneMeta(zone, pAsset, pGetter);
ZoneManager.drawEnd(zone);
```

`BeginNativeDrawPass` clears transient representative accumulators. `RecordNativeDrawZone` resolves the representative through the transient meta cache, remembers the representative even if a center is missing, and adds `zone.centerTile.posV` weighted by `zone.tiles_with_ground`.

- [ ] **Step 4: Finalize and publish all countries before leaving the draw**

Convert each accumulator result to the existing metrics and size rule:

```csharp
var geometry = new HierarchicalVassalMapModeGeometryMetrics
{
    Area = metrics.LandArea,
    Centroid = new Vector2((float)metrics.AnchorX, (float)metrics.AnchorY),
    SpanX = metrics.SpanX,
    SpanY = metrics.SpanY,
    Angle = metrics.Angle
};
int gap = HierarchicalVassalMapModeRules.CalculateCountryLabelGapLevel(
    displayName, metrics.SpanX);
var placement = new HierarchicalVassalMapModeLabelPlacement
{
    Centroid = geometry.Centroid,
    Angle = geometry.Angle,
    Size = HierarchicalVassalMapModeGeometry.CalculateLabelSize(
        geometry, displayName, gap)
};
```

Fallback order is capital `city_center`, first valid visible city center, then first valid Zone center. Publish the complete active-key set only after all representatives finalize; on failure keep the accepted same-layer nodes visible.

- [ ] **Step 5: Disconnect visible labels from worker processing**

Make the hierarchical label layer stop calling `HierarchicalVassalMapLabelRuntime.ProcessFrame`; activation, redraw, rename, ownership change, and layer switching must never create a discovery/build job. Keep legacy worker types compiled only for isolated tests.

- [ ] **Step 6: Run both guards and the complete rules project**

Expected: all PASS.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/policy/AWMapModeMetaLibrary.cs Code/core/policy/HierarchicalVassalMapModeService.cs Code/core/policy/HierarchicalVassalMapModeLabelLayer.cs Tests/HierarchicalVassalMapNativeRenderSourceGuard.ps1 Tests/HierarchicalVassalMapTerrainLabelSourceGuardTests.ps1
git commit -m "perf: publish country labels in native zone draw"
```

### Task 3: Native City Anchors and Material-Change Cache

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalMapModeService.cs`
- Modify: `Code/core/policy/HierarchicalVassalMapModeLabelLayer.cs`
- Modify: `Tests/HierarchicalVassalMapLabelLifecycleSourceGuard.ps1`

- [ ] **Step 1: Add failing lifecycle requirements**

```powershell
Require $service 'city.city_center' 'city labels do not use the native anchor'
Require $service 'HideRuntimeLabelsExcept(null);' `
    'layer switch does not hide previous labels immediately'
Require $service 'World.world?.zone_calculator?.setDrawnZonesDirty();' `
    'layer switch does not request the native redraw'
Require $labels 'PositionThreshold' 'label nodes have no position threshold'
Require $labels 'SizeThreshold' 'label nodes have no size threshold'
Require $labels 'AngleThreshold' 'label nodes have no angle threshold'
```

- [ ] **Step 2: Run the lifecycle guard and verify RED**

Run `pwsh -File Tests/HierarchicalVassalMapLabelLifecycleSourceGuard.ps1`.

Expected: at least one direct-anchor, immediate-hide, redraw, or cache requirement fails.

- [ ] **Step 3: Publish cities directly from live centers**

At `EndNativeDrawPass`, when `IsCityLayer`, iterate `GetVisibleCities()` once. Skip invalid cities, sum `tiles_with_ground` from their Zones without traversing Tile arrays, calculate the existing bounded city size, and publish at `city.city_center` with angle zero.

- [ ] **Step 4: Hide old labels in the option-change frame**

Before changing `_selectedLayer`, call `HierarchicalVassalMapModeLabelLayer.HideRuntimeLabelsExcept(null)`. Then clear the transient meta cache and call `World.world?.zone_calculator?.setDrawnZonesDirty()` so colors and labels share one redraw.

- [ ] **Step 5: Avoid redundant TextMesh mutation**

Each `LabelNode` stores its last text, position, size, angle, gap, style, and country/city flag. Compare with `0.1` position, `0.01` size, and `0.5` degree thresholds. Equivalent state only reactivates the node; changed state updates transform, text, character size, rotation, color, outline, and sorting.

- [ ] **Step 6: Run lifecycle guard and full rules tests**

Expected: both PASS.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/policy/HierarchicalVassalMapModeService.cs Code/core/policy/HierarchicalVassalMapModeLabelLayer.cs Tests/HierarchicalVassalMapLabelLifecycleSourceGuard.ps1
git commit -m "perf: cache native city and country label state"
```

### Task 4: Preserve Army Flags and Information

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalMapModeRules.cs`
- Modify: `Code/patch/AW_HierarchicalVassalMapMinimapPatch.cs`
- Modify: `Code/patch/AW_ArmyMapInformationMinimapPatch.cs`
- Create: `Tests/HierarchicalVassalMapArmyVisibilitySourceGuard.ps1`

- [ ] **Step 1: Write the failing visibility guard**

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$rules = Get-Content -Raw (Join-Path $root 'Code/core/policy/HierarchicalVassalMapModeRules.cs')
$map = Get-Content -Raw (Join-Path $root 'Code/patch/AW_HierarchicalVassalMapMinimapPatch.cs')
$info = Get-Content -Raw (Join-Path $root 'Code/patch/AW_ArmyMapInformationMinimapPatch.cs')
if (-not $rules.Contains('pAssetId == "armies"')) { throw 'Army asset is not retained' }
if ($map.Contains('private static bool SkipArmyFlags')) { throw 'drawArmies is skipped' }
if ($map.Contains('MinimapArmyFlagSortingOrder')) { throw 'Army sorting is overridden' }
if ($info.Contains('HierarchicalVassalMapModeService.IsActive()) return;') {
    throw 'Army information is disabled'
}
Write-Output 'HierarchicalVassalMapArmyVisibilitySourceGuard: PASS'
```

- [ ] **Step 2: Run the guard and verify RED**

Run `pwsh -File Tests/HierarchicalVassalMapArmyVisibilitySourceGuard.ps1`.

Expected: the Army asset is not retained or native `drawArmies` is skipped.

- [ ] **Step 3: Restore native Army rendering**

Add `armies` to `ShouldKeepMinimapQuantumAsset`. Remove the `drawArmies` prefix that returns false and the postfix that moves flags to `EffectsBack/-2`. Keep filtering ordinary unit avatars, king icons, leader icons, and other nonessential markers.

- [ ] **Step 4: Permit existing Army information text**

Remove only the hierarchical-mode early return from `AW_ArmyMapInformationMinimapPatch.DrawArmies_Postfix`; preserve the RTS setting and selected-kingdom gates.

- [ ] **Step 5: Run the Army guard and complete rules project**

Expected: both PASS.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/policy/HierarchicalVassalMapModeRules.cs Code/patch/AW_HierarchicalVassalMapMinimapPatch.cs Code/patch/AW_ArmyMapInformationMinimapPatch.cs Tests/HierarchicalVassalMapArmyVisibilitySourceGuard.ps1
git commit -m "fix: preserve armies in hierarchical map mode"
```

### Task 5: Regression and Live Acceptance

**Files:**
- Modify only if a guard exposes a missing MapMode contract.

- [ ] **Step 1: Run every hierarchical MapMode guard**

```powershell
Get-ChildItem Tests -Filter 'HierarchicalVassalMap*.ps1' | ForEach-Object {
    & pwsh -File $_.FullName
    if ($LASTEXITCODE -ne 0) { throw "failed: $($_.Name)" }
}
```

Expected: every script prints `PASS`.

- [ ] **Step 2: Run the complete rules project**

Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`.

Expected: `All AncientWarfare3 rules tests passed.`

- [ ] **Step 3: Check repository integrity**

Run `git diff --check` and `git status --short`.

Expected: no whitespace errors; only intended MapMode files are changed or committed. Do not stage `WarForceEliminationRules.cs` or its tests if another session still owns them.

- [ ] **Step 4: Deploy source and perform live acceptance**

Use the normal source-mod mirror procedure without compiling a DLL. On a large save verify:

1. Country colors and names appear in the same redraw, within one to two frames.
2. City names appear at city centers immediately.
3. City to country switching removes city text in the click frame.
4. Re-entering a layer reuses cached text unless name, position, size, angle, or style materially changed.
5. Army flags and enabled Army information remain visible on both hierarchy layers.
6. Drill-down, return to root, water cleanup, clicks, minimap capture, and exit to vanilla MapMode remain correct.
7. Recent benchmark shows no label Tile-copy or worker backlog.

- [ ] **Step 5: Commit an acceptance correction only if needed**

Stage only the exact MapMode correction and its regression guard, then commit with `git commit -m "fix: close hierarchical map acceptance gap"`. Skip this step when live acceptance needs no correction.
