# Classic Civilization Lifespan Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Set the human, elf and dwarf ActorAsset genome lifespan to `70f` without affecting other races or additive lifespan bonuses.

**Architecture:** Add a small idempotent content initializer that resolves the three vanilla ActorAssets after registration and replaces only their genome `lifespan` value. Register it in the existing content startup sequence after vanilla assets and Xia cloning are available.

**Tech Stack:** C#, WorldBox ActorAsset library, PowerShell source guard, .NET rules tests.

---

### Task 1: Add a failing source guard

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/ClassicCivLifespanSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] Guard for an initializer containing exactly the IDs `human`, `elf`, `dwarf`, target value `70f`, and an initialization call from content startup.
- [ ] Guard against modifying `orc`, `Xia`, monkey assets or `base_stats["lifespan"]`.
- [ ] Run the guard and confirm RED because the initializer is missing.

### Task 2: Add the idempotent initializer

**Files:**
- Create: `Code/content/ClassicCivLifespanContent.cs`
- Modify: `Code/content/XiaContent.cs` or `Code/ModClass.cs`

- [ ] Resolve each ActorAsset from `AssetManager.actor_library` and replace its genome lifespan with `70f` through the same genome mutation mechanism already used by AW3 content.
- [ ] Skip missing assets safely and make repeated `Init()` calls produce the same result.
- [ ] Register after vanilla actor assets exist and after `XiaRace.Init()` so Xia keeps its own cloned/delta lifespan.
- [ ] Run the focused guard and complete rules tests.
- [ ] Run `git diff --check` and commit; do not compile the main DLL.
