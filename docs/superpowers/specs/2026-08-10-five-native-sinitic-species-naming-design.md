# Five Native Sinitic Species Naming Design

## Goal

Make the following civilized species use the same complete personal-name and
lineage lifecycle as `civ_monkey`:

- `civ_dog`
- `civ_fox`
- `civ_lemon_man`
- `civ_rabbit`
- `civ_turtle`

Their actor names remain sourced from their current word-library-backed
`civ_*_name` generators. They display surname before given name, inherit a
stable surname through the family, enter AW3 genealogy, and never use Western
family particles or Western actor-name ordering.

This is a naming and genealogy classification. It does not make these species
biological Xia, monkeys, or automatically eligible for Xia institutions,
policies, courts, textures, or gameplay balance.

## Chosen Architecture

Add a dedicated persisted naming profile for native Sinitic species instead of
putting the five species into `NamingProfileId.Monkey` or retaining the
`Western` profile with scattered exceptions.

The new profile shares a generalized native-Sinitic personal-name and lineage
lifecycle with the monkey profile. Monkey-specific assets, fallback pools,
name-set registration, balance changes, textures, and policy gates remain in
`CivMonkeyNamingContent` and `CivMonkeyPolicyRules`. The shared lifecycle is
selected through semantic predicates rather than checks named only for
`civ_monkey`.

The persisted profile id is distinct from `monkey` and `xia`. Existing monkey
saves retain their current profile id and behavior.

## Species Boundary

One pure rule owns the exact five-species set. It is used by natural naming
profile selection for actors and cultures. Wild or miniature assets are not
included merely because their ids contain `dog`, `fox`, `rabbit`, `turtle`, or
`lemon`.

The rule must return false for at least:

- wild `dog`, `fox`, `rabbit`, and `turtle` actors;
- `lemon_snail` and `miniciv_lemon_snail`;
- unrelated civilized species such as `civ_bear`;
- `civ_monkey`, which retains its dedicated monkey profile.

Resolution precedence remains explicit: biological Xia, civilized monkey, the
five native-Sinitic species, orc nomadic, then Western. A contradictory asset
cannot accidentally acquire a Western tradition.

## Word-Library Authority

The current AW3 resources are authoritative. Personal-name words continue to
come from these existing generators in
`name_generators/default/creatures.json`:

| Species | Existing generator |
| --- | --- |
| Dog | `civ_dog_name` |
| Fox | `civ_fox_name` |
| Lemon person | `civ_lemon_man_name` |
| Rabbit | `civ_rabbit_name` |
| Turtle | `civ_turtle_name` |

Those generators continue to read the word libraries already referenced by
their current templates. This design does not replace Japanese, Shanhai, or
other currently selected libraries; does not import a new naming package; and
does not add hard-coded surname or given-name arrays for the five species.

The existing `civ_*_city` and `civ_*_kingdom` generators remain authoritative
for city and kingdom names. They must not be replaced with monkey or generic
Xia generators. Culture, language, religion, alliance, book, item, and war
naming are outside this change unless their existing explicit generator already
handles them.

If a configured generator or referenced library is unavailable, the resolver
must not cross into a Western generator. It retains the last valid persisted or
vanilla name and emits one bounded warning. Repair can retry after naming
resources become available; it cannot invent a code-owned fallback pool.

## Structured Personal Name

The five current actor generators already place the generated surname at the
front and tag it as `family_name`. Their given-name portions are not uniformly
tagged: rabbit and fox have no `given_name` tag, while dog, lemon person, and
turtle tag only part of the generated given name.

The integration therefore must not shorten names by trusting only the existing
`given_name` component. It resolves name parts as follows:

1. Generate the complete name with the actor's current `civ_*_name` generator.
2. Read the generated `family_name` component.
3. Require that family component to be the visible prefix for these five
   generators.
4. Derive the complete given name by removing that one prefix from the complete
   generated name and trimming only separator whitespace.
5. Persist the resulting family and complete given name separately.
6. Project the actor as `family + given`, without Western spaces or particles.

This preserves every word selected by the current template, including untagged
second-name elements. A malformed result with no usable family or given portion
is rejected atomically; it does not partially overwrite an existing identity.

## Birth And Inheritance

New actors follow the monkey lifecycle:

1. Resolve the child's native-Sinitic naming profile from the exact actor asset.
2. Prefer an existing surname from the lineage-bearing parent using the current
   monkey parent-selection convention, including the male-parent preference and
   valid fallback parent.
3. Generate the child's given name from the child's own current species
   generator.
4. When no parent supplies a usable surname, use the surname selected by that
   same generated species name.
5. Persist `family_name`, `chinese_family_name`, the localized family component,
   and the complete given-name fields before display projection.
6. Record parent edges and the actor archive through the atomic birth archive
   service.

An inherited surname is not rerolled merely because the child belongs to a
different one of the five species. The given name still comes from the child's
own species generator. Historical authored names and player custom names remain
protected from automatic regeneration.

## Lineage And Branches

The five species use the same surname identity semantics as civilized monkeys:

- `family_name`, `chinese_family_name`, and the initial `clan_name` carry the
  same visible surname;
- rulers, heirs, nobles, and officials receive or inherit stable lineage and Shi
  ids before being projected or archived;
- promotion never derives a Western family stem from a city and never adds
  `von`, `de`, `van`, `di`, or their localized particles;
- descendants inherit the branch and surname through the existing atomic birth
  path;
- enfeoffment and official branch creation use the existing AW3 branch
  lifecycle, while retaining surname-before-given-name projection;
- family-tree entry, detached archive portraits, death updates, succession, and
  save/load restoration use the same full genealogy coverage as monkeys.

The implementation introduces a naming/genealogy predicate separate from
`IsNativeXiaCultureActor`. Policy and institutional gates continue to recognize
only the species they recognize today. Biological-Xia rendering gates remain
biological.

## Culture And Xiaization Interaction

A culture founded by one of the five species persists the new naming profile
and does not persist a Western naming tradition. Culture splits inherit the
profile. Actors of these species keep the native-Sinitic personal-name profile
when living in an ordinary Western or Xia culture, just as civilized monkeys
keep their dedicated naming profile.

The kingdom may still progress through the existing Xiaization system for
technology, policy, and court behavior. That institutional transition does not
regenerate an already valid native-Sinitic personal identity. Name integration
may add its normal markers and branches, but it cannot reverse name order or
replace the actor's current species word source.

## Existing Saves

Migration is lazy and bounded. No load-time or per-frame world scan is added.

When an affected actor, family branch, promotion, succession, or family-tree
query first enters the naming boundary:

1. Preserve historical authored and player-custom names unchanged.
2. If a complete structured native-Sinitic identity already exists, reuse it.
3. If a branch has several living or archived members, resolve one surname for
   the branch from its founder/root identity and propagate that surname; never
   reroll one surname per descendant.
4. Replace a legacy Western family display with a surname selected through the
   affected species' current generator and stable branch/founder seed.
5. Preserve a valid existing given name when it can be separated safely;
   otherwise regenerate the given name through the actor's current species
   generator.
6. Update live data and archive projection through the existing bounded write
   queue and transactional lineage writer.

Repeated migration is idempotent. A failed database write leaves the previous
identity visible and retryable rather than publishing half of a converted
branch.

## Manual Rename And Persistence

The existing split surname/given-name editor treats the new profile as
surname-first. A player edit updates the same structured fields and marks the
identity as custom. Subsequent births may inherit the edited surname where the
normal parent rule selects that actor, but projection, promotion, succession,
language refresh, save/load, and lazy migration cannot overwrite the edited
actor's name.

## Performance

All work is event-driven and bounded:

- generation occurs at actor creation or an explicit repair boundary;
- family conversion operates one branch at a time;
- archive writes use the existing bounded queue;
- no frame update enumerates actors, cultures, kingdoms, or family trees;
- no load callback synchronously migrates every affected actor.

## Verification

Pure rule tests cover:

- exact inclusion of all five civilized asset ids and exclusion of wild,
  miniciv, lemon-snail, monkey, and unrelated civilized ids;
- profile serialization, parsing, culture inheritance, and exclusion from
  Western tradition persistence;
- exact actor, city, and kingdom generator ids for each species;
- complete family/given extraction for every current five-species template,
  including the currently untagged given-name suffixes;
- surname-first projection with no Western particle or Western whitespace;
- inherited surname plus child-species given-name generation;
- missing-resource behavior without cross-profile fallback.

Lifecycle and source tests cover:

- birth persistence and atomic parent-edge archival;
- ruler, official, and landed-branch admission;
- family-tree availability for descendants and archived actors;
- old-save branch migration, idempotence, and bounded retry;
- player custom-name persistence across promotion, succession, and save/load;
- unchanged monkey naming, unchanged Western human/elf/dwarf behavior, and
  unchanged orc nomadic behavior;
- absence of periodic world scans and institutional eligibility expansion.

The current baseline rules suite has an unrelated failure in
`KingSuccessionPreparationRulesTests.ArchiveCandidateLossRescanIsBounded` at
commit `5fd1ff57`. Implementation verification must report that baseline
separately and run the naming-specific rule and source suites even if the
unrelated succession assertion remains unresolved.

## Acceptance Criteria

- Every newly generated actor of the five species obtains all personal-name
  words from its current species generator and referenced current libraries.
- Every displayed personal name is surname before complete given name.
- The surname is stable across promotion, succession, save/load, and ordinary
  projection refreshes.
- Children inherit the selected family surname and retain a given name from
  their own species generator.
- Rulers, officials, nobles, and descendants retain working family-tree entry
  and archive history.
- No affected actor receives a Western naming tradition, family particle, or
  given-before-family projection.
- No monkey-specific words, city names, kingdom names, policy eligibility,
  textures, or balance settings leak into the five species.
- Existing player-edited names are never overwritten.
- The feature adds no per-frame or unbounded load-time migration.
