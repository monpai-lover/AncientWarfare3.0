# AW3 Xia Alliance Naming, Window Crash, And Occupation Design

## Goal

Make newly created Xia alliances receive Spring-and-Autumn-style names after the
Chinese Name mod has run, remove a kingdom-window naming crash, and make uncontested
occupation finish continuously without allowing cities to fall after all attackers
leave.

This design targets new worlds only. It does not repair old `NAME` alliances, old
generic alliance names, old occupation state, or old save data.

## Confirmed Root Causes

### Xia Alliance Naming

The current AW3 `Alliance.addFounders` patch is compiled only under
`#if !一米_中文名`. The project defines that symbol, so the AW3 Xia-specific branch
is absent from the runtime DLL.

Chinese Name patches `WorldLog.logAllianceCreated` with Harmony owner
`set_alliance_name`. Its generic alliance generator contains empire-style templates,
so a Xia alliance can receive an ordinary empire or federation name rather than an
AW3 Xia alliance name.

### Kingdom Window Crash

The latest runtime log shows:

`LineageService.IsXiaKingdom -> XiaizationService.GetLevel ->
XiaNamingRepair.TryRenameKingdom -> Kingdom_LoadNameInput_Prefix`.

Opening a kingdom window currently performs a naming mutation during
`WindowMetaGeneric<Kingdom, KingdomData>.loadNameInput`. A selected kingdom can be in
an incomplete or invalid lifecycle state, and Xia detection then reads unsafe kingdom
species or king state. UI name loading must not perform world-state migration.

### Occupation Oscillation

`ArmyRetreatService` can clear the entire source city's attack target after the force
crosses a loss threshold, even when the target city has no effective defenders and an
occupation is already progressing.

`CityOccupationAccelerationService` runs after vanilla `City.updateCapture`, trusts
stale `being_captured_by` without verifying active capture units, and caps its direct
write at 99.5. Vanilla can decrease abandoned progress and AW3 can then add it back,
while AW3 itself cannot complete the capture. The combined behavior produces repeated
rise, retreat, and decline.

## Final Xia Alliance Naming Order

Patch `WorldLog.logAllianceCreated` with an AW3 Harmony prefix that is compiled in all
configurations. It uses `Priority.Last` and `HarmonyAfter("set_alliance_name")` so the
order is:

1. Chinese Name generic prefix, when installed;
2. AW3 Xia eligibility and final-name prefix;
3. original `WorldLog.logAllianceCreated`, which records the final name.

At this point both founders have joined and the alliance member list still identifies
the founding pair. If either founder is Xia and the name is not player-customized,
AW3 replaces the generic generated name with a Xia alliance name. Non-Xia alliances
retain the Chinese Name or vanilla result. Player custom names are never overwritten.

Chinese Name users load an AW3 `Xia_alliance` generator/resource containing
Spring-and-Autumn meeting and covenant forms. Non-Chinese users use the AW3 English
fallback pool. Both routes share one eligibility rule and one validity rule.

The final prefix validates empty, `NAME`, `NO_NAME`, and other generator placeholders
before the world log is written. Creation failure leaves the best valid prior name
rather than writing an internal placeholder.

No alliance-window repair or periodic alliance scan is included because old saves are
out of scope.

## Kingdom Naming Safety

Remove `Kingdom_LoadNameInput_Prefix`. Kingdom naming may run only at explicit state
transitions:

- new civilization kingdom creation;
- reaching full Xiaization;
- initial new-world setup where all kingdom objects are valid.

`LineageService.IsXiaKingdom` and the Xia naming eligibility helper must be pure,
non-mutating, and exception-safe. They first validate kingdom and data, prefer
`original_actor_asset` and a valid kingdom asset, and isolate optional king/founder
asset lookup behind a safe helper. Invalid, rekt, incomplete, or null kingdoms return
false rather than throwing.

Opening, closing, or preloading a kingdom window performs no kingdom rename and no
Xiaization database lookup.

## Continuous Uncontested Occupation

Preserve tactical tug-of-war while preventing an already broken city from being
abandoned by AW3's loss-threshold retreat.

An occupation is protected from loss-threshold retreat only while all conditions hold:

- the target city reports active capture units for the army's kingdom;
- the same kingdom is the current capturer;
- the target has no effective defending warriors;
- attacker and defender are still enemies;
- the target has not already transferred ownership.

This protection ends immediately if the attackers leave, defenders regain effective
control, another enemy becomes the active capturer, the war ends, or the city changes
owner. Royal Guard's existing special behavior remains unchanged.

Move AW3 acceleration to a prefix before vanilla `City.updateCapture`. It may add
bounded extra progress only for the currently active enemy capturer with actual capture
units and no effective defenders. The added value stays below the completion boundary;
the following vanilla update owns the threshold check and calls `finishCapture` once.

If no attacker remains, AW3 adds nothing and vanilla decay proceeds. If defenders
return or multiple enemy kingdoms contest the city, vanilla selection and reversal
rules remain authoritative. AW3 never calls `finishCapture`, transfers city ownership,
or writes conquest history itself.

## Performance And Safety

- Alliance naming runs once per new alliance.
- Kingdom window open performs no naming work.
- Occupation checks are constant-time city/role checks inside the existing update.
- The war-goal cache remains bounded and is consulted only for an active capturer.
- No world scan, old-save repair, or periodic alliance repair is added.
- All reflection access fails closed and cannot prevent vanilla capture processing.

## Verification

Alliance tests must cover:

- Chinese Name prefix followed by AW3 Xia final prefix;
- Xia plus Xia and Xia plus non-Xia founders using Xia style;
- non-Xia founders retaining the prior generated name;
- player-customized names remaining unchanged;
- invalid generator output never reaching the creation log;
- English fallback validity when Chinese Name resources are unavailable.

Kingdom safety tests must cover null, incomplete, rekt, no-king, and valid Xia/non-Xia
kingdoms. Opening a kingdom window must not invoke rename or throw.

Occupation tests must cover:

- active attackers plus no defenders producing continuous progress and one vanilla
  completion;
- occupation-in-progress suppressing loss-threshold retreat only while uncontested;
- attackers leaving, after which AW3 adds no progress and vanilla decay resumes;
- defenders returning, after which retreat and reversal remain possible;
- a third kingdom contesting without stale-capturer acceleration;
- ownership transfer clearing protection and preventing duplicate settlement/history.

Normal and `DEBUG;TRACE` net48 builds must complete with zero errors and warnings.
