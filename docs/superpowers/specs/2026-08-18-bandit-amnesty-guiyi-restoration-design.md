# Bandit Amnesty, Stronghold Disposal, and Guiyi Restoration Design

## Goal

Repair bandit stronghold cleanup when the last resident dies, extend bandit
amnesty with an optional office or virtual-title promise, and add a Guiyi Army
restoration subtype for Xia-integrated cities occupied by a non-integrated
foreign kingdom.

The implementation must reuse the existing bandit stronghold, court,
virtual-title, foreign-occupation, and kingdom-identity continuity systems.

## Confirmed Rules

- A zero-population bandit stronghold falls even when empty-city survival is
  enabled.
- Stronghold zones return to the recorded mother city before the stronghold
  city is physically removed.
- Amnesty may promise no reward, one vacant court office, or one virtual noble
  title.
- An office promise never dismisses an incumbent. Only currently vacant and
  eligible offices are selectable.
- A virtual title belongs to the granting kingdom. The title becomes extinct
  when that kingdom is destroyed. A later kingdom may actively grant a new
  title, but the old title never transfers automatically.
- A Guiyi Army is a bandit-route restoration subtype, not a separate duplicate
  rebellion system.
- Guiyi eligibility requires a city culture with the integrated-Xia trait, a
  current occupier whose primary culture lacks that trait, and city loyalty
  below `-50`.
- One occupying kingdom may have at most one active Guiyi Army anywhere in its
  territory. The slot becomes available after that Guiyi Army is destroyed or
  completes restoration.
- The restoration target is the city's pre-occupation kingdom. If it still
  exists, the Guiyi Army returns territory to it. If it is extinct, the
  existing kingdom-identity continuity system restores that historical
  kingdom.

## Root Cause of the Empty Stronghold Failure

The current death hook calls `OnBanditResidentDied` from the postfix of
`Actor.die`. At that point the actor can already be marked dead while still
remaining in the city's resident collection. `City.getPopulationPeople()` can
therefore report a non-zero value for the final resident. No later resident
removal hook repeats the stronghold population test.

At the same time, empty-city survival deliberately suppresses vanilla border
shrink and natural destruction. A missed final-resident test therefore leaves
the stronghold alive indefinitely. Runtime restoration only repairs this
after a load and is not a live lifecycle guarantee.

Physical city removal is already deferred to avoid mutating `CityManager`'s
live collection during `CityManager.update`. The repair must retain that
mutation boundary and must not restore direct removal from the death callback.

## Stronghold Fall Lifecycle

### Observation

The actor-death hook records a potentially empty stronghold by city ID. The
resident-removed hook and an authority-cycle safety scan may record the same
ID. Requests coalesce.

Population is evaluated only from deferred authority work after actor and city
collections have completed their current mutation. The count includes living,
non-rekt, non-boat residents whose `city` is the stronghold. A count of zero
queues logical fall regardless of death cause, including starvation.

### Logical fall

Logical fall is idempotent and performs these steps:

1. Resolve the persisted stronghold state, stronghold city, mother city, and
   suppressor/restoration context.
2. Persist `Falling` before world mutations.
3. Move any surviving residents to the mother city.
4. Return all stronghold zones to the mother city.
5. Remove stronghold towers and restore the recorded wall tiles.
6. Clear raid runtime data and persist `Completed`.
7. Schedule physical disposal through
   `BanditStrongholdCityDisposalService`.

The mother city is the recorded pre-split city. If that object is no longer
valid, the fallback is the nearest live city of the recorded mother/origin
kingdom. Cleanup does not silently assign the zones to an unrelated kingdom.

### Forced physical disposal

Scheduled stronghold disposal is an explicit lifecycle operation, not vanilla
natural city death. Empty-city survival and resettlement must recognize a
pending stronghold disposal and abstain from preserving or repopulating that
city.

The disposal queue retries while `CityManager.update` is active or a transient
removal error occurs. It succeeds only when the city is absent/rekt or
`CityManager.removeObject` completes. Completed stronghold state is cleared
only after physical disposal is confirmed or its owning kingdom is being
destroyed.

## Amnesty Settlement

### Entry and window

The existing bandit-amnesty divine power remains the entry. Clicking an
eligible stronghold opens a draggable wide settlement window using the
existing AW3 window chrome.

The window shows the origin kingdom, bandit kingdom, stronghold, and bandit
leader. It offers exactly three reward modes:

- no promise;
- vacant office;
- virtual noble title.

The office list contains only vacant central offices in the offering kingdom's
current court institution for which the leader will be intrinsically eligible
after naturalization. It never offers occupied offices. The virtual-title mode
reuses the existing title text validation and hereditary toggle.

### Authority validation

The authority side re-resolves all IDs and repeats every eligibility check.
It rejects settlement before mutation when the origin changed, the stronghold
is inactive, the selected office became occupied, the leader died, the title
became invalid/duplicate, or persistence is unavailable.

### Settlement state machine

Amnesty uses a persisted, idempotent settlement state:

`Prepared -> TerritorialSettlement -> RewardPending -> Completed`

Execution order is:

1. Validate the entire offer and persist `Prepared`.
2. End the bandit's wars.
3. Run the stronghold logical-fall path without suppression chronicles.
4. Naturalize the leader and surviving residents into the origin/mother city.
5. Clear rebel and bandit markers and restore ordinary government projection.
6. Grant the selected vacant office through the existing court appointment
   service, or grant the virtual title through
   `VirtualNobleTitleService.TryGrant`.
7. Record both kingdom histories and the mother-city chronicle, then persist
   `Completed`.

If reward persistence fails after territorial settlement, the record remains
`RewardPending` and authority cycles retry only the reward and final history.
They do not repeat war ending, zone transfer, or city disposal.

## Virtual Title Lifecycle

The existing `VirtualNobleTitle` schema already stores the granting kingdom in
`KINGDOM_ID`. `VirtualNobleTitleService.OnKingdomDestroying` already closes all
active titles issued by that kingdom with `kingdom_destroyed` and `extinct`.
The amnesty flow uses this service without creating a second title store.

The extinct title remains historical. A new kingdom may use the normal virtual
title grant entry to issue a new title to the same actor after the actor belongs
to that kingdom. No automatic replacement or issuer reassignment occurs.

## Guiyi Army Restoration Subtype

### Trigger

Foreign occupation supplies the trigger context. City transfer schedules one
deferred check after the ownership mutation completes. The annual foreign-
occupation update checks again so that a city whose loyalty falls below the
threshold later is not missed. Either check may create a Guiyi Army only when
all conditions hold:

- the city and occupying kingdom are live;
- `XiaCultureIntegrationService.IsIntegrated(city.culture)` is true;
- `XiaCultureIntegrationService.IsIntegrated(occupier.culture)` is false;
- city loyalty is below `-50`;
- the city is genuinely held by a different culture/foreign occupier;
- the occupier has no active Guiyi Army;
- the city is not already a stronghold and has no child stronghold;
- a valid ordinary resident can become the stronghold leader;
- the normal four-zone stronghold plan succeeds.

Creation runs through deferred authority work, never inside `City.setKingdom`.
The per-occupier uniqueness index is rebuilt from persisted active Guiyi states
after load.

### State and presentation

The existing bandit route state gains a subtype such as
`guiyi_restoration`. Its persisted extension records:

- occupying kingdom ID;
- pre-occupation/original kingdom ID;
- original city ID;
- restoration identity/claim ID when available;
- creation year and current restoration stage.

The government remains the bandit government during the stronghold phase, but
names, map labels, tooltips, and chronicles identify it as a Xia Guiyi Army.
It reuses bandit walls, towers, raids, suppression wars, and zero-population
cleanup.

### Restoration objective

Guiyi does not use the ordinary bandit pressure rule that decides whether to
remain bandits or become a generic peasant rebellion. It evaluates restoration
strength with the existing restoration/mandate strength model.

When strong enough:

- if the original kingdom is alive, Guiyi enters a restoration war whose
  territorial result returns eligible occupied cities to that kingdom;
- if the original kingdom is extinct, Guiyi uses
  `KingdomIdentityContinuityService` and existing restoration-claim machinery
  to revive the original kingdom identity;
- successful restoration destroys the temporary stronghold through the common
  logical-fall path and releases the occupier's active-Guiyi slot.

If the Guiyi stronghold is suppressed or reaches zero population, it follows
normal bandit cleanup and releases the slot without restoring the kingdom.

## History and Localization

New localized history fragments cover:

- Guiyi stronghold established under foreign occupation;
- the original kingdom/identity it seeks to restore;
- Guiyi suppression;
- territory returned to a surviving original kingdom;
- extinct kingdom restored;
- amnesty promised without reward, with an office, or with a virtual title;
- reward fulfillment and issuer-kingdom title extinction.

Events are written to the Guiyi/bandit kingdom, occupying kingdom, original
kingdom when live, and relevant city chronicle. Stronghold creation and
suppression remain city-history events rather than ordinary atlas territory
change nodes where existing rules require that distinction.

## Failure Handling

- All deferred requests coalesce by stable city/settlement ID.
- Every stage re-resolves objects instead of holding Unity object references.
- World mutations occur only on the authoritative main thread.
- A stale or replaced city ID cannot delete an unrelated city.
- A failed stronghold plan creates no kingdom and reserves no Guiyi slot.
- A failed amnesty preflight changes no war, city, government, office, or
  title state.
- A partially completed amnesty or fall resumes from persisted stage after an
  authority cycle or load.
- Multiplayer replicas receive the resulting authoritative state and never
  independently create, dispose, appoint, or grant.

## Verification

Rules and source-guard tests cover:

- the final resident is still visible during `Actor.die`, then absent during
  deferred evaluation;
- starvation and hostile death both queue fall at true population zero;
- empty-city survival cannot preserve or resettle a disposal-pending
  stronghold;
- zones, walls, towers, and mother-city ownership are restored exactly once;
- disposal retries safely outside `CityManager.update`;
- occupied offices are excluded and rejected by authority revalidation;
- title promises preserve issuer kingdom and become extinct on issuer death;
- a new kingdom can issue a new independent title;
- integrated city plus non-integrated occupier plus loyalty below `-50`
  triggers Guiyi eligibility;
- loyalty `-50` or higher, integrated occupier, or an existing Guiyi slot
  blocks creation;
- the active-Guiyi uniqueness index survives save/load;
- live-original and extinct-original restoration paths select the correct
  objective;
- suppression releases the per-occupier slot.

Runtime verification covers last-resident starvation, last-resident combat
death, amnesty with each reward mode, issuer extinction, Guiyi creation after
foreign occupation, suppression, and both restoration outcomes.

## Non-Goals

- No automatic transfer of extinct virtual titles to successor kingdoms.
- No displacement of an incumbent to satisfy an amnesty promise.
- No second wall, raid, court, title, or restoration engine.
- No more than one simultaneous Guiyi Army per occupying kingdom.
