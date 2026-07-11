# AW3 Vassal, Succession, And Xiaization Correctness Design

## Goal

Fix three related kingdom-state correctness defects without changing unrelated
war, rebellion, or government systems:

1. A kingdom may establish a new vassal relation only with a directly bordering
   kingdom.
2. Monarchical succession must prefer the king's real eldest eligible son and
   must continue through the wider paternal genealogy instead of incorrectly
   declaring extinction or creating a republic.
3. A foreign kingdom that becomes fully Xiaized receives one Xia-style kingdom
   name, while later player or mod renames remain respected.

This specification supersedes any older succession text that allowed an adult
younger son to outrank an underage elder son or treated non-king offices as
succession disqualifiers.

## Confirmed Defects

- `VassalService.CanSetVassal` validates kingdom state but does not validate a
  shared border.
- `VassalAIService.FindBestSuzerain` gives neighbors a score bonus, so a remote
  kingdom can still win the candidate ranking.
- `HeirService.FindHeir` checks `PickEldestLivingSon` only when genealogy has
  already failed. A stale or collateral genealogy candidate can therefore beat
  a valid direct son.
- `HeirService.GetHeir` accepts any cached eligible actor without comparing that
  actor with the king's current eldest eligible son.
- `LineageService.OnActorBornWithParents` refreshes succession while
  `BabyHelper.applyParentsMeta` is running, but the original `BabyMaker.makeBaby`
  assigns the baby's final sex afterward. A son can consequently be omitted from
  the cached succession result at birth.
- `XiaizationService.TrySetLevel` updates the Xiaization level but does not invoke
  kingdom naming. `XiaNamingRepair.IsXiaKingdom` recognizes original Xia identity
  but not a foreign kingdom whose Xiaization level has reached the maximum.

## Direct-Border Vassal Rule

### Shared Definition

Two kingdoms are directly adjacent when at least one living city or territory
cell belonging to the first kingdom touches a living city or territory cell of
the second kingdom according to the game's existing kingdom-neighbor data. An
alliance, a common enemy, a shared suzerain, a vassal chain, or sea proximity does
not count as a direct border.

The adjacency test is centralized in a pure rule/helper so the service and AI do
not develop different definitions. It rejects null, dead, identical, or
territory-less kingdoms before checking adjacency.

### Creation And Reparenting

`VassalService.CanSetVassal` becomes the mandatory gate for every path that
creates or changes a vassal relation:

- voluntary submission;
- peace or war settlement;
- AI-selected vassalization;
- transfer to a different suzerain;
- manual or scripted relation setting.

The prospective vassal and prospective suzerain must border at the moment the
relation is created or reparented. `VassalAIService.FindBestSuzerain` filters out
non-neighbors before scoring instead of merely awarding a neighbor bonus. A
caller cannot bypass the rule by directly supplying a high-scoring remote
suzerain.

Existing relations are not dissolved when later conquest, rebellion, or city
loss separates the two states. The rule governs establishment and reparenting,
not continuous validity. Independence and release operations remain available
without an adjacency requirement.

## Monarchical Succession

### Eligibility

A monarchical heir candidate is eligible when the actor is:

- male;
- alive and resolvable;
- not mad;
- not a slave;
- not currently serving as any kingdom's king.

Age does not affect eligibility or primogeniture order. An eligible underage
eldest son outranks an eligible adult younger son. The office dimension excludes
only actors already serving as king. Generals, city leaders, captains, fief
holders, central officers, local officers, and other leaders remain eligible.

### Search Order

Every full refresh applies one deterministic search order:

1. The current king's real direct sons, eldest first.
2. The king's paternal descendants by branch: the nearest generation first and
   elder branches before younger branches.
3. The king's living brothers, eldest first.
4. The descendants of those brothers, preserving elder-branch priority.
5. More distant paternal collateral branches ordered by nearest common paternal
   ancestor, branch seniority, generation distance, age, and stable actor ID.

The direct-son pass reads the live parent/child relation and persistent lineage
records before any general genealogy ranking. Thus the death of a crown prince
and that prince's sons cannot hide the old king's other sons. If no direct son is
eligible, the genealogy pass still explores brothers, nephews, and collateral
branches instead of returning immediately from the dead crown prince's branch.

Republican succession remains separate: the current strongest eligible citizen
is head of state and the second-strongest eligible citizen is successor. A
monarchy changes to a republic only after the complete paternal search is truly
empty and a valid republican election can be completed.

### Cache Reconciliation

`GetHeir` no longer permanently trusts an arbitrary eligible cached actor. Its
cheap validation compares the cached heir with the current king's direct sons:

- if an older eligible direct son exists, the cache is stale;
- if the cached actor is dead, missing, ineligible, outside the expected
  succession relation, or already a king, the cache is invalid;
- otherwise the cached result remains usable without a full genealogy scan.

Each kingdom performs a cheap yearly reconciliation that inspects only the
current king, the cached heir, and the king's direct children. It invokes the
full genealogy refresh only when that small check reports stale or invalid
state. This avoids a yearly whole-lineage traversal.

A full or forced refresh also runs at these correctness boundaries:

- after the baby's final sex has been assigned;
- after a new king accedes;
- immediately before the old king's death transition commits its successor;
- when the registered heir dies, leaves the kingdom, becomes a slave, becomes
  mad, or becomes another kingdom's king.

The birth hook is moved or supplemented with a postfix that executes after the
original final-sex assignment. The earlier parent-registration hook may record
lineage, but it may not commit a sex-dependent succession result.

### Transition Safety

During the original game's delayed `timer_new_king` window, AW3 preserves the
preselected heir and former-king baseline. A temporary null `kingdom.king` does
not mean dynastic extinction and must not:

- create a republic;
- invoke the original whole-kingdom fragmentation path;
- disband the royal guard;
- mark all princes as independent rulers;
- overwrite a prepared heir ID with an empty value.

The existing explicit shattered-crown event and AW3's general or fief rebellions
remain valid. Filling vacant city-leader positions with princes also remains
enabled; holding that office does not remove a prince from succession.

## Fully Xiaized Kingdom Naming

### One-Time Transition

When a non-original-Xia kingdom first reaches maximum Xiaization, the transition
requests one Xia-style kingdom name through the existing Xia naming pipeline. It
does not rename actors and does not alter the separate, already-correct crown
prince naming path.

After a successful rename, the kingdom stores a persistent
`XIA_FULL_NAME_APPLIED` marker. Later maintenance, loading, membership changes,
and Xiaization checks do not rename a marked kingdom. A player or another mod can
therefore rename it afterward without AW3 overwriting that choice.

### Repair And Failure Handling

Low-frequency Xia naming maintenance also recognizes maximum Xiaization as Xia
kingdom identity. A level-five foreign kingdom without the marker receives the
one-time rename and marker, covering kingdoms that reached the level before the
transition hook ran.

The marker is written only after a valid name is produced and applied. If the
kingdom is null, dead, or the naming pipeline cannot produce a valid name, the
operation leaves the marker unset so a later maintenance pass can retry. Original
Xia kingdoms continue through their existing naming behavior and are not forced
through this foreign-Xiaization transition.

## Performance Constraints

- Vassal adjacency uses existing neighbor data and runs only while evaluating a
  new relation; it does not scan every kingdom pair.
- Annual succession reconciliation scans only the current king's children unless
  it detects stale state.
- Full genealogy search is event-driven or stale-state-driven and deterministic.
- Xiaized naming runs at the level transition or existing low-frequency naming
  maintenance, never per frame.
- UI reads do not mutate heir state or trigger full refreshes.

## Verification

The user intentionally removed repository test projects, so focused TDD harnesses
are created temporarily under `F:\tmp` and are not added to the repository.

Required rule and integration scenarios:

- a remote high-score kingdom cannot become suzerain;
- a bordering kingdom can become suzerain through each supported creation path;
- an established vassal relation survives later border separation;
- reparenting to a remote suzerain is rejected;
- an underage elder son outranks an adult younger son;
- generals, leaders, captains, fief holders, and officers remain eligible;
- an actor already serving as king is rejected;
- a son's final male sex is visible before succession refresh;
- a stale cached outsider is replaced by the current eldest eligible son;
- after a crown prince and his sons die, the crown prince's living brother becomes
  heir rather than returning null or creating a republic;
- when direct sons are exhausted, brothers, nephews, and paternal collateral
  branches are searched deterministically;
- the delayed accession window preserves the prepared heir and monarchy;
- a fully Xiaized foreign kingdom is renamed exactly once;
- a later manual rename is not overwritten;
- a maximum-Xiaization kingdom missing the marker is repaired once.

Final verification includes the temporary focused tests, a normal
`dotnet build AncientWarfare3.csproj`, inspection of Harmony patch ordering around
`BabyMaker.makeBaby`, and a diff/status check that leaves all user-owned changes
untouched.

## Non-Goals

- Do not dissolve existing vassal relations solely because borders change.
- Do not require adjacency for independence, release, alliance, or ordinary
  diplomacy.
- Do not change the explicit shattered-crown event or AW3 general/fief rebellions.
- Do not exclude non-king offices from succession.
- Do not make adulthood outrank primogeniture.
- Do not repeatedly overwrite fully Xiaized kingdom names.
