# AW3 Expanded Court, Schools, And National Direction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a rank-ordered wide living court with fourteen schools, specialist medicine, office biographies, genealogy titles, and cached livelihood/aggression/peace influence on state AI.

**Architecture:** Extend the existing court domain with pure school, rank, medical, title, and direction rules; keep actor/SQLite/WorldBox access in services; expose one deduplicated court read model to a reusable portrait node and wide panning/zooming window. AI reads a cached three-axis snapshot and never rescans the court during a decision.

**Tech Stack:** C# 11, .NET Framework 4.8, Unity UI, Harmony, WorldBox actor/kingdom APIs, `UiUnitAvatarElement`, SQLite, CSV localization, temporary .NET console rule tests.

**Execution constraint:** Work directly on `master`. The six user-authored school icons and current `XiaTraits.cs` edit are intentionally incorporated only in Task 1. Preserve all intentional test-directory deletions.

---

## File Map

- Modify `Code/core/court/CourtIds.cs`: offices, schools, traits, and royal-care trait ID.
- Modify `Code/core/court/CourtTierRules.cs`: specialist slots and rank metadata.
- Create `Code/core/court/CourtSchoolAssignmentRules.cs`: deterministic school scoring.
- Modify `Code/core/court/CourtTraitRules.cs`: map all fourteen schools.
- Modify `Code/content/XiaTraits.cs`: exact icon paths and trait stats.
- Modify `Locales/aw3_court.csv` and `Code/core/lineage/HistoryLocalizationRules.cs`: zh/en/ch strings.
- Modify school-label switches in `CourtWindow`, `KingdomWindowAddition`, `ChronicleEvents`, and `CaptiveTreatmentRules`.
- Create `Code/core/court/RoyalMedicalCareRules.cs` and `RoyalMedicalCareService.cs`: annual care and cleanup.
- Modify `Code/core/lineage/LineageKeys.cs`: previous medical targets and direction cache keys.
- Create `Code/core/court/CourtDirectionRules.cs` and `CourtDirectionService.cs`: pure vectors and runtime cached aggregation.
- Modify `CourtService`, `CourtSnapshot`, `KingdomCourtStateTableItem`, and `LineageKeys`: lifecycle and persistence.
- Modify `CourtAIRules`, `WarDecisionAI`, `VassalAIService`; create `CourtPeaceService.cs`: bounded AI integration.
- Create `Code/core/court/CourtTitleRules.cs`: combined office labels.
- Modify `ChronicleEvents`, `LineageArchiveWriter`, and `LineageQuery`: biography/archive/genealogy office history.
- Create `Code/core/court/CourtPyramidRules.cs` and `CourtReadModelService.cs`: deduplication, role ranks, positions.
- Create `Code/ui/items/CourtActorNodeView.cs`: live portrait, flag, colored border/name, school icon, tooltip.
- Rewrite `Code/ui/windows/CourtWindow.cs`: fixed summary plus movable/scalable pyramid canvas.
- Reuse `Code/ui/items/TreeDragPanHandler.cs` and the wide-window setup in `KingdomPolicyWindow` without changing their behavior unless a focused UI test requires it.
- Add user assets: six PNG files already present under `GameResources/ui/Icons/traits/`.
- Create only temporarily: `F:\tmp\AW3CourtExpansionRuleTests\AW3CourtExpansionRuleTests.csproj` and `Program.cs`.

### Task 1: Register Fourteen Schools, Two Specialist Offices, And Exact Icons

**Files:**
- Modify: `Code/core/court/CourtIds.cs`
- Modify: `Code/core/court/CourtTraitRules.cs`
- Modify: `Code/core/court/CourtTierRules.cs`
- Create: `Code/core/court/CourtSchoolAssignmentRules.cs`
- Modify: `Code/content/XiaTraits.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Locales/aw3_court.csv`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Modify: `Code/ui/windows/KingdomWindowAddition.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/CaptiveTreatmentRules.cs`
- Add: `GameResources/ui/Icons/traits/iconnong.png`
- Add: `GameResources/ui/Icons/traits/iconmingjia.png`
- Add: `GameResources/ui/Icons/traits/iconzajia.png`
- Add: `GameResources/ui/Icons/traits/iconshangjia.png`
- Add: `GameResources/ui/Icons/traits/icongongjia.png`
- Add: `GameResources/ui/Icons/traits/iconshijia.png`
- Test: `F:\tmp\AW3CourtExpansionRuleTests\Program.cs`

- [ ] **Step 1: Create the temporary pure-rule harness**

Create a net8 console project linking `CourtIds.cs`, `CourtTierRules.cs`, `CourtTraitRules.cs`, and the new `CourtSchoolAssignmentRules.cs`. Do not place the project inside the repository.

- [ ] **Step 2: Write failing ID, tier, and deterministic-assignment tests**

Use this core test body:

```csharp
using AncientWarfare3.core.court;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

string[] schools = CourtSchoolAssignmentRules.AllSchools();
Check(schools.Length == 14 && schools.Distinct().Count() == 14, "court must expose 14 unique schools");
Check(CourtTierRules.CentralOfficesForTier(CourtTier.SanGongJiuQing)
    .Contains(CourtOfficeId.ImperialPhysician), "official court must contain Imperial Physician");
Check(CourtTierRules.CentralOfficesForTier(CourtTier.SanShengLiuBu)
    .Contains(CourtOfficeId.ImperialAstrologer), "advanced court must contain Imperial Astrologer");

var stats = new CourtCandidateProfile(actorId: 77, stewardship: 12, diplomacy: 4,
    warfare: 1, intelligence: 15, existingSchool: "");
Check(CourtSchoolAssignmentRules.ResolveSchool(CourtOfficeId.ImperialPhysician, stats) == CourtSchoolId.Medical,
    "physician school must be Medical");
Check(CourtSchoolAssignmentRules.ResolveSchool(CourtOfficeId.ImperialAstrologer, stats) == CourtSchoolId.YinYang,
    "astrologer school must be Yin-Yang");
Check(CourtSchoolAssignmentRules.ResolveSchool(CourtOfficeId.Hubu, stats) ==
      CourtSchoolAssignmentRules.ResolveSchool(CourtOfficeId.Hubu, stats),
    "school assignment must be deterministic");
Console.WriteLine("court school rules passed");
```

Run: `dotnet run --project F:\tmp\AW3CourtExpansionRuleTests\AW3CourtExpansionRuleTests.csproj`

Expected: compilation fails on missing offices/schools/rules.

- [ ] **Step 3: Extend IDs and mappings**

Add `ImperialPhysician` and `ImperialAstrologer`; add Medical, Syncretist, Merchant, Craftsman, Historian school and trait constants; add `RoyalMedicalCare = "aw_royal_medical_care"`. Extend `CourtTraitRules.TraitForSchool` and `CourtService.AllSchoolTraits` to cover all fourteen traits.

- [ ] **Step 4: Add deterministic school scoring**

Implement a pure `CourtCandidateProfile` and `ResolveSchool`. Fixed roles return immediately; other roles score all allowed schools from role preferences and normalized stats, use existing school as a small continuity bonus, and break exact ties by stable actor ID plus school index. Required fixed/strong cases:

```csharp
if (officeId == CourtOfficeId.ImperialPhysician) return CourtSchoolId.Medical;
if (officeId == CourtOfficeId.ImperialAstrologer) return CourtSchoolId.YinYang;
if (officeId == CourtOfficeId.Marshal || officeId == CourtOfficeId.Bingbu)
    scores[CourtSchoolId.Military] += 100f;
if (officeId == CourtOfficeId.Hubu)
{
    scores[CourtSchoolId.Agrarian] += 45f;
    scores[CourtSchoolId.Merchant] += 45f;
}
if (officeId == CourtOfficeId.Gongbu)
{
    scores[CourtSchoolId.Mohist] += 45f;
    scores[CourtSchoolId.Craftsman] += 45f;
}
```

Return all schools from `AllSchools()` in the fixed ID order specified by the design.

- [ ] **Step 5: Add specialist slots and candidate scoring**

Append both specialist offices to both official tier arrays. In `CourtService`, resolve the candidate's school after selection rather than passing a single hardcoded school into `FillCentralOffice`. Weight physician candidates with `intelligence * 2 + stewardship * 1.5`, astrologers with `intelligence * 2 + diplomacy * 1.5`, and then call `CourtSchoolAssignmentRules.ResolveSchool`.

- [ ] **Step 6: Register all trait resources and royal care**

Keep the user's files unchanged and use these exact resource IDs:

```csharp
RegisterCourtSchoolTrait(CourtTraitId.Agrarian, "ui/Icons/traits/iconnong", 2f, 0f, 0f, 1f);
RegisterCourtSchoolTrait(CourtTraitId.YinYang, "ui/Icons/traits/iconyingyang", 1f, 1f, 0f, 2f);
RegisterCourtSchoolTrait(CourtTraitId.Logician, "ui/Icons/traits/iconmingjia", 0f, 2f, 0f, 2f);
RegisterCourtSchoolTrait(CourtTraitId.Medical, "ui/Icons/traits/iconoisha", 2f, 0f, 0f, 2f);
RegisterCourtSchoolTrait(CourtTraitId.Syncretist, "ui/Icons/traits/iconzajia", 1f, 1f, 1f, 1f);
RegisterCourtSchoolTrait(CourtTraitId.Merchant, "ui/Icons/traits/iconshangjia", 2f, 2f, 0f, 1f);
RegisterCourtSchoolTrait(CourtTraitId.Craftsman, "ui/Icons/traits/icongongjia", 2f, 0f, 1f, 2f);
RegisterCourtSchoolTrait(CourtTraitId.Historian, "ui/Icons/traits/iconshijia", 1f, 2f, 0f, 2f);

ActorTrait care = NewTrait(CourtTraitId.RoyalMedicalCare, "ui/Icons/traits/icondanyao", XiaTraitGroups.AW2);
care.base_stats["multiplier_health"] = 0.5f;
care.base_stats["lifespan"] = 15f;
```

- [ ] **Step 7: Add all localization and switch cases**

Add zh/en/ch rows for both offices, five new schools, three direction labels, vacancy/appointment-year/city/ability tooltips, and medical cure biography. Extend every existing school-name switch so no consumer displays raw IDs.

- [ ] **Step 8: Run tests, build, and commit IDs/resources**

Run the temporary harness and `dotnet build AncientWarfare3.csproj`; expect success and zero errors.

Stage only the listed files and six icons, then commit:

```powershell
git add -- Code/core/court/CourtIds.cs Code/core/court/CourtTraitRules.cs Code/core/court/CourtTierRules.cs Code/core/court/CourtSchoolAssignmentRules.cs Code/content/XiaTraits.cs Code/core/court/CourtService.cs Code/core/lineage/HistoryLocalizationRules.cs Code/core/lineage/ChronicleEvents.cs Code/core/lineage/CaptiveTreatmentRules.cs Code/ui/windows/KingdomWindowAddition.cs Locales/aw3_court.csv GameResources/ui/Icons/traits/iconnong.png GameResources/ui/Icons/traits/iconmingjia.png GameResources/ui/Icons/traits/iconzajia.png GameResources/ui/Icons/traits/iconshangjia.png GameResources/ui/Icons/traits/icongongjia.png GameResources/ui/Icons/traits/iconshijia.png
git commit -m "feat: expand court schools and specialists"
```

### Task 2: Persist Every Office Term In Biography And Genealogy

**Files:**
- Create: `Code/core/court/CourtTitleRules.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/LineageArchiveWriter.cs`
- Modify: `Code/core/lineage/LineageQuery.cs`
- Test: `F:\tmp\AW3CourtExpansionRuleTests\Program.cs`

- [ ] **Step 1: Write failing combined-title tests**

Link `CourtTitleRules.cs` and add:

```csharp
if (CourtTitleRules.Combine("世子", "中书令") != "世子 · 中书令")
    throw new Exception("heir and office title must combine");
if (CourtTitleRules.Combine("大将", "兵部尚书", "大将") != "大将 · 兵部尚书")
    throw new Exception("duplicate roles must collapse in stable order");
```

Run and expect a missing-type failure.

- [ ] **Step 2: Implement stable title composition**

Create `CourtTitleRules.Combine(params string[])` using an ordinal `HashSet<string>` plus ordered list, ignoring empty values and joining with ` " · " `.

- [ ] **Step 3: Remove the personal-biography importance gate**

In `OnCourtOfficerAppointed` and `OnCourtOfficerDismissed`, always call `HistoryWriter.RecordPerson` for a valid actor/kingdom. Retain `ChronicleGate.IsImportant` only around the kingdom-level appointment record.

- [ ] **Step 4: Synchronize archive after each office transition**

After appointment record creation, call `LineageService.ArchiveActor(pActor, pAlive: true)`. During dismissal, capture office/school first, close the DB row, clear active keys, write the dismissal biography, then archive so the live snapshot reflects the remaining concurrent roles. Death maintenance closes the row before the final `pAlive: false` archive write.

- [ ] **Step 5: Resolve combined live and archived titles**

In both `ResolveSocialTitleSnapshot` and `ApplyLiveSocialTitle`, collect rather than immediately return for heir/general/governor/office roles. Keep captive/former-king and current-king titles as exclusive high-priority states. Compose normal roles with `CourtTitleRules.Combine`, resolving office text from `COURT_OFFICE_ID`. Keep `城市名 太守`; use the kingdom color for both title and archive color.

- [ ] **Step 6: Close stale active database rows during maintenance**

During `CourtService.ValidateOfficers`, query active `CourtOfficer` rows for the kingdom once, resolve each actor ID, and close the row with reason `dead`, `missing`, or `defected` when the actor is not a living member of that kingdom. Archive a resolvable dead actor with its captured office before clearing the active row. `CourtWindow` may skip the stale row immediately, but only maintenance mutates the database.

- [ ] **Step 7: Run rules, build, and commit**

Run the temporary harness and normal build, then:

```powershell
git add -- Code/core/court/CourtTitleRules.cs Code/core/court/CourtService.cs Code/core/lineage/ChronicleEvents.cs Code/core/lineage/LineageArchiveWriter.cs Code/core/lineage/LineageQuery.cs
git commit -m "feat: record court office history"
```

### Task 3: Add Annual Imperial Medical Care

**Files:**
- Create: `Code/core/court/RoyalMedicalCareRules.cs`
- Create: `Code/core/court/RoyalMedicalCareService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/HeirService.cs`
- Test: `F:\tmp\AW3CourtExpansionRuleTests\Program.cs`

- [ ] **Step 1: Write failing medical eligibility/cleanup tests**

Test `ShouldTreat` for alive/active/same-kingdom physician and patient, and `ShouldKeepCare` for physician loss/heir replacement. Explicitly assert that routine healing with zero removed traits does not request a biography entry while `removedCurableTraits > 0` does.

- [ ] **Step 2: Implement pure medical rules**

Create methods with explicit booleans:

```csharp
public static bool ShouldTreat(bool physicianAlive, bool physicianActive,
    bool sameKingdom, bool patientAlive) => physicianAlive && physicianActive && sameKingdom && patientAlive;

public static bool ShouldRecordCure(int removedCurableTraits) => removedCurableTraits > 0;
```

Run the harness and expect success.

- [ ] **Step 3: Implement the runtime care service**

Add `COURT_MEDICAL_KING_ID` and `COURT_MEDICAL_HEIR_ID` actor-ID keys. Find the active Imperial Physician from court actor keys, build a target set containing the current king and `HeirService.PeekRegisteredHeir`, resolve the two previous IDs, and remove `aw_royal_medical_care` from any previous target no longer in the new set. Store the new IDs only after reconciliation. For each valid target:

```csharp
if (!target.hasTrait(CourtTraitId.RoyalMedicalCare))
    target.addTrait(CourtTraitId.RoyalMedicalCare);
target.restoreHealthPercent(1f);
int removed = 0;
foreach (ActorTrait trait in target.getTraits().ToList())
{
    if (!trait.can_be_cured || trait.id == CourtTraitId.RoyalMedicalCare) continue;
    target.removeTrait(trait.id);
    removed++;
}
if (RoyalMedicalCareRules.ShouldRecordCure(removed))
    ChronicleEvents.OnRoyalMedicalCure(physician, target, target.kingdom, removed);
```

Do not intercept death methods; the trait only modifies stats and annual healing.

- [ ] **Step 4: Wire care after court validation**

Call `RoyalMedicalCareService.OnKingdomYear` after `ValidateOfficers`/`EnsureMinimumCourt`, so a dead or defected physician is cleared before treatment. Call `RoyalMedicalCareService.ReconcileTargets` after `HeirService.StoreHeirSelection` changes the registered heir and after `CourtService.ClearOfficer` dismisses the physician, so care transfers or clears without waiting for the next year.

- [ ] **Step 5: Run rules, build, and commit**

```powershell
git add -- Code/core/court/RoyalMedicalCareRules.cs Code/core/court/RoyalMedicalCareService.cs Code/core/court/CourtService.cs Code/core/lineage/ChronicleEvents.cs Code/core/lineage/LineageKeys.cs Code/core/lineage/HeirService.cs
git commit -m "feat: add imperial medical care"
```

### Task 4: Compute And Cache Three-Axis National Direction

**Files:**
- Create: `Code/core/court/CourtDirectionRules.cs`
- Create: `Code/core/court/CourtDirectionService.cs`
- Modify: `Code/core/court/CourtInfluenceRules.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/db/KingdomCourtStateTableItem.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/core/lineage/GeneralService.cs`
- Modify: `Code/patch/AW_HeirPatch.cs`
- Test: `F:\tmp\AW3CourtExpansionRuleTests\Program.cs`

- [ ] **Step 1: Write failing vector/normalization/dedup tests**

Construct a king contribution and duplicate general/officer contributions. Assert:

```csharp
CourtDirectionSnapshot direction = CourtDirectionRules.Aggregate(contributions);
Check(direction.Livelihood >= 0f && direction.Livelihood <= 1f, "livelihood bounded");
Check(direction.Aggression >= 0f && direction.Aggression <= 1f, "aggression bounded");
Check(direction.Peace >= 0f && direction.Peace <= 1f, "peace bounded");
Check(direction.CountedActorIds.Distinct().Count() == direction.CountedActorIds.Count,
    "multi-role actor counted once");
Check(Math.Abs(direction.KingShare - 0.25f) < 0.02f, "king share near 25 percent");
```

Run and expect missing types.

- [ ] **Step 2: Implement pure school vectors and role weights**

Define immutable `CourtDirectionVector`, `CourtInfluenceContribution`, and `CourtDirectionSnapshot`. Map all fourteen schools to the approved livelihood/aggression/peace vectors, clamp each final axis to `[0,1]`, normalize the king bucket to `0.25` and the non-king bucket to `0.75`, and deduplicate by actor ID using highest rank plus a bounded `0.15` concurrent-role supplement.

- [ ] **Step 3: Add persisted cache fields**

Add `COURT_DIRECTION_LIVELIHOOD`, `COURT_DIRECTION_AGGRESSION`, and `COURT_DIRECTION_PEACE` keys. Add matching `double` fields to `KingdomCourtStateTableItem` and `float` fields to `CourtSnapshot`; read/write them in `GetSnapshot` and `UpsertCourtSnapshot`.

- [ ] **Step 4: Build runtime contributions once per court refresh**

`CourtDirectionService.Recalculate` gathers:

- king once with the royal profile;
- active central officers from actor court keys;
- `GeneralService.GetActiveGenerals(pKingdom)` with bounded merit weight;
- each living city leader with the small local rank;

Use actor-ID dictionary deduplication. Do not query the court DB or scan `World.world.units`. Store the snapshot on kingdom data before the existing court-state upsert.

Add `MarkDirty(Kingdom)` and `RecalculateIfDirty(Kingdom, currentYear)`. Mark the kingdom dirty from `CourtService.SetOfficer/ClearOfficer`, successful `Kingdom.setKing` handling in `AW_HeirPatch`, and general appointment/retirement or material merit-rank changes in `GeneralService`. The yearly court path recalculates once if dirty or if the stored direction year differs; consumers never recalculate.

- [ ] **Step 5: Run rules, build, and commit**

```powershell
git add -- Code/core/court/CourtDirectionRules.cs Code/core/court/CourtDirectionService.cs Code/core/court/CourtInfluenceRules.cs Code/core/court/CourtService.cs Code/core/db/KingdomCourtStateTableItem.cs Code/core/lineage/LineageKeys.cs Code/core/lineage/GeneralService.cs Code/patch/AW_HeirPatch.cs
git commit -m "feat: cache national court direction"
```

### Task 5: Apply Direction To Research, War, Vassals, And White Peace

**Files:**
- Modify: `Code/core/court/CourtAIRules.cs`
- Modify: `Code/core/policy/KingdomPolicyAI.cs`
- Modify: `Code/core/lineage/WarDecisionAI.cs`
- Modify: `Code/core/lineage/VassalAIService.cs`
- Create: `Code/core/court/CourtPeaceService.cs`
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs`
- Test: `F:\tmp\AW3CourtExpansionRuleTests\Program.cs`

- [ ] **Step 1: Write failing bounded-bias tests**

Add pure assertions that high aggression raises an ordinary offensive score, high peace lowers it, livelihood raises agriculture/economy research, and all returned multipliers stay in `[0.5, 1.5]`. Assert Mandate/defense/independence exemptions return multiplier `1` rather than a block.

- [ ] **Step 2: Implement pure bounded modifiers in `CourtDirectionRules`**

Use explicit formulas:

```csharp
public static float OffensiveWarMultiplier(float aggression, float peace, float livelihood,
    bool protectedWar)
{
    if (protectedWar) return 1f;
    return Clamp(1f + aggression * 0.45f - peace * 0.35f - livelihood * 0.15f, 0.5f, 1.5f);
}

public static float VoluntaryDiplomacyMultiplier(float peace) => Clamp(0.8f + peace * 0.7f, 0.5f, 1.5f);
public static float ForcedVassalMultiplier(float aggression) => Clamp(0.8f + aggression * 0.7f, 0.5f, 1.5f);
```

- [ ] **Step 3: Integrate cached values into research/decision scoring**

Extend `CourtAIRules.ScoreResearch` and `ScoreDecision` to accept a `CourtDirectionSnapshot`. Add livelihood bonuses only to agrarian/economic/population/medical/engineering nodes and use aggression/peace on ordinary declaration/fabrication decisions. Keep existing hard availability checks untouched.

Update both call sites in `KingdomPolicyAI` to pass the already-read `CourtSnapshot` instead of causing a second court lookup.

- [ ] **Step 4: Integrate ordinary war selection**

In `WarDecisionAI`, read `CourtService.GetSnapshot(pKingdom)` once per check. Apply `OffensiveWarMultiplier` to the initial `0.28` chance, target score, and `StillWantsWar`. Pass `protectedWar: true` for Mandate, defense, and independence paths. Do not change casus belli checks or `WarTerritoryService` legality.

- [ ] **Step 5: Integrate voluntary and forced vassal behavior**

Read the snapshot once in `VassalAIService.OnKingdomYear`. Multiply `TryActiveVassal` probability by the peace modifier and `TryVassalWar` probability/score by the aggression modifier. Continue to call `VassalService.CanSetVassal` and shared direct adjacency; direction cannot bypass either.

- [ ] **Step 6: Add annual stalled/losing white-peace evaluation**

`CourtPeaceService.OnKingdomYear` runs only for the main attacker of each live war and only once per war year. It reads both main kingdoms' cached direction, `war.getAge()`, current side power, and war type. It skips Mandate, defense-only, and independence wars. For an ordinary war at least 10 years old, compute a bounded chance that rises with both sides' peace, duration, stalemate, or attacker disadvantage and falls with aggression. On success call:

```csharp
World.world.wars.endWar(war, WarWinner.Peace);
```

Invoke it from `AW_KingdomPolicyPatch` before `WarDecisionAI`, so a concluded war cannot also queue a new action in the same yearly pass.

- [ ] **Step 7: Run rules, build, and commit**

```powershell
git add -- Code/core/court/CourtAIRules.cs Code/core/court/CourtDirectionRules.cs Code/core/court/CourtPeaceService.cs Code/core/policy/KingdomPolicyAI.cs Code/core/lineage/WarDecisionAI.cs Code/core/lineage/VassalAIService.cs Code/patch/AW_KingdomPolicyPatch.cs
git commit -m "feat: steer kingdom AI from court direction"
```

### Task 6: Build A Deduplicated Rank-Pyramid Read Model

**Files:**
- Create: `Code/core/court/CourtPyramidRules.cs`
- Create: `Code/core/court/CourtReadModelService.cs`
- Modify: `Code/core/court/CourtService.cs`
- Test: `F:\tmp\AW3CourtExpansionRuleTests\Program.cs`

- [ ] **Step 1: Write failing rank/dedup/layout tests**

Test that king rank is above central, specialist, military, and local; one actor with general plus minister roles appears once at minister rank with both role labels; empty central offices remain placeholder models; and each tier is centered with multiple columns.

- [ ] **Step 2: Implement pure rank and position rules**

Define ranks `0 King`, `10 ExcellencyOrDepartment`, `20 MinisterOrMinistry`, `30 Specialist`, `40 Military`, `50 Local`. `Deduplicate` groups by actor ID, selects minimum rank, and retains stable distinct role IDs. `Layout` groups by rank, sorts by office order/merit/name/actor ID, and returns centered `(x,y)` positions with fixed node/gap dimensions.

- [ ] **Step 3: Build runtime node models without a world scan**

`CourtReadModelService.Build(Kingdom)` reads active officer rows once, resolves only their actor IDs, adds the king, active generals, and each city's leader, then deduplicates through `CourtPyramidRules`. Each occupied model includes the school ID and `school_icon_path` resolved from `AssetManager.traits.get(CourtTraitRules.TraitForSchool(schoolId))?.path_icon`. Skip dead/missing dynamic actors. Add vacancy models for every central/specialist slot absent from the active actor set.

- [ ] **Step 4: Run rules, build, and commit**

```powershell
git add -- Code/core/court/CourtPyramidRules.cs Code/core/court/CourtReadModelService.cs Code/core/court/CourtService.cs
git commit -m "feat: build ranked court read model"
```

### Task 7: Create Live Court Portrait Nodes

**Files:**
- Create: `Code/ui/items/CourtActorNodeView.cs`
- Modify: `Code/ui/items/FamilyTreeNodeView.cs`
- Test: build plus in-game UI smoke test.

- [ ] **Step 1: Expose the proven avatar prefab path**

Change `FamilyTreeNodeView.GetAvatarPrefab` from `private` to `internal` so the court reuses the exact `Resources.Load<UiUnitAvatarElement>("ui/UnitAvatarElement")` path instead of inventing another portrait loader.

- [ ] **Step 2: Implement occupied and vacancy node construction**

`CourtActorNodeView.Create(parent)` builds a fixed node shell. `Bind(model, kingdom)` instantiates/shows the live avatar for occupied models, uses `ActionLibrary.openUnitWindow(actor)` on click, and renders a neutral vacancy plate otherwise. Set the avatar holder border to a darker kingdom color and the name to the normal kingdom text color.

- [ ] **Step 3: Render school icon without source stretching**

Load the registered path with `SpriteTextureLoader.getSprite(model.school_icon_path)` into a `52x52` slot with `Image.preserveAspect = true`, centered anchors, and no assumption about source PNG dimensions. Use `ui/icons/iconKnowledge` only as the missing-resource fallback.

- [ ] **Step 4: Build the complete tooltip**

Use `AW_RawTooltip` and include actor age, intelligence/stewardship/diplomacy/warfare summary, all concurrent roles, school, appointment year, and city. Omit unavailable fields rather than displaying invalid IDs.

- [ ] **Step 5: Build and commit the node**

```powershell
git add -- Code/ui/items/CourtActorNodeView.cs Code/ui/items/FamilyTreeNodeView.cs
git commit -m "feat: add living court portrait nodes"
```

### Task 8: Rewrite `CourtWindow` As A Wide Pannable/Zoomable Pyramid

**Files:**
- Modify: `Code/ui/windows/CourtWindow.cs`
- Reuse: `Code/ui/windows/KingdomPolicyWindow.cs`
- Reuse: `Code/ui/items/TreeDragPanHandler.cs`
- Test: build plus in-game UI smoke test.

- [ ] **Step 1: Port the wide shell setup**

Use the policy window's `560x360` layout, title drag handler, resize handles, viewport mask, disabled native scrollbars, and a `CourtCanvas` under the viewport. Keep the summary outside `CourtCanvas` so panning/zoom never moves it.

- [ ] **Step 2: Install canvas pan/zoom**

Attach `TreeDragPanHandler` to both the canvas and transparent viewport drag surface with `Setup(_canvasRect, null)`. Preserve its existing `0.25..2.0` scale clamp. Reset `anchoredPosition` and `localScale` when opening a different kingdom; preserve them during a refresh of the same kingdom.

- [ ] **Step 3: Render the fixed summary**

Read one `CourtSnapshot` and display kingdom flag/name/government, tier/efficiency, dominant and secondary schools, and formatted livelihood/aggression/peace values. Do not call appointment or recalculation methods from UI.

- [ ] **Step 4: Render positioned nodes in batches**

Call `CourtReadModelService.Build`, calculate positions through `CourtPyramidRules.Layout`, reuse/pool `CourtActorNodeView` instances, and parent all nodes to `CourtCanvas`. Size the canvas to computed bounds plus padding. For large dynamic tiers, build a fixed number per frame using a coroutine so opening the window does not create one allocation spike.

- [ ] **Step 5: Remove the old vertical text-list builder**

Delete `AddOfficerSection`, `AddBureauSection`, and row-based content-height behavior after the pyramid path renders all required roles. Keep only shared localization helpers that the new summary/node code still uses.

- [ ] **Step 6: Build and perform UI smoke tests**

Verify default, `0.25`, `1.0`, and `2.0` zoom; window dragging/resizing; canvas dragging; fixed summary; kingdom switch reset; click-to-actor; vacancy nodes; multi-role dedup; kingdom colors; non-square icons; and a large kingdom with many generals/governors.

- [ ] **Step 7: Commit the wide court UI**

```powershell
git add -- Code/ui/windows/CourtWindow.cs
git commit -m "feat: render a wide court pyramid"
```

### Task 9: Final Court Verification And Performance Audit

**Files:**
- Verify all court files and user assets.

- [ ] **Step 1: Run the complete temporary rule harness**

Run: `dotnet run --project F:\tmp\AW3CourtExpansionRuleTests\AW3CourtExpansionRuleTests.csproj`

Expected: fourteen-school, title, medicine, direction, rank, deduplication, and layout assertions pass.

- [ ] **Step 2: Verify every icon resource exists**

Run `Get-Item` for every ID registered in `XiaTraits.cs`. Expected: no missing file; all six user icons remain byte-identical to the versions supplied by the user.

- [ ] **Step 3: Build both symbol configurations**

```powershell
dotnet build AncientWarfare3.csproj
dotnet build AncientWarfare3.csproj -p:DefineConstants="DEBUG;TRACE"
```

Expected: zero errors in both configurations.

- [ ] **Step 4: Exercise medical and history scenarios in game**

Verify physician appointment, annual heal, curable-trait removal, one cure biography, no routine biography spam, physician death/defection cleanup, heir replacement transfer, office appointment/reassignment/dismissal biographies, and retained dead-actor office title/color in the family tree.

- [ ] **Step 5: Exercise strategic scenarios in game**

Observe several yearly decisions for livelihood-heavy, Military/Legalist-heavy, and Daoist/Diplomat-heavy courts. Confirm biases are visible but ordinary legality, direct-border vassal requirements, Mandate wars, defense, and independence remain intact.

- [ ] **Step 6: Profile the large-court window and yearly court refresh**

Use existing `UpdateAgeBenchmark` labels around read-model build, portrait batches, direction aggregation, and medical care. Confirm UI opening does not scan `World.world.units`, AI reads cached direction, medical care is yearly/event-driven, and no new court stage becomes a major `updateAge/updateYear` spike.

- [ ] **Step 7: Audit git state and commits**

Run `git status --short` and `git log --oneline -n 12`. Expected: production work is split by the plan's focused commits, intentional test deletions remain unstaged, and temporary harness files remain only under `F:\tmp`.
