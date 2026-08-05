# Virtual Noble Titles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with verification checkpoints.

**Goal:** Implement arbitrary-text hereditary virtual titles, expose them in Kingdom and Actor UI, make them the primary ceremonial title when no formal hereditary title exists, and preserve synchronized chronicle records.

**Architecture:** Keep formal noble ranks in `NobleRankService`, add a dedicated persistent virtual-title service/table, and expose one shared ceremonial-title resolver to UI, genealogy, archives, and history. Player writes use the existing authoritative command router; read models are cached and invalidated by title/death/save events.

**Tech Stack:** C#, Unity UI, Harmony, SQLite reflection schemas, existing AW3 multiplayer command facade, existing `HistoryWriter`/`HistoryText` APIs, rule-test console project.

---

### Task 1: Add persistent virtual-title schema and pure rules

**Files:**
- Create: `Code/core/db/VirtualNobleTitleTableItem.cs`
- Create: `Code/core/lineage/VirtualNobleTitleRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/VirtualNobleTitleRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing rule tests** for trimming, length validation, normalized duplicate keys, primary-title selection, and successor eligibility.
- [ ] **Step 2: Run the rule test project** and verify the new tests fail because the rule type does not exist.
- [ ] **Step 3: Add the reflection table model** with `TITLE_ID`, Kingdom/Actor IDs, text, grantor, timestamps, predecessor, succession state, active flag, and archived primary-title text.
- [ ] **Step 4: Implement pure rules** with a fixed maximum input length, ordinal normalization, and deterministic primary-title selection.
- [ ] **Step 5: Run the rule project** and verify all new and existing tests pass.
- [ ] **Step 6: Commit only schema/rules/tests** with `feat: add virtual noble title model`.

### Task 2: Implement authoritative service, projection, inheritance, and history

**Files:**
- Create: `Code/core/lineage/VirtualNobleTitleService.cs`
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Create: `Code/core/lineage/CeremonialTitleResolver.cs`
- Modify: `Code/patch/AW_ActorDeathPatch.cs`
- Modify: `Code/patch/AW_SavePatch.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/VirtualNobleTitleSourceGuard.ps1`

- [ ] **Step 1: Add source-guard tests** requiring a transaction-backed grant, no active projection after failed persistence, death succession handling, and save/reload projection rebuild.
- [ ] **Step 2: Verify the guard fails** before the service exists.
- [ ] **Step 3: Implement `VirtualNobleTitleService.TryGrant`**: validate authoritative ownership, Actor/Kingdom liveness, normalized duplicate text, and atomic title/noble identity persistence.
- [ ] **Step 4: Implement cached read models** for active Kingdom rows and Actor titles; invalidate on grant, succession, revocation, Kingdom destruction, and save reload.
- [ ] **Step 5: Implement death succession** using existing lineage parent/child resolution, closing predecessor rows and preserving title text for archived snapshots.
- [ ] **Step 6: Implement `GetPrimaryCeremonialTitle`** with priority: deceased posthumous layer, living sovereign appellation, formal hereditary title, primary virtual title, remaining virtual titles.
- [ ] **Step 7: Add `ChronicleEvents.OnVirtualNobleTitleGranted/Inherited/Extinct`** writing Kingdom and person records with structured Actor/Kingdom targets and the ruler ceremonial appellation.
- [ ] **Step 8: Hook Actor death and save/reload** so virtual-title inheritance and projections run on authority cycles, never on render frames.
- [ ] **Step 9: Run source guards and rule tests** and commit with `feat: persist virtual noble titles`.

### Task 3: Add authoritative player command

**Files:**
- Modify: `Code/api/multiplayer/AW3MultiplayerCatalogModels.cs`
- Modify: `Code/core/multiplayer/commands/AW3AuthoritativeCommandRouter.cs`
- Modify: `Code/core/multiplayer/commands/AW3RecordsCommandHandler.cs`
- Modify: `Code/core/multiplayer/commands/AW3MultiplayerCatalog.cs`
- Modify: `Code/ui` command facade call sites
- Create: `Tests/AncientWarfare3.Rules.Tests/VirtualNobleTitleCommandSourceGuard.ps1`

- [ ] **Step 1: Add a failing source guard** requiring a distinct command kind and request factory carrying target Actor and title text.
- [ ] **Step 2: Verify the guard fails.**
- [ ] **Step 3: Add `GrantVirtualNobleTitle`** to the enum, descriptor/catalog, request factory, and authoritative router.
- [ ] **Step 4: Route the command through `AW3RecordsCommandHandler`** to `VirtualNobleTitleService.TryGrant`, mapping validation failures to localized command errors.
- [ ] **Step 5: Run command/source tests** and commit with `feat: add virtual title command`.

### Task 4: Add shared ceremonial-title projection to archives and genealogy

**Files:**
- Modify: `Code/core/lineage/HistoryText.cs`
- Modify: `Code/core/lineage/AncestryAnalysisService.cs`
- Modify: `Code/core/lineage/ChronicleTextExportService.cs`
- Modify: `Code/core/db/ActorArchiveTableItem.cs` and archive projection writer
- Modify: `Code/patch/AW_UnitWindowPatch.cs`
- Modify: `Code/ui/items/CourtActorNodeView.cs`
- Modify: `Code/ui/windows/FamilyTreeWindow.cs` only where title text is bound

- [ ] **Step 1: Add failing assertions** that a commoner with a virtual title displays that title as primary, a formal noble title wins when present, and archived snapshots retain the historical primary title.
- [ ] **Step 2: Verify the assertions fail.**
- [ ] **Step 3: Route all affected views through the shared resolver**, leaving ruler imperial/royal and deceased posthumous layers intact.
- [ ] **Step 4: Persist the primary title snapshot during `ArchiveActor`** without rewriting older records.
- [ ] **Step 5: Run genealogy/archive tests** and commit with `fix: prioritize virtual ceremonial titles`.

### Task 5: Add Kingdom title-holder roster and Actor grant controls

**Files:**
- Modify: `Code/ui/windows/KingdomWindowAddition.cs`
- Create: `Code/patch/AW_VirtualTitleUnitWindowPatch.cs`
- Modify: `Code/ui/AW_L10n.cs` only if a new fallback key helper is required; otherwise use existing `AW_L10n.Text` fallback strings in the new UI/service calls

- [ ] **Step 1: Add a failing UI/source guard** requiring cached Kingdom roster reads, Actor navigation via `ActionLibrary.openUnitWindow`, and a grant input/button bound to the authoritative command.
- [ ] **Step 2: Verify the guard fails.**
- [ ] **Step 3: Build the Kingdom side roster** with formal titles first, virtual titles next, stable ordering, vacancy rows, and click-through holder navigation.
- [ ] **Step 4: Add Actor-side text input and grant button** using existing UI layout helpers; disable for replica/read-only or invalid targets and show real command feedback.
- [ ] **Step 5: Refresh both views** after command completion, death succession, save reload, and title cache invalidation.
- [ ] **Step 6: Run UI source guards and the rule suite** and commit with `feat: add virtual title UI`.

### Task 6: Close city/Kingdom territory-history gaps and wartime deployment regression tests

**Files:**
- Modify: `Code/core/lineage/ChronicleEvents.cs`
- Modify: `Code/core/lineage/MandateRebelService.cs`
- Modify: `Code/core/lineage/GeneralRebellionService.cs`
- Modify: `Code/core/lineage/AutonomousRestorationService.cs`
- Modify: `Code/core/lineage/CoupRestorationService.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/CityKingdomChronicleSyncSourceGuard.ps1`
- Create: `Tests/AncientWarfare3.Rules.Tests/WarPreparationDeploymentSourceGuard.ps1`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add failing source guards** requiring every owner-changing path to call a shared city/old-Kingdom/new-Kingdom sync helper, while allowing `pFromLoad` to skip history.
- [ ] **Step 2: Verify guards fail** on direct special-path writes.
- [ ] **Step 3: Extract/use a shared transfer-history helper** and update special migration paths without changing load behavior.
- [ ] **Step 4: Add deployment guard assertions** that `WarNoticeService` still calls `ArmyDeploymentService.ActivateNotice` and that retired temporary recruitment/mobilization entry points are not called by the preparation path.
- [ ] **Step 5: Run all source guards and rule tests** and commit with `test: cover deployment and territory chronicle invariants`.

### Task 7: Final verification

**Files:**
- No production changes unless verification exposes a defect.

- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore`.
- [ ] Run the project source-guard scripts and `git diff --check`.
- [ ] Confirm no DLL compilation was performed.
- [ ] Review `git diff --stat` and ensure unrelated dirty-worktree changes are untouched.
- [ ] Report deployment status separately from source/test status; do not claim in-game UI validation without launching the game.
