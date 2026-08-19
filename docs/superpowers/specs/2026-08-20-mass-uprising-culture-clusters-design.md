# Mass Uprising Culture Clusters Design

**Date:** 2026-08-20  
**Status:** Approved for inline implementation

## Goal

Extend the existing great-uprising system so that low-loyalty cities form separate peasant-rebel kingdoms by exact culture and geographic connectivity, while the capital and its directly adjacent cities are protected as uprising origins. After each rebel completes its local uprising objective, the rebel kingdoms fight an internal civil war until one winner remains and then the winner resumes the unification war against the original capital.

## Existing Boundaries

- `BanditGreatUprisingService` owns the annual great-uprising trigger and bounded bandit conversion budget.
- `PeasantRebelBanditStrongholdService.TryCreateDirect` creates a rebel/stronghold kingdom using the existing route and persistence path.
- `PeasantRebelRouteService` owns rebel route metadata, war admission, city acquisition and cleanup.
- `WarLoyaltyContent` already registers first-class vanilla `LoyaltyAsset` modifiers.
- `KingdomAnnualWorkService` is the yearly authority-cycle owner.

The new behavior must remain authoritative-only, bounded per year, and compatible with existing rebel, bandit, restoration and multiplayer guards.

## Loyalty and Corruption

Register a `LoyaltyAsset` named `aw_local_corruption` through `WarLoyaltyContent.Init`. Its calculator returns the negative city corruption score from `CorruptionService.ReadCity(city).Score`, clamped to the existing corruption range. The entry uses a localized positive/negative label so the normal city loyalty detail shows `地方腐败: -X`.

No separate uprising-only loyalty calculation is introduced. All existing callers of `City.getLoyalty()` observe the modifier through the vanilla `LoyaltyCalculator` pipeline.

## Candidate Selection

For each eligible origin kingdom during the existing great-uprising cycle:

1. Read each live owned city loyalty with `city.getLoyalty()`.
2. Exclude the capital itself.
3. Exclude every city directly adjacent to the capital through `capital.neighbours_cities`.
4. Keep only cities with effective loyalty `< 0`.
5. Mark cities with effective loyalty `< -50` as core uprising cities.
6. Read the exact city culture ID. Cities without a valid culture ID are not grouped.

The existing country-level bandit-ratio and sustained-corruption/famine gate remains required before clusters are created.

## Cluster Construction

Build connected components over candidate cities. Two cities belong to the same cluster only when:

- their culture IDs are equal; and
- either city appears in the other's `neighbours_cities` set.

Components are deterministic: sort by city ID, use breadth-first search, and sort each resulting cluster by city ID. A component must contain at least one core city. Components without a `< -50` city remain eligible candidates but do not found a mass-uprising rebel in that annual pass.

## Rebel Lifecycle

Each cluster creates at most one rebel kingdom for its origin/year/cluster key. The creation path calls `TryCreateDirect` on the lowest-loyalty core city, then stores additive cluster metadata on the rebel kingdom:

- origin kingdom ID;
- stable cluster key;
- culture ID;
- core city IDs;
- target city IDs;
- phase (`cluster_uprising`, `civil_war`, `unification`, `failed`);
- completion flag and last processed year.

The existing rebel route remains responsible for naming, government class, stronghold state, population and disposal. Cluster metadata is additive and must not alter city population accounting.

## War Phases

- **Cluster uprising:** the rebel can acquire/attack only target cities from its own cluster. Other rebel kingdoms from the same origin are protected from rebel-on-rebel war.
- **Cluster completion:** once every target city belongs to the rebel, mark that cluster complete. If the original kingdom still owns its capital or protected ring, the original kingdom remains alive.
- **Civil war:** when every active cluster for the origin is complete, declare wars among all surviving rebel kingdoms using the existing war system. Rebel-to-rebel admission is allowed only for this stored civil-war phase.
- **Defeat:** use existing elimination/territory settlement behavior to transfer all defeated-rebel cities to the winner and remove the defeated rebel kingdom. Preserve ordinary actors and archive records according to existing rebel cleanup rules.
- **Unification:** when one rebel remains, it exits civil-war mode and declares the existing origin-suppression/unification war against the original kingdom, including the protected capital and adjacent cities. The final winner therefore resolves the capital protection rather than leaving the country permanently split.

## Persistence and Recovery

Cluster state uses additive `KingdomData` keys and stable IDs only. Runtime indexes are rebuilt after load and cleared on world reset. Missing cities, destroyed rebels, changed cultures or invalid origin kingdoms cause the cluster to be marked failed and removed from active scheduling; no synchronous SQLite query is needed.

## Performance and Safety

- One bounded origin scan and one bounded cluster build per origin/year.
- One bounded cluster lifecycle transition per authority cycle.
- No world-wide actor scan and no map/P0 work.
- All mutation paths honor `PeasantRebelRouteRules.CanMutateAuthority` and replica-apply guards.
- Existing war and city-transfer protections remain authoritative.

## Verification

Pure tests cover effective loyalty thresholds, capital adjacency exclusion, exact-culture connected components, deterministic cluster keys and civil-war phase transitions. Source guards cover loyalty asset registration, annual scheduling, bounded work, persistence keys, rebel war admission and final unification. Full rules, main build, locale JSON parsing and `git diff --check` are required before merge.
