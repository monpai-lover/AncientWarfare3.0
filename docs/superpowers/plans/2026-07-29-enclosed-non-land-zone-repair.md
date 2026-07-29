# Enclosed Non-Land Zone Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow a fully enclosed unowned component containing non-land Zones to be assigned to the best neighbouring city.

**Architecture:** Keep the existing bounded connected-component repair and same-kingdom boundary validation. Remove only the groundless-component rejection and its now-unused runtime state; world-edge, mixed-kingdom, live-owner, and 64-Zone budget checks remain authoritative.

**Tech Stack:** C# 10, WorldBox `TileZone`/`City` APIs, PowerShell source guards, .NET 9 rule-test executable.

---

### Task 1: Permit Enclosed Non-Land Components

**Files:**
- Modify: `Tests/EnclosedUnownedZoneRulesTests.cs.txt`
- Modify: `Code/core/lineage/EnclosedUnownedZoneRules.cs`
- Modify: `Code/core/lineage/EnclosedUnownedZoneRepairService.cs`
- Modify: `Tests/EnclosedUnownedZoneSourceGuard.ps1`

- [ ] **Step 1: Write the failing groundless-component rule test**

Add `GroundlessComponentUsesWholeBoundary();` to `Main` and add:

```csharp
private static void GroundlessComponentUsesWholeBoundary()
{
    EnclosedZoneNeighbourFacts city = Neighbour(82L, 9L, 10, 10);
    Equal(82L, EnclosedUnownedZoneRules.SelectComponentTargetCity(
            touchesWorldEdge: false,
            containsGroundlessZone: true,
            exceededZoneBudget: false,
            componentCenterX: 10,
            componentCenterY: 10,
            ownedBoundary: new[] { city, city, city, city }),
        "an enclosed non-land component is assigned");
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests/EnclosedUnownedZoneRulesTests.csproj -c Release
```

Expected: exit code 1 with `an enclosed non-land component is assigned: expected 82, got -1`.

- [ ] **Step 3: Implement the minimal behavioural change**

Change the component guard in `EnclosedUnownedZoneRules.SelectComponentTargetCity` from:

```csharp
if (touchesWorldEdge || containsGroundlessZone ||
    exceededZoneBudget || ownedBoundary == null ||
    ownedBoundary.Count == 0)
```

to:

```csharp
if (touchesWorldEdge || exceededZoneBudget ||
    ownedBoundary == null || ownedBoundary.Count == 0)
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the command from Step 2. Expected: exit code 0 and `Enclosed unowned Zone rule tests passed.`

- [ ] **Step 5: Remove the obsolete groundless parameter and runtime state**

Change the rule signature to:

```csharp
public static long SelectComponentTargetCity(bool touchesWorldEdge,
    bool exceededZoneBudget, int componentCenterX, int componentCenterY,
    IReadOnlyList<EnclosedZoneNeighbourFacts> ownedBoundary)
```

Remove `containsGroundlessZone` named arguments from all test calls. In `EnclosedUnownedZoneRepairService.TryRepair`, remove:

```csharp
bool containsGroundlessZone = false;
```

and:

```csharp
if (current.tiles_with_ground <= 0)
    containsGroundlessZone = true;
```

Then call the rule as:

```csharp
EnclosedUnownedZoneRules.SelectComponentTargetCity(
    touchesWorldEdge, exceededZoneBudget,
    centerX, centerY, boundaryFacts);
```

- [ ] **Step 6: Strengthen the source guard**

Add the rules file to `EnclosedUnownedZoneSourceGuard.ps1`, then require `containsGroundlessZone` to be absent from both the rules and service sources:

```powershell
Require-Absent $rules 'containsGroundlessZone' `
    'Rule selection must not reject enclosed non-land components.'
Require-Absent $service 'containsGroundlessZone' `
    'Runtime repair must not reject or track groundless components.'
```

- [ ] **Step 7: Run focused and broad verification**

Run:

```powershell
dotnet run --project Tests/EnclosedUnownedZoneRulesTests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/EnclosedUnownedZoneSourceGuard.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
dotnet build AncientWarfare3.csproj -c Debug --nologo
dotnet build AncientWarfare3.csproj -c Release --nologo
```

Expected: all commands exit 0; both builds report 0 errors.

- [ ] **Step 8: Preserve overlapping working-tree ownership**

Run `git diff --check` on the four scoped files. Do not stage or commit the overlapping production/test files because they already contain uncommitted work from the earlier enclosed-component implementation; report the scoped diff explicitly.

### Task 2: Deploy The Production Change

**Files:**
- Deploy: `Code/core/lineage/EnclosedUnownedZoneRules.cs`
- Deploy: `Code/core/lineage/EnclosedUnownedZoneRepairService.cs`

- [ ] **Step 1: Confirm WorldBox is closed and record `.runtime` integrity**

Require no running `WorldBox` process. Record the installed `.runtime` recursive file count and deterministic SHA-256 tree digest.

- [ ] **Step 2: Copy only the two production files**

Copy the two files above to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0` without mirror deletion and without copying tests, docs, build output, or `.runtime`.

- [ ] **Step 3: Verify deployment and installed build**

Compare source/destination SHA-256 for both files, require the `.runtime` count and digest to remain identical, then run Debug and Release builds from the installed mod directory. Expected: hashes match, `.runtime` is unchanged, and both builds exit 0 with 0 errors.

---

## Self-Review

- Spec coverage: the rule test covers enclosed non-land assignment; existing tests retain world-edge, mixed-kingdom, and budget rejection; runtime traversal and city selection remain unchanged.
- Scope: only the obsolete groundless rejection and tracking are removed. No Zone size, expansion, border-shrink, or ownership-transfer behavior changes.
- Placeholders: none.
- Type consistency: the revised five-argument component selector is used consistently in tests and runtime.
