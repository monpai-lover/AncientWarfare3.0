# AW3 Minimap Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every AW3 actor minimap marker derive from its authoritative identity record, survive valid cross-kingdom heir transitions, and render once per actor.

**Architecture:** Keep per-kingdom heir records and FigureState as authoritative data. Maintain `IS_HEIR` as derived state only during mutation paths, and keep all per-frame work indexed and allocation-free by reusing one marker-id set.

**Tech Stack:** C# 9, Harmony, Unity/WorldBox quantum sprites, .NET 9 rule executable, PowerShell source guards.

---

### Task 1: Lock Heir Registration Semantics

**Files:**
- Create: `Code/core/lineage/HeirRegistrationRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Add failing assertions that zero other registrations clears `IS_HEIR`,
  while one or more other registrations preserve it.
- [ ] Run the Release rule executable and confirm compilation fails because
  `HeirRegistrationRules` does not exist.
- [ ] Implement:

```csharp
public static bool ShouldClearGlobalFlag(int pOtherLiveRegistrations)
{
    return pOtherLiveRegistrations <= 0;
}
```

- [ ] Rerun the Release rule executable and confirm the new assertions pass.

### Task 2: Preserve Cross-Kingdom Heir State

**Files:**
- Modify: `Code/core/lineage/HeirService.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add failing guards requiring `CountOtherLiveHeirRegistrations` and
  `HeirRegistrationRules.ShouldClearGlobalFlag` in `ClearOldHeirFlag`.
- [ ] Add a failing guard forbidding the minimap lookup from reading
  `heir.data.get(LineageKeys.IS_HEIR`.
- [ ] Run source guards and confirm those checks fail for the expected reasons.
- [ ] Count only other non-rekt civilization kingdoms with cities and the same
  `KINGDOM_HEIR_ID`; clear the actor flag only when the count is zero.
- [ ] Make `PeekStoredHeirForMinimap` return the stored living actor directly,
  leaving succession eligibility and mutation untouched.
- [ ] Rerun focused tests and source guards.

### Task 3: Deduplicate Heir Markers

**Files:**
- Create: `Code/core/lineage/MinimapActorMarkerRules.cs`
- Modify: `Code/patch/AW_HeirMinimapPatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add failing assertions that the first reservation of a valid actor id
  succeeds, a duplicate fails, and a negative id fails.
- [ ] Run the rule executable and confirm the missing rule causes RED.
- [ ] Implement `TryReserve(HashSet<long>, long)` with `HashSet.Add`.
- [ ] Add one static `HashSet<long>` to the heir patch, clear it once per draw,
  and skip actors whose id cannot be reserved.
- [ ] Preserve the existing current-affiliation color, hover scaling, visibility
  filters and bounded quantum-sprite growth.
- [ ] Rerun focused tests and source guards.

### Task 4: Use FigureState for Historical Markers

**Files:**
- Modify: `Code/content/figures/HistoricalFigureMinimapRules.cs`
- Modify: `Code/patch/AW_FigurePatch.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Change the rule-test call to require `isRegisteredFigure` and add a
  failing trait-lookalike case with no registration.
- [ ] Run the rule executable and confirm RED from the old signature.
- [ ] Replace the two trait inputs with one authoritative registration input.
- [ ] Resolve it through `FigureStateStore.IndexOfActor(unit.data.id) >= 0` in
  the existing bounded favorite pass.
- [ ] Require that lookup in source guards and forbid `TRAIT_FIGURE` and
  `TRAIT_FIRST` in the minimap patch.
- [ ] Rerun focused tests and source guards.

### Task 5: Full Verification and Deployment

**Files:**
- Verify all changed source and tests.
- Synchronize: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] Run Release rule tests and all source guards.
- [ ] Build `AncientWarfare3.csproj` in Debug and Release with zero warnings and
  zero errors.
- [ ] Run `git diff --check` and inspect the scoped minimap diff.
- [ ] Record the runtime database SHA-256, synchronize the mod while excluding
  `.git`, `.runtime`, `Tests`, `docs`, `bin` and `obj`, then confirm the database
  hash is unchanged.
- [ ] Launch WorldBox and confirm both minimap Harmony patches load without AW3
  errors, then stop the verification process.

