# Historical School Masters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace automatic school assignment with a persistent Hundred Schools simulation seeded by 84 one-time historical masters who descend only into Xia states, travel, teach, debate, write, found institutions, serve foreign courts temporarily, die permanently, and leave real lineages.

**Architecture:** A fixed registry and pure schedulers define historical identity and world-stage descent. SQLite membership, affiliation, influence, institution, work, debate, and event records are authoritative; actor data contains only hot projection keys. A bounded world-year runtime drives descent and academic actions, custom actor tasks handle physical travel and presentation, and the existing school MapMode/UI consume cached read models rather than inferring schools from office, family, nationality, stats, or city dominance.

**Tech Stack:** C# 11, .NET Framework 4.8, .NET 9 temporary rule harness, WorldBox actor/task/status/UI APIs, Harmony, NeoModLoader, SQLite, existing AW3 court/history/map-mode infrastructure, AW3/Cultiway-selected global path owner.

**Dependency:** Execute after `2026-07-12-global-streaming-pathfinding.md` is green. Historical masters use the winning global path owner for physical travel; only their higher-level lifecycle may use the timed sea-voyage exception.

**Execution constraint:** Work directly on `master`. Put tests only under `F:/tmp/AW3HistoricalSchoolRuleTests`; never restore or stage deleted repository tests.

---

## File Structure

- `Code/content/schools/HistoricalSchoolMasterDefinition.cs`: immutable master definition and ability/identity metadata.
- `Code/content/schools/HistoricalSchoolMasterRegistry.cs`: exact 14x6 canonical roster, waves, aliases, works, and preferences.
- `Code/content/schools/HistoricalSchoolContent.cs`: traits, statuses, world-log assets, citizen job, actor job, and tasks.
- `Code/core/schools/HistoricalSchoolRules.cs`: pure wave, fairness, target, mortality, conversion, and action rules.
- `Code/core/schools/HistoricalSchoolState.cs`: lifecycle/state/source enums and read models.
- `Code/core/schools/HistoricalSchoolStore.cs`: normalized SQLite reads/writes and in-memory indexes.
- `Code/core/schools/SchoolMembershipService.cs`: sole formal-membership authority and trait/data projection.
- `Code/core/schools/HistoricalAffiliationService.cs`: immutable nationality/hometown plus residence/service resolution.
- `Code/core/schools/HistoricalSchoolDescentService.cs`: eligible-year scheduler, Xia home selection, spawning, and one-time state.
- `Code/core/schools/HistoricalSchoolTravelService.cs`: destination scoring, path travel, dock failures, timed voyages, and arrival.
- `Code/core/schools/HistoricalSchoolActionService.cs`: lectures, persuasion, recruitment, writing, retirement, and bounded annual work.
- `Code/core/schools/SchoolLineageService.cs`: teacher graph, successors, reputable itinerants, conversion, and rediscovery.
- `Code/core/schools/CitySchoolLedgerService.cs`: five-component persistent influence, decay, indexes, and dirty snapshots.
- `Code/core/schools/SchoolInstitutionService.cs`: logical institutions, works, custodians, condition, and leading landmark.
- `Code/core/schools/SchoolDebateService.cs`: eligibility, topic, deterministic score/result, presentation, and history.
- `Code/core/schools/SchoolGuestOfficeService.cs`: invitation, 8-20 year service, renewal/dismissal, and protection.
- `Code/core/schools/HistoricalSchoolRuntime.cs`: once-per-world-year orchestration, bounded frame presentation, and resets.
- `Code/core/court/CourtAffiliationResolver.cs`: shared domestic/foreign court affiliation decision.
- `Code/core/db/HistoricalSchoolMasterTableItem.cs`, `SchoolMembershipTableItem.cs`, `SchoolAffiliationTableItem.cs`, `CitySchoolLedgerTableItem.cs`, `SchoolInstitutionTableItem.cs`, `SchoolWorkTableItem.cs`, `SchoolDebateTableItem.cs`, `SchoolEventTableItem.cs`: durable normalized records.
- `Code/ai/behaviours/actor/BehHistoricalSchoolTravel.cs`, `BehHistoricalSchoolArrive.cs`, `BehHistoricalSchoolDebate.cs`: physical actor task stages.
- `Code/patch/AW_HistoricalSchoolPatch.cs`: year, death, migration/name protection, combat guest, and lifecycle integration.
- `Code/ui/items/SchoolMasterCardView.cs`, `SchoolInfluenceBreakdownView.cs`, `SchoolInstitutionRowView.cs`, `SchoolLineageRowView.cs`: pooled school UI elements.
- `Code/ui/windows/SchoolWindow.cs`: overview/gallery/lineage/city detail integration.
- `Code/core/policy/SchoolMapBottomBarController.cs`, `Code/ui/items/SchoolCompositionElement.cs`: persistent influence details and landmark/debate summary.
- `Locales/aw3_school.csv`, `Locales/trait.csv`, `Locales/others.csv`, `Locales/message.csv`: complete `cz,en,ch` text.

### Task 1: Lock the 84-person registry and pure descent rules

**Files:**
- Create: `Code/content/schools/HistoricalSchoolMasterDefinition.cs`
- Create: `Code/content/schools/HistoricalSchoolMasterRegistry.cs`
- Create: `Code/core/schools/HistoricalSchoolState.cs`
- Create: `Code/core/schools/HistoricalSchoolRules.cs`
- Create temporarily: `F:/tmp/AW3HistoricalSchoolRuleTests/AW3HistoricalSchoolRuleTests.csproj`
- Create temporarily: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Create the temporary source-link harness**

Use net9 and link the four pure files plus existing `CourtSchoolDefinition.cs` and `CourtSchoolRegistry.cs`. Add assertions:

```csharp
var all = HistoricalSchoolMasterRegistry.All;
Check(all.Count == 84, "exactly 84 masters");
Check(all.Select(x => x.Id).Distinct().Count() == 84, "unique master IDs");
foreach (var group in all.GroupBy(x => x.SchoolId))
    Check(group.Count() == 6, "six masters for " + group.Key);
Check(all.Single(x => x.Id == "aw_master_mohist_06").CanonicalName == "田鸠", "田鸠 spelling");
Check(all.Single(x => x.Id == "aw_master_agrarian_04").CanonicalName == "氾胜之", "氾胜之 spelling");
Check(all.All(x => x.Wave == HistoricalSchoolRules.WaveForOrder(x.Order)), "wave mapping");
```

Stable IDs are school ID plus 1-based roster position, for example `aw_master_ru_01` through `aw_master_historian_06`; IDs never depend on transliteration.

- [ ] **Step 2: Run and verify RED**

Run `dotnet run --project F:\tmp\AW3HistoricalSchoolRuleTests\AW3HistoricalSchoolRuleTests.csproj` and expect missing registry/rule types.

- [ ] **Step 3: Implement the exact canonical registry**

Copy the 14 rows and spellings from the approved design unchanged. Each entry exposes ID, canonical name, aliases, school ID, order 1-6, wave, preferred state names, adult spawn age, male historical sex, school-shaped four-stat profile, debate topics, canonical works, and institution preference. The wave function is:

```csharp
public static int WaveForOrder(int order) => order switch
{
    1 => 1,
    2 => 2,
    3 or 4 => 3,
    5 => 4,
    6 => 5,
    _ => 0
};
```

- [ ] **Step 4: Implement eligible-year and fair queue rules**

Wave opening years are 10/35/70/120/180. Eligible time advances only while a living Xia kingdom owns a living city. `SelectDue` sorts open unspawned entries by least descents in that school, wave, order, last-school-selection year, registry order, and ID, and returns at most two. Tests simulate 300 eligible years and require every school to have five by 240 and all six by 300; inserting a 50-year no-Xia gap must not advance the counter.

- [ ] **Step 5: Verify and commit**

Run the harness and commit production files with `git commit -m "feat: register historical school masters"`.

### Task 2: Add normalized persistence and authoritative membership

**Files:**
- Create: eight `Code/core/db/*School*TableItem.cs` files listed in File Structure
- Create: `Code/core/schools/HistoricalSchoolStore.cs`
- Create: `Code/core/schools/SchoolMembershipService.cs`
- Modify: `Code/core/court/CourtSchoolIdentityRules.cs`
- Modify: `Code/core/court/CourtSchoolAssignmentRules.cs`
- Modify: `Code/core/court/CourtTraitRules.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/CitySchoolSnapshotService.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`
- Modify: `Code/patch/AW_SchoolInfluencePatch.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/ui/windows/SchoolWindow.cs`
- Modify temporarily: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Write failing membership-source tests**

Require `SchoolMembershipSource` values `HistoricalDescent`, `DirectDiscipleship`, `LaterDiscipleship`, `ExplicitConversion`, `PreservedWork`, and `AuthoredEvent`. Test one active membership per actor, close-before-open conversion, no biological inheritance, historical master non-conversion, and rejection of unbacked actor strings/traits.

- [ ] **Step 2: Verify RED**

Expected: existing identity rules still assign from stats, parent, city, and actor jitter.

- [ ] **Step 3: Add durable tables and indexes**

Use one primary field per AW3 table. Membership rows contain membership ID, actor/school/source/source ID, teacher, city, generation, reputation, start/end year, active. Master rows contain string master ID, actor, spawned/dead, home identity, spawn/death years and state. Affiliation rows contain actor, immutable home IDs/names, residence, destination, service, lifecycle state, term/voyage times, and transport failures. Ledger primary key is the string `cityId:schoolId`. Events store event ID/type, actor/school/city/kingdom, year, payload, and importance. Add indexes for active actor membership, school members, city-school ledger, master actor, residence city, service kingdom, institution city, work school, debate city/year, and recent event school.

- [ ] **Step 4: Make membership the only authority**

Move the current nested membership service out of `CitySchoolSnapshotService`. Its API is:

```csharp
string GetSchool(long pActorId);
bool TryJoin(Actor pActor, string pSchoolId, SchoolMembershipSource pSource,
    string pSourceId, long pTeacherActorId, long pCityId, int pGeneration);
bool TryConvert(Actor pActor, string pSchoolId, string pSourceId, long pCityId);
void OnDeath(Actor pActor);
Actor[] LivingMembers(string pSchoolId);
void LoadIndexes();
void ClearRuntime();
```

`CourtService.EnsurePersonalSchool` reads this service, projects a valid school to `LineageKeys.COURT_SCHOOL` and exactly one school trait, or clears the string/all 14 traits and returns `None`. Delete parent/city/stat/jitter resolution and remove officer dismissal trait removal. Schoolless officials remain valid and contribute no school direction.

- [ ] **Step 5: Reset/load indexes with archive switches**

On new world and load, clear in-memory indexes, load authoritative rows, repair only backed trait/data projections, and clear unbacked school values because no old-save compatibility is required.

- [ ] **Step 6: Verify and commit**

Run the harness plus both net48 builds. Commit with `git commit -m "fix: require explicit school membership sources"`.

### Task 3: Spawn masters once in Xia states and protect canonical identity

**Files:**
- Create: `Code/content/schools/HistoricalSchoolContent.cs`
- Create: `Code/core/schools/HistoricalSchoolDescentService.cs`
- Create: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Create: `Code/patch/AW_HistoricalSchoolPatch.cs`
- Modify: `Code/content/XiaContent.cs`
- Modify: `Code/content/XiaTraits.cs`
- Modify: `Code/content/XiaStatus.cs`
- Modify: `Code/core/lineage/ChronicleGate.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/patch/AW_NameProtectPatch.cs`
- Modify: `Locales/trait.csv`, `Locales/others.csv`, `Locales/message.csv`
- Modify temporarily: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Write failing descent/home tests**

Test Xia-only home selection, preferred state match, underrepresented-state weighting, any-Xia fallback, no-Xia queue pause, two-per-year cap, atomic spawn marking, canonical name protection, and permanent death without respawn.

- [ ] **Step 2: Verify RED**

Expected: scheduler/spawn/content types are absent.

- [ ] **Step 3: Register traits, statuses, logs, and tasks**

Add `aw_historical_school_master` with `health +20`, `lifespan +25`, disease resistance supported by current stats, and modest four-stat bonuses; do not reuse `figure`/`first`, which would trigger mandate and political-figure logic. Add status assets `aw_school_guest`, `aw_school_debate`, and `aw_school_voyage`; voyage and debate are presentation statuses, not traits. Register descent/death/lecture/debate world logs and the historical scholar citizen/actor jobs.

- [ ] **Step 4: Spawn an adult Xia actor atomically**

Choose a living Xia city; call `World.world.units.createNewUnit` with Xia actor asset, city tile, a living Xia subspecies when available, and `pAdultAge: true`; call `joinCity(home)` exactly once. Set registry age/sex/stats, master trait, canonical display keys, favorite flag, hot master ID, and explicit historical membership. Only after actor, membership, and affiliation writes succeed mark the master spawned. On failure dispose the partial actor and leave the registry entry queued.

- [ ] **Step 5: Protect names and record lifecycle**

All Xia name repair, Chinese Name hooks, random surname grants, and office-Shi display repair first call `HistoricalSchoolDescentService.IsCanonicalMaster(actor)` and leave canonical display untouched. A master may receive a clan record only when registry lineage metadata explicitly allows it. Death marks the master permanently dead, closes membership/service/travel, records cause/location, chooses lineage successor, and never queues the same ID again.

- [ ] **Step 6: Wire a once-per-world-year scheduler and commit**

Call `HistoricalSchoolRuntime.OnWorldYear()` from the guarded first `Kingdom.updateAge` invocation each year. Run tests/builds and commit with `git commit -m "feat: descend historical masters into Xia states"`.

### Task 4: Separate nationality, residence, service, and physical travel

**Files:**
- Create: `Code/core/schools/HistoricalAffiliationService.cs`
- Create: `Code/core/schools/HistoricalSchoolTravelService.cs`
- Create: `Code/ai/behaviours/actor/BehHistoricalSchoolTravel.cs`
- Create: `Code/ai/behaviours/actor/BehHistoricalSchoolArrive.cs`
- Modify: `Code/content/schools/HistoricalSchoolContent.cs`
- Modify: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Modify: `Code/patch/AW_HistoricalSchoolPatch.cs`
- Modify temporarily: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Write failing state/target tests**

Cover `AtHome -> ChoosingDestination -> Travelling -> Resident`, AI destination scores, return cooldown, war/disaster penalties, same-city rejection, immutable home nationality, residence changes without `joinCity`, service separate from both, and home-destruction engine-pointer repair without naturalization.

- [ ] **Step 2: Verify RED**

Expected: affiliation and travel state APIs are missing.

- [ ] **Step 3: Implement shared affiliation resolution**

`HistoricalAffiliationService` stores immutable home kingdom ID/name and hometown city, resolves current residence/service, and exposes `IsAffiliatedWith(actor, kingdom)` for host guest logic. It never calls `joinCity` for residence. Migration/reassignment patches reject silent naturalization of active masters; if dead home objects make vanilla pointers unsafe, repair engine pointers but retain historical nationality in every school/history/UI query.

- [ ] **Step 4: Implement bounded AI destination selection**

Run quarterly-equivalent decisions by dividing active masters into four stable actor-ID buckets processed during the world year. Score population, development, capital, school underrepresentation, debate rivals, candidate disciples, receptive ruler/open office, matching city problems, cooldown, war, occupation, disaster, and transport availability. Cap choices to a sampled/indexed candidate set rather than scanning every city per master.

- [ ] **Step 5: Drive physical travel through actor tasks**

`BehHistoricalSchoolTravel` resolves a target city tile into `beh_tile_target`; vanilla `BehGoToTileTarget` invokes the winning global path owner; arrival behavior verifies the city, writes residence, applies guest status, and records journey. Serving actors do not receive travel tasks.

- [ ] **Step 6: Implement master-only timed sea voyage**

After two transport failures and at least five waiting years with no physical route, a historical master enters voyage. Hide them using `stayInBuilding` at a valid departure dock when possible, maintain logical voyage isolation if the dock disappears, clear office/influence/presentation eligibility, and set deterministic arrival year from distance. On arrival call `exitBuilding`, `spawnOn(destinationDockTile)`, write residence, and record a voyage event. Later disciples and ordinary actors never enter this path.

- [ ] **Step 7: Verify and commit**

Run state tests, build, and commit with `git commit -m "feat: send school masters between states"`.

### Task 5: Add teaching, disciples, conversion, works, and lineage succession

**Files:**
- Create: `Code/core/schools/HistoricalSchoolActionService.cs`
- Create: `Code/core/schools/SchoolLineageService.cs`
- Modify: `Code/core/schools/HistoricalSchoolStore.cs`
- Modify: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Modify temporarily: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Write failing teaching/lineage tests**

Test real-actor recruitment only, 1-2 direct disciples per master/year, direct cap, teacher/city/year/generation persistence, women eligible to study/teach/travel/debate/succeed, no biological inheritance, one school per person, qualified later teachers, itinerant cap, successor scoring, and preserved-work rediscovery only after living lineage extinction.

- [ ] **Step 2: Verify RED**

Expected: action and lineage services are absent.

- [ ] **Step 3: Implement lectures and explicit recruitment**

Select real living residents through indexed residence/city units. Ability, interest, availability, exposure, and reputation affect success; office/gender do not block study. Public lectures add momentum and exposure but do not automatically assign all listeners. Private discipleship calls `SchoolMembershipService.TryJoin` with teacher and generation.

- [ ] **Step 4: Implement lineage leadership and itinerants**

Direct and reputable later disciples may teach, debate, found institutions, and travel. Successor score combines reputation, learning, debate record, and followers with stable actor-ID tie-break. Display successors as numbered lineage holders under their own names. Cap simultaneous non-historical itinerants per school at six.

- [ ] **Step 5: Implement explicit conversion and rediscovery**

Conversion requires at least three years without a same-school teacher, overwhelming rival exposure, and a recorded conversion action; it closes the prior row first. When no living member exists, an active institution or preserved work may select one real reader and create a `PreservedWork` membership event. A work without a reader never creates city membership.

- [ ] **Step 6: Verify and commit**

Run tests/build and commit with `git commit -m "feat: propagate schools through real disciples"`.

### Task 6: Replace instantaneous elite influence with the five-component city ledger

**Files:**
- Create: `Code/core/schools/CitySchoolLedgerService.cs`
- Create: `Code/core/schools/SchoolInstitutionService.cs`
- Modify: `Code/core/court/CitySchoolInfluenceRules.cs`
- Modify: `Code/core/court/CitySchoolSnapshotService.cs`
- Modify: `Code/core/court/CitySchoolDirtyQueue.cs`
- Modify: `Code/core/court/CourtDirectionService.cs`
- Modify: `Code/core/schools/HistoricalSchoolActionService.cs`
- Modify temporarily: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Write failing influence/institution tests**

Test `total = tradition + membership + institutions + active presence + momentum`, signed momentum decay, long-inactivity tradition decay, positive-total shares, no zero rows, member role caps, no influence from schoolless officials, real-source institution creation, multiple logical institutions, leading landmark selection, condition loss, and preserved works surviving landmark changes.

- [ ] **Step 2: Verify RED**

Expected: current snapshots sum only political elites and do not expose components.

- [ ] **Step 3: Implement ledger/index updates**

Persist tradition, institution, momentum, last active/decay years; compute membership and active presence from indexed living members/residence/office rather than world scans. Founding, repeated teaching, works, and major debate wins add bounded tradition. Momentum decays toward zero yearly. Store/delete only non-zero records and mark only affected cities dirty.

- [ ] **Step 4: Implement logical institutions and works**

Masters, lineage holders, or a state action backed by a member/transmitted work may found an institution. Store founder, custodian, level, condition, works, and year. Occupation/destruction/no custodian reduces condition. Choose one leading active institution by effective influence, level, founding year, and ID; logical competitors remain intact.

- [ ] **Step 5: Rebuild existing snapshots from components**

Extend `CitySchoolSnapshot` with per-school `CitySchoolInfluenceBreakdown` and preserve `Scores`, `Share`, `DominantSchool`, `Generation`, and contributor APIs for existing MapMode callers. Court direction uses authoritative membership plus service affiliation; a schoolless actor contributes nothing.

- [ ] **Step 6: Verify and commit**

Run tests/build and commit with `git commit -m "feat: persist city school ecosystems"`.

### Task 7: Add deterministic physical/background debates

**Files:**
- Create: `Code/core/schools/SchoolDebateService.cs`
- Create: `Code/ai/behaviours/actor/BehHistoricalSchoolDebate.cs`
- Modify: `Code/content/schools/HistoricalSchoolContent.cs`
- Modify: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Modify: `Code/core/schools/CitySchoolLedgerService.cs`
- Modify temporarily: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Write failing debate tests**

Cover different-school/same-residence eligibility, master or reputable lineage qualification, cooldown, one debate per city/year, condition-derived topic, deterministic seed and score, decisive/narrow/draw margins, underdog reward, diminishing returns, no forced conversion, and identical on/off-screen results.

- [ ] **Step 2: Verify RED**

Expected: debate service and records are absent.

- [ ] **Step 3: Implement topic and resolution rules**

Topics map famine/livelihood, war/defense, aggression, peace/diplomacy, order/law, commerce, technology/institutions, and medicine/epidemic to existing `CourtSchoolDirection`. Score normalized intelligence/diplomacy/stewardship where relevant, reputation, experience, topic affinity, local support, and a bounded seeded random term. Persist all inputs before presentation and resolve the record exactly once.

- [ ] **Step 4: Add physical presentation with bounded timeout**

When visible, force both actors to a leading landmark/city-center debate task and apply `aw_school_debate`. Arrival or timeout resolves the pre-seeded record. A failed path skips animation only. Off-screen resolution calls the same resolver without portraits/tasks. Update reputation, momentum, small major-win tradition, cooldowns, biographies, city history, and world history.

- [ ] **Step 5: Verify and commit**

Run deterministic tests/build and commit with `git commit -m "feat: stage Hundred Schools debates"`.

### Task 8: Support temporary foreign guest offices safely

**Files:**
- Create: `Code/core/schools/SchoolGuestOfficeService.cs`
- Create: `Code/core/court/CourtAffiliationResolver.cs`
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/CourtDirectionService.cs`
- Modify: `Code/core/court/OfficialCareerService.cs`
- Modify: `Code/core/court/CitySchoolSnapshotService.cs`
- Modify: `Code/patch/AW_HistoricalSchoolPatch.cs`
- Modify temporarily: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Write failing guest-affiliation tests**

Test host residence, no active foreign office, king/slave/madness rejection, male-only central office, women retaining all non-office school activity, reputation/fit, 8-20 year term, renewal/resignation/dismissal, annual validation, host court direction, immutable nationality, and home-host war not automatically naturalizing or enlisting the guest.

- [ ] **Step 2: Verify RED**

Expected: `CourtService` rejects every actor whose `actor.kingdom != host`.

- [ ] **Step 3: Centralize court affiliation**

`CourtAffiliationResolver.CanServe(actor, kingdom, layer)` returns true for same-kingdom actors or a valid active guest service row. Replace direct equality checks in validation, candidate selection for invited scholars, direction, career refresh, city influence, and UI. Do not widen ordinary recruitment: only `SchoolGuestOfficeService` may create a foreign guest service row.

- [ ] **Step 4: Implement invitation and terms**

Eligible resident masters/reputable itinerants may accept a fit open central office. Pick a deterministic term from 8-20 years, pause long travel, set service kingdom independently, apply guest protection, and write normal `CourtOfficer`/biography events. At term end score renewal versus resignation; host collapse, capture, occupation, city loss, dismissal, or death closes both service and career.

- [ ] **Step 5: Protect guests without invulnerability**

Patch `BaseSimObject.canAttackTarget(BaseSimObject,bool,bool)` so a host actor and its valid guest cannot target one another; third parties, disasters, collateral damage, capture, slavery, and player actions remain effective. War between home and host does not change nationality and does not automatically add the scholar to either army.

- [ ] **Step 6: Verify and commit**

Run tests/build and commit with `git commit -m "feat: appoint travelling scholars as guests"`.

### Task 9: Build the historical school UI, landmark, portraits, and history

**Files:**
- Create: `Code/ui/items/SchoolMasterCardView.cs`
- Create: `Code/ui/items/SchoolInfluenceBreakdownView.cs`
- Create: `Code/ui/items/SchoolInstitutionRowView.cs`
- Create: `Code/ui/items/SchoolLineageRowView.cs`
- Create: `Code/core/schools/SchoolLandmarkService.cs`
- Modify: `Code/ui/windows/SchoolWindow.cs`
- Modify: `Code/ui/items/SchoolActorCardView.cs`
- Modify: `Code/core/policy/SchoolMapBottomBarController.cs`
- Modify: `Code/ui/items/SchoolCompositionElement.cs`
- Modify: `Code/core/policy/SchoolMapModeService.cs`
- Modify: `Code/patch/AW_DeferredRuntimeWorkPatch.cs`
- Modify: `Locales/aw3_school.csv`, `Locales/trait.csv`, `Locales/others.csv`, `Locales/message.csv`
- Modify temporarily: `F:/tmp/AW3HistoricalSchoolRuleTests/Program.cs`

- [ ] **Step 1: Write failing read-model/source tests**

Require an 84-master live/dead gallery, real live portraits through `UiUnitAvatarElement.show`, archived dead portraits through family-tree snapshot logic, lineage rows, top cities/kingdoms/institutions/works, recent events, five labeled influence components, resident masters/disciples, pending/recent debate, and one leading landmark per city. Reject defaulting a schoolless actor/city to Ru.

- [ ] **Step 2: Verify RED**

Expected: current window shows only five live representatives and one unexplained score.

- [ ] **Step 3: Add pooled overview/detail sections**

Keep the existing wide scrollable window and school list. Add tabs/sections for Overview, Masters, Lineage, Institutions/Works, and Events. Create at most eight portraits per frame as `CourtWindow` does. Each master card shows canonical name, school position, alive/dead state, home nationality, current residence/service, teacher/lineage standing, and current action; live cards open unit details.

- [ ] **Step 4: Explain city influence everywhere**

The school city detail and bottom bar label Tradition, Members, Institutions, Active Presence, Momentum, raw total, and share for each non-zero school. Show resident masters/reputable disciples, leading institution, preserved works, and debate state. The MapMode continues selecting the real city through `selected_aw_school_city`.

- [ ] **Step 5: Render one pooled academic landmark per city**

`SchoolLandmarkService` uses a pooled world-space icon marker anchored to the leading institution's city tile, tinted by school color and using its existing school icon. It is visual only and never occupies a building tile. Update on leading-institution dirty events, hide off camera/when MapMode and normal landmark rules require, and clear on world switch.

- [ ] **Step 6: Add history and localization**

Use `SchoolEvent` plus existing person/city/kingdom history writers for descent, residence, journey, teacher, disciple, debate, work, institution, guest office, retirement, and death. Add complete unique `cz,en,ch` rows for every new trait/status/heading/component/state/action/topic/result; validate with `Import-Csv -Encoding UTF8` and reject duplicate/empty keys.

- [ ] **Step 7: Verify and commit**

Run harness/build, source-check pooled elements and portrait budgets, then commit with `git commit -m "feat: visualize historical school lineages"`.

### Task 10: Combined lifecycle, performance, and live acceptance

**Files:**
- Modify: `Code/core/schools/HistoricalSchoolRuntime.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/patch/AW_DeferredRuntimeWorkPatch.cs`
- Verify: all pathfinding and historical-school files
- Inspect live log: `C:/Users/24908/AppData/LocalLow/mkarpenko/WorldBox/Player.log`

- [ ] **Step 1: Finalize bounded orchestration**

World-year order is: update eligible time and spawn up to two; reconcile dead/invalid affiliations; finish voyages/terms; decay ledgers; process one annual action per active scholar bucket; schedule debates; refresh dirty city snapshots; persist indexes. Frame work is limited to path/arrival presentation, active voyages, debates currently visible, portrait creation, landmarks, and dirty queues. No frame path scans all units/cities/masters.

- [ ] **Step 2: Run both complete temporary harnesses**

```powershell
dotnet run --project F:\tmp\AW3PathfindingRuleTests\AW3PathfindingRuleTests.csproj
dotnet run --project F:\tmp\AW3HistoricalSchoolRuleTests\AW3HistoricalSchoolRuleTests.csproj
```

Expected: both success messages with every registry, causality, travel, debate, guest, influence, and concurrency assertion green.

- [ ] **Step 3: Run full production verification**

```powershell
dotnet build AncientWarfare3.csproj -c Debug --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
git diff --check
git status --short
```

Expected: zero warnings/errors; only intended production/docs files are committed; all user-deleted tests remain deleted and unstaged.

- [ ] **Step 4: Run long-simulation invariants**

Instrument a fresh Xia world and run past 300 eligible years. Assert from SQLite/logs: exactly 84 unique master IDs, no reincarnation, no descent outside Xia, five per school by 240, six by 300, no unbacked membership, one active school per actor, nationality unchanged across residence/service, city totals equal five components, no city debate duplicates, no more than one rendered landmark per city, and bounded itinerant/queue counts.

- [ ] **Step 5: Run visual and behavioral acceptance**

Observe physical land and dock travel, master-only timed voyage after prolonged failure, lectures recruiting real actors, women learning/teaching/debating/succeeding, male-only central office, foreign guest terms, mortality and lineage continuation, deterministic on/off-screen debates, works/institutions, real portraits, city influence labels, School MapMode city details, and leading landmarks.

- [ ] **Step 6: Verify both path owners**

Run once with embedded AW3 pathfinding and once with Cultiway. In the latter, log must select `inmny.cultiway`, AW3 workers/transport stay disabled, and historical scholars still travel through Cultiway. No run may show Harmony failures, worker exceptions, missing localization, invalid casts, stale-world access, repeated names, automatic Ru assignment, or per-frame log spam.

- [ ] **Step 7: Commit acceptance corrections and prepare push only on request**

Every correction receives a focused RED/GREEN assertion and a narrow commit. Do not push until the user explicitly asks.
