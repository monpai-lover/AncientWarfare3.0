# Western General Policy And Integrated Naming Design

Date: 2026-07-31

## Goal

Make AW3 self-contained for localized naming and give every civilized non-Xia
society a complete playable institutional path.

The completed feature must:

- integrate the full `ChineseName_1.5.0new` generator, templates, and word
  libraries into AW3 so the external mod and `Chinese_Name.dll` are not needed;
- enable Chinese localized names only while the game language is `ch` or `cz`;
- retain a native name and a Chinese name so changing language immediately
  changes presentation without changing identity;
- give non-Xia cultures stable western naming traditions and noble family trees;
- use the nomadic libraries as the complete naming profile for `orc`;
- automatically assign the `WesternGeneral` technology and policy profile to
  civilized non-Xia kingdoms;
- preserve the existing native-Xia behavior for biological Xia and
  `civ_monkey`;
- support kingdom/culture splits, full Xiaization, saves, multiplayer mirrors,
  and old-save migration without periodic whole-world scans.

## Scope Boundary

`civ_monkey` is excluded from every western automatic-assignment rule. This
does not remove its existing genealogy. It remains a native-Xia-culture species
and continues to use its monkey naming resources, Xia policy profile, Xia
institutions, and Xia family-tree lifecycle.

Biological Xia also remains on the Xia profile. A foreign culture can move from
`WesternGeneral` to Xia only through the Xiaization rules described below.

The feature applies to civilized assets (`asset.civ == true`). Non-civilized
animals and transient creatures do not gain policy profiles or noble families.

## Architecture

### Naming subsystem

Move the Chinese Name implementation into an AW3-owned `core/naming` subsystem:

- `AWNameGeneratorLibrary`
- `AWNameGeneratorAsset`
- `AWNameTemplate`
- `AWWordLibraryManager`
- `AWNameParameterGetters`
- `AWLocalizedNameService`
- focused Harmony patches under `patch/naming`

Copy the complete `name_generators/default` and `word_libraries/default`
resources into AW3. Existing AW3 Xia and monkey resources register into the same
library and can override default generator ids explicitly. AW3 initialization
registers patches directly through its normal Harmony lifecycle; it does not
copy the old assembly-wide `IPatch` reflection scan.

Remove these external integration points:

- the `一米_中文名` conditional compilation symbol;
- the `Chinese_Name.dll` project reference;
- `一米_中文名` from `OptionalDependencies`;
- compile-time `Chinese_Name` namespace imports and reflection workarounds.

Preserve the original MIT license and attribution in AW3's third-party notices.
Declare `一米_中文名` incompatible in `mod.json`. If a loader still starts both
mods, AW3 logs one explicit error and disables only its naming patches rather
than allowing two patches to rewrite the same names.

### Policy profiles

Replace the single global `KingdomPolicyDefs` view with a profile-aware catalog:

- `Xia`
- `WesternGeneral`
- `Common`

Every node declares one or more profile ids. Common decisions can appear in
both profiles. `KingdomPolicyProfileService` is the only authority for resolving
a kingdom's current profile. UI, AI, research, city spread, status queries,
snapshots, and map modes must query through this service rather than enumerate
the unfiltered global list.

### Court profiles

Keep one court persistence and appointment engine, but separate the office
catalog and appointment strategy:

- `XiaCourtProfile` continues to expose Zhou/Han/Tang/Song institutions.
- `WesternCourtProfile` exposes western offices and election/appointment rules.

An actor has one office record and one grade record. Profile changes end or map
incompatible offices; they never let an actor hold a Xia and western version of
the same court layer simultaneously.

## Language-Safe Dual Names

### Language gate

`AWNamingLanguageRules.IsChinesePresentation` returns true only for language
codes `ch` and `cz`, case-insensitively. All other codes use native names.

Objects carry two presentation values:

- `native_name`: the name produced by the original game path before AW3's
  localized postfix runs;
- `chinese_name`: the AW3 localized projection.

Actors additionally retain structured given-name and family identity fields.
Identity, database joins, family membership, and policy ownership use stable
ids, never either presentation string.

When the language changes, AW3 refreshes the visible projection once. It does
not regenerate random names. Actor `getName` uses the selected projection;
objects whose UI reads a raw `data.name` field are rebound by the same bounded
language-change refresh.

### Stable generation

Name generation uses a local deterministic random stream seeded by stable
object id, culture id, generator id, and naming-schema version. It must not use
`UnityEngine.Random` while rendering, opening a window, or changing language.
The same object therefore receives the same Chinese alias on server, client,
save reload, and UI refresh.

Where the game object supports custom data, both projections are saved there.
Other persistent types use an AW3 localized-name table keyed by meta type and
stable object id. Ephemeral objects may use a bounded runtime cache, provided
their visible name is not expected to survive a save.

The multiplayer archive/strategic snapshot includes the structured identity
components required to reproduce names. A replica never mutates authoritative
world names merely because its local UI language differs.

## Naming Profiles

### Western cultural traditions

Every newly created non-Xia, non-monkey culture receives exactly one persisted
western naming tradition:

| Tradition id | Chinese family particle | Example family title |
| --- | --- | --- |
| `western_von` | `冯` | `冯·维也纳家族` |
| `western_de` | `德` | `德·巴黎家族` |
| `western_van` | `范` | `范·布鲁日家族` |
| `western_di` | `迪` | `迪·罗马家族` |

Culture creation selects a tradition from a stable culture seed. Culture
splits inherit the parent tradition. A country's conquest, dynasty change, or
capital move does not change the tradition.

Human cultures use the corresponding French, central-European, Low Countries,
or Latin personal-name pool. Add dedicated Low Countries male/female word
libraries rather than pretending that `范` is a British tradition.

Civilized non-human species keep their species-specific given-name pool. The
culture tradition changes only their noble family form. For example, an elf
keeps an elven given name and may later display `本名 范·布鲁日` after joining a
noble family.

### Orc nomadic profile

`orc` is an explicit exception to western family particles. It still receives
the `WesternGeneral` policy profile, but all localized naming categories use the
nomadic resources:

- actor: `游牧男名`, `游牧女名`, and `游牧姓氏`;
- city: `游牧城名`;
- culture and clan: `游牧姓氏` plus the appropriate tribe label;
- kingdom: `游牧姓氏` plus `游牧国名后缀`;
- language and related contextual names: the corresponding nomadic source.

The fantasy orc libraries remain fallback data only when a required nomadic
library is missing or empty. A normal successful `orc` generation must not mix
the two profiles within one culture.

An orc noble family-tree title uses the nomadic clan/tribe form, for example
`孛儿只斤部落`. It does not use `冯/德/范/迪·城名家族`.

### Xia and monkey profiles

Biological Xia continues to use the current Xia personal, clan, city, kingdom,
and lineage rules. `civ_monkey` continues to use its dedicated monkey resources.
Neither receives a western tradition field during ordinary creation or old-save
migration.

## Personal And Family Display Rules

A civilized non-Xia actor without an AW3 noble family displays only the
localized given name. A hidden generator surname may be retained as migration
or fallback material, but it is not shown as a commoner's family identity.

Examples:

- commoner: `路易`
- noble member: `路易 德·巴黎`
- family-tree heading: `德·巴黎家族`

The actor name never includes the final word `家族`. The family tree and clan
heading do.

Family identity stores at least:

- stable family/branch id;
- parent branch id;
- naming profile and western tradition;
- origin city id and stable origin-city Chinese projection;
- family display stem;
- creation year and source type.

When a noble establishes a landed branch, the branch uses the new grant's city:

- parent: `德·巴黎家族`
- child branch: `德·里昂家族`

The branch remains connected to the parent by id. Family-tree expansion and
"locate family root" traverse through that parent link, so the renamed branch
does not visually sever ancestry.

## Non-Xia Genealogy Lifecycle

All civilized non-Xia species except monkey participate in lightweight parent
edge recording from birth. Full archive rows, portrait snapshots, and branch
records are required for rulers, heirs, nobles, officials admitted to noble
status, and their traced family members. This preserves promotion ancestry
without creating full portrait archives for every commoner in the world.

Automatic western family-tree enablement means:

- a new non-Xia king receives or inherits a family immediately;
- a noble promotion creates or inherits a family before the actor is shown in
  court candidate or family-tree UI;
- children inherit the appropriate parent branch under the existing lineage
  rules;
- death updates the same archive row and retains the actor's species portrait;
- title and office refreshes update the archive without changing family id;
- family-tree queries remain detached, bounded, and projection-versioned.

The migration admission queue is kingdom-bounded and authority-cycle driven.
It must not scan every actor every frame or process every school/culture during
one annual update.

## WesternGeneral Technology Tree

Research nodes represent acquired institutional or material knowledge. A
separate social policy adopts the unlocked governmental form.

```text
文字记事
├─ 陶范铸造 → 青铜铸造 → 铁器铸造
│              ├─ 铸造金币
│              └─ 仓储记账 → 城防营造
├─ 井田测绘 → 灌溉渠 → 分封考
├─ 税务官 + 铸造金币 → 地主税
└─ 官职体系 + 陶范铸造 + 地主税
   → 选举制 → 礼乐制度 → 封建家臣 → 国王直辖
```

Profile-specific ids use an `aw_west_` prefix. Truly shared material nodes may
retain their existing id so Xiaization mapping does not duplicate completion.

### Technology effects

| Node | Required effect contract |
| --- | --- |
| 文字记事 | unlock policy records, organized research, and basic administration |
| 陶范铸造 | improve basic workshop output and unlock bronze work |
| 青铜铸造 | improve equipment production and unlock coin/granary branches |
| 铁器铸造 | improve late equipment output and material quality |
| 铸造金币 | enable minted-money tax and trade accounting |
| 仓储记账 | increase food storage and enable organized famine transfers |
| 城防营造 | improve garrison/occupation resistance and advanced defenses |
| 井田测绘 | improve land records and advance the existing city-zone tech tier |
| 灌溉渠 | improve farm output and famine resilience |
| 分封考 | unlock systematic governor, grant, and vassal administration |
| 税务官 | unlock the tax official and organized collection |
| 地主税 | increase regular revenue at a noble-loyalty cost |
| 官职体系 | enable the western court and its initial offices |
| 选举制 | unlock elected official terms; it does not elect the king |
| 礼乐制度 | improve legitimacy, same-culture relations, and succession stability |
| 封建家臣 | unlock landed-retainer offices and the feudal policy |
| 国王直辖 | unlock direct royal appointments and centralization |

Numeric balance values live in focused rules constants and receive rule tests.
No effect may be implemented only as tooltip text.

## Western Social Policies And Court

### Social policy path

```text
户籍与税务
├─ 奴隶制 → 奴隶管控 → 奴隶军
└─ 地主税制 → 贵族议政
               ├─ 选举官制
               └─ 封建家臣制 → 王室直辖制
```

The slavery branch is optional. AI selection depends on economy, war pressure,
population composition, and the dominant school; it is not a mandatory step in
the western profile.

Government adoption nodes require their corresponding technology. Government
states are mutually exclusive even though completed research remains recorded.

### Office stages

`官职体系` opens:

- 执政官
- 元老院长老
- 祭司长老
- 前线将军
- 市长

Under `选举官制`, the king remains a lifelong ruler governed by the kingdom's
inheritance law. Officials serve six-year terms, vacancies trigger an immediate
bounded election, and incumbents may be re-elected. Ability, merit, family,
school, and faction influence contribute to voting.

`封建家臣制` exposes:

- 大法官
- 财政大臣
- 宫禁总管
- 王室事务长
- 元帅
- 文书官
- 郡长

Landed nobles and educated candidates receive preference. `王室直辖制` gives
the king direct appointment and dismissal power, improves administrative/tax
effects, and increases noble/vassal dissatisfaction.

Appointment and grade promotion are one atomic operation. A candidate does not
need to possess the office's grade before appointment. Profile-specific office
localization is resolved by court profile rather than reusing Zhou/Han/Tang/Song
titles.

## Decisions

Common decisions visible to both policy profiles include:

- move capital;
- royal/realm expansion;
- fabricate a core;
- seek a protector/suzerain;
- absorb a vassal after satisfying the existing spy-network/annex conditions;
- control slaves when slavery is active.

Generalize `抚夏民` into `安抚异族`. It targets controlled cities whose culture
differs from the ruling culture. Xia cities retain Xia-specific localization and
Xiaization side effects.

Western kingdoms gain `巩固王权`, which spends political points to reduce
succession controversy and increase legitimacy. Governor enfeoffment remains a
court operation and does not occupy the normal decision slot.

`采夏礼` and `行夏制` are hidden until a foreign kingdom has qualifying Xia
contact. They remain the explicit route into the Xia policy profile.

## Profile Assignment And Inheritance

Resolution order is explicit:

1. Native Xia culture, including `civ_monkey`: `Xia`.
2. Culture with the persisted full-entry-into-Xia trait: `Xia`.
3. Civilized non-Xia kingdom: `WesternGeneral`.
4. Non-civilized or invalid object: no AW3 policy profile.

New kingdoms receive a profile before policy AI or UI can query them. Rebellion
and succession splits inherit the parent kingdom's completed nodes, current
research, and progress only when those nodes belong to the resolved child
profile. Culture splits inherit the parent naming tradition and entry-into-Xia
trait.

A non-Xia rebellion therefore keeps the source kingdom's western progress. If
its founding culture already has the entry-into-Xia trait, it receives the Xia
profile and mapped Xia progress instead of reverting to an uninitialized state.

## Full Xiaization Transition

Full Xiaization is an atomic profile transition:

1. mark the ruling culture with the persisted entry-into-Xia trait;
2. map common/material technology completion and proportional current progress;
3. preserve already-created material effects such as equipment, storage,
   defenses, and minted resources;
4. end incompatible western offices and enqueue eligible former officials into
   the Xia court candidate pool;
5. disable western institutional effects;
6. switch policy/UI/AI/map-mode reads to the Xia profile;
7. establish a new Xia family branch beneath the actor's existing western or
   nomadic family branch.

The family conversion is a real child branch, not an alias and not a retroactive
rename. The founder and descendants assigned to the new branch use Xia Shi rules
from that point onward. The old western/nomadic branch, its dead members, and its
historical display names remain intact and traceable.

Automatic reversion to `WesternGeneral` is forbidden. A later kingdom founded by
a culture without the entry-into-Xia trait still follows its own western state.

## AI Behavior

AI research selection is need-driven rather than a fixed historical order:

- food shortage: irrigation and granary accounting;
- weak equipment: bronze/iron casting;
- active war or threatened borders: city defenses;
- poor treasury: coin minting, tax official, and landlord tax;
- court vacancies: office system and the next available appointment institution;
- large territory: land survey, enfeoffment study, and administrative stages;
- weak royal authority: ritual order or royal domain;
- strong noble opposition: noble council/elective compromise instead of forced
  centralization.

AI scores only nodes in the current profile and continues to respect player node
locks. It cannot start a Xia-only policy merely because that node remains in the
global catalog.

## Save Migration

Migration is schema-versioned and idempotent.

For an old save without the external Chinese Name mod:

- preserve the current object name as `native_name`;
- generate a deterministic Chinese projection only when `ch/cz` needs it;
- assign existing non-Xia cultures a deterministic persisted tradition;
- assign eligible kingdoms `WesternGeneral` before admitting existing nobles.

For an old save previously written by Chinese Name, the original vanilla name
cannot always be recovered. Preserve the current value as the legacy Chinese
projection. When a non-Chinese language first needs a native projection, generate
one through the original template path once and persist it. Do not repeatedly
rename the object on later loads.

Existing non-Xia rulers and nobles are admitted in bounded kingdom batches. The
migration uses existing Clan/city/parent data where possible and never rebuilds
the whole world in one annual tick.

## Failure Handling

- Missing localized resources fall back to the native name and log a bounded,
  deduplicated warning.
- A missing culture tradition is derived deterministically and persisted once.
- An invalid family origin falls back to the founder's current city, then the
  kingdom capital; a family is not created with an empty display stem.
- A failed Xiaization transaction leaves the old profile and family active; it
  does not expose a half-migrated court or branch.
- A profile contains no unknown node ids after migration; obsolete ids remain in
  a diagnostic archive field rather than appearing in the current UI.
- Language switching never performs authoritative diplomacy, policy, family, or
  history writes.

## Performance Requirements

- No naming, profile, or genealogy world scan runs per frame.
- Language refresh is event-driven and processes visible objects first, then a
  bounded queue.
- Birth and culture/kingdom creation perform O(1) identity work.
- Full family portrait archives are not created for every commoner.
- Old-save admission is split across authority cycles and stops when the world is
  paused or loading.
- Policy AI enumerates only the active profile's nodes.
- Name templates and word libraries load once and use indexed lookup.
- Missing-resource diagnostics are deduplicated by generator/library id.

## Verification

### Pure rules

- language gate accepts only `ch/cz` case-insensitively;
- stable naming seed does not change across repeated calls;
- culture tradition assignment and split inheritance;
- commoner, noble, family heading, and landed-branch formatting;
- `orc` resolves to nomadic resources and never a western particle;
- Xia and monkey profile exclusion from western assignment;
- profile resolution and split/rebellion inheritance;
- technology prerequisite graph and profile filtering;
- election term, vacancy, and appointment-grade rules;
- Xiaization mapping and irreversible transition;
- western-family-to-Xia-child-branch projection.

### Source and resource guards

- no `Chinese_Name` reference, conditional symbol, or optional dependency remains;
- all expected generator/resource categories exist in AW3;
- the old external mod is declared incompatible;
- every policy UI/AI/status enumeration passes through profile filtering;
- naming patches check the language/display service rather than directly testing
  arbitrary locale strings;
- no `UnityEngine.Random` call exists in display-time name generation;
- `civ_monkey` remains routed to native Xia institutions and monkey names.

### Integration and runtime

- create human, elf, dwarf, and orc cultures and verify names, profiles, policies,
  and family-tree entry;
- switch `ch -> en -> cz` without identity or family-tree changes;
- split a culture and kingdom, save, reload, and compare traditions/progress;
- promote a commoner and verify the name changes from given-only to
  given-plus-family exactly once;
- establish a landed family branch and trace it to the root;
- fully Xiaize a western and an orc family and verify a connected Xia child branch;
- run host/client with different UI languages and compare authoritative ids;
- load saves created with and without the old Chinese Name mod;
- run policy/court/family-tree rule suites, source guards, Debug build, and Release
  build without requiring `Chinese_Name.dll`.

## Acceptance Criteria

- AW3 loads and builds with no external Chinese Name installation or DLL.
- All migrated naming categories work in `ch/cz`; other languages show native
  names and can switch live.
- Non-Xia civilized kingdoms automatically receive the complete
  `WesternGeneral` profile and usable policy AI.
- Non-Xia nobles automatically receive stable, correctly formatted genealogy.
- `orc` uses the nomadic naming profile while retaining western policies.
- Xia and `civ_monkey` retain their existing Xia and monkey behavior.
- profile/family transitions survive splits, Xiaization, multiplayer replication,
  and save reloads.
- profiling shows no new per-frame or annual whole-world naming/genealogy scan.
