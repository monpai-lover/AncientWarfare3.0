# AW3 Aristocratic Succession Before Republic Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Install the strongest surviving domestic noble house as a new monarchy before allowing a royal-line extinction to create a republic.

**Architecture:** Keep hereditary selection in `HeirService`, add a pure aristocratic ranking seam and one live kingdom-unit grouping service, then invoke it inside the existing vacancy resolver before republican candidate ranking. Reuse vanilla `setKing()` and the existing AW3 accession hooks so the selected house becomes the royal clan and opens a normal dynasty without changing class policy.

**Tech Stack:** C# 11, .NET Framework 4.8, Harmony, WorldBox clan/kingdom APIs, .NET 9 executable rule tests, PowerShell source guards.

---

### Task 1: Add Red Aristocratic Succession Rules

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Create: `Code/core/lineage/AristocraticSuccessionRules.cs`

- [ ] Add assertions that a valid house blocks republic, a pending succession defers fallback, higher clan renown wins, exact ties use the lower clan ID, and an eligible house chief precedes a stronger non-chief:

```csharp
Equal(AristocraticVacancyDecision.InstallHouse,
    AristocraticSuccessionRules.DecideVacancy(false, false, true, 8, true),
    "a surviving noble house precedes republic");
Equal(AristocraticVacancyDecision.Defer,
    AristocraticSuccessionRules.DecideVacancy(true, false, true, 8, true),
    "timer_new_king defers house selection");
True(AristocraticSuccessionRules.CompareHouses(
        new AristocraticHouseScore(10, 80, 1, 4, 2,
            new AristocraticRulerScore(1, true, 10, 10, 10, 3, 20, 40)),
        new AristocraticHouseScore(11, 40, 4, 12, 6,
            new AristocraticRulerScore(2, true, 20, 20, 20, 8, 80, 30))) < 0,
    "clan renown is the primary house rank");
True(AristocraticSuccessionRules.CompareRulers(
        new AristocraticRulerScore(1, true, 5, 5, 5, 1, 10, 30),
        new AristocraticRulerScore(2, false, 20, 20, 20, 10, 100, 40)) < 0,
    "eligible chief precedes a stronger ordinary member");
```

- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore` and confirm compilation fails because `AristocraticSuccessionRules` and its score types do not exist.
- [ ] Add the pure score structs, eligibility rule, vacancy decisions, and deterministic comparers with these public seams, then link the production file from the test project:

```csharp
public enum AristocraticVacancyDecision { Defer, InstallHouse, ElectRepublic }
public readonly struct AristocraticRulerScore
{
    public AristocraticRulerScore(long actorId, bool isChief, int diplomacy,
        int warfare, int stewardship, int level, float combatStrength, int age);
}
public readonly struct AristocraticHouseScore
{
    public AristocraticHouseScore(long clanId, int renown, int officeHolders,
        int realmMembers, int eligibleAdultMales, AristocraticRulerScore bestRuler);
}
public static class AristocraticSuccessionRules
{
    public static AristocraticVacancyDecision DecideVacancy(bool successionPending,
        bool hasHereditaryHeir, bool hasHouseCandidate, int electableCount,
        bool monarchyEstablished);
    public static bool IsEligibleRuler(bool inLineageSystem, bool hasVisibleClan,
        bool isMale, bool isAdult, bool isAlive, bool isSlave, bool isKing);
    public static int CompareHouses(AristocraticHouseScore left,
        AristocraticHouseScore right);
    public static int CompareRulers(AristocraticRulerScore left,
        AristocraticRulerScore right);
}
```

- [ ] Re-run the focused rule executable and confirm it prints `Rule tests passed.`.

### Task 2: Select And Install The Strongest House

**Files:**
- Create: `Code/core/lineage/AristocraticSuccessionService.cs`
- Modify: `Code/core/lineage/RepublicGovernmentService.cs`
- Modify: `Code/core/lineage/HeirService.cs`
- Modify: `Code/patch/AW_HeirPatch.cs`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Group living domestic actors by visible clan in one pass and count offices, members, and eligible adult males. Expose only `public static Actor SelectRuler(Kingdom pKingdom)` from `AristocraticSuccessionService`.
- [ ] Prefer an eligible domestic chief inside each house; otherwise select the best eligible male by governing attributes, level, combat strength, age, and actor ID.
- [ ] Rank the resulting houses with the tested pure comparer and return the winning actor.
- [ ] Add `public static void MarkClanFallbackSuccession(Kingdom pKingdom, Actor pRuler)`, which calls `ClearHeir`, then writes `SuccessionMode.CLAN_FALLBACK` only for a valid incoming ruler. It must not set `KINGDOM_HEIR_ID` to that ruler.
- [ ] In the non-republic vacancy path, return a valid aristocratic ruler before ranking republican candidates or writing `ClassRepublic`.
- [ ] Route both royal-clan and leader fallback hooks through the same vacancy resolver so one-city kingdoms cannot bypass house selection via an arbitrary capital leader.
- [ ] Add source guards that require the house selection call to appear before `SetRepublic` and forbid class-policy mutation in `AristocraticSuccessionService`.

### Task 3: Verify And Deploy

**Files:**
- Verify: `Code/core/lineage/AristocraticSuccessionRules.cs`
- Verify: `Code/core/lineage/AristocraticSuccessionService.cs`
- Deploy: `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0`

- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore` and require `Rule tests passed.`.
- [ ] Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/SourceGuardTests.ps1` and require `Source guard tests passed.`.
- [ ] Run `dotnet build AncientWarfare3.csproj -c Debug --no-restore` and `dotnet build AncientWarfare3.csproj -c Release --no-restore`; both must report zero warnings and zero errors.
- [ ] Run `git diff --check` and inspect `git status --short`.
- [ ] Deploy tracked mod files while preserving `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0/.runtime/aw3_lineage_archive.db`, then compare source and assembly hashes.
- [ ] Start a fresh world, force repeated wartime ruler deaths, and verify history shows a new house dynasty without a preceding republic event whenever an eligible house remains.
