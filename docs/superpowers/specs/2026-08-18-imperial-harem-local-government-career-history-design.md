# Imperial Harem, Office History, and Local Government Design

**Date:** 2026-08-18
**Status:** Approved design
**Repository:** AncientWarfare3.0

## Objective

Extend the existing AW3 household and court systems without introducing a
second office framework:

1. Give imperial and Mandate rulers ten persistent, fixed harem ranks.
2. Display complete incumbent history for every central, censorial, military,
   and local office.
3. Turn each city's abstract bureau into a real local government staffed by
   actors with terms, careers, examination backgrounds, and hometown patronage.

The implementation must preserve existing saves, reuse the current court UI,
and avoid full-world actor scans in annual maintenance or window rendering.

## Scope Boundaries

- `RulerHousehold` remains the sole source of truth for ruler spouses and
  consorts.
- `CourtOfficer` and `OfficialCareer` remain the sole source of truth for
  official appointments and completed terms.
- The EmpireCraft `OfficeObject` system is a behavioral reference only and
  will not be copied into AW3.
- Local government reuses the central court window, cards, links, controls,
  layout parameters, and office parameter types. It does not introduce a
  separately designed window.
- Local offices have local names and hierarchy. They do not duplicate central
  titles such as chancellor or the Six Ministries in every city.
- Harem rank is not automatically revoked because a consort turns 35.

## Imperial Harem Ranks

### Eligibility

The fixed-rank system applies only to empire-tier and Mandate realms. Kingdoms
and lower-tier realms retain the existing queen/principal-wife/consort titles
and capacities.

### Fixed Seats

Imperial and Mandate rulers have exactly these ordered seats:

| Order | Rank code | Display title |
|---:|---|---|
| 1 | `empress` | 皇后 |
| 2 | `consort_de` | 德妃 |
| 3 | `consort_li` | 丽妃 |
| 4 | `consort_zhuang` | 庄妃 |
| 5 | `consort_xian` | 贤妃 |
| 6 | `consort_hui` | 惠妃 |
| 7 | `consort_an` | 安妃 |
| 8 | `consort_he` | 和妃 |
| 9 | `consort_xi` | 僖妃 |
| 10 | `consort_kang` | 康妃 |

`RulerHouseholdRecord.RankCode` stores the stable rank code. The display title
is resolved from localization and realm context rather than persisted as free
text.

### Assignment and Succession

- The principal-wife relationship fills `empress` first.
- Other eligible relationships fill the remaining seats in fixed order.
- Empty seats are filled independently; occupied seats do not reorder when a
  lower seat becomes vacant.
- A ruler's death, abdication, or replacement ends that ruler's active
  household records through the existing relationship lifecycle.
- A new ruler receives a fresh set of empty seats. Former consorts are never
  silently transferred to the successor.
- Historical records retain the rank held under the former ruler and render
  with a historical prefix such as `先帝德妃` when that context is known.
- Age alone never ends an active harem relationship or frees a seat.

### Candidate Selection

Noble status is not a hard requirement. Candidate selection retains existing
age, relationship, realm, duplicate-membership, and safety gates, then scores
eligible actors by the existing useful attributes such as charm, intelligence,
health, and loyalty. Noble status may remain a small tie-breaking bonus but may
not exclude a stronger commoner.

### Legacy Save Migration

Existing imperial household records are assigned ranks deterministically:

1. The active principal wife receives `empress`.
2. Remaining active consorts are ordered by relationship start time and stable
   record ID, then assigned seats 2 through 10.
3. Active legacy records beyond the ten-seat capacity are ended as historical
   legacy relationships; no actor or relationship record is deleted.
4. Existing non-imperial records retain their current generic rank behavior.

Migration must be idempotent so repeated loads do not reshuffle ranks.

## Complete Office History

### Source of Truth

The existing `CourtOfficer`/`OfficialCareer` persistence already records the
required facts: kingdom, city, layer, office, actor, actor-name snapshot,
appointment year/time, end year/time, active state, and end reason. The feature
must expose those records rather than create a duplicate history table.

### Query Contract

A shared office-history query accepts:

- kingdom ID;
- office layer and office ID;
- optional city ID for local offices.

It returns terms in reverse appointment order. Each item includes actor ID,
stored actor name, start year, end year, active state, and end reason. Active
terms render as `开始年份—至今`; ended terms render as
`开始年份—结束年份`. Stored names remain available after actor death.

The query contract applies equally to central, censorial, military, and city
offices. Every office card exposes the same history command and presentation.

### Lifecycle Completeness

Appointment, dismissal, death, disqualification, transfer, city ownership
change, realm destruction, and term expiry must close the current record with
an explicit reason. A transfer closes the old term before opening the new one;
one actor cannot hold multiple formal offices concurrently.

## Local Government

### Navigation and UI Reuse

The national court view replaces the current flat list of local officials with
one large local-government card per city. Each card shows:

- city name and visual identity;
- the local leader;
- the leader's current term;
- filled offices versus available offices;
- local government efficiency.

Selecting the card opens `CourtWindow` with a city context instead of a new
window type. The city context reuses the existing summary frame, office cards,
hierarchy links, pan/zoom behavior, portraits, appointment actions, tooltips,
and history entry. Reuse must be structural: the same components and layout
parameter types are bound to a different read model.

### Office Structure

- The city leader is the root of the local hierarchy.
- Subordinate local offices are generated from a local office definition set
  selected by the realm's court profile and culture.
- Local definitions use the existing office parameter model for grade, school,
  eligibility, effects, and hierarchy layout.
- Local office count continues to use the existing population, zone count, and
  capital-status calculation so the simulation does not gain a second capacity
  formula.
- `CityBureauState` remains the cached aggregate state but its filled count and
  officer IDs are derived from real appointments instead of synthetic values.

### Appointment and Terms

Every local official is a real actor with an `OfficialCareer` appointment. A
local term lasts a deterministic 10 to 15 years, derived from ability,
performance, age, and a stable actor/year tie breaker. The minimum is always 10
years and the maximum is always 15 years.

At expiry:

1. Close the incumbent's current career record.
2. Prefer a qualified transfer to another city when a suitable vacancy exists.
3. Otherwise appoint from the candidate pool.
4. Permit the prior incumbent to compete again only through the ordinary
   scoring path; do not grant unlimited automatic renewal in place.

Local terms remain finite even when the realm's central term law grants
lifetime tenure. This preserves the user-visible distinction between central
tenure and circulating local officials.

### Annual Processing

Annual maintenance reads city capacity and current appointments, then processes
only vacancies and terms that are due. It uses the existing deferred, sliced
work infrastructure. No annual path may enumerate every world actor for every
city.

An appointment transition is atomic from the domain perspective: close the old
career, clear its projection, create the new career, and update city aggregate
state as one committed operation. A failed write leaves the prior appointment
authoritative and queues a retry rather than producing two incumbents or an
unrecorded replacement.

## Civil Service Recruitment

### Before Examination Technology

Realms without civil-service examination technology use the existing legal,
ability, school, identity, and appointment rules for local candidates.

### After Examination Technology

Once examination technology is active, local appointments prefer the civil
service talent pool but do not require final palace-examination success. The
eligible local pool includes:

- candidates who passed local or prefectural stages;
- candidates in the civil-service waiting or reserve pool;
- candidates who advanced to a higher stage but did not receive a final high
  rank.

Final high honors remain scarce. Expansion applies mainly to lower-stage
admission and reserve capacity. Per-session capacity is derived from projected
central and local vacancies with a bounded reserve margin, so adding local
government does not leave most offices permanently empty.

Local service creates normal career merit and can qualify a successful official
for transfer or later central appointment.

## Hometown Patronage

`OfficialCareerState.NATIVE_CITY_ID` is the canonical native-place identity.
No duplicate hometown field is introduced.

When a city leader recommends or selects candidates:

- candidates sharing the leader's native city receive a material scoring bonus;
- ability, legality, examination eligibility, and office-specific requirements
  remain mandatory;
- hometown is never a hard gate and cannot make an invalid actor appointable;
- recommendation metadata stores the recommending actor and a
  `hometown_recommendation` source reason.

Officials sharing a native city are grouped as a hometown faction in the read
model when multiple members serve together. This is a computed political group,
not a separately spawned organization. Its influence is expressed through
recommendation preference and UI explanation. Finite terms, transfers, deaths,
and dismissals naturally break up local concentrations.

## Data Flow

### Harem Replenishment

1. Resolve realm tier and current ruler.
2. Load active ruler-household records.
3. Normalize legacy rank codes once if required.
4. Determine empty fixed seats.
5. Query eligible candidates without a noble hard gate.
6. Assign the highest-scoring candidate to the highest empty seat through the
   existing household service.
7. Refresh the household read model.

### Local Office Rotation

1. Annual court maintenance schedules city slices.
2. A city slice loads office capacity, active appointments, and due terms.
3. The candidate query reads indexed examination, career, and native-city
   facts.
4. Selection applies hard eligibility gates followed by ability, merit,
   examination, circulation, and hometown scores.
5. Persistence closes and opens career records atomically.
6. Runtime actor and city projections update only after persistence succeeds.
7. Court read-model caches are invalidated for the affected kingdom and city.

### Window Rendering

1. `CourtWindow` receives a kingdom context or a kingdom-plus-city context.
2. The shared court read-model facade returns either national office nodes and
   city cards or local office nodes.
3. Existing pooled card, link, portrait, and control components render the
   model.
4. Opening office history performs a bounded indexed history query; it never
   scans live actors.

## Failure Handling

- Missing or dead actors use the persisted name snapshot in history views.
- A destroyed city closes its active local appointments and retains completed
  history.
- A conquered city closes appointments under the former kingdom before the new
  owner creates its local government.
- Ruler succession closes only the former ruler's household relationships; it
  does not rewrite their rank history.
- Invalid or duplicate active appointments are repaired through the existing
  authoritative-career repair path before new appointments are created.
- Candidate exhaustion leaves a visible vacancy and retries during later
  maintenance. It must not appoint an invalid actor or perform a full-world
  emergency scan.
- UI navigation tolerates stale cards: if the city or kingdom no longer exists,
  it returns to the valid parent view instead of throwing.

## Performance Requirements

- Annual local-government work remains sliced and coalesced by kingdom/city.
- Candidate pools use existing examination, career, city-resident, and
  native-city indexes.
- Opening national or local court views reads cached snapshots and bounded
  persistence queries.
- Portrait creation remains pooled and incremental.
- No window refresh, appointment, or hometown-faction calculation may perform
  an unbounded world-actor enumeration.

## Verification Strategy

### Rule Tests

- Fixed rank ordering and realm-tier eligibility.
- Commoners remain eligible and can outrank weaker nobles.
- Age above 35 does not revoke a rank.
- Legacy rank assignment is deterministic and idempotent.
- Local term generation is always between 10 and 15 years.
- Same-native-city recommendation increases score but does not bypass gates.
- Examination participants without final honors can qualify for local office.

### Persistence Tests

- Appointment creates a current term with correct name and start year.
- Dismissal, death, transfer, expiry, conquest, and destruction close the term
  with the correct end year and reason.
- Transfer cannot create two concurrent formal appointments.
- Historical actor names survive actor deletion.
- Failed replacement writes preserve the previous authoritative appointment.

### Read-Model and UI Tests

- National court returns one city-government card per valid city.
- City context returns the local hierarchy using the shared court components.
- History is reverse chronological and renders `至今` only for the incumbent.
- Switching between national and city contexts does not leak pooled nodes,
  links, portraits, or state.
- A stale city card exits safely.

### Integration and Save Tests

- Existing imperial saves receive stable fixed harem ranks without actor loss.
- Existing generic kingdom households are unchanged.
- Legacy city-bureau saves acquire real officials gradually after load.
- Examination-enabled realms fill local vacancies from expanded talent pools.
- Multiple term cycles produce permanent, correctly bounded histories.
- Annual maintenance and court-window opening stay within the existing work
  budget and do not add full-world scans.

## Acceptance Criteria

The feature is complete when:

1. Imperial and Mandate households show all ten fixed titles and preserve them
   historically across succession.
2. A high-quality commoner can be selected as a consort.
3. Every office card can show all former and current holders with year ranges.
4. The national court shows city-government cards and each card opens a local
   court view built from the same UI components.
5. Local officials are real actors, serve 10-to-15-year finite terms, and
   rotate without duplicate appointments.
6. Examination participants who did not receive final honors can enter local
   service, with capacity expanded to meet local demand.
7. City leaders materially prefer qualified people from their own native city,
   and the resulting hometown faction is visible and explainable.
8. Old saves load without losing household relationships, office history, or
   actors, and the feature does not add unbounded actor scans.
