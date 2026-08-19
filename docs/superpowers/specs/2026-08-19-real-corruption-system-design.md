# Real Corruption System Design

## Goal

Add a persistent two-level corruption system for AncientWarfare:

- every city has a real corruption value from 0 to 100;
- every civilization has a real country corruption value from 0 to 100;
- both values evolve once per kingdom year from existing economic,
  administrative and stability signals;
- the kingdom policy window exposes the country value and its sources;
- city/local-government views expose the city value;
- the Great Uprising Era reads this real corruption state instead of using
  mandate/authority as a corruption proxy.

## Scope

This feature covers corruption state, annual calculation, persistence,
diagnostics and UI display. It does not replace mandate, loyalty, famine or
the existing court appointment rules. Those systems provide signals to the
corruption calculation but keep their own state and behavior.

## Data Model

### City state

Persist on the existing city lineage/economy state:

- `CorruptionScore` (0..100);
- `CorruptionLastYear`;
- `CorruptionHighStreakYears`;
- `CorruptionTaxPressure`;
- `CorruptionOfficialPressure`;
- `CorruptionOrderPressure`;
- `CorruptionFoodPressure`.

### Country state

Persist on `Kingdom.data`:

- `CorruptionScore` (0..100);
- `CorruptionLastYear`;
- `CorruptionHighStreakYears`;
- `CorruptionVeryHighStreakYears`;
- `CorruptionCentralPressure`;
- `CorruptionFiscalPressure`;
- `CorruptionAverageCityScore`;
- `CorruptionHighestCityScore`;
- `CorruptionHighestCityId`.

All reads default to zero for old saves. Values are clamped before writing.
Annual updates are idempotent by the stored year.

## Annual Calculation

The calculation runs from `KingdomAnnualWorkService` after city economy and
court annual work, once per valid civilization kingdom. It never runs in an
actor/frame task and never scans cities more than once during that annual
pass.

### City score

Each live city receives a bounded pressure score from existing signals:

- tax pressure from the city's calculated tax contribution and tax burden;
- official pressure from active local officials' negative merit/violation
  signals and missing qualified administration;
- order pressure from low loyalty and low civil-order/economy stability;
- food pressure from low food stability or a high hungry-population ratio.

The score is updated with inertia:

```
target = clamp(tax + official + order + food, 0, 100)
next = clamp(previous + (target - previous) * 0.25, 0, 100)
```

When all pressures are low, the score decays rather than remaining stuck.
Each pressure component is stored for explanation in the UI.

### Country score

The country score is population-weighted across live cities, then adjusted by
central and fiscal pressure:

```
averageCity = sum(cityScore * cityPopulation) / max(1, totalPopulation)
countryTarget = clamp(averageCity + centralPressure + fiscalPressure, 0, 100)
nextCountry = clamp(previousCountry + (countryTarget - previousCountry) * 0.25, 0, 100)
```

`highestCityScore` and its city ID are retained for UI navigation.

### Thresholds

- `0..30`: low/controlled;
- `31..60`: elevated;
- `61..80`: high corruption;
- `81..100`: extreme corruption.

High corruption is score >= 60. Extreme corruption is score >= 80. The
high-streak counter increments at >= 60; the extreme-streak counter increments
at >= 80. A below-threshold year resets the corresponding streak.

## Great Uprising Integration

`BanditGreatUprisingService` replaces its current mandate/authority proxy with
the real country corruption state:

- corruption condition: country score >= 60 for the configured high-streak
  duration;
- famine condition remains the existing two-year hungry-population condition;
- the bandit ratio requirement remains 5 percent.

Mandate and authority remain independent signals and are not silently treated
as corruption after this change.

### Corruption-driven bandit-to-rebel tendency

The existing Great Uprising activation gate remains unchanged: the origin
kingdom must have at least 5% bandit population and either the configured
long-term high-corruption streak or the existing famine streak. Corruption
does not create a nationwide uprising before that gate is met.

Once the era is active, the real country corruption score controls the
likelihood that each eligible bandit kingdom becomes a founding peasant-rebel
kingdom. The deterministic annual conversion tendency is:

| Country corruption | Per-candidate conversion chance |
| --- | ---: |
| `0..30` | 5% |
| `31..60` | 15% |
| `61..80` | 35% |
| `81..100` | 60% |

The chance is evaluated with a stable hash of origin kingdom ID, candidate
kingdom ID and current year. This avoids save/load and multiplayer divergence
while still making higher corruption produce more rebels over time. The
existing annual conversion budget and cursor remain authoritative, so a
single year cannot convert more than the configured budget and candidates are
fairly rotated.

## Clean-corruption national decision

Add a repeatable national decision with ID `aw_decision_clean_corruption`.
It uses the existing decision research/progress pipeline and appears in the
KingdomPolicyWindow. The decision is available when the kingdom corruption
score is at least `40`, no clean-corruption decision is already active or
queued, and the kingdom can pay the existing policy-point resource. Its
definition cost is `50` policy points. It is not blocked by peace/war state,
because corruption cleanup is an internal measure that can be ordered during
either state.

The AI priority is score-based:

- below `40`: unavailable to AI;
- `40..59`: normal administrative priority;
- `60..79`: high priority;
- `80..100`: highest priority and preferred over ordinary social decisions.

While the decision is active, the annual corruption target pressure for the
kingdom is multiplied by `0.85`; this is persisted as an active cleanup flag
and applies to both country-level central/fiscal pressure and each city's
combined pressure. On completion it clears the flag, reduces the country
score by 20 points (clamped to `0`) and reduces each of the three
highest-corruption live cities by 10 points (also clamped). The annual
corruption service runs afterward and may rebuild part of the score from
current pressure, preserving the system's inertia instead of permanently
erasing economic or administrative causes.

The completion effect is idempotent: a completed decision records its year and
cannot apply its reduction twice when a save is loaded or a replica snapshot
is replayed.

The conversion tendency uses a deterministic 32-bit FNV-1a hash over the
origin kingdom ID, candidate kingdom ID and current year. The unsigned hash
modulo `10_000` is compared with the selected chance multiplied by `10_000`;
no Unity/global random state is consumed.

## Persistence and Compatibility

- Existing saves initialize missing fields to zero and begin accumulating
  normally.
- World-load and clear-world pipelines reset only runtime indexes, not saved
  corruption values.
- Replica sessions consume stored snapshots and do not perform authoritative
  updates.
- Invalid/disposed cities are excluded from country aggregation.
- A missing city population uses a safe minimum denominator and never creates
  NaN or infinity.

## UI

### Kingdom policy window

Add a corruption section using the existing policy-window visual language:

- country corruption score and severity label;
- high/very-high streak years;
- average city score and highest-corruption city;
- central pressure, fiscal pressure and weighted city score components;
- a button to focus/open the highest-corruption city when it is still valid.

### City/local-government view

Add the same compact corruption block to the existing city/local-government
details rather than creating a separate standalone window:

- city corruption score and severity;
- four pressure components;
- last update year and short source text.

No new top-level window is introduced.

## Diagnostics and Tests

Pure rules test:

- clamping and severity boundaries;
- inertia and decay;
- weighted country aggregation;
- streak increment/reset;
- zero-population safety.
- corruption-band conversion chances and stable hash determinism;
- clean-corruption decision availability, priority and completion reduction;
- conversion budget/cursor behavior under different corruption bands.

Source guards verify:

- annual wiring after city economy/court work;
- kingdom and city persistence keys;
- Great Uprising reads corruption score/streak rather than mandate as the
  corruption condition;
- Great Uprising uses corruption-band conversion tendency after activation;
- the clean-corruption decision is defined, scored, progressed and applied
  exactly once;
- UI sections are present in policy and city/local-government views.

Runtime acceptance verifies that a corrupt official/tax-heavy city raises its
city and country scores over years, clean administration lowers them, and a
loaded save preserves the values.
