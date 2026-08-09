# Military Governorate Warrior Appearance Implementation Plan

> **For Codex:** Execute this plan task by task with `superpowers:executing-plans` and test-driven development.

**Goal:** Keep military governorate rulers and designated successors mechanically king/heir while presenting them with warrior skins everywhere, expose direct governorate command nodes in the suzerain court, and provide a reusable internal visual-role switcher.

**Architecture:** A fixed-order `ActorVisualRoleResolver` accepts allocation-free runtime providers. The military governorate provider reads only the subject kingdom's in-memory governorate projection and resolves governor/successor actors to `Warrior`. Harmony presentation patches translate that role at the shared body, head and avatar boundaries without changing profession, AI, ownership or succession state. Lifecycle services invalidate affected actors when projections change. Court projection adds direct governorate actors as military nodes validated against their subject kingdom.

**Tech Stack:** C#/.NET Framework 4.8.1, Harmony, Unity/WorldBox publicized assemblies, repository rule tests and PowerShell source guards.

---

### Task 1: Add reusable visual-role rules and resolver

**Files:**
- Create: `Code/core/presentation/ActorVisualRoleRules.cs`
- Create: `Code/core/presentation/ActorVisualRoleResolver.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ActorVisualRoleRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Add failing tests for provider priority, `Default` fallback, role flag conversion and exception fall-through.
- [ ] Run the focused rules build and confirm it fails because the production types do not exist.
- [ ] Implement `Default`, `Civilian`, `Warrior`, `Leader`, `King`, immutable provider publication, ordered resolution and pure flag rules.
- [ ] Register the test slice and run it to green.

### Task 2: Resolve military governorate presentation from runtime projection

**Files:**
- Create: `Code/core/presentation/MilitaryGovernorateVisualRoleProvider.cs`
- Create: `Code/core/presentation/MilitaryGovernorateAppearanceService.cs`
- Modify: `Code/ModClass.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/ActorVisualRoleRulesTests.cs.txt`

- [ ] Add failing tests for active governor, designated successor, ordinary king/heir, stale actor, ended state and kingdom mismatch.
- [ ] Implement a provider that uses only actor, actor kingdom and `MilitaryGovernorateStore` runtime projection fields.
- [ ] Register the provider once during mod initialization and publish the fixed provider array.
- [ ] Run the focused rules test to green.

### Task 3: Apply visual roles at every live presentation boundary

**Files:**
- Create: `Code/patch/AW_ActorVisualRolePatch.cs`
- Create: `Tests/MilitaryGovernorateWarriorAppearanceSourceGuard.ps1`
- Modify: `Code/content/XiaTexturePatch.cs` only if required to preserve patch priority

- [ ] Add a failing source guard requiring patches for `Actor.getUnitTexturePath`, `Actor.checkSpriteHead` and `ActorAvatarData.setData`, and forbidding profession/AI mutation or data-store access from patches.
- [ ] Prefix body selection at Harmony `Priority.First`, reproducing vanilla warrior texture lookup for the actor's current asset/subspecies/mutation and declining safely when unavailable.
- [ ] Resolve special heads so a warrior override suppresses king head and follows vanilla warrior helmet/head behavior.
- [ ] Resolve avatar `is_king`/`is_warrior` flags from the same role without changing the actor.
- [ ] Run the source guard and production compile.

### Task 4: Invalidate graphics across governorate role transitions

**Files:**
- Modify: `Code/core/presentation/MilitaryGovernorateAppearanceService.cs`
- Modify: `Code/core/lineage/MilitaryGovernorateSuccessionService.cs`
- Modify: `Code/core/lineage/MilitaryGovernorateStore.cs`
- Modify: `Code/core/lineage/VassalService.cs`
- Test: `Tests/MilitaryGovernorateWarriorAppearanceSourceGuard.ps1`

- [ ] Extend the failing source guard to require old/new governor and successor invalidation on designation, replacement, committed succession, projection restoration/clear and vassal end.
- [ ] Implement idempotent invalidation through the existing graphics API, accepting missing/dead actors.
- [ ] Capture old/new IDs at mutation boundaries and refresh only affected live actors, without world scans or database reads from rendering paths.
- [ ] Run the source guard and relevant military governorate rule tests.

### Task 5: Add direct governorate command nodes to the suzerain court

**Files:**
- Modify: `Code/core/court/CourtReadModelService.cs`
- Modify: `Code/core/court/CourtPyramidRules.cs`
- Modify: `Code/ui/items/CourtActorNodeView.cs`
- Modify: `Locales/others.csv`
- Create: `Tests/AncientWarfare3.Rules.Tests/MilitaryGovernorateCourtRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Test: `Tests/MilitaryGovernorateWarriorAppearanceSourceGuard.ps1`

- [ ] Add failing rule tests for governor/successor rank, stable ordering, direct-only filtering and subject-kingdom actor validation.
- [ ] Add military governor and successor role IDs/ranks and display labels.
- [ ] Iterate `VassalService.GetVassals(suzerain)` only, select active direct military governorates, and add valid subject king/successor nodes.
- [ ] Preserve existing actor-window click behavior and add localization for both roles.
- [ ] Run focused court tests and the source guard.

### Task 6: Full verification and integration readiness

**Files:**
- Test: all files above

- [ ] Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/MilitaryGovernorateWarriorAppearanceSourceGuard.ps1`.
- [ ] Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1` and confirm scheduler migration files are unchanged.
- [ ] Run `dotnet build Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore`.
- [ ] Run `dotnet build AncientWarfare3.csproj -c Release --no-restore -p:TargetFrameworkVersion=v4.8.1` and require zero errors.
- [ ] Inspect `git diff --check`, `git diff --stat`, and ensure no generated DLL or unrelated file is included.
- [ ] Commit the implementation and use `superpowers:finishing-a-development-branch` for merge/push handoff.
