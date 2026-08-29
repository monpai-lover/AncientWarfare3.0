# Xia Official Actor Textures Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace standard Xia actor artwork and select green-to-purple official clothing from the actor's live rank.

**Architecture:** A pure `XiaActorTextureRules` class owns rank tiers, stable head selection, and skin-list expansion. Existing Xia Harmony patches consume those rules; the career projection invalidates graphics only when an actor crosses a visual tier. Resource replacement is limited to standard Xia actor directories and preserves bandit, child, slave, clan, and special assets.

**Tech Stack:** C#/.NET Framework 4.8, Harmony, WorldBox actor texture APIs, PowerShell resource guards, PNG sprite directories.

---

### Task 1: Pure Texture Selection Rules

**Files:**
- Create: `Code/core/presentation/XiaActorTextureRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/XiaActorTextureRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing boundary tests**

Add assertions for tier `0` at rank 0, tier `1` at ranks 1 and 6, tier `2` at ranks 7 and 12, tier `3` at ranks 13 and 18, clamping above 18, stable positive head indices, and cycling `female_1/female_2` to three slots.

```csharp
Equal(0, XiaActorTextureRules.ResolveOfficialTier(0), "unranked");
Equal(1, XiaActorTextureRules.ResolveOfficialTier(6), "low boundary");
Equal(2, XiaActorTextureRules.ResolveOfficialTier(7), "middle boundary");
Equal(3, XiaActorTextureRules.ResolveOfficialTier(13), "high boundary");
SequenceEqual(new[] { "female_1", "female_2", "female_1" },
    XiaActorTextureRules.ExpandSkins(new[] { "female_1", "female_2" }, 3));
```

- [ ] **Step 2: Run the rules project and verify failure**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: compilation fails because `XiaActorTextureRules` does not exist.

- [ ] **Step 3: Implement the pure rules**

Create constants for no/low/middle/high tier and implement:

```csharp
public static int ResolveOfficialTier(int pRank);
public static string ResolveOfficialBodyDirectory(int pRank);
public static string ResolveOfficialHeadPath(int pRank);
public static int StableVariantIndex(long pActorId, int pCount);
public static string[] ExpandSkins(string[] pSkins, int pCount);
```

Use `OfficialCareerRankRules.ClampRank`; return `leader_<tier>` and `heads_leader/head_<tier-1>` only for formal ranks.

- [ ] **Step 4: Run the rules project and verify pass**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: exit code 0 and the normal rules-test completion message.

### Task 2: Runtime Body and Head Selection

**Files:**
- Modify: `Code/content/XiaTexturePatch.cs`
- Modify: `Code/content/XiaTextures.cs`
- Modify: `Code/patch/AW_ActorVisualRolePatch.cs`
- Create: `Tests/XiaOfficialActorTextureSourceGuard.ps1`

- [ ] **Step 1: Write the failing source/resource guard**

Require the runtime source to contain rank reads, `ResolveOfficialBodyDirectory`, `ResolveOfficialHeadPath`, `leader_1` fallback, and new ruler/heir/warrior head paths. Reject `texture_path_leader = pBasePath + "leader"`.

- [ ] **Step 2: Run the guard and verify failure**

Run: `powershell -ExecutionPolicy Bypass -File Tests/XiaOfficialActorTextureSourceGuard.ps1`

Expected: failure reporting that rank-based official texture integration is missing.

- [ ] **Step 3: Integrate live body selection**

In `XiaTexturePatch`, keep civ-monkey handling first, then resolve Xia king/heir semantics, then read `LineageKeys.OFFICER_RANK` and return `XiaRace.TEXTURE_PATH + bodyDirectory`. Ranked officials override ordinary leader/warrior presentation, while registered special visual roles remain handled by the earlier priority-first patch.

- [ ] **Step 4: Integrate heads and standard fallbacks**

In `AW_ActorVisualRolePatch`, keep bandit and registered visual roles first. For ordinary Xia actors, apply dedicated king, heir, official-tier, unranked city-leader, and deterministic warrior heads through `ActorAnimationLoader.getHeadSpecial`.

In `XiaTextures`, set `texture_path_leader` to `leader_1`, point special defaults at the new head directories, and expand shorter skin arrays to the maximum discovered variant count.

- [ ] **Step 5: Run the guard and rules tests**

Run: `powershell -ExecutionPolicy Bypass -File Tests/XiaOfficialActorTextureSourceGuard.ps1`

Expected: `Xia official actor texture source guard passed.`

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

Expected: exit code 0.

### Task 3: Refresh Appearance on Rank-Tier Changes

**Files:**
- Modify: `Code/core/court/OfficialCareerStateService.cs`
- Modify: `Tests/XiaOfficialActorTextureSourceGuard.ps1`

- [ ] **Step 1: Extend the guard for tier-aware invalidation**

Require `ProjectHotState` to capture the previous rank, compare old and new `ResolveOfficialTier` values, and call `clearGraphicsFully()` only for a Xia actor whose visual tier changed.

- [ ] **Step 2: Run the guard and verify failure**

Run: `powershell -ExecutionPolicy Bypass -File Tests/XiaOfficialActorTextureSourceGuard.ps1`

Expected: failure reporting missing rank-tier invalidation.

- [ ] **Step 3: Add minimal invalidation**

Compute `nextRank`, read `previousRank` before writing it, then invalidate only when:

```csharp
LineageService.IsXia(pActor) &&
XiaActorTextureRules.ResolveOfficialTier(previousRank) !=
XiaActorTextureRules.ResolveOfficialTier(nextRank)
```

Set `dirty_sprite_head = true` and call `clearGraphicsFully()` after projecting hot state.

- [ ] **Step 4: Run guard and rules tests**

Run both commands from Task 2 Step 5 and expect exit code 0.

### Task 4: Replace Standard Xia Actor Resources

**Files:**
- Replace: `GameResources/actors/species/civs/Xia/male_*`
- Replace: `GameResources/actors/species/civs/Xia/female_*`
- Replace: `GameResources/actors/species/civs/Xia/warrior_*`
- Replace: `GameResources/actors/species/civs/Xia/king`
- Replace: `GameResources/actors/species/civs/Xia/heir`
- Remove: `GameResources/actors/species/civs/Xia/leader`
- Create: `GameResources/actors/species/civs/Xia/leader_1`
- Create: `GameResources/actors/species/civs/Xia/leader_2`
- Create: `GameResources/actors/species/civs/Xia/leader_3`
- Replace/Create: `GameResources/actors/species/civs/Xia/heads_male`, `heads_female`, `heads_king`, `heads_heir`, `heads_leader`, `heads_warrior`

- [ ] **Step 1: Verify deletion targets**

Resolve every standard actor target and confirm it is a child of `GameResources/actors/species/civs/Xia`. Do not include `bandit_*`, `heads_bandit`, `child`, `slave`, `clans`, or `special`.

- [ ] **Step 2: Remove obsolete standard variants and copy source assets**

Delete only the listed standard body/head directories, then recursively copy each directory from the workspace actor-asset source. Preserve filenames and `sprites.json` byte-for-byte.

- [ ] **Step 3: Verify inventories**

Run: `powershell -ExecutionPolicy Bypass -File Tests/XiaOfficialActorTextureSourceGuard.ps1`

Expected: all configured body/head directories exist, old numbered variants and the legacy `leader` directory are absent, and preserved resources still exist.

- [ ] **Step 4: Build the mod**

Run: `dotnet build AncientWarfare3.csproj`

Expected: build succeeds with no errors.

- [ ] **Step 5: Check staged scope and commit implementation**

Run: `git diff --check`

Expected: no whitespace errors.

Stage only the new texture rules/tests, three integration files, career projection file, source guard, and Xia actor resources. Do not stage unrelated pre-existing working-tree changes.
