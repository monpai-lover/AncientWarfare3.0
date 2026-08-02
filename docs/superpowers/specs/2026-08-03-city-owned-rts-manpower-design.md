# City-Owned RTS Manpower Design

## Scope

This change replaces the actor-ID reserve pool with a city-owned manpower
ledger and guarantees that diplomatic notice preparation and active war can
fill the city's ordinary army. It retains the existing Mandate-history gate
that prevents ordinary worlds from declaring Zhulu wars before a real Mandate
collapse.

## Required Behavior

1. Every living city owns at most one ordinary army. Royal guards, slave
   vanguards, naval formations, and other explicitly special formations do not
   count against this invariant.
2. The ordinary army remains anchored to its source city. The city alone owns
   and replenishes it; another city may neither adopt nor supply it.
3. The war director reuses the same army for attack, defense, relief, retreat,
   and reorganization. A mission change must never create a second ordinary
   army for the city.
4. A city's military manpower capacity is one half of its authentic resident
   population. Existing mobilized soldiers sourced from the city count against
   this capacity.
5. Synthetic temporary soldiers do not increase the authentic population used
   to calculate capacity. This prevents generated soldiers from recursively
   creating more manpower.
6. Recruitment first converts eligible authentic residents of the source city
   into soldiers. If the army still has an approved shortage, the same request
   creates only that many synthetic temporary Actor soldiers and immediately
   assigns them to the city's ordinary army.
7. A diplomatic war notice and a formal war use the same monthly recruitment
   operation. Army creation and replenishment are both legal during notice
   preparation and during active war.
8. An army may deploy at 80 percent strength and continues replenishing in the
   background. A failed resident scan is not reserve exhaustion while unused
   manpower capacity remains; the shortage is filled synthetically.
9. At emergency end, surviving authentic residents without military merit
   return to their source city as civilians. Synthetic soldiers without
   military merit are removed from the world and produce no person history,
   lineage archive, birth record, enlistment record, demobilization record, or
   death record.
10. Any surviving soldier with positive military merit remains a real Actor.
    A synthetic soldier is promoted by clearing its synthetic lifecycle flag;
    an authentic resident remains a normal veteran.
11. A synthetic soldier killed during war follows normal tactical casualty and
    war-score accounting but is excluded from person-history and lineage
    persistence.
12. A source city lost to enemy control cannot replenish its army. Reclaiming
    the city restores its city-owned manpower and recruitment authority.
13. Ordinary-world Zhulu declarations remain illegal until a Mandate period
    has existed and collapsed. Explicit Zhulu age behavior remains valid.

## Data Model

`CityReservePoolService` becomes a count ledger instead of a selected Actor-ID
registry. For each city it publishes:

- authentic population base;
- manpower capacity (`floor(authentic population / 2)`);
- active city-sourced military count;
- available manpower (`max(0, capacity - active count)`);
- current reconciliation epoch and exhaustion state.

Authentic population includes living residents that originated as normal
world actors, including residents temporarily serving in the city's army. It
excludes Actors marked as synthetic temporary soldiers. The count is rebuilt
from city and army indexes after load and updated incrementally on birth,
death, migration, profession change, enlistment, demobilization, and synthetic
promotion or removal.

Synthetic soldiers store a dedicated flag, source city ID, source kingdom ID,
war or notice identity, creation epoch, and lifecycle state. These fields are
separate from the existing temporary-levy marker so authentic resident levies
can be restored rather than deleted.

The ledger is derived state. Saves may persist a compact count snapshot for
fast startup, but the runtime rebuild is authoritative and must not persist a
list of all eligible resident Actor IDs.

## Recruitment Flow

Once per in-game month, `TemporaryLevyService` visits each controlled source
city with an active notice or war:

1. resolve or create the city's canonical ordinary army;
2. calculate its city-bounded target strength;
3. calculate shortage without borrowing another city's population;
4. recruit eligible authentic residents using a bounded city scan;
5. create synthetic temporary Actors for the remaining approved shortage;
6. assign every successful recruit to the same canonical army;
7. publish the roster mutation once and release deployment at 80 percent.

Synthetic creation is a fallback inside the same operation, not a separate
army or deferred reserve queue. A partial failure rolls back the unfilled
ledger reservation. The operation exits on full strength, loss of source-city
authority, ended emergency, invalid army, or a bounded runtime deadline. It
cannot report confirmed exhaustion solely because no eligible resident Actor
was found.

## Demobilization And History

Demobilization snapshots the soldier's provenance before clearing military
fields:

- authentic + no merit: remove from army, restore civilian profession and
  source-city membership;
- synthetic + no merit: detach from army and safely remove the Actor through
  the WorldBox lifecycle;
- any provenance + positive merit: retain the Actor and clear temporary
  lifecycle fields.

All AW3 history and lineage entry points check the synthetic flag. A synthetic
soldier is invisible to person history until promotion. Aggregate war deaths
and war score remain accurate even when no person archive is created.

## Performance Contract

The system never pre-creates half of a city's population. It materializes only
the current approved army shortage, in bounded batches, on the authoritative
main thread. No Unity or WorldBox object is created or destroyed from an async
worker.

Hot-path reserve queries are O(1) ledger reads. Monthly reconciliation is
bounded by changed residents and the requested shortage rather than scanning
every actor in every city. Synthetic soldiers run only military movement,
combat, food, healing, transport, retreat, and formation-follow behavior;
social, laughter, singing, sleep, civilian work, marriage, reproduction,
office, school, lineage, and normal history tasks are rejected while their
synthetic flag is active.

## Failure Handling

- A missing ledger triggers a city-local rebuild and is never interpreted as
  confirmed zero manpower.
- Failure to create a synthetic Actor releases the reserved count and records
  a bounded diagnostic with city, army, phase, shortage, and failure stage.
- Failure to assign a created Actor removes it safely instead of leaving an
  ungrouped warrior.
- Legacy saves with duplicate city armies deterministically retain the army
  with a stable living captain, then the lowest army ID, and merge or retire
  duplicates through bounded maintenance.
- Load repair normalizes illegal pre-Mandate Chaos to Golden and all Zhulu
  declaration paths revalidate through the shared eligibility gate.

## Verification

Rule tests must prove:

- capacity is exactly half of authentic population and synthetic soldiers do
  not feed capacity;
- active city-sourced soldiers reduce available manpower without cross-city
  borrowing;
- notice and war both recruit for `Create` and `Replenish`;
- resident shortage falls back to exactly the required synthetic count;
- one city cannot create a second ordinary army;
- authentic demobilization restores civilians, synthetic demobilization
  removes Actors, and merit preserves both;
- synthetic actors cannot enter person-history or civilian task paths;
- missing ledger state does not become false exhaustion;
- pre-Mandate ordinary Zhulu remains rejected.

The adversarial simulation must cover ten cities and twenty armies, including
empty initial armies, dispersed residents, notice preparation, 80-percent
deployment, casualties, repeated replenishment, city capture and recapture,
war completion, and cleanup. It must finish without a permanently replenishing
army, a city with two ordinary armies, an ungrouped synthetic soldier, or a
cross-city recruit.

Production verification includes focused rules, actor-runtime rules, the RTS
simulation, release compilation, source-only deployment with SHA-256 matching,
and a new in-game diagnostic log showing non-zero city manpower, notice-phase
recruitment, successful roster growth, and no illegal Zhulu declaration.
