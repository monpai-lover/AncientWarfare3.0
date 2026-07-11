# AW3 Naming, Occupation, And Court AI Fixes Design

## Goal

Fix three independent integration defects: let the optional Chinese Name mod own
alliance naming when present, prevent non-Xia cities from entering the Xia foreign
occupation pipeline, and make AI research every court technology in the intended
order.

## Xia Alliance Naming

NeoModLoader exposes the optional dependency `一米_中文名` to AW3 as the existing
C# preprocessor symbol with the same name. AW3 already uses this symbol in
`ModClass.cs`, `XiaNaming.cs`, and `XiaNamingRepair.cs`; alliance integration must
reuse it rather than inspect Harmony owners at runtime.

When the symbol is defined, AW3 does not compile its `Alliance.addFounders`
renaming Postfix. Chinese Name keeps exclusive ownership of its
`WorldLog.logAllianceCreated` prefix and `alliance_name` generator.

When the symbol is absent, AW3 compiles the Postfix. An alliance with at least one
Xia founder receives a deterministic English name such as `Nine Provinces League`,
`Four Seas Pact`, `Jade Concord`, or `Xia Covenant`. Non-Xia alliances and later
membership changes remain untouched.

## Foreign Occupation Identity

`foreign_entry` and `pseudo_dynasty` represent non-Xia rule over Xia land. A city
has Xia identity only when its original actor asset is Xia or its current culture
or language originates from Xia. A single Xia resident does not change the
identity of the city, and Mandate legal-core membership alone does not make a
non-Xia city Xia.

Detection order becomes:

1. A Xia owner never creates foreign occupation.
2. A Xia-identity city under a non-Xia owner is `pseudo_dynasty` only when the
   existing legal-core and control-ratio conditions pass; otherwise it is
   `foreign_entry`.
3. A non-Xia city with a culture or language mismatch is `normal_conquest`.
4. All other non-Xia cities produce no occupation record.

This prevents false history and also blocks the associated false Xiaization,
leader replacement, slavery, and accelerated assimilation effects.

## Court Technology AI Order

The AI already filters research through `PolicyNodeStatus.Available`, so it does
not bypass prerequisites. The defect is its preferred order: `rites_music` is
listed before `official_court`, while `three_departments` is absent entirely.

Move the technology order into a small pure rule and include all 12 defined
technology IDs. Preserve the existing early economic and military order, then use
this tail order:

1. `aw_tech_city_defense`
2. `aw_tech_official_court`
3. `aw_tech_rites_music`
4. `aw_tech_three_departments`

This makes the AI establish the official court before starting rites when both
are available and guarantees that the advanced court technology has an explicit
priority. Because court-school context bonuses can outweigh adjacent order
scores, AI selection also defers `rites_music` until `official_court` is complete
and defers `three_departments` until `rites_music` is complete. This gate affects
only AI choice, not player research or hard prerequisites. Current in-progress
research is not cancelled; the corrected order is used when the next slot becomes
empty.

## Verification

The user intentionally removed repository test projects. TDD therefore uses a
temporary focused executable under `F:\tmp` and links only pure rule files.

- With Chinese Name integration, the AW3 alliance Postfix is absent from the
  compiled source path.
- Without the symbol, the fallback branch compiles and produces non-empty ASCII
  alliance names.
- A non-Xia legal-core city without Xia identity never becomes `foreign_entry` or
  `pseudo_dynasty`; a real Xia legal-core city still can.
- The AI order contains every defined technology exactly once and orders official
  court before rites and three departments; its AI-only gate enforces that chain
  even when court-school context bonuses favor a later technology.
- Build AW3 with and without the optional-dependency symbol.
- Build the normal mod with zero errors and keep all user deletions unstaged.
