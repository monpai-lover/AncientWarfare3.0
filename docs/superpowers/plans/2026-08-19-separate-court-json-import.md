# Separate Court JSON Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Separate central-court and local-government JSON workflows while preserving the opposite context and resolve local office names from template data.

**Architecture:** Keep the existing normalized `CustomCourtTemplate` envelope and atomic store, but add explicit central-only and local-only document projections plus deterministic merge rules. Resolve `Courtjson/Central` and `Courtjson/Local` through the path service, then make the existing workflow dropdown select the store and merge operation from `_editingLocal`.

**Tech Stack:** C# 11 production code targeting .NET Framework 4.8, .NET 9 linked-source rules tests, Newtonsoft.Json, Unity UI, CSV localization.

---

### Task 1: Lock The Document And Merge Contract

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtTemplateCodecTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtTemplateRulesTests.cs.txt`
- Modify: `Code/core/court/CustomCourtTemplatePathService.cs`
- Create: `Code/core/court/CustomCourtTemplateDocumentRules.cs`

- [ ] Add failing tests asserting `ResolveCentralRoot(root)` returns `Courtjson/Central`, `ResolveLocalRoot(root)` returns `Courtjson/Local`, central projection has no local templates, and local projection has exactly one local template and no central offices.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj` and confirm failure names the missing path/projection APIs.
- [ ] Implement `CentralRootPath`, `LocalRootPath`, `ResolveCentralRoot`, and `ResolveLocalRoot` in the path service.
- [ ] Implement `CreateCentralDocument`, `CreateLocalDocument`, `IsCentralDocument`, and `TryGetLocalDocument` using normalized object graphs rather than string manipulation.
- [ ] Run the rules executable and confirm the new projection tests pass up to any later intentionally failing test.

### Task 2: Preserve The Opposite Court Context During Import

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtTemplateCodecTests.cs.txt`
- Modify: `Code/core/court/CustomCourtTemplateDocumentRules.cs`

- [ ] Add failing tests proving `ApplyCentralDocument` changes central name/offices/edges but preserves local templates, and `TryApplyLocalDocument` replaces or appends one local template without changing central fields.
- [ ] Add limit and wrong-document tests proving failed local imports leave the destination unchanged.
- [ ] Run the rules executable and confirm failures are caused by missing merge methods.
- [ ] Implement central merge by cloning the imported central fields onto the existing normalized working template while retaining local state.
- [ ] Implement local merge on a normalized clone, replacing by ordinal ID or appending below `MaximumTemplates`, and only assign the result after validation succeeds.
- [ ] Run the rules executable and confirm all document-rule tests pass.

### Task 3: Wire Context-Specific Editor Storage

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`
- Modify: `Code/ui/windows/CustomCourtWorkflowWindow.cs`
- Modify: `Locales/aw3_court.csv`

- [ ] Add failing source-guard assertions for separate central/local selected filenames, context-specific roots, document projections, merge rules, and import-list refresh on context switch.
- [ ] Run the rules executable and confirm the source guard fails on the missing wiring.
- [ ] Replace `_selectedImportFile` with central and local selections and add `ActiveTemplateRoot()` / active-selection helpers.
- [ ] Make Save and Export persist `CreateCentralDocument(_template)` in central mode or `CreateLocalDocument(ActiveLocalTemplate)` in local mode.
- [ ] Make `RefreshImportFiles` list only the active directory and use context-specific localized captions.
- [ ] Make `ImportTemplate` reject the wrong document kind, then apply only the active context and focus the imported graph.
- [ ] Refresh import files from `SelectEditorContext` and keep dropdown popup positioning unchanged.
- [ ] Add Chinese, English, and Traditional Chinese strings for central/local import captions and descriptions.
- [ ] Run the rules executable and confirm workflow guards pass.

### Task 4: Resolve Local Office Names From Template Data

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CustomCourtWorkflowSourceGuardTests.cs.txt`
- Modify: `Code/core/court/CustomCourtRuntime.cs`

- [ ] Add a failing source guard requiring `OfficeDisplayName` to inspect `snapshot.LocalTemplates` after central offices.
- [ ] Run the rules executable and confirm the missing local lookup fails.
- [ ] Extend `OfficeDisplayName` to search non-null local templates and return the matched office's localized `Name`.
- [ ] Run the rules executable and confirm generated keys such as `aw_court_office_minzhou_governor` are no longer the primary path.

### Task 5: Verify, Build, Deploy, And Integrate

**Files:**
- Verify all modified production, test, localization, spec, and plan files.

- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj` and require exit code 0.
- [ ] Run the relevant localization/source guards and require all pass.
- [ ] Run `dotnet build AncientWarfare3.csproj --no-restore` and require exit code 0 with no new warnings.
- [ ] Review `git diff --check`, semantic diff, and status; exclude the LF-only baseline workaround and ignored guard script from commits.
- [ ] Commit implementation on `feature/separate-court-json-import`, merge it into `master` without touching the user's existing bandit files, and push `origin/master`.
- [ ] Deploy source to `D:/SteamLibrary/steamapps/common/worldbox/Mods/AncientWarfare3.0` using the repository deployment workflow and report the backup path.
