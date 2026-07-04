# AW3 City Tech Spread And Foreign Occupation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refine the kingdom-level research system into a capital-first city adoption system, add neighbor diffusion bonuses, and connect foreign Mandate-core occupation to accelerated culture/language assimilation.

**Architecture:** Keep `KingdomPolicyService` as the owner of national research and prerequisites. Add a city-level adoption layer that records which cities have actually adopted each completed tech, then use yearly propagation from the capital and nearby adopted cities. Add a foreign occupation service that detects non-native control of Mandate core/Xia cities and applies culture, language, slavery, leader replacement, resentment, and history effects through existing AW3 services.

**Tech Stack:** C# net48, NeoModLoader, Harmony, WorldBox `Kingdom`/`City`/`Actor` culture-language APIs, AW3 SQLite `[TableDef]`, existing policy/tech mapmode/history/slavery/mandate patterns.

---

## AW2 Research Notes

AW2 Mandate references checked before this plan update:

- `Code/utils/MoH/MoHTools.cs`
  - `MoHKingdomBoom()` triggers collapse when Mandate value drops below `-30`.
  - Collapse records the original Mandate cities in `_moh_cities`.
  - Non-capital, non-capital-neighbor cities can rebel through vanilla `rebellion` plots.
  - Historical figures with `first` block instant collapse protection.
- `Code/content/PlotsLibrary.cs`
  - Replaces vanilla rebellion action.
  - If rebellion starts from current Mandate kingdom，former Mandate kingdom，or existing rebel kingdom，it calls `startTianmingRebellion`.
  - New rebel kingdom is marked `Rebel = true`.
  - The rebellion war uses `tianmingrebel`.
  - Supporter cities can join the rebel kingdom.
- `Code/core/AW_KingdomManager.MoH.cs`
  - If no Mandate kingdom exists，rebels and former Mandate states can claim Mandate.
  - A claimant can become emperor after controlling about 65% of original Mandate cities.
  - Existing rebels are resolved before former-Mandate restoration is preferred.
- `Code/patch/MoH/MoHCorePatch.cs`
  - AW2 marks Mandate kingdoms on the map by patching `MapText.showTextKingdom` and replacing `base_icon.sprite` with `SpriteTextureLoader.getSprite("moh_nameplate")`.
- `Code/content/WarTypeLibrary.cs`
  - AW2 has separate war types: `tianming` and `tianmingrebel`.

AW3 should reuse the gameplay intent，not copy the AW2 implementation. The AW3 version must persist rebel state，record history，show UI/map state，and avoid relying on memory-only `_moh_cities`.

---

## Scope

This plan does not rewrite the existing national policy tree. National tech completion still unlocks national policies and decisions. The new city tech layer controls local adoption, mapmode color, city-level history, and later local effects such as buildings, economy, border defenses, and assimilation resistance.

Foreign occupation assimilation is connected to the Mandate system, but it must work even before the full pseudo-dynasty/rebel systems are complete. The service records enough state now so pseudo-dynasty, rebel risk, and occupation mapmodes can consume it later without reworking the table.

---

## File Structure

- Create: `Code/core/db/CityTechStateTableItem.cs`
  - Persistent city-tech adoption/exposure state.
- Create: `Code/core/db/CityOccupationStateTableItem.cs`
  - Persistent foreign occupation and assimilation state per city.
- Create: `Code/core/policy/CityTechService.cs`
  - Capital-first adoption, yearly spread, neighbor diffusion bonus, city reports, tech map color.
- Create: `Code/core/lineage/ForeignOccupationService.cs`
  - Foreign-entry detection, occupation state, culture/language assimilation, slave conversion hooks, city leader replacement, history records.
- Create: `Code/core/lineage/MandateRebelService.cs`
  - AW2-inspired Mandate rebellion creation, rebel-government rules, original-core control checks, rebel claim-to-Mandate.
- Create: `Code/core/policy/MandateMapMarkerService.cs`
  - AW2-inspired map nameplate/minimap markers for Mandate, rebel Mandate claimant, and pseudo-foreign dynasty states.
- Modify: `Code/core/lineage/LineageKeys.cs`
  - Add small data keys for last yearly ticks, city tech cache markers, and occupation flags.
- Modify: `Code/core/policy/KingdomPolicyService.cs`
  - Call city tech service when a national tech completes; include neighbor diffusion in tech gain; expose current-tech bonus for UI.
- Modify: `Code/core/policy/KingdomPolicyInheritanceService.cs`
  - New independent kingdoms inherit national tech progress from their cities instead of starting blank.
- Modify: `Code/core/policy/TechMapModeService.cs`
  - Switch tech mapmode from kingdom color to city/local-tech color where possible.
- Modify: `Code/core/policy/TechMapLayer.cs`
  - Draw city/zone tiles from city tech reports instead of only using kingdom color.
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs`
  - Run city tech yearly tick and foreign occupation yearly tick in the same kingdom-year cadence.
- Modify: `Code/core/lineage/MandateService.cs`
  - Add legal-core helpers, pseudo-foreign origin fields/events, rebel origin fields/events, and public marker/report helpers.
- Modify: `Code/patch/AW_MandateMapModePatch.cs`
  - Add map text/nameplate marker patch if AW3 can still hook `MapText.showTextKingdom`; otherwise add an equivalent Quantum/map-icon overlay.
- Modify: `Code/core/lineage/SlaveService.cs`
  - Add safe entry point for occupation-driven Xia enslavement if the existing capture-only path cannot be reused cleanly.
- Modify: `Code/core/lineage/ChronicleEvents.cs`
  - Add event ids for capital adoption, city adoption, neighbor diffusion, foreign occupation, assimilation milestones, and occupation slavery.
- Modify: `Locales/aw3_policy.csv`, `Locales/aw3_mandate.csv`, `Locales/others.csv`
  - Add zh/en/ch text for UI, tooltips, history, and mapmode lines.
- Modify: `README.md`, `docs/AW3_Roadmap.md`
  - Document the city tech spread model and foreign occupation assimilation model.

---

### Task 1: Persistence For City Tech And Occupation

**Files:**
- Create: `Code/core/db/CityTechStateTableItem.cs`
- Create: `Code/core/db/CityOccupationStateTableItem.cs`
- Modify: `Code/core/db/MandateStateTableItem.cs`
- Modify: `Code/core/db/MandatePeriodTableItem.cs`
- Modify: `Code/core/lineage/LineageKeys.cs`

- [ ] Create `CityTechStateTableItem` with one row per city and tech:
  - `record_id` primary key.
  - `city_id`, `city_name`, `kingdom_id`, `kingdom_name`.
  - `tech_id`.
  - `adopted` as `0/1`.
  - `adoption_progress` from `0` to `100`.
  - `exposure_progress` from `0` to `60`; exposure is neighbor knowledge before national completion.
  - `source_type`: `capital`, `same_kingdom`, `neighbor`, `inheritance`, `debug`.
  - `source_city_id`, `source_kingdom_id`.
  - `first_seen_time`, `adopted_time`, `updated_time`.
- [ ] Create `CityOccupationStateTableItem` with one row per occupied city:
  - `occupation_id` primary key.
  - `city_id`, `city_name`.
  - `original_kingdom_id`, `original_kingdom_name`, `original_kingdom_color`.
  - `occupier_kingdom_id`, `occupier_kingdom_name`, `occupier_kingdom_color`.
  - `original_culture_id`, `original_language_id`, `occupier_culture_id`, `occupier_language_id`.
  - `occupation_type`: `foreign_entry`, `pseudo_dynasty`, `normal_conquest`.
  - `assimilation_progress`, `resentment`, `slave_converted_count`.
  - `leader_replaced` as `0/1`.
  - `start_time`, `end_time`, `updated_time`.
- [ ] Add `LineageKeys.CITY_TECH_LAST_YEAR`, `LineageKeys.FOREIGN_OCCUPATION_LAST_YEAR`, `LineageKeys.FOREIGN_OCCUPATION_ID`, and `LineageKeys.CITY_ORIGINAL_KINGDOM_ID`.
- [ ] Extend Mandate persistence with rebel and map-marker fields:
  - `ORIGIN_TYPE`: `native`, `rebel`, `pseudo_foreign`.
  - `ORIGINAL_CORE_COUNT`.
  - `REBEL_ORIGIN_KINGDOM_ID`, `REBEL_ORIGIN_KINGDOM_NAME`.
  - `CLAIMANT_KIND`: `orthodox`, `rebel`, `foreign_pseudo`.
  - `MAP_MARKER_KIND`: `moh`, `rebel_claimant`, `pseudo_foreign`.
- [ ] Build once to verify the reflection-based table creation sees both new `[TableDef]` classes.

### Task 2: CityTechService Core

**Files:**
- Create: `Code/core/policy/CityTechService.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs`

- [ ] Add `CityTechService.OnNationalTechCompleted(Kingdom kingdom, KingdomPolicyDef tech)`.
- [ ] When a national tech completes, immediately mark the capital city as adopted with `source_type=capital`, `adoption_progress=100`, and record:
  - kingdom history: the tech first appears in the capital.
  - city history: the capital adopts the tech.
- [ ] Do not mark all cities as adopted on completion.
- [ ] Add `CityTechService.OnKingdomYear(Kingdom kingdom)` after `KingdomPolicyService.OnKingdomYear`.
- [ ] For every completed national tech and every living city in the kingdom:
  - capital stays adopted.
  - non-capital cities gain adoption progress yearly.
  - source strength is calculated from the nearest adopted same-kingdom city, with capital influence weighted higher.
- [ ] Use this spread formula:
  - `base = 9f`.
  - `distanceFactor = clamp(1 / (1 + distanceTiles / 45), 0.12, 1.0)`.
  - `capitalBonus = 1.25` if the best source is the capital, otherwise `1.0`.
  - `policyBonus = 1.15` if the kingdom has `aw_policy_household_registry`, `1.25` if it also has `aw_policy_early_law`.
  - `yearlyGain = min(28, base * distanceFactor * capitalBonus * policyBonus)`.
  - city adoption completes at `100`.
- [ ] When a city adopts a tech, record city history and dirty the tech map if active.
- [ ] Cache yearly source lists per kingdom so this never scans all cities for every tile or every frame.

### Task 3: Neighbor Diffusion Without Instant Completion

**Files:**
- Modify: `Code/core/policy/CityTechService.cs`
- Modify: `Code/core/policy/KingdomPolicyService.cs`
- Modify: `Code/ui/windows/KingdomPolicyWindow.cs`
- Modify: localization CSV files for tooltip text.

- [ ] Add `CityTechService.GetNeighborTechResearchBonus(Kingdom kingdom, string techId)` returning a multiplier from `1.0` to `1.35`.
- [ ] Neighbor bonus source:
  - Nearby foreign city has adopted the same tech.
  - Distance from one of this kingdom's cities is within about 75 tiles.
  - Bonus is stronger if relations are peaceful or neutral, weaker if hostile.
  - War does not remove exposure, but caps the multiplier lower.
- [ ] Apply the multiplier only when the kingdom is actively researching that tech.
- [ ] Add exposure progress to border cities when neighbor influence exists:
  - Cities can reach at most `60` exposure before national completion.
  - Exposure does not unlock the tech.
  - When the kingdom later completes the tech, adoption starts from the stored exposure value.
- [ ] Update current research tooltip:
  - Show `邻国思潮 +x%` / English equivalent.
  - Show the strongest source city and source kingdom when available.
- [ ] Keep prerequisites strict: diffusion speeds valid research but does not bypass the tech tree.

### Task 4: City-Based Tech Mapmode

**Files:**
- Modify: `Code/core/policy/TechMapModeService.cs`
- Modify: `Code/core/policy/TechMapLayer.cs`
- Modify: `Code/patch/AW_TechMapModePatch.cs` if the color hook still reads kingdom colors only.

- [ ] Change the tech map report from pure kingdom score to local city score:
  - adopted count.
  - partial adoption progress.
  - exposure progress if no national completion.
- [ ] Color city zones by local score:
  - deep red: isolated/no exposure.
  - orange: exposed but not adopted.
  - yellow: partially adopting.
  - light green: most basic techs adopted.
  - green: high local adoption.
- [ ] Keep kingdom-level tooltip lines but add city lines:
  - city tech level.
  - adopted tech count.
  - current spreading techs.
  - distance from capital.
  - neighbor diffusion bonus.
- [ ] If a tile has no city, fall back to transparent/no overlay instead of kingdom color.
- [ ] Verify this fixes the current symptom where the tech map shows national colors instead of technology level colors.

### Task 5: New Kingdom And Rebel/Split Inheritance

**Files:**
- Modify: `Code/core/policy/KingdomPolicyInheritanceService.cs`
- Modify: `Code/core/policy/CityTechService.cs`

- [ ] When a new kingdom is created from an old kingdom or a rebel city, keep city tech rows attached to the city and update `kingdom_id`.
- [ ] Build the new kingdom national tech snapshot from its cities:
  - If the new capital has adopted a tech and at least 40% of its cities adopted it, mark the national tech completed.
  - If the old kingdom completed a tech but the new capital has not adopted it, convert local adoption/exposure into national progress instead of blank state.
  - If only exposure exists, convert the best exposure into partial progress for the current or next valid tech.
- [ ] Preserve social policies more conservatively:
  - inherited from parent if it was a planned split/vassal/rebel succession.
  - reduced if it is an unrelated breakaway with low local adoption.
- [ ] Record kingdom history only when inheritance materially changes the new state's starting tech level.

### Task 6: Foreign Occupation Detection

**Files:**
- Create: `Code/core/lineage/ForeignOccupationService.cs`
- Modify: `Code/core/lineage/MandateService.cs`
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs`

- [ ] Add `ForeignOccupationService.OnKingdomYear(Kingdom kingdom)`.
- [ ] Detect occupied cities using these rules:
  - `foreign_entry`: non-Xia/non-native kingdom controls a Mandate legal core city or a city whose original owner/culture was Xia.
  - `pseudo_dynasty`: foreign kingdom controls enough Mandate legal core to claim a pseudo-dynasty.
  - `normal_conquest`: culture/language differ but the city is outside Mandate/Xia focus.
- [ ] Record the original city owner/culture/language when first seen.
- [ ] If the city changes hands again, close the old occupation row with `end_time` and open a new row if conditions still apply.
- [ ] Add `MandateService.IsLegalCoreCity(City city)` and `MandateService.GetCoreControlRatioFor(Kingdom kingdom)` as public helpers.
- [ ] Extend Mandate period/state with an origin marker:
  - `native`.
  - `rebel`.
  - `pseudo_foreign`.
- [ ] Pseudo-foreign Mandate must display as `伪朝` in UI/history and must not count as orthodox restoration.

### Task 7: Culture And Language Assimilation Under Foreign Rule

**Files:**
- Modify: `Code/core/lineage/ForeignOccupationService.cs`
- Modify: `Code/content/XiaNameSets.cs` only if language/culture ids need explicit lookup helpers.

- [ ] Inspect the actual WorldBox culture/language set APIs before the implementation step and wrap them behind:
  - `GetCityCultureId(City city)`.
  - `SetCityCulture(City city, string cultureId)`.
  - `GetCityLanguageId(City city)`.
  - `SetCityLanguage(City city, string languageId)`.
- [ ] Yearly assimilation progress:
  - base gain `2`.
  - `foreign_entry` multiplier `2.0`.
  - `pseudo_dynasty` multiplier `3.0`.
  - occupier city leader multiplier `1.25`.
  - strong garrison/border army multiplier `1.15`.
  - high resentment multiplier `0.65`.
- [ ] At 25/50/75% progress, record city history milestone.
- [ ] At 100%, apply occupier culture/language to the city and record:
  - city history.
  - occupier kingdom history.
  - Mandate event if the city is legal core.
- [ ] Assimilation should be accelerated for invaded regions, but not instant; a city can be liberated before completion and keep partial memory.

### Task 8: Occupation Slavery And Leader Replacement

**Files:**
- Modify: `Code/core/lineage/ForeignOccupationService.cs`
- Modify: `Code/core/lineage/SlaveService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`

- [ ] When a pseudo-foreign or foreign-entry occupation begins, convert a bounded number of local Xia commoners into slaves:
  - Prefer adult commoners.
  - Exclude king, heir, city leader, army leader, historical figures, royal guard, and already enslaved actors.
  - Cap conversion per city-year to avoid huge spikes.
- [ ] Record only important enslaved persons as individual biography events:
  - former king.
  - former city leader.
  - former army leader.
  - noble/known lineage member.
- [ ] Record city history for aggregate conversion: `外族入关，城中夏人被役使为奴 x 名`.
- [ ] If the city leader is local and the occupier has a valid adult candidate, replace the leader with the occupier's race/culture candidate.
- [ ] Leader replacement records city history and increases resentment.
- [ ] Feed resentment into the future rebel system through a stable getter: `ForeignOccupationService.GetResentment(City city)`.

### Task 9: Mandate Rebellion And Map Markers

**Files:**
- Create: `Code/core/lineage/MandateRebelService.cs`
- Create: `Code/core/policy/MandateMapMarkerService.cs`
- Modify: `Code/core/lineage/MandateService.cs`
- Modify: `Code/patch/AW_KingdomPolicyPatch.cs`
- Modify: `Code/patch/AW_MandateMapModePatch.cs`
- Modify: `Code/content/DiplomacyContent.cs`
- Modify: `Code/ui/windows/MandateDynastyWindow.cs`
- Modify: `Locales/aw3_mandate.csv`
- Modify: `Locales/war.csv`

- [ ] Add `MandateRebelService.OnMandateCollapse(Kingdom mandateKingdom, string reason)` and call it before `MandateService.ClearMandate`.
- [ ] Persist the original legal-core city ids from `MandateCoreCityTableItem` instead of AW2's memory-only `_moh_cities`.
- [ ] Rebellion spawn rules adapted from AW2:
  - Do not spawn rebellion in the capital.
  - Prefer non-capital cities not adjacent to the capital.
  - Increase chance when occupation resentment is high，food is low，or the city has recently been conquered.
  - Use city leader as rebel founder when valid; otherwise choose a local adult military/city notable.
  - Historical-figure Mandate rulers with `first`/`figure` still receive collapse protection and should not be removed by ordinary low-Mandate checks.
- [ ] Rebel government rules:
  - set origin/government state to `农民义军`.
  - mark king with `义军领袖` trait.
  - prevent normal noble promotion for rebel king and rebel city leaders.
  - no royal guard; rebel army organization replaces it.
  - adult male mobilization target is high but capped yearly for performance.
- [ ] Rebel war rules:
  - register/use `tianmingrebel` war type with icon `ui/wars/war_tianmingrebel`.
  - if multiple rebel kingdoms exist，they can merge or prioritize fighting the Mandate remnant/foreign pseudo-dynasty.
  - Mandate remnant and current Mandate state should prefer rebel suppression as a war target.
- [ ] Rebel claim-to-Mandate rule:
  - if no orthodox Mandate exists and a rebel kingdom controls at least `65%` of original legal-core cities，it can declare Mandate.
  - successful rebel Mandate sets `ORIGIN_TYPE=rebel`.
  - history wording must be special: `义军受命建立天朝`.
  - grant a short unification buff to rebel armies after claim，then expire it.
- [ ] Map marker rules based on AW2 `moh_nameplate`:
  - Current orthodox Mandate kingdom gets a special nameplate or minimap/name icon marker.
  - Rebel claimant gets a different marker，using `iconRebel`/`iconrebel` style assets where available.
  - Pseudo-foreign dynasty gets a distinct marker and must read as `伪朝`.
  - Marker tooltip shows emperor/rebel leader，Mandate value，legal-core control，origin type，and capital.
- [ ] Implement marker with the newest AW3-safe hook:
  - First attempt: patch `MapText.showTextKingdom` like AW2 and set `base_icon.sprite` if that method/field still exists.
  - Fallback: draw a lightweight Quantum/map-icon overlay tied to capital or king position.
  - Never do full kingdom/city scans every frame; cache current marker targets and dirty them only on Mandate/rebel/occupation state changes.

### Task 10: UI, History, And Localization

**Files:**
- Modify: `Code/ui/windows/KingdomPolicyWindow.cs`
- Modify: `Code/ui/windows/MandateDynastyWindow.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Locales/aw3_policy.csv`
- Modify: `Locales/aw3_mandate.csv`
- Modify: `Locales/others.csv`

- [ ] Policy window tech tooltip shows:
  - national research status.
  - adopted city count.
  - capital adoption status.
  - nearest neighbor diffusion source.
- [ ] Mandate window shows:
  - orthodox/pseudo/rebel origin.
  - occupied legal core count.
  - strongest assimilation/resentment cities.
  - active rebel claimants and their legal-core control percentage.
  - current map marker type.
- [ ] City chronicle records local tech adoption and occupation assimilation milestones.
- [ ] Kingdom chronicle records national tech completion, first capital appearance, and pseudo-dynasty creation.
- [ ] Kingdom chronicle records Mandate collapse，Mandate rebellion，rebel city joins，rebel claim-to-Mandate，and special rebel dynasty founding.
- [ ] Person biography records only role-important slavery and leader-replacement events.
- [ ] Add Chinese, English, and fallback text. Avoid half-width commas in Chinese CSV text.

### Task 11: README And Roadmap

**Files:**
- Modify: `README.md`
- Modify: `docs/AW3_Roadmap.md`

- [ ] Document that research has two levels:
  - national mastery.
  - city adoption.
- [ ] Document that the capital receives completed tech first.
- [ ] Document neighbor diffusion as speed bonus, not free completion.
- [ ] Document foreign-entry occupation:
  - accelerated culture/language assimilation.
  - Xia enslavement.
  - pseudo-dynasty marker.
  - rebel-risk integration point.
- [ ] Document AW2-compatible Mandate rebellion behavior:
  - low Mandate collapse.
  - original legal-core city memory.
  - rebel claim after controlling at least 65% of legal core.
  - special map markers for orthodox Mandate，rebel claimant，and pseudo-foreign dynasty.

### Task 12: Verification

**Files:**
- No source files created in this task.

- [ ] Run `$env:DOTNET_ROLL_FORWARD='Major'; dotnet build`.
- [ ] Fix compile errors until the build exits with `0 warnings, 0 errors`.
- [ ] In game, test Xia kingdom with at least three cities:
  - complete `文字记事`.
  - verify only the capital adopts immediately.
  - verify closer cities adopt faster than far cities.
- [ ] Place a neighboring Human/Xia kingdom:
  - verify it gets research speed bonus for exposed tech.
  - verify it does not instantly complete that tech.
- [ ] Use tech mapmode:
  - verify colors are city/local-tech colors, not kingdom colors.
  - verify tooltip shows city tech status.
- [ ] Simulate a non-Xia conquest of a Mandate legal core or Xia city:
  - verify occupation row is created.
  - verify culture/language assimilation progresses yearly.
  - verify Xia slavery conversion is bounded.
  - verify city leader replacement records history.
- [ ] Force low Mandate collapse:
  - verify rebels spawn from non-capital legal-core cities.
  - verify rebel kingdoms use `农民义军` identity and `tianmingrebel` war type.
  - verify a rebel kingdom can claim Mandate after controlling at least 65% of original legal-core cities.
- [ ] Check map markers:
  - orthodox Mandate kingdom has special marker/nameplate.
  - rebel claimant has rebel marker.
  - pseudo-foreign dynasty has pseudo marker.
  - marker tooltip values match Mandate window values.
- [ ] Save and reload:
  - verify city tech rows and occupation rows persist.
  - verify rebel origin and marker state persist.
  - verify no duplicate capital adoption/history entries appear.

---

## Implementation Order

1. Persistence tables and keys.
2. City tech service with capital-first adoption.
3. Yearly same-kingdom spread.
4. Neighbor diffusion bonus and exposure.
5. Tech mapmode conversion to city colors.
6. New kingdom inheritance from city tech.
7. Foreign occupation state detection.
8. Culture/language assimilation, slave conversion, leader replacement.
9. Mandate rebellion and map markers.
10. UI/history/localization.
11. README/Roadmap.
12. Build and in-game verification.

## Risk Controls

- All spreading and assimilation runs yearly, never per frame.
- Tech mapmode reads cached reports and only rebuilds when dirty.
- City tech rows are created only for relevant tech/city pairs, not every possible pair at world start.
- Culture/language writes are wrapped behind helper methods so API mismatch fails softly instead of breaking the whole mod.
- Existing national policy prerequisites remain strict; diffusion never bypasses the tree.
- Occupation slavery excludes rulers, heirs, leaders, historical figures, and special military actors unless a dedicated important-person event handles them.
- Mandate map markers must use cached current-state data and must not scan all kingdoms/cities every frame.
- Rebel claim logic uses persisted legal-core city ids，not runtime city object references.
