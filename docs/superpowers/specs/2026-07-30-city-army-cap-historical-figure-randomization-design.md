# City Army Capacity And Historical Figure Randomization

## Purpose

Prevent RTS armies from advertising or seeking reinforcement numbers that a
city cannot sustain, while removing chronological rigidity from historical
figures after Ji Fa.

## City-Based Reinforcement Capacity

Each ordinary RTS army has a recruitment city. Its target strength must be
derived from that city rather than from a kingdom-wide potential estimate.

The sum of ordinary army target strengths anchored to one city is capped by:

```
min(effective warrior slots, floor(current city population * 0.35))
```

The cap is shared across all armies anchored to the city. It is not a separate
cap per army. Royal guards and other explicitly exempt special formations keep
their existing rules and do not consume this ordinary-army allocation.

When demand exceeds the city cap, allocate capacity deterministically in this
order:

1. Armies returning to recapture their own occupied city.
2. Armies on an active frontline or defending a threatened city.
3. Other active war missions.
4. Non-war and reserve armies.

Within the same tier, use stable army identity as the tie breaker. This keeps
save/load and multiplayer authority results deterministic.

Population changes never delete existing soldiers. A reduced city cap only
blocks further replenishment until the army total again fits the cap.

## Immediate Replenishment

The UI's pending-replenishment value represents approved capacity only:

```
approved target strength - current living members
```

It must never show speculative recruitment demand above the city's assigned
capacity. Once a replenishment is approved, recruit and attach all approved
members in that authority cycle. The existing arrival reconciliation may still
place them at the captain's formation point, but it must not leave the army in
an indefinite Replenish state or repeatedly re-request the same members.

The live population and city military count are re-read before mutation. If
they no longer support an approved allocation, the allocation is recomputed
without creating extra actors.

## Historical Figures

Ji Fa remains the sole first historical figure. After Ji Fa has committed
successfully, each eligible historical-figure attempt selects randomly from
all not-yet-spawned, eligible definitions. `RegistryIndex` remains stable for
persistence, but `SpawnOrder`, `FoundingYear`, and a predecessor's death do
not determine selection after Ji Fa.

An integration-gated figure remains unavailable until integration is enabled;
normal uniqueness reservation and durable commit semantics remain unchanged.

Historical display uses a pure state name. Definitions that currently use
directional dynasty labels keep their historical dynasty metadata, but their
kingdom projection and public label remove directional identifiers. For
example, Western Han and Eastern Han both project as `Han`.

## Integration Points

- `WartimeRecruitmentPopulationRules` gains pure, testable city-cap and
  allocation rules.
- `TemporaryLevyService`, `StandingArmyService`, and
  `ArmyRtsControllerService` consume the same approved target strength before
  demand display or actor creation.
- `ArmyMapInformationRules` continues to format the label but receives only
  approved shortage values.
- `HistoricalFigureSpawnRules` owns random post-Ji-Fa candidate selection.
- `HistoricalFigureService` uses the selected candidate and applies the
  normalized kingdom-name projection.

## Failure Handling

Invalid, destroyed, foreign, or unanchored cities receive zero new capacity.
If no eligible historical figure is available, the spawn attempt is a no-op;
it does not mark any definition as spawned or consume another figure.

## Verification

Add focused rule tests for:

- One city with several armies never exceeding 35% of population or warrior
  slots in total.
- Priority ordering and stable tie breaking.
- A pending label equal to approved, immediately creatable strength only.
- Population decline blocking new reinforcement without deleting current
  members.
- Ji Fa as the first figure and random eligible selection thereafter.
- No directional dynasty prefix in projected historical kingdom names.

Run the full rules test project and build the Release mod assembly before
deployment.
