# AW3 School Academy Building Design

## Goal

Turn the supplied `school` artwork into a real Xia academy building and make
historical-school lectures and debates happen inside that building. The change
must remove the remaining city-center venue behavior without adding an annual
city or resident scan.

## Building Model

- The building asset ID is `academy_Xia` and its unique building type is
  `type_aw_school_academy`.
- It is cloned from `library_Xia`, so it retains normal construction cost,
  book slots, damage, ruin, road, and city ownership behavior.
- Xia architecture maps vanilla `order_library` to `academy_Xia` after Xia
  styled buildings are generated. The first successfully committed historical
  master descent in a city starts one academy construction site without waiting
  for the population-50/building-15 library gate. Vanilla `order_library`
  remains responsible for rebuilding an academy after later destruction.
- Event placement checks at most 24 city zones and 8 sampled tiles per zone.
  Later committed descents rotate through subsequent zone windows when an
  earlier attempt found no legal footprint. The original `canBuildFrom` check
  rejects overlap, foreign zones, invalid terrain, and incompatible footprints.
- Duplicate prevention checks both the concrete `academy_Xia` asset ID and the
  unique `type_aw_school_academy` type, with an in-memory city claim covering
  the interval before WorldBox refreshes its building indexes.
- The academy replaces the Xia library on fresh worlds; no old-save migration
  or repair scan is added.
- Art is loaded from
  `GameResources/buildings/civ_main/Xia/academy_Xia/`. The supplied 134x93
  images use an X/Y scale of `0.07975` (`0.055 * 1.45`) while retaining the
  original `0.25` Z scale and `BuildingFundament(3, 3, 2, 0)` footprint.
  This is a visual-only enlargement: collision, placement, and city occupancy
  remain unchanged.
  NML metadata crops only the 7x6 opaque minimap region from the supplied
  134x93 `mini_0.png`; the source PNG remains byte-for-byte unchanged.

## Venue Model

`HistoricalSchoolAcademyService` is the academy venue source. It resolves a
finished, usable academy through the city's building-type index, so activity
startup is O(1) and does not scan city buildings.

Lecture and debate venue requests are academy-only. Public and local tile
sources remain available for travel arrival and idle roaming, but are never a
fallback for academic work. A debate uses the same academy building and main
tile for both participants.

The existing reservation book remains authoritative. The academy main tile is
reserved once per activity, so a city can run only one lecture or debate at a
time. Different cities can continue work concurrently within the existing
global activity limits.

## Actor Lifecycle

Activity claims carry the concrete academy `Building`, not only a tile.
Preparation sets `Actor.beh_building_target`; the task then uses the vanilla
`BehGoToBuildingTarget` and `BehStayInBuildingTarget` behavior. Completion is
accepted only while the actor is actually inside the claimed academy.

Every terminal path releases the task lease and venue claim, clears the exact
building target, and calls `exitBuilding()` when the actor is inside the claimed
academy. This applies to successful persistence, interruption, timeout, death,
city change, fresh-world clear, academy destruction, construction state,
abandonment, and ownership/city change.

The first debater enters the academy before switching from the travel task to
the debate task. The second debater enters the same academy and remains there
for the receiving task. No disciple crowd is moved to the academy.

## Failure Semantics

- A city without a finished academy does not start a lecture or debate.
- A destroyed, ruined, unfinished, abandoned, or foreign-city academy
  invalidates the activity during bounded validation.
- Failed activation consumes no venue reservation and leaves no actor inside a
  building.
- A committed descent with no legal building footprint leaves no partial
  building and may retry on a later committed descent in that city.
- There is no city-center fallback and no global academy repair pass.

## Verification

Pure rule tests cover academy-only routing, one-building mutual exclusion,
same-building debate, lifecycle validity, construction eligibility, and rotated
placement windows. Source guards cover event-driven construction, bounded
placement, duplicate prevention, building registration, architecture mapping,
building-target behaviors, absence of tile movement for lecture/debate, and
cleanup calls.

Debug and Release builds, all rule tests, source guards, localization checks,
and `git diff --check` must pass. Live verification uses a fresh map and checks
construction, completed and ruined sprites, minimap sprite, one academy per
city, both actors hidden inside during debate, successful exit, and cancellation
after destruction. The academy should render exactly 1.45 times its former
X/Y size without changing its construction footprint.
