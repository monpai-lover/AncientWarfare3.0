# AW3 Alliance Naming, Window Crash, And Occupation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make new Xia alliances use the AW3 Chinese Name route, remove the kingdom-window null crash, and finish uncontested occupations without stale progress or premature retreat.

**Architecture:** Keep vanilla ownership transfer authoritative. Add pure eligibility rules, run AW3's alliance prefix after Chinese Name but before the original world log, remove UI-time kingdom mutation, and apply occupation acceleration only to the active capturer before vanilla capture processing.

**Tech Stack:** C# net48, Harmony, NeoModLoader, WorldBox publicized API, Chinese_Name generator API, temporary net9 rule harness.

---

## File Structure

- Modify `Code/content/XiaAllianceNamingRules.cs`: final-name eligibility.
- Modify `Code/content/XiaNamingRepair.cs`: Chinese Name alliance generator route.
- Modify `Code/content/XiaNaming.cs`: register/replace `Xia_alliance`.
- Modify `Code/patch/AW_XiaNamingPatch.cs`: final ordered creation prefix and removal of window mutation.
- Create `name_generators/Xia/alliances.json`: Chinese Xia alliance templates.
- Create `name_generators/lib/Xia会盟雅称.txt`: Spring-and-Autumn-style fixed forms.
- Modify `Code/core/lineage/CityOccupationAccelerationRules.cs`: active-capturer rules.
- Modify `Code/core/lineage/CityOccupationAccelerationService.cs`: prefix-time bounded progress.
- Modify `Code/core/lineage/ArmyRetreatRules.cs`: uncontested-occupation protection.
- Modify `Code/core/lineage/ArmyRetreatService.cs`: pass live occupation state.
- Modify `Code/patch/AW_CityOccupationAccelerationPatch.cs`: postfix to prefix.
- Modify `F:/tmp/AW3CorrectnessRuleTests/*`: temporary regression harness only.

### Task 1: Failing Pure Rule Tests

- [ ] Add `XiaAllianceNamingRules`, `CityOccupationAccelerationRules`, and `ArmyRetreatRules` to `F:/tmp/AW3CorrectnessRuleTests/AW3CorrectnessRuleTests.csproj`.
- [ ] Add assertions to `Program.cs` for Xia/non-Xia/custom alliance finalization, active/inactive capturer acceleration, and protected/unprotected retreat:

```csharp
Check(XiaAllianceNamingRules.ShouldFinalizeCreation(true, false), "Xia creation must finalize");
Check(!XiaAllianceNamingRules.ShouldFinalizeCreation(true, true), "custom name must survive");
Check(CityOccupationAccelerationRules.ExtraCapturePoints(true, true, false, true, 0) > 0f,
    "active undefended capturer must accelerate");
Check(CityOccupationAccelerationRules.ExtraCapturePoints(true, false, false, true, 0) == 0f,
    "stale capturer must not accelerate");
Check(ArmyRetreatRules.ProtectUncontestedOccupation(true, true, true, false),
    "active uncontested occupation must finish before retreat");
```

- [ ] Run `dotnet run --project F:\tmp\AW3CorrectnessRuleTests\AW3CorrectnessRuleTests.csproj` and verify compilation fails on the new signatures.

### Task 2: Pure Rules

- [ ] Implement `ShouldFinalizeCreation(bool usesXiaNaming, bool customName)` in `XiaAllianceNamingRules` as `usesXiaNaming && !customName`.
- [ ] Change `ExtraCapturePoints` to accept `pHasActiveCaptureUnits` and return zero unless enemy, active, and undefended.
- [ ] Add `ProtectUncontestedOccupation(bool sameCapturer, bool activeUnits, bool noDefenders, bool ownershipChanged)` and require the first three values and no ownership change.
- [ ] Run the temporary correctness harness and verify all assertions pass.
- [ ] Commit the pure rule change with `git commit -m "fix: define safe alliance and occupation rules"`.

### Task 3: Chinese Name Xia Alliance Route

- [ ] Create `name_generators/lib/Xia会盟雅称.txt` with valid forms such as `诸夏会盟`, `葵丘之盟`, `弭兵之盟`, `尊王攘夷之盟`, `九州盟誓`, and `王畿会盟`.
- [ ] Create `name_generators/Xia/alliances.json` defining `Xia_alliance` with `{Xia会盟雅称}` and `$k1_short$$k2_short$之盟` templates.
- [ ] Add `Xia_alliance` to `RemoveExistingGenerators` in `XiaNaming.Init`.
- [ ] In `XiaNamingRepair.GenerateAllianceName`, call `GenerateChineseName(XiaNameSets.AllianceGenerator, ...)`; fill parameters using `ParameterGetters.GetAllianceParameterGetter(generator.parameter_getter)` and return it before the vanilla English fallback.
- [ ] Add resource assertions that `alliances.json` contains `Xia_alliance`, `Xia会盟雅称`, and founder parameters.
- [ ] Run the correctness harness and both build configurations.
- [ ] Commit with `git commit -m "fix: route Xia alliances through Chinese Name"`.

### Task 4: Harmony Ordering And Kingdom Window Crash

- [ ] Remove the conditional `Alliance.addFounders` patch and remove `Kingdom_LoadNameInput_Prefix` from `AW_XiaNamingPatch`.
- [ ] Add an unconditional `WorldLog.logAllianceCreated` prefix with `HarmonyPriority(Priority.Last)` and `HarmonyAfter("set_alliance_name")`.
- [ ] Determine Xia eligibility from the two members present at creation, preserve `data.custom_name`, generate the Xia name, validate it, then call `setName(name, false)` before the original log runs.
- [ ] Replace direct `pKingdom.getActorAsset()` eligibility in `XiaNamingRepair` with the exception-safe `LineageService.IsXiaKingdom` path.
- [ ] Expand the source regression test to assert the dangerous kingdom window patch is absent and the ordered WorldLog prefix is present.
- [ ] Build normal and `DEBUG;TRACE`; verify zero errors and warnings.
- [ ] Commit with `git commit -m "fix: finalize Xia alliance names safely"`.

### Task 5: Continuous Occupation

- [ ] Convert `AW_CityOccupationAccelerationPatch` to a prefix calling `BeforeUpdateCapture`.
- [ ] In the service, require `pCity.isGettingCapturedBy(capturer)` before consulting goals or writing `_capture_ticks`.
- [ ] Cap pre-vanilla bonus below 100; let the following vanilla update call `finishCapture`.
- [ ] In `ArmyRetreatService`, inspect target current capturer, actual capture units, defenders, and ownership before applying the loss retreat; skip retreat only for the active uncontested capture.
- [ ] Add source checks proving no `AfterUpdateCapture`, no postfix, and an active-unit guard.
- [ ] Run harness and both builds.
- [ ] Commit with `git commit -m "fix: complete uncontested city occupations"`.

### Task 6: Verification

- [ ] Run the correctness and court-expansion harnesses.
- [ ] Run `dotnet build AncientWarfare3.csproj` and the explicit `DEBUG;TRACE` build.
- [ ] Inspect Harmony source order and resource registration with `rg`.
- [ ] In game, create Xia+Xia, Xia+foreign, and foreign+foreign alliances with Chinese Name loaded; verify only the first two use AW3 meeting names and the creation world log matches.
- [ ] Open kingdom windows during king death and kingdom creation; verify no exception.
- [ ] Observe an undefended active occupation reach 100 continuously; remove attackers and verify vanilla decay resumes.

