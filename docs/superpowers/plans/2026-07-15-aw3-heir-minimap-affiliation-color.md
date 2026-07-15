# AW3 Heir Minimap Affiliation Color Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make an heir minimap marker use the heir actor's live kingdom color immediately after affiliation changes while preserving the legal succession record.

**Architecture:** Keep legal heir lookup indexed by kingdom, but add a read-only display lookup that does not reject temporary foreign affiliation. Resolve the marker's visual kingdom from the actor's current kingdom first and the legal realm second during the existing minimap draw pass.

**Tech Stack:** C# 9, Harmony, Unity/WorldBox quantum sprites, .NET 9 rule-test executable, PowerShell source guards.

---

### Task 1: Lock the visual ownership contract

**Files:**
- Create: `Code/core/lineage/HeirMinimapVisualRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs`

- [ ] **Step 1: Write failing rule tests**

Add assertions that `ResolveVisualKingdomId(101, 202)` returns `202`, that
`ResolveVisualKingdomId(101, -1)` returns `101`, and that two missing owners
return `-1`.

- [ ] **Step 2: Run the rule tests and verify RED**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: compilation fails because `HeirMinimapVisualRules` does not exist.

- [ ] **Step 3: Implement the minimal rule**

Create a pure rule whose implementation is:

```csharp
public static long ResolveVisualKingdomId(long pLegalKingdomId, long pCurrentKingdomId)
{
    return pCurrentKingdomId >= 0 ? pCurrentKingdomId : pLegalKingdomId;
}
```

- [ ] **Step 4: Link the production rule and verify GREEN**

Add the production file to the rule-test project and rerun the command. Expected:
all rule assertions pass after any pre-existing red academy rule dependency is
completed.

### Task 2: Draw from live affiliation without changing succession

**Files:**
- Modify: `Code/core/lineage/HeirService.cs`
- Modify: `Code/patch/AW_HeirMinimapPatch.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] **Step 1: Write failing source guards**

Require the minimap patch to call `PeekStoredHeirForMinimap`, resolve
`visualKingdom`, and color with `visualKingdom.getColor()`. Retain the existing
guard forbidding `foreach (Actor unit in kingdom.getUnits())`.

- [ ] **Step 2: Run source guards and verify RED**

Run: `powershell -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1`

Expected: the new lookup/visual-owner guards fail against the current patch.

- [ ] **Step 3: Add the read-only display lookup**

Read `KINGDOM_HEIR_ID`, resolve the actor from `World.world.units`, and return it
only when its data exists, it is alive, and its `IS_HEIR` flag is true. Do not
call `IsRegisteredCandidateEligible` and do not mutate kingdom or actor data.

- [ ] **Step 4: Resolve and apply the current visual kingdom**

Use `HeirMinimapVisualRules.ResolveVisualKingdomId` to choose `unit.kingdom`
when present and the legal kingdom otherwise. Use the resulting kingdom for
`DynamicSprites.getIcon` and for the capital fallback in scale anchoring.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run both the rule-test command and `Tests/SourceGuardTests.ps1`. Expected: all
assertions and source guards pass.

### Task 3: Full verification

**Files:**
- Verify only

- [ ] **Step 1: Build Debug**

Run the repository's established Debug build command and require zero errors.

- [ ] **Step 2: Build Release**

Run the repository's established Release build command and require zero errors.

- [ ] **Step 3: Check the working diff**

Run `git diff --check` and inspect the scoped diff to confirm no existing user
changes were reverted.

- [ ] **Step 4: Deploy with the repository's established deployment command**

Copy the verified source/build output to the installed mod and compare hashes so
the installed heir patch and compiled mod match the workspace.
