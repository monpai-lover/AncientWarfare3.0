# Xia Actor and Official Texture Design

## Goal

Replace the standard Xia actor artwork with the user-provided actor asset directory at the workspace root and use the three `leader_*` sets as rank-dependent official clothing. The visual tier must update from current actor data, including after promotion, demotion, appointment, and save loading.

## Resource Scope

Copy the new civilian, warrior, ruler, heir, official, and head directories into `GameResources/actors/species/civs/Xia/`.

Replace these existing standard resources:

- `male_*`, `female_*`, and `warrior_*`
- `king`, `heir`, and the old `leader`
- `heads_male`, `heads_female`, and the standard ruler, heir, warrior, and leader heads

Preserve unrelated Xia resources, including children, slaves, clans, special actors, and bandit bodies and heads.

All supplied variants must remain reachable. Because the source contains three male sets but two female and warrior sets, skin arrays will use the largest supplied count and cycle the shorter lists. This keeps the engine's shared skin index valid without dropping `male_3`.

## Official Tier Rules

Read the actor's current `LineageKeys.OFFICER_RANK` value and clamp it through `OfficialCareerRankRules`.

- `leader_1`: ranks 1-6, low officials, green
- `leader_2`: ranks 7-12, middle officials
- `leader_3`: ranks 13-18, high officials, purple

Rank zero is not a formal official tier. An unranked ordinary city leader falls back to `leader_1` so the removed legacy `leader` path is never requested.

The tier is resolved whenever WorldBox asks for the actor texture path. No visual tier is persisted separately, so existing saves and later rank changes cannot retain stale clothing.

## Visual Priority

Existing special projections keep priority. The effective order is:

1. Bandit and registered special visual-role overrides
2. King body and head
3. Heir body and head
4. Ranked official body and matching official head
5. Unranked city leader using `leader_1`
6. Warrior and civilian selection

The three files in `heads_leader` correspond directly to `leader_1`, `leader_2`, and `leader_3`. King and heir use their single dedicated heads. Warrior heads are selected deterministically from `heads_warrior` so an actor's appearance remains stable.

## Implementation Boundaries

Add a pure presentation rule that converts a rank into an official texture tier and test it independently. Integrate that rule into the existing Xia texture and head patches instead of extending `ActorVisualRole`; this avoids changing military-governorate, rebel, avatar, and bandit semantics.

Update `XiaTextures` to point standard special-head paths at the new resources and to bind all supplied civilian and warrior variants safely. Do not modify appointment, promotion, biography, or vacancy state.

## Failure Handling

Invalid and out-of-range ranks are clamped using the existing career rules. Missing actor data or non-Xia actors fall through to the existing WorldBox texture logic. Existing body and sprite fallbacks remain active if an asset cannot be loaded.

## Verification

- Unit tests cover all rank boundaries: 0, 1, 6, 7, 12, 13, 18, and out-of-range values.
- Source/resource tests verify that every configured path exists and the old `leader` path is no longer used.
- Build the rules tests and the main mod project.
- Confirm the copied resource inventory matches the source while preserved bandit and child resources remain present.
