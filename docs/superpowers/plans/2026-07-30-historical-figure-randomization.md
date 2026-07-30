# Historical Figure Randomization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep Ji Fa first, select later eligible historical figures randomly, and display only pure state names.

**Architecture:** Pure rules select one registry entry from durable state and eligibility. The runtime service preserves reservation/commit safety and sends `KingdomName`, rather than a directional dynasty label, to public messages.

**Tech Stack:** C# 11, WorldBox/NeoModLoader APIs, AW3 rules test console project.

---

### Task 1: Add Testable Post-Ji-Fa Candidate Selection

**Files:**
- Modify: `Code/content/figures/HistoricalFigureSpawnRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureSpawnRulesTests.cs.txt`

- [ ] **Step 1: Write the failing selection cases**

```csharp
Equal(0, HistoricalFigureSpawnRules.SelectCandidate(false,
    new[] { 0, 1, 2 }, new[] { 0, 0, 0 },
    new[] { true, true, true }, 2));
Equal(2, HistoricalFigureSpawnRules.SelectCandidate(true,
    new[] { 0, 1, 2, 3 }, new[] { 1, 0, 0, 0 },
    new[] { true, false, true, true }, 1));
```

Also assert pending state and an empty candidate set return `-1`; a living
earlier figure cannot block a later candidate once Ji Fa has committed.

- [ ] **Step 2: Run `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --nologo`**

Expected: compilation failure naming `SelectCandidate`.

- [ ] **Step 3: Implement the rules API**

```csharp
public static int SelectCandidate(bool jiFaCommitted, int[] registryIndices,
    int[] spawnStates, bool[] eligible, int randomOrdinal)
```

Before Ji Fa commits, return only registry index zero when available and
eligible. Afterwards collect available, eligible, nonzero entries and return
the element at `randomOrdinal % candidates.Count`. Return `-1` for invalid
arrays, pending reservations, and no candidates. Never use spawn order,
founding year, death state, or predecessor order after Ji Fa.

- [ ] **Step 4: Re-run the rules test and commit**

Expected: new selection assertions pass. Commit with
`git add Code/content/figures/HistoricalFigureSpawnRules.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureSpawnRulesTests.cs.txt`
then `git commit -m "feat: randomize historical figures after ji fa"`.

### Task 2: Connect Runtime Selection To Durable State

**Files:**
- Modify: `Code/content/figures/HistoricalFigureService.cs`
- Modify: `Code/core/db/FigureStateTableItem.cs` only if a complete read-only spawn-state snapshot is missing
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureSpawnRulesTests.cs.txt`

- [ ] **Step 1: Write a failing runtime source guard**

Require `TrySpawnOn` to snapshot durable spawn states, calculate integration
eligibility for every definition, call `SelectCandidate`, and retain
`TryReserveSpawn` before `addTrait`.

- [ ] **Step 2: Run the rules test**

Expected: guard failure while the old `NextSpawnableIndex()` path remains.

- [ ] **Step 3: Implement one RNG selection adapter**

Build registry-indexed arrays, derive Ji Fa commit state from index zero, then
call `SelectCandidate(jiFaCommitted, registryIndices, spawnStates, eligible,
Rng.Next())`. Retrieve a definition only after a valid index and retain its
chance roll. Preserve live-figure, mandate, integration, reservation, commit,
and rollback guards. A missed chance or failed reservation changes no state.

- [ ] **Step 4: Re-run rules tests and build**

Run `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --nologo`, then
`dotnet build AncientWarfare3.csproj -c Release --nologo`. Record but do not
repair the known unrelated school-portrait baseline regression. Commit runtime
changes with message `feat: choose later historical figures at random`.

### Task 3: Project Pure State Names In Public Messages

**Files:**
- Modify: `Code/content/figures/HistoricalFigureSpawnRules.cs`
- Modify: `Code/content/figures/HistoricalFigureService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalFigureSpawnRulesTests.cs.txt`

- [ ] **Step 1: Write the failing name-projection tests**

```csharp
Equal("Han", HistoricalFigureSpawnRules.ProjectStateName("Western Han", "Han"));
Equal("Han", HistoricalFigureSpawnRules.ProjectStateName("Eastern Han", "Han"));
Equal("Qin", HistoricalFigureSpawnRules.ProjectStateName("Qin", "Qin"));
Equal("Liu Bang - Han", HistoricalFigureSpawnRules.FormatLocalizedLabel("Liu Bang", "Han"));
```

The source guard must prove `AnnounceFigure` uses projected `KingdomName`, not
`DynastyLocaleKey`.

- [ ] **Step 2: Run the rules test**

Expected: compilation failure naming `ProjectStateName`.

- [ ] **Step 3: Implement projection and message use**

```csharp
public static string ProjectStateName(string dynastyName, string kingdomName) {
    return string.IsNullOrWhiteSpace(kingdomName) ? dynastyName ?? "" : kingdomName;
}
```

Keep dynasty metadata unchanged. Pass `ProjectStateName(pDef.DynastyName,
pDef.KingdomName)` to `FormatLocalizedLabel`; Western and Eastern Han will
therefore both appear as Han.

- [ ] **Step 4: Run final verification and commit**

Run the rules test, `dotnet build AncientWarfare3.csproj -c Release --nologo`,
and `git diff --check`. Commit the changed rules, service, and test with
`git commit -m "fix: use pure state names for historical figures"`.
