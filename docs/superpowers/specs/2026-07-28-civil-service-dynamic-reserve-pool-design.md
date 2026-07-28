# Dynamic Civil-Service Reserve Pool Design

**Date:** 2026-07-28

## Goal

Replace the current `vacancies + 25 percent` final-admission rule with a
bounded reserve pool. Examinations must continue to fill real vacancies while
keeping enough permanently qualified people waiting for later appointments.
This prevents a realm with a full court from admitting only one person out of
dozens for many consecutive sittings, without allowing an unbounded graduate
population.

## Scope

This change affects examination demand, persistence, read models, and the
examination-window summary. It does not change examination cadence, scores,
social-origin selection, gender eligibility, office rank assignment, official
circulation, or the rule that a foreign graduate changes nationality and
residence only after a committed formal appointment.

## Definitions

### Established civil posts

The established post count is:

```text
central offices enabled by the realm's current court tier
+ one governor post for every living city in the realm
```

Only valid, living cities belonging to the realm are counted. Military-only
roles and temporary acting positions are not additional established posts.

### Waiting qualified candidate

A person counts once in the host realm's waiting pool when all of the following
are true:

- alive, adult, male, and not enslaved;
- holds the latest completed host-issued formal qualification;
- is currently available for office;
- holds no active central, city, or guest office;
- is either a domestic person or a foreign resident qualified by the host.

`jinshi` is a formal qualification. `gongshi` remains formal where the existing
institution rules accept it. `juren` is not a waiting formal candidate. An
active official is excluded because the reserve pool represents people who can
fill a new vacancy without first creating another vacancy.

The query deduplicates by actor and uses the latest completed qualifying result.
Foreign graduates remain foreign while waiting and are naturalized only by the
existing atomic appointment transaction.

## Reserve Target

The target is half of the established civil posts, rounded up and bounded:

```text
reserve_target = clamp(ceil(established_posts / 2), 4, 32)
```

The lower bound gives a small realm enough succession depth. The upper bound
keeps database scans, UI lists, and long-lived qualified populations bounded.
Population still controls the sitting candidate target through the existing
population-based pipeline; it does not inflate the reserve target independently
of the realm's actual administration.

## Final Admission Demand

At session creation, freeze all demand inputs:

```text
vacancies = central_vacancies + city_vacancies
raw_demand = vacancies + reserve_target - waiting_candidate_count
final_admission_quota = clamp(raw_demand, 1, final_stage_capacity)
```

This formula accounts for the waiting candidates who will fill the current
vacancies. Examples:

- 20 posts, 3 vacancies, 4 waiting: target 10, admit 9.
- 20 posts, 3 vacancies, 10 waiting: target 10, admit 3.
- 20 posts, 3 vacancies, 15 waiting: target 10, admit 1.
- 6 posts, no vacancies, no waiting: target 4, admit 4.
- 6 posts, no vacancies, 4 waiting: target 4, admit 1.

The one-person minimum preserves the triennial institution and allows gradual
renewal. A sitting cannot admit more people than its final-stage capacity.
Stage quotas continue to derive from the frozen final quota: local/prefectural
up to four times final demand, metropolitan up to twice final demand, and
palace/national equal to final demand.

## Persistence And Compatibility

New sessions persist two additional snapshots:

- `waiting_candidate_count`
- `reserve_target`

`central_vacancies`, `city_vacancies`, and `admission_quota` remain frozen as
they are now. Loading an existing session never recalculates its admission
quota. Legacy sessions use `-1` for the two new fields and display the legacy
summary without inventing historical reserve values.

Schema migration is additive and idempotent. Save/load must preserve the frozen
figures exactly.

## Runtime Flow

1. The three-year session scheduler resolves mode, court tier, live cities,
   vacancies, final-stage capacity, and established posts.
2. A bounded indexed query counts waiting domestic and host-qualified foreign
   candidates outside all active offices.
3. Pure rules calculate the reserve target and final admission quota.
4. The session transaction persists the complete demand snapshot.
5. Every later examination stage reads only the frozen session quota.
6. After completion, the existing appointment pipeline fills vacancies by
   score and office suitability; remaining graduates stay permanently eligible.

The waiting-pool query runs only during authoritative session preparation or a
bounded annual candidate-supply refresh. It never runs from window rendering,
tooltips, or per-frame actor updates. Multiplayer clients consume replicated or
persisted read models and do not execute authority queries.

## User Interface

The examination window summary displays:

```text
aw_civil_service_vacancies N
aw_civil_service_reserve W/T
aw_civil_service_admission Q
```

where `W` is the frozen waiting count, `T` is the frozen reserve target, and
`Q` is the frozen final admission quota. Legacy sessions with unknown reserve
snapshots show the existing vacancy and admission values and omit `W/T`.

The Simplified Chinese locale renders these labels as the established Chinese
terms for office vacancies, waiting reserve, and current-sitting admission.

The summary uses existing compact typography and localization. It does not add
a new modal or nested card.

## Failure Handling

- A failed waiting-pool query aborts new-session creation for that authority
  cycle; it must not silently assume zero and over-admit.
- An invalid capital, destroyed realm, missing examination technology, or zero
  final-stage capacity continues to prevent session creation.
- If the persisted snapshot cannot commit atomically, no in-memory session is
  adopted.
- A stale or dead waiting candidate is rejected again at appointment time.

## Verification

Automated verification must cover:

- reserve-target lower bound, upper bound, and rounding;
- the five admission examples above and final-stage capacity clamping;
- local and foreign waiting-candidate SQL eligibility, office exclusion, latest
  result selection, and actor deduplication;
- additive migration and exact save/load of both new session fields;
- examination UI localization and legacy-session fallback;
- a century simulation with no zero-candidate completed sitting, no remaining
  vacancy accumulation, a bounded waiting pool, and no collapse of noble,
  declined-noble, or commoner participation;
- Debug and Release builds in both the development tree and installed mod;
- runtime autosave acceptance with SQLite integrity `ok` and no civil-service,
  async-write, or schema errors in `Player.log`.

The deployment remains scoped to civil-service dependencies and preserves the
entire installed `.runtime` tree byte-for-byte.
