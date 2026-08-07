# Western Court Offices Design

**Date:** 2026-08-07

## Goal

Make Western-general kingdoms use a functional Western court with two
technology-driven bureaucratic levels. Government form continues to control
appointment behavior, while the bureaucratic level controls which offices
exist. Western mayors are stable circulating officials with fixed ten-year
terms and must move between cities at rotation.

## Existing Failure

The existing implementation mixes two different concepts in one
`COURT_INSTITUTION` state:

- bureaucratic development (`western_primitive`, `western_base`);
- government and appointment form (`western_elective`, `western_feudal`,
  `western_royal_direct`).

As a result, Western office availability depends on the active government
state rather than the completed bureaucratic technology. Most runtime filling
only processes central offices, while city appointments remain hard-coded to
the Xia `governor` office and military offices are definitions without a full
placement lifecycle.

## Court Levels

The Western court has three runtime states, two of which are formal levels:

1. `western_primitive`: household council, no formal offices.
2. `western_bureaucratic`: bureaucratic system, unlocked by
   `aw_west_tech_office_system`.
3. `western_feudal_bureaucratic`: feudal bureaucratic system, unlocked after
   the office system by `aw_west_tech_feudal_retainers`.

The second level retains every first-level office and adds its own offices.
Election, feudal-retainer, and royal-direct policies do not select an office
catalog. They remain independent appointment modifiers stored in the existing
government state.

Legacy values migrate as follows:

| Legacy institution | Bureaucratic level |
|---|---|
| `western_primitive` | household council |
| `western_base` | bureaucratic system |
| `western_elective` | bureaucratic system |
| `western_feudal` | feudal bureaucratic system |
| `western_royal_direct` | feudal bureaucratic system when feudal-retainer technology is complete; otherwise bureaucratic system |

The government state is preserved independently, so migration does not erase
election terms, feudal candidate preference, or royal appointment authority.

## Office Catalog

### Bureaucratic System

| Office | Layer | Responsibility |
|---|---|---|
| Executive Magistrate | central | administration, policy execution, state coordination |
| Senate Elder | central | noble deliberation, legislation, judicial advice |
| High Priest | central | rites, calendar, legitimacy, wartime divination |
| Field General | military | field command, mobilization, expedition leadership |
| Mayor | city | city administration, security, taxation |

### Feudal Bureaucratic System Additions

| Office | Layer | Responsibility |
|---|---|---|
| High Justice | central | royal seal, law, realm-wide justice |
| Treasurer | central | treasury, taxation, customs, coinage |
| Palace Steward | central | royal estates, palace staff, ceremonies |
| Royal Constable | central military | realm army, military justice, knight administration |
| Marshal | military | formation command, recruitment, equipment, expeditions |
| Secretary | central | royal documents, decrees, official archives |
| Count | city/territorial | regional administration, taxation, security, militia |

`west_royal_chamberlain` is migrated to the canonical Royal Constable office
instead of being silently reinterpreted. Existing active career and court rows
must retain actor, kingdom, appointment year, and history continuity.

## Appointment Behavior

Office availability is determined only by bureaucratic level. Appointment
behavior is determined independently:

- elective offices: six-year central terms and election-based vacancy filling;
- feudal retainers: landed nobles and educated retainers receive candidate
  weight without excluding qualified common candidates;
- royal direct rule: the ruler may manually appoint and dismiss officials;
- default Western government: bounded AI vacancy filling through the existing
  court candidate cache.

The ruler cannot become an ordinary official. Existing cross-profile and
same-layer incompatibility rules remain authoritative.

## Western Mayor Circulation

After the bureaucratic system unlocks, a Western city leader is projected as
`west_mayor`, not the Xia `governor` office.

- A formal mayor has a fixed ten-year term. The general court term law does
  not alter this term.
- The kingdom stores a common mayoral rotation cycle. At cycle expiry, live
  formal mayors form a deterministic cross-city rotation ring.
- Every mayor must move to a different eligible city. The operation updates
  actor city membership, city leaders, career state, court officer state,
  previous city, destination city, and the next term end year.
- Persistence commits as one transaction before runtime projection. Runtime
  application is guarded by `GovernorRotationRuntimeScope`. Any failure rolls
  the entire plan back; partial rotations are invalid.
- A one-city kingdom retains its mayor and retries annually. Once a second
  eligible city exists, rotation may proceed.
- A city founded during a cycle receives a mayor through normal vacancy
  filling, but that appointment inherits the kingdom's current cycle end year.
- Death, defection, capture, or destruction closes the invalid appointment and
  fills the vacancy. A replacement inherits the remaining cycle rather than
  resetting ten years.
- Native-city exclusion remains in force: a circulating mayor cannot be sent
  back to the actor's frozen native city.
- Rotation reads the indexed active mayor rows and live city list only. It
  must not scan residents or all kingdom actors.

The existing all-or-nothing governor rotation machinery is reused after it is
generalized from a hard-coded `governor` office to a supplied city-office ID.

## Military and Territorial Offices

Field General, Royal Constable, and Marshal use the existing military career
transition hooks. Appointment must release incompatible civil or active army
state before committing the military office. Removal restores the actor's
latest valid career projection.

Mayor and Count are separate office identities. A city has one active leader;
the active leader's office is selected by Western bureaucratic level and the
city's territorial role. Upgrading the institution migrates compatible local
appointments without changing the actor or resetting the current mayoral
cycle unless the office itself changes.

## Effects

Office effects are defined by pure rules and aggregated from the existing
active-officer cache. Opening the court window and annual effect reads must
not scan all actors.

- central administrative offices affect administration, policy execution,
  legitimacy, justice, and tax collection;
- military offices affect mobilization, organization, garrison, and command;
- local offices affect only the assigned city or territorial seat;
- vacancies contribute no effect;
- effects are bounded and additive to existing policy effects rather than
  replacing them.

Exact numerical values belong in rule constants covered by unit tests. The
initial implementation should use restrained values so the primary delivery
is functional office behavior, not a large balance rewrite.

## UI and Localization

The existing draggable wide court window is reused. It displays the current
Western bureaucratic level and separates offices into central, military, and
local sections. The kingdom-side court button uses Western labels instead of
`Eastern Zhou Six Ministers` or `Hundred Schools Court`.

Every institution name, office name, duty description, vacancy label,
appointment action, rotation result, and migration-facing biography label is
provided in simplified Chinese, English, and traditional Chinese CSV entries.

## Persistence and Recovery

Migration is idempotent and versioned. It updates institution identity and
known renamed office IDs without deleting career history. Stale or impossible
active rows are closed with an explicit migration reason, then filled through
the normal bounded vacancy pipeline.

On load, runtime projections are rebuilt from committed career rows. A failed
runtime mayor rotation schedules bounded repair; it does not repeat database
mutation or create a second appointment.

## Verification

Tests must cover:

- Western profile selection and the two formal bureaucratic levels;
- cumulative office catalogs at both levels;
- independence between office level and appointment government state;
- legacy institution and Royal Chamberlain migration;
- Western city leader projection as Mayor;
- fixed ten-year mayor terms and inherited cycle end for replacements;
- deterministic cross-city rotation, one-city deferral, native-city exclusion,
  rollback, and no partial persistence;
- military and local office lifecycle;
- UI source guards and all three localization columns;
- bounded annual work with no resident or all-actor scan;
- main project compilation and source-only deployment verification.

The complete Rules suite may still expose unrelated existing failures. The
Western court slices and source guards must pass independently, and any
unrelated failure must be reported rather than hidden by this work.
