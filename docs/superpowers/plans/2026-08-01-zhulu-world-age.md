# Zhulu World Age Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Register a selectable vanilla WorldBox Zhulu age and run an AW3-authoritative monthly unification director that grants every independent civilization the Zhulu casus belli, continues wars until one realm remains, and force-grants Mandate at a 2:1 score lead.

**Architecture:** `ZhuluWorldAgeContent` owns vanilla asset registration, while pure `ZhuluAgeRules` owns scoring and deterministic target ordering. `ZhuluAgeDirectorService` is the only runtime orchestrator and runs from `AWAuthorityCycleService`; it delegates persistence to `ZhuluAgeStatePersistence`, declarations to the existing `ZhuluWarService`, and forced Mandate creation to a narrowly extended `MandateService` entry point.

**Tech Stack:** C# 10, NeoModLoader, Harmony-compatible WorldBox runtime APIs, AW3 SQLite archive, existing console rules test project, PowerShell source guards.

---

## File Map

- Create `Code/core/lineage/ZhuluAgeRules.cs`: pure score, threshold, eligibility and target ordering rules.
- Create `Code/content/ZhuluWorldAgeContent.cs`: idempotent `WorldAgeAsset`, pool and `WorldLaws` registration.
- Create `Code/core/db/ZhuluAgeStateTableItem.cs`: one-row persistent entry-state schema.
- Create `Code/core/lineage/ZhuluAgeStatePersistence.cs`: read/write the persistent entry flag.
- Create `Code/core/lineage/ZhuluAgeDirectorService.cs`: monthly lifecycle, ranking, Mandate and war orchestration.
- Modify `Code/core/lineage/ZhuluWarRules.cs`: accept an explicit age override without weakening ordinary-era rules.
- Modify `Code/core/lineage/ZhuluWarService.cs`: expose era-aware `CanDeclare` and `TryDeclare` overloads.
- Modify `Code/core/lineage/MandateService.cs`: add a forced, validated Zhulu-age grant path.
- Modify `Code/core/performance/AWAuthorityCycleService.cs`: invoke and reset the director under existing authority gates.
- Modify `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`: rebuild/clear Zhulu age runtime state.
- Modify `Code/ModClass.cs`: register the age before UI content initialization.
- Create `Locales/aw3_zhulu_age.csv`: title and description.
- Create `Tests/AncientWarfare3.Rules.Tests/ZhuluAgeRulesTests.cs.txt`: pure rule coverage.
- Modify `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`: link the new pure rules file and test.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: execute the new test suite.
- Create `Tests/ZhuluWorldAgeSourceGuardTests.ps1`: registration, authority and ordinary-era safety guards.

### Task 1: Pure score and target rules

**Files:**
- Create: `Code/core/lineage/ZhuluAgeRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/ZhuluAgeRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write the failing score and threshold tests**

Add tests that call these exact contracts:

```csharp
long direct = ZhuluAgeRules.DirectScore(
    cityCount: 2, zoneCount: 30, population: 100,
    recruitableWarriors: 20);
Assert.Equal(620L, direct);
Assert.False(ZhuluAgeRules.HasMandateLead(199, 100, 2));
Assert.True(ZhuluAgeRules.HasMandateLead(200, 100, 2));
Assert.True(ZhuluAgeRules.HasMandateLead(1, 0, 1));
Assert.Equal(50L, ZhuluAgeRules.VassalContribution(100));
```

Add target tests using `ZhuluAgeTargetFacts` to prove: adjacent precedes overseas, then lower squared distance, then weaker score, then lower kingdom id; invalid/self/same-root/already-at-war candidates are excluded.

- [ ] **Step 2: Run the rules project and verify failure**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: build fails because `ZhuluAgeRules` and `ZhuluAgeRulesTests` do not exist.

- [ ] **Step 3: Implement the pure rules**

Create constants and deterministic methods:

```csharp
public readonly struct ZhuluAgeTargetFacts
{
    public ZhuluAgeTargetFacts(long kingdomId, bool valid, bool isSelf,
        bool sameRoot, bool alreadyAtWar, bool diplomaticBlocked,
        bool sameAlliance, bool directlyAdjacent, long distanceSquared,
        long score)
    {
        KingdomId = kingdomId;
        Valid = valid;
        IsSelf = isSelf;
        SameRoot = sameRoot;
        AlreadyAtWar = alreadyAtWar;
        DiplomaticBlocked = diplomaticBlocked;
        SameAlliance = sameAlliance;
        DirectlyAdjacent = directlyAdjacent;
        DistanceSquared = Math.Max(0L, distanceSquared);
        Score = Math.Max(0L, score);
    }

    public long KingdomId { get; }
    public bool Valid { get; }
    public bool IsSelf { get; }
    public bool SameRoot { get; }
    public bool AlreadyAtWar { get; }
    public bool DiplomaticBlocked { get; }
    public bool SameAlliance { get; }
    public bool DirectlyAdjacent { get; }
    public long DistanceSquared { get; }
    public long Score { get; }
}

public static class ZhuluAgeRules
{
    public const string AgeId = "age_zhulu";
    public const long CityWeight = 200;
    public const long ZoneWeight = 2;
    public const long PopulationWeight = 1;
    public const long RecruitableWeight = 3;

    public static long DirectScore(int cityCount, int zoneCount,
        int population, int recruitableWarriors)
    {
        long result = 0L;
        result = AddSaturated(result, MultiplySaturated(cityCount, CityWeight));
        result = AddSaturated(result, MultiplySaturated(zoneCount, ZoneWeight));
        result = AddSaturated(result, MultiplySaturated(population, PopulationWeight));
        return AddSaturated(result,
            MultiplySaturated(recruitableWarriors, RecruitableWeight));
    }

    public static long VassalContribution(long childScore) =>
        Math.Max(0L, childScore) / 2L;

    public static bool HasMandateLead(long first, long second,
        int independentCount)
    {
        if (independentCount <= 0) return false;
        if (independentCount == 1) return first >= 0L;
        if (first < 0L || second < 0L) return false;
        return second <= long.MaxValue / 2L && first >= second * 2L;
    }

    public static bool IsEligibleTarget(ZhuluAgeTargetFacts facts)
    {
        return facts.Valid && !facts.IsSelf && !facts.SameRoot &&
               !facts.AlreadyAtWar && !facts.DiplomaticBlocked &&
               !facts.SameAlliance;
    }

    public static int CompareTargets(ZhuluAgeTargetFacts left,
        ZhuluAgeTargetFacts right)
    {
        int result = right.DirectlyAdjacent.CompareTo(left.DirectlyAdjacent);
        if (result != 0) return result;
        result = left.DistanceSquared.CompareTo(right.DistanceSquared);
        if (result != 0) return result;
        result = left.Score.CompareTo(right.Score);
        return result != 0 ? result : left.KingdomId.CompareTo(right.KingdomId);
    }

    private static long MultiplySaturated(int value, long weight)
    {
        if (value <= 0 || weight <= 0L) return 0L;
        return value > long.MaxValue / weight ? long.MaxValue : value * weight;
    }

    private static long AddSaturated(long left, long right)
    {
        left = Math.Max(0L, left);
        right = Math.Max(0L, right);
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }
}
```

Use saturating non-negative multiplication/addition so malformed population or deep vassal values cannot overflow `long`.

- [ ] **Step 4: Run the rules project and verify pass**

Run the command from Step 2.

Expected: the new `ZhuluAgeRulesTests` group reports pass and all existing rule groups remain green.

- [ ] **Step 5: Commit the pure rules slice**

```powershell
git add Code/core/lineage/ZhuluAgeRules.cs Tests/AncientWarfare3.Rules.Tests/ZhuluAgeRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: add zhulu age scoring rules"
```

### Task 2: Vanilla age asset registration

**Files:**
- Create: `Code/content/ZhuluWorldAgeContent.cs`
- Modify: `Code/ModClass.cs`
- Create: `Locales/aw3_zhulu_age.csv`
- Create: `Tests/ZhuluWorldAgeSourceGuardTests.ps1`

- [ ] **Step 1: Write the failing source guard**

Assert the source contains all of the following:

```powershell
Assert-Contains $content 'id = ZhuluAgeRules.AgeId'
Assert-Contains $content 'AssetManager.era_library.add'
Assert-Contains $content 'list_only_normal'
Assert-Contains $content 'pool_by_slots'
Assert-Contains $content 'world_laws.add'
Assert-Contains $modClass 'ZhuluWorldAgeContent.Init()'
```

Also parse `Locales/aw3_zhulu_age.csv` and require `age_zhulu_title` and `age_zhulu_description` for `cz` and `en`.

- [ ] **Step 2: Run the source guard and verify failure**

Run:

```powershell
pwsh -NoProfile -File Tests/ZhuluWorldAgeSourceGuardTests.ps1
```

Expected: fail because the content file and locale file are absent.

- [ ] **Step 3: Register the age idempotently**

`ZhuluWorldAgeContent.Init()` must:

```csharp
WorldAgeAsset age = AssetManager.era_library.get(ZhuluAgeRules.AgeId)
    ?? AssetManager.era_library.add(new WorldAgeAsset {
        id = ZhuluAgeRules.AgeId,
        path_icon = "ui/Icons/traits/iconTianming",
        path_background = "ui/AgeWheel/backgrounds/age_chaos_background",
        rate = 1,
        years_min = 35,
        years_max = 55,
        global_unfreeze_world = true,
        title_color = Toolbox.makeColor("#D9B44A"),
        default_slots = new List<int> { 1,2,3,4,5,6,7,8 }
    });
```

Repair the fields when an old asset already exists. Add the same object once to `list_only_normal` and each slot pool. If a world exists, call `world.world_laws.add(new PlayerOptionData(age.id) { boolVal = true })`; future worlds receive it automatically from `WorldLaws.init()`.

Call `ZhuluWorldAgeContent.Init()` in `OnModLoad()` after `XiaContent.Init()` and before `GodPowerLibrary.Init()`.

- [ ] **Step 4: Add localized title and description**

CSV text:

```csv
key,cz,en
age_zhulu_title,逐鹿时代,Age of Contention
age_zhulu_description,群雄并起，诸国为统一天下不断征战，最强者将获得天命。,All realms contend for unification, and the strongest may seize the Mandate.
```

- [ ] **Step 5: Run the source guard and commit**

Expected: `ZhuluWorldAgeSourceGuardTests: PASS`.

```powershell
git add Code/content/ZhuluWorldAgeContent.cs Code/ModClass.cs Locales/aw3_zhulu_age.csv Tests/ZhuluWorldAgeSourceGuardTests.ps1
git commit -m "feat: register selectable zhulu world age"
```

### Task 3: Persistent entry state and forced Mandate path

**Files:**
- Create: `Code/core/db/ZhuluAgeStateTableItem.cs`
- Create: `Code/core/lineage/ZhuluAgeStatePersistence.cs`
- Modify: `Code/core/lineage/MandateService.cs`
- Modify: `Tests/ZhuluWorldAgeSourceGuardTests.ps1`

- [ ] **Step 1: Extend the source guard to fail on missing persistence and grant APIs**

Require `[TableDef("ZhuluAgeState")]`, primary `state_id`, `entry_active`, `TryForceGrantMandateForZhuluAge`, and a forced path that does not call ordinary declaration eligibility.

- [ ] **Step 2: Run the source guard and verify failure**

Expected: missing state table and forced Mandate method.

- [ ] **Step 3: Add the one-row state table and persistence service**

Schema:

```csharp
[TableDef("ZhuluAgeState")]
public sealed class ZhuluAgeStateTableItem :
    AbstractTableItem<ZhuluAgeStateTableItem>
{
    [TableItemDef(pIsPrimary: true)] public long state_id;
    public int entry_active;
    public double updated_time;
}
```

`ZhuluAgeStatePersistence.ReadEntryActive()` returns false when the archive is not ready or the row is absent. `WriteEntryActive(bool)` upserts row `STATE_ID=1` synchronously on the authority thread because it is written only on age transitions.

- [ ] **Step 4: Refactor Mandate declaration into ordinary and forced wrappers**

Keep the existing public behavior:

```csharp
public static bool TryDeclareMandate(Kingdom kingdom,
    string reason = "decision", string originType = "native",
    string claimantKind = "orthodox", Kingdom rebelOrigin = null) =>
    TryDeclareMandateCore(kingdom, reason, originType, claimantKind,
        rebelOrigin, pForceZhuluAge: false);

public static bool TryForceGrantMandateForZhuluAge(
    Kingdom target, out string reason)
{
    // Validate DB, live civ realm and living ruler. Return true if already holder.
    // Select orthodox origin for mandate-system realms and foreign-pseudo origin otherwise.
    return TryDeclareMandateCore(target, "zhulu_age_lead", origin,
        claimant, null, pForceZhuluAge: true);
}
```

Inside the core, force mode skips only `HasActivePrincipalWars()` and `CanDeclareMandateForOrigin()`. It must retain target validity, living king, DB readiness, period replacement, history, title, legal core and projection writes.

- [ ] **Step 5: Run guards and rules tests, then commit**

```powershell
pwsh -NoProfile -File Tests/ZhuluWorldAgeSourceGuardTests.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git add Code/core/db/ZhuluAgeStateTableItem.cs Code/core/lineage/ZhuluAgeStatePersistence.cs Code/core/lineage/MandateService.cs Tests/ZhuluWorldAgeSourceGuardTests.ps1
git commit -m "feat: persist zhulu age entry and force mandate"
```

### Task 4: Era-scoped Zhulu casus belli override

**Files:**
- Modify: `Code/core/lineage/ZhuluWarRules.cs`
- Modify: `Code/core/lineage/ZhuluWarService.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt`
- Modify: `Tests/ZhuluWorldAgeSourceGuardTests.ps1`

- [ ] **Step 1: Add failing ordinary-versus-age override tests**

Extend `ZhuluEligibilityFacts` with `AgeOverride`. Prove ordinary calls still require Chaos and Mandate eligibility, while override calls accept non-Xia realms but still reject subjects, same-root, diplomatic blockers, alliances and existing wars.

- [ ] **Step 2: Run rules tests and verify failure**

Expected: constructor/signature failure before implementation.

- [ ] **Step 3: Implement the narrow override**

Use this eligibility shape:

```csharp
bool eraGate = facts.AgeOverride ||
    facts.Phase == MandatePhase.Chaos &&
    facts.AttackerMandateEligible && facts.DefenderMandateEligible;
return eraGate && facts.AttackerValid && facts.DefenderValid &&
    !facts.AttackerIsSubject && !facts.SameSubjectTree &&
    !facts.DiplomaticBlocked && !facts.SameAlliance &&
    !facts.AlreadyAtWar;
```

Add overloads `CanDeclare(Kingdom attacker, Kingdom defender, bool pZhuluAgeOverride, out string reason)` and `TryDeclare(Kingdom attacker, Kingdom defender, bool pZhuluAgeOverride, out string reason)`; existing two-kingdom signatures forward `false`. In override mode, subject checks use `VassalService.GetSuzerain(attacker)`, not `GetDiplomaticSuzerain`, so tributaries remain independent participants.

- [ ] **Step 4: Run tests and guard, then commit**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
pwsh -NoProfile -File Tests/ZhuluWorldAgeSourceGuardTests.ps1
git add Code/core/lineage/ZhuluWarRules.cs Code/core/lineage/ZhuluWarService.cs Tests/AncientWarfare3.Rules.Tests/ZhuluWarRulesTests.cs.txt Tests/ZhuluWorldAgeSourceGuardTests.ps1
git commit -m "feat: scope zhulu casus belli override to world age"
```

### Task 5: Authority-only monthly director

**Files:**
- Create: `Code/core/lineage/ZhuluAgeDirectorService.cs`
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/core/multiplayer/AW3RuntimeRestorePipeline.cs`
- Modify: `Tests/ZhuluWorldAgeSourceGuardTests.ps1`

- [ ] **Step 1: Extend the source guard for lifecycle and authority hooks**

Require calls to `ZhuluAgeDirectorService.ProcessAuthorityCycle`, `Reset`, and `RebuildRuntime`, and require the director to compare `World.world.map_stats.world_age_id` with `ZhuluAgeRules.AgeId` and call `ZhuluWarService.TryDeclare(source, target, pZhuluAgeOverride: true, out reason)`.

- [ ] **Step 2: Run the source guard and verify failure**

Expected: director and hooks absent.

- [ ] **Step 3: Implement lifecycle and monthly throttling**

The director must keep `_lastProcessedMonthKey`, `_runtimeAgeActive`, and a bounded per-realm failure log cache. `ProcessAuthorityCycle()`:

```csharp
bool active = World.world?.map_stats?.world_age_id == ZhuluAgeRules.AgeId;
HandleTransition(active);
if (!active || !ArchiveReady()) return;
int monthKey = KingdomDecisionMonthlyRules.ToMonthKey(
    Date.getCurrentYear(), Date.getCurrentMonth());
if (monthKey == _lastProcessedMonthKey) return;
_lastProcessedMonthKey = monthKey;
ProcessMonth();
```

On false-to-true transition, if persisted `entry_active` is false: call `MandateService.ClearMandate("zhulu_age_entered")`, then persist true. On true-to-false: persist false. `RebuildRuntime()` reads current age and the persisted flag without clearing a Mandate; `Reset()` clears only runtime fields.

- [ ] **Step 4: Build realm snapshots and score recursively**

Snapshot only live civ, non-neutral realms with cities. A realm is independent when `VassalService.GetSuzerain(realm) == null`; tributary suzerains do not disqualify it. Compute direct score from city count, `countZones()`, summed `getPopulationPeople()`, and `WartimeMilitaryPotentialService.CountPotentialWarriors()`. Add direct vassals recursively at 50% per level using a visited id set.

- [ ] **Step 5: Implement rank, grant and declaration flow**

Sort independent snapshots by score descending then id ascending. If the lead rule passes and the leader is not current Mandate, call `TryForceGrantMandateForZhuluAge`. For every independent realm without an active principal Zhulu war, build target facts for every other root, sort with `ZhuluAgeRules.CompareTargets`, and try candidates until `ZhuluWarService.TryDeclare(source, target, pZhuluAgeOverride: true, out reason)` succeeds.

Adjacency is true when any source-system city zone borders any target-system city zone. Capital distance is squared tile distance between representative capitals. Do not scan actors or every map tile.

- [ ] **Step 6: Add authority and restore hooks**

Call the director beside other monthly authority services in `AWAuthorityCycleService.ProcessCycle()`, and call `Reset()` from `AWAuthorityCycleService.Reset()`. Add `RebuildRuntime` to both restore-stage lists and `Reset` to the runtime-cache reset list.

- [ ] **Step 7: Run guards and rules tests, then commit**

```powershell
pwsh -NoProfile -File Tests/ZhuluWorldAgeSourceGuardTests.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git add Code/core/lineage/ZhuluAgeDirectorService.cs Code/core/performance/AWAuthorityCycleService.cs Code/core/multiplayer/AW3RuntimeRestorePipeline.cs Tests/ZhuluWorldAgeSourceGuardTests.ps1
git commit -m "feat: direct monthly zhulu unification wars"
```

### Task 6: Full verification and source deployment

**Files:**
- Verify all files above
- Deploy source/resources only to the configured WorldBox Mods directory

- [ ] **Step 1: Run focused automated verification**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
pwsh -NoProfile -File Tests/ZhuluWorldAgeSourceGuardTests.ps1
git diff --check
```

Expected: rules project exits 0, source guard prints PASS, and `git diff --check` has no output.

- [ ] **Step 2: Run the repository source deployment verifier**

Locate the current deployment script from existing repository tooling rather than inventing a DLL flow. Run its source-copy mode and verify `Code/content/ZhuluWorldAgeContent.cs`, `Code/core/lineage/ZhuluAgeDirectorService.cs`, `Locales/aw3_zhulu_age.csv`, and the required icon/background references are present under the installed AW3 mod folder.

- [ ] **Step 3: Perform static requirement audit**

Confirm evidence for every goal item:

```text
selectable in vanilla age list
all independent civ races eligible
tributaries independent, vassals folded into roots
monthly wars continue until one root
199% no grant, 200% grant
forced grant ignores ordinary conditions
ordinary era Zhulu limits preserved
pause/load/replica authority gates preserved
source-only deployment, no DLL produced or copied
```

- [ ] **Step 4: Document required game test**

Report that automated checks cannot prove the Unity window render or live AI outcome. Give the user the exact first in-game test: open vanilla Ages, select 逐鹿时代, run four mixed-race independent realms for one month, verify wars begin, then verify the Mandate transition at a constructed 2:1 score lead.
