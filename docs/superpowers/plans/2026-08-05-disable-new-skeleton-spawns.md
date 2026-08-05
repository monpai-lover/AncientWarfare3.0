# Disable New Skeleton Spawns Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with verification checkpoints.

**Goal:** Stop vanilla WorldBox from creating new skeleton actors while preserving existing skeletons.

**Architecture:** Keep the policy as a pure `SkeletonSpawnRules` decision and use a Harmony Prefix on the vanilla `ActionLibrary.spawnSkeleton` boundary. The Prefix returns `false` before the original method, covering necromancer behavior, spell casting, magic rites, and skeleton transformations that converge on this method.

**Tech Stack:** C# net48 mod, HarmonyLib, the existing `AncientWarfare3.Rules.Tests` executable test runner.

---

### Task 1: Add the failing skeleton-spawn policy test

**Files:**
- Create: `Code/core/lineage/SkeletonSpawnRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Create/modify: `Tests/AncientWarfare3.Rules.Tests/SkeletonSpawnRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing test**

Add a test that calls `SkeletonSpawnRules.ShouldBlockNewSpawn()` and expects
`true`. Add a focused runner switch `--skeleton-spawn` that prints
`Skeleton spawn rules passed.`.

- [ ] **Step 2: Run the focused test and confirm RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests -c Release -- --skeleton-spawn
```

Expected: compilation fails because `SkeletonSpawnRules` is not defined yet.

- [ ] **Step 3: Add the minimal pure rule**

Create `SkeletonSpawnRules` with one method:

```csharp
public static bool ShouldBlockNewSpawn() => true;
```

Register the production file in the test project.

- [ ] **Step 4: Run the focused test and confirm GREEN**

Run the same command and expect `Skeleton spawn rules passed.`.

### Task 2: Add the Harmony generation guard

**Files:**
- Create: `Code/patch/AW_SkeletonSpawnPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/SkeletonSpawnRulesTests.cs.txt`

- [ ] **Step 1: Add a source-boundary regression assertion**

Read `Code/patch/AW_SkeletonSpawnPatch.cs` in the test and assert that the
patch targets `ActionLibrary.spawnSkeleton`, calls
`SkeletonSpawnRules.ShouldBlockNewSpawn()`, and returns `false` when the rule
is enabled.

- [ ] **Step 2: Implement the minimal Prefix**

Add a Harmony patch whose Prefix is:

```csharp
[HarmonyPrefix]
public static bool Prefix()
{
    return !SkeletonSpawnRules.ShouldBlockNewSpawn();
}
```

The Prefix must not delete existing actors or touch world state.

- [ ] **Step 3: Run the focused test**

Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests -c Release -- --skeleton-spawn` and expect the focused test to pass.

### Task 3: Full verification

**Files:** None beyond the files above.

- [ ] **Step 1: Run the full rules suite**

Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests -c Release` and expect `Rule tests passed.`.

- [ ] **Step 2: Build the mod**

Run:

```powershell
$pkg = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netframework.referenceassemblies.net48\1.0.3'
$ref = Join-Path $pkg 'build\.NETFramework\v4.8'
$root = Join-Path $pkg 'build\'
dotnet build AncientWarfare3.csproj -c Release --no-restore `
  -p:FrameworkPathOverride=$ref -p:TargetFrameworkRootPath=$root
```

Expect 0 warnings and 0 errors.

- [ ] **Step 3: Check the worktree**

Run `git diff --check` and inspect `git status --short`; keep the prior
surname-fix changes intact and do not stage them with this task.
