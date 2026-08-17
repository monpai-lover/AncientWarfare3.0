# Custom Court Workflow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` for inline implementation. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a player-authored, shareable, card-workflow court-template system that can be applied safely to individual kingdoms.

**Architecture:** Built-in Xia and Western profiles stay immutable fallbacks. JSON templates and persisted kingdom snapshots feed a single resolver; court views, appointment rules, effects, and AI use its resolved office graph. The editor authors a directed acyclic graph of office cards with management and prerequisite edges.

**Tech Stack:** C# 10, .NET Framework 4.8, Newtonsoft.Json, Unity UI/NeoModLoader, existing AW3 multiplayer commands, .NET 9 rules tests.

---

## File Structure

- Create `Code/core/court/CustomCourtTemplateModels.cs`, `CustomCourtTemplateRules.cs`, and `CustomCourtTemplateJsonCodec.cs` for template DTOs, validation, normalization, and content hashing.
- Create `Code/core/court/CustomCourtTemplateStore.cs` for atomic local JSON import/export.
- Create `Code/core/court/CustomCourtInstanceModels.cs`, `CustomCourtInstanceCodec.cs`, and `CustomCourtInstanceService.cs` for kingdom snapshots and overrides.
- Create `Code/core/court/CourtDefinitionResolver.cs`, `CustomCourtPrerequisiteRules.cs`, `CustomCourtEffectRules.cs`, `CustomCourtEffectService.cs`, `CustomCourtApplicationRules.cs`, and `CustomCourtApplicationService.cs`.
- Create `Code/ui/windows/CustomCourtWorkflowWindow.cs` and `Code/ui/components/CourtWorkflowCanvas.cs`, `CourtWorkflowOfficeCard.cs`, and `CourtWorkflowEdgeView.cs`.
- Modify `CourtService.cs`, `CourtReadModelService.cs`, `CourtInstitutionService.cs`, `CourtManualAppointmentRules.cs`, `CivilServiceExamService.cs`, and `WesternCourtElectionService.cs` to resolve custom offices.
- Modify `LineageKeys.cs`, `AW3MultiplayerCatalogModels.cs`, `AW3MultiplayerCatalog.cs`, `AW3CourtCommandHandler.cs`, `AW3AuthoritativeCommandRouter.cs`, `CourtWindow.cs`, `KingdomWindowAddition.cs`, and `Locales/aw3_court.csv`.
- Create focused rule tests and register them in `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj` and `Program.cs.txt`.

### Task 1: Template Contracts And Pure Validation

**Files:** Create `CustomCourtTemplateModels.cs`, `CustomCourtTemplateRules.cs`, and `CustomCourtTemplateRulesTests.cs.txt`.

- [ ] **Step 1: Write failing validation tests**

```csharp
True(CustomCourtTemplateRules.IsValidTemplateId("xia_custom_1"));
False(CustomCourtTemplateRules.IsValidTemplateId("Xia Custom"));
Equal(CustomCourtTemplateValidationError.Cycle,
    CustomCourtTemplateRules.ValidateGraph(new[] {
        Edge("a", "b", CustomCourtEdgeKind.Management),
        Edge("b", "a", CustomCourtEdgeKind.Management) }));
Equal(CustomCourtTemplateValidationError.DanglingOffice,
    CustomCourtTemplateRules.ValidateGraph(new[] { "a" },
        new[] { Edge("a", "missing", CustomCourtEdgeKind.AppointmentPrerequisite) }));
False(CustomCourtTemplateRules.IsEffectValueValid(
    CustomCourtEffectId.TaxIncome, CustomCourtEffectMode.AddPercent, 999));
```

- [ ] **Step 2: Run RED**

Run `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --custom-court-template`.
Expected: compile failure because custom template contracts do not exist.

- [ ] **Step 3: Implement the models and validator**

Define `CustomCourtTemplate`, `CustomCourtOffice`, `CustomCourtEdge`, localized text, layout, requirements, and effects. Validate supported schema version, stable IDs, unique nodes/edges, bounded slots/grades/layout, whitelist effects/requirements, dangling edges, and acyclicity across all edge kinds.

- [ ] **Step 4: Run GREEN and commit**

Expected: `Custom court template rules passed.` Commit with `feat: add custom court template validation`.

### Task 2: Canonical JSON And Local Template Store

**Files:** Create `CustomCourtTemplateJsonCodec.cs`, `CustomCourtTemplateStore.cs`; extend template rules tests.

- [ ] **Step 1: Write failing codec/store tests**

```csharp
Equal(first, CustomCourtTemplateJsonCodec.Normalize(second));
Equal(CustomCourtTemplateJsonCodec.Hash(first), CustomCourtTemplateJsonCodec.Hash(second));
True(CustomCourtTemplateStoreRules.ShouldReplaceAtomically(true, true));
False(CustomCourtTemplateStoreRules.ShouldReplaceAtomically(false, true));
```

- [ ] **Step 2: Run RED**

Run the `--custom-court-template` slice. Expected: missing codec/store contracts.

- [ ] **Step 3: Implement canonical JSON and safe file handling**

Use deterministic node/edge ordering, UTF-8 without BOM, and SHA-256 of normalized JSON. Store templates under WorldBox persistent data, not the Mods directory. Reject traversal/reparse-point paths; write `<id>.json.tmp`, validate by re-reading, and only then replace atomically. Return structured validation warnings/errors to the UI.

- [ ] **Step 4: Run GREEN and commit**

Expected: normalization, hash, and atomic replacement tests pass. Commit with `feat: add shareable custom court templates`.

### Task 3: Kingdom Instances And Definition Resolver

**Files:** Create instance models/codec/service and `CourtDefinitionResolver.cs`; modify `LineageKeys.cs`, `CourtService.cs`, and `CourtInstitutionService.cs`; add instance rules tests.

- [ ] **Step 1: Write failing instance tests**

```csharp
Equal("local", CustomCourtInstanceRules.ResolveName("builtin", "template", "local"));
Equal("template", CustomCourtInstanceRules.ResolveName("builtin", "template", ""));
True(CustomCourtInstanceRules.CanUseSavedSnapshot(true, false));
False(CustomCourtInstanceRules.CanUseSavedSnapshot(false, false));
```

- [ ] **Step 2: Run RED**

Run `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --custom-court-instance`.
Expected: custom instance contracts do not exist.

- [ ] **Step 3: Implement persistence and resolver precedence**

Persist template ID, revision, resolved snapshot, overrides, and legacy offices under explicit kingdom data keys. Add `CourtDefinitionResolver.Resolve(kingdom, officeId)` and `ResolveGraph(kingdom)` with precedence: instance snapshot, installed template, built-in profile, empty result. Route the narrow court lookup APIs through the resolver while leaving built-in behavior unchanged.

- [ ] **Step 4: Run GREEN and commit**

Expected: instance tests pass and pre-existing court tests remain green. Commit with `feat: resolve custom court definitions per kingdom`.

### Task 4: Court Graph And Appointment Prerequisites

**Files:** Create `CustomCourtPrerequisiteRules.cs`; modify `CourtReadModelService.cs`, `CourtManualAppointmentRules.cs`, `CourtService.cs`, `CivilServiceExamService.cs`, and `WesternCourtElectionService.cs`; add prerequisite tests.

- [ ] **Step 1: Write failing graph and eligibility tests**

```csharp
True(CustomCourtPrerequisiteRules.CanAppoint(true, true, true, true));
False(CustomCourtPrerequisiteRules.CanAppoint(true, true, false, true));
Equal(3, CustomCourtPrerequisiteRules.ResolveHierarchyRank(1, 2));
```

- [ ] **Step 2: Run RED**

Run `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -- --custom-court-prerequisite`.
Expected: prerequisite rules are absent.

- [ ] **Step 3: Implement resolved graph usage**

Build vacancies and hierarchy from `ResolveGraph`, preserving king, heir, general, military-governorate, feudatory, and city-leader synthetic nodes. Evaluate custom school/stat/rank/prerequisite facts in candidate projection and command-time appointment validation. Update civil-service and Western election vacancy scans to consume resolved office IDs.

- [ ] **Step 4: Run GREEN and commit**

Expected: custom prerequisites work and Xia/Western lists are identical when no instance is active. Commit with `feat: apply custom court graph to appointments`.

### Task 5: Template Application, Officer Preservation, And Rollback

**Files:** Create `CustomCourtApplicationRules.cs` and `CustomCourtApplicationService.cs`; modify `CourtService.cs`; add application tests.

- [ ] **Step 1: Write failing migration tests**

```csharp
Equal(CustomCourtOfficeMigration.KeepIncumbent,
    CustomCourtApplicationRules.ResolveMigration(true, true, true));
Equal(CustomCourtOfficeMigration.PreserveLegacy,
    CustomCourtApplicationRules.ResolveMigration(true, false, false));
Equal(CustomCourtOfficeMigration.Vacate,
    CustomCourtApplicationRules.ResolveMigration(false, true, false));
```

- [ ] **Step 2: Run RED**

Run the `--custom-court-application` slice. Expected: migration rules are absent.

- [ ] **Step 3: Implement diff/apply transaction**

Validate the proposed snapshot, diff it against current resolved definitions, preserve compatible incumbents by office ID, create legacy-office entries for removals, and only vacate in the explicitly selected mode. Persist the instance after actor updates succeed; restore old instance and actor office IDs on any failure.

- [ ] **Step 4: Run GREEN and commit**

Expected: application, preservation, legacy, and rollback tests pass. Commit with `feat: apply custom court templates safely`.

### Task 6: Whitelisted Effects And AI Template Selection

**Files:** Create `CustomCourtEffectRules.cs` and `CustomCourtEffectService.cs`; modify existing court effect consumers; add effect tests.

- [ ] **Step 1: Write failing aggregation tests**

```csharp
Equal(15f, CustomCourtEffectRules.CombineAdditivePercent(10f, 5f));
Equal(25f, CustomCourtEffectRules.ClampValue(CustomCourtEffectId.TaxIncome, 300f));
False(CustomCourtEffectRules.CanApplyToScope(CustomCourtEffectId.ArmyMorale,
    CustomCourtEffectScope.City));
```

- [ ] **Step 2: Run RED**

Run the `--custom-court-effect` slice. Expected: effect registry is absent.

- [ ] **Step 3: Implement registry and consumers**

Define effect ID, mode, scope, bound, and stacking metadata in one registry. Aggregate active resolved offices with living incumbents. Existing consumers retain current values when no custom instance is active. AI may score and choose compatible installed templates but cannot edit template content.

- [ ] **Step 4: Run GREEN and commit**

Expected: effects clamp/stack deterministically and built-in modifiers retain regression coverage. Commit with `feat: add custom court effect modules`.

### Task 7: Host-Authoritative Application Commands

**Files:** Modify `Code/api/multiplayer/AW3MultiplayerCatalogModels.cs`, `AW3MultiplayerCatalog.cs`, `AW3CourtCommandHandler.cs`, and `AW3AuthoritativeCommandRouter.cs`; add multiplayer rules tests.

- [ ] **Step 1: Write failing authority tests**

```csharp
True(CustomCourtMultiplayerRules.CanApply(true, true, true));
False(CustomCourtMultiplayerRules.CanApply(false, true, true));
False(CustomCourtMultiplayerRules.CanApply(true, false, true));
```

- [ ] **Step 2: Run RED**

Run the `--custom-court-multiplayer` slice. Expected: custom court request kinds are absent.

- [ ] **Step 3: Add host command routing**

Add apply/migrate command kinds containing kingdom ID, template ID, revision, canonical hash, expected instance revision, and migration mode. The host reloads/validates the local template, compares revision/hash, rejects stale instances, and performs the application inside current authority/replica scopes.

- [ ] **Step 4: Run GREEN and commit**

Expected: replicas and hash mismatches are rejected, host applications succeed. Commit with `feat: synchronize custom court application`.

### Task 8: Card Workflow Editor And Court Entry

**Files:** Create the workflow window/canvas/card/edge components; modify `CourtWindow.cs`, `KingdomWindowAddition.cs`, and `Locales/aw3_court.csv`.

- [ ] **Step 1: Add source-level editor guards**

Require `CustomCourtWorkflowWindow.Open(`, `CourtWorkflowCanvas`, `CreateManagementEdge`, `CreateAppointmentPrerequisiteEdge`, `CustomCourtTemplateStore.TryImport`, `CustomCourtTemplateJsonCodec.Export`, and `ApplyCustomCourtTemplate` in source-guard tests.

- [ ] **Step 2: Run RED**

Run the court UI source guard. Expected: the custom workflow entry is absent.

- [ ] **Step 3: Implement the wide workflow window**

Add a court-window action that opens a wide resizable editor. Implement template library, palette, pan/zoom canvas, node drag, lane snap, management/prerequisite handles, inspector controls, import/export, validation badges, diff preview, and apply/migration actions. Cards show localized name, grade, slots, effect count, and incumbent count. Generated IDs are diagnostic-only and never free-form user input.

- [ ] **Step 4: Manual acceptance and commit**

Create a three-card court, add management and prerequisite edges, export/delete/import JSON, apply to one kingdom, and confirm a second remains built-in. Commit with `feat: add custom court workflow editor`.

### Task 9: Full Verification, Deployment, And Push

- [ ] **Step 1: Build production**

Run `dotnet build AncientWarfare3.csproj -c Release --no-restore`.
Expected: 0 warnings and 0 errors.

- [ ] **Step 2: Run focused and full tests**

Run the custom-court focused test slices, then `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore`.
Expected: all focused slices and `Rule tests passed.`

- [ ] **Step 3: Deploy and load-test**

Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy-local.ps1`, restart WorldBox, then confirm `Ancient Warfare 3.0 loaded` and no AW3 custom-court/Harmony exceptions in `Player.log`.

- [ ] **Step 4: Final commit and push**

Run `git diff --check`, stage only intended code/locales/tests, commit with `feat: add custom court workflow templates`, and push `master`.
