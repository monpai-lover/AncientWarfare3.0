# Xia Building Resources and Academy Capacity

## Scope

Keep the Xia building resources under `GameResources/buildings/civ_main/Xia/`, which is the path assigned by `XiaArchitecture` and `SchoolAcademyBuildingContent`. Remove only byte-identical duplicate files under the legacy root `GameResources/buildings/*_Xia` for the newly supplied barracks, docks, and hall assets.

The Xia academy remains a clone of `library_Xia`. It keeps the vanilla library behavior through `book_slots` and gains four housing slots through the existing `BuildingAsset.housing_slots` field. No custom population system or localization changes are introduced.

## Runtime behavior

- `academy_Xia` has `housing_slots = 4`.
- `academy_Xia.book_slots` remains copied from `library_Xia`.
- City housing totals include the academy through the vanilla `City` housing calculation.
- City book capacity and book-related behaviors continue to include the academy through the vanilla `book_slots` cache.

## Resource policy

- Runtime source of truth: `GameResources/buildings/civ_main/Xia/`.
- Remove only exact duplicate files in the legacy root directories for `barracks_Xia`, `docks_Xia`, `hall_Xia_0`, `hall_Xia_1`, and `hall_Xia_2`.
- Do not remove existing legacy resources that are not part of this verified duplicate set.

## Verification

- Assert the academy asset sets four housing slots and retains the source library book slots.
- Verify all removed resource hashes match their nested counterparts before deletion.
- Build the mod, deploy to the WorldBox Mods directory, and verify the deployed resource paths and assembly timestamp.
