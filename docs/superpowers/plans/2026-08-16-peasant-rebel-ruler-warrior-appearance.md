# Peasant Rebel Ruler Warrior Appearance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the king and designated heir of both founding-rebel and bandit peasant-rebel realms with their race's warrior presentation without changing gameplay identity.

**Architecture:** Extend the pure visual-role rules, then add one peasant-rebel provider registered through the existing appearance bootstrap. Route and heir transitions invalidate cached actor graphics while `AW_ActorVisualRolePatch` continues to own all texture and portrait selection.

**Tech Stack:** C# 11/net48, WorldBox Actor textures, Harmony, detached net9 rules tests, PowerShell source guards.

---

## File Map

- `Code/core/presentation/ActorVisualRoleRules.cs`: pure rebel king/heir role decision.
- `Code/core/presentation/PeasantRebelVisualRoleProvider.cs`: live rebel marker, kingdom, king, and heir lookup.
- `Code/core/presentation/PeasantRebelAppearanceService.cs`: provider registration and best-effort sprite invalidation.
- `Code/ModClass.cs`: register the provider at mod load.
- `Code/core/lineage/PeasantRebelRouteService.cs`: invalidate roles when rebel route state is entered, restored, or removed.
- `Tests/AncientWarfare3.Rules.Tests/ActorVisualRoleRulesTests.cs.txt`: pure role cases.
- `Tests/PeasantRebelWarriorAppearanceSourceGuard.ps1`: integration and no-gameplay-mutation boundary.

### Task 1: Dynamic Rebel King And Heir Warrior Presentation

- [ ] **Step 1: Write failing pure tests and source guard.**

Add assertions to `ActorVisualRoleRulesTests.cs.txt` for the wished-for API:

```csharp
Equal(ActorVisualRole.Warrior,
    ActorVisualRoleRules.ResolvePeasantRebelRole(
        pRebelActive: true, pActorAlive: true,
        pActorKingdomMatches: true, pActorId: 11L,
        pKingActorId: 11L, pHeirActorId: 12L),
    "founding or bandit rebel king uses warrior presentation");
Equal(ActorVisualRole.Warrior,
    ActorVisualRoleRules.ResolvePeasantRebelRole(
        true, true, true, 12L, 11L, 12L),
    "rebel heir uses warrior presentation");
Equal(ActorVisualRole.Default,
    ActorVisualRoleRules.ResolvePeasantRebelRole(
        false, true, true, 11L, 11L, 12L),
    "former rebel king restores vanilla presentation");
Equal(ActorVisualRole.Default,
    ActorVisualRoleRules.ResolvePeasantRebelRole(
        true, true, true, 13L, 11L, 12L),
    "ordinary rebel actor keeps vanilla presentation");
```

Create `Tests/PeasantRebelWarriorAppearanceSourceGuard.ps1` requiring provider registration, `MandateRebelService.IsRebelKingdom`, `KINGDOM_HEIR_ID`, `ActorVisualRole.Warrior`, route invalidation, and `clearGraphicsFully`; reject `setProfession`, `joinKingdom`, trait writes, army mutation, and SQLite access in provider/presentation files.

- [ ] **Step 2: Run RED verification.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/PeasantRebelWarriorAppearanceSourceGuard.ps1
```

Expected: the rules build fails because `ResolvePeasantRebelRole` is absent, and the guard fails because the provider is absent.

- [ ] **Step 3: Implement the pure rule and provider.**

Add the pure method:

```csharp
public static ActorVisualRole ResolvePeasantRebelRole(
    bool pRebelActive, bool pActorAlive,
    bool pActorKingdomMatches, long pActorId,
    long pKingActorId, long pHeirActorId)
{
    if (!pRebelActive || !pActorAlive ||
        !pActorKingdomMatches || pActorId < 0)
        return ActorVisualRole.Default;
    return pActorId == pKingActorId || pActorId == pHeirActorId
        ? ActorVisualRole.Warrior
        : ActorVisualRole.Default;
}
```

The provider reads `MandateRebelService.IsRebelKingdom(subject)`, the current king ID, and `LineageKeys.KINGDOM_HEIR_ID`, verifies the actor still belongs to that kingdom and is alive, then delegates to the pure rule.

- [ ] **Step 4: Register and invalidate presentation.**

Add `PeasantRebelAppearanceService.Initialize()` beside `MilitaryGovernorateAppearanceService.Initialize()` in `ModClass.OnModLoad`. Its `OnProjectionChanged(Kingdom)` resolves the current king and heir and calls `clearGraphicsFully()` best-effort. Invoke it after route metadata becomes active and before `RemoveRuntime` loses the old projection; existing `HeirService.SetHeirFlag` continues to invalidate heir replacements.

- [ ] **Step 5: Run GREEN verification and production build.**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/PeasantRebelWarriorAppearanceSourceGuard.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/MilitaryGovernorateWarriorAppearanceSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
```

Expected: all commands exit 0; production build reports zero errors and zero warnings.

- [ ] **Step 6: Commit the appearance increment.**

```powershell
git add Code/ModClass.cs Code/core/presentation/ActorVisualRoleRules.cs Code/core/presentation/PeasantRebelVisualRoleProvider.cs Code/core/presentation/PeasantRebelAppearanceService.cs Code/core/lineage/PeasantRebelRouteService.cs Tests/AncientWarfare3.Rules.Tests/ActorVisualRoleRulesTests.cs.txt Tests/PeasantRebelWarriorAppearanceSourceGuard.ps1
git commit -m "feat: show rebel rulers with warrior appearance"
```
