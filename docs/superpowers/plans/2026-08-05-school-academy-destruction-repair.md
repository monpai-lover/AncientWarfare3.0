# School Academy Destruction Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cleanly detach destroyed academy buildings and restore the same institution through a bounded original-tile-first repair queue.

**Architecture:** Add academy-only building lifecycle hooks that capture physical identity before removal, pass cleanup into a focused lifecycle service, persist one repair ticket per city/institution, and process a small number of tickets outside the damage stack. Institution activity and map/venue caches follow the physical building state.

**Tech Stack:** C#, Harmony, SQLite lineage store, AW3 authority scheduler, .NET rules tests.

---

### Task 1: Add lifecycle and live-building RED tests

**Files:**
- Modify/Create focused academy rules tests under `Tests/AncientWarfare3.Rules.Tests`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Add failing cases for OnRemove/removed/ruin/unusable academy rejection, academy-only destruction matching, one-ticket idempotency, original-tile preference and fallback.
- [ ] Run focused tests and confirm RED for missing lifecycle/repair rules.

### Task 2: Add academy-only building destruction hooks

**Files:**
- Modify: `Code/patch/AW_HistoricalSchoolPatch.cs`
- Create: `Code/core/lineage/HistoricalSchoolAcademyLifecycleService.cs`

- [ ] Patch the earliest destruction/ruin method that still exposes city and tile, recording academy ID/type, city ID, building ID and tile coordinates.
- [ ] Patch final removal to confirm cleanup and enqueue; all handlers must reject non-academy buildings.
- [ ] Clear `under_construction_building` only when it references the destroyed academy.
- [ ] Do not call `addBuilding` inside the Harmony callback.

### Task 3: Clean runtime ownership and venue state

**Files:**
- Modify: `Code/core/lineage/HistoricalSchoolAcademyConstructionService.cs`
- Modify: `Code/core/lineage/HistoricalSchoolAcademyService.cs`
- Modify: `Code/core/lineage/HistoricalSchoolVenueService.cs`
- Modify: `Code/core/lineage/SchoolLandmarkService.cs`

- [ ] Reject on-remove/removed/unusable entities in live academy checks.
- [ ] Remove destroyed academy entries from construction caches.
- [ ] Add `ReleaseCityClaims(cityId)` that removes matching `ByOperation` claims and synchronizes `OccupiedByCity`.
- [ ] Mark the city landmark dirty after cleanup and after rebuild.
- [ ] Run focused tests GREEN.

### Task 4: Persist institution physical state and repair tickets

**Files:**
- Add the smallest DB table/item or schema extension under `Code/core/db`
- Modify: `Code/core/lineage/HistoricalSchoolStore.cs`
- Create: `Code/core/lineage/HistoricalSchoolAcademyRepairService.cs`
- Modify the relevant restore pipeline registration

- [ ] Persist institution ID, city ID, building ID, tile x/y and state (`active`, `repair_pending`, `rebuilding`).
- [ ] Enforce one active ticket per institution/city with an operation key or unique constraint.
- [ ] On destruction, mark the institution temporarily inactive/repairing without deleting its history.
- [ ] Restore missing tickets from persisted repair state on load.

### Task 5: Process bounded original-tile-first repairs

**Files:**
- Modify: `Code/core/lineage/HistoricalSchoolAcademyRepairService.cs`
- Modify: `Code/core/lineage/HistoricalSchoolAcademyConstructionService.cs`
- Modify the AW3 authority/year scheduler entry used for school work

- [ ] Process a small fixed number of tickets per authority/year slice.
- [ ] Validate city still exists; cancel on city destruction and rebind current ownership on transfer.
- [ ] Try the saved Tile first; if invalid, call the existing placement fallback.
- [ ] Start one academy construction and update the physical binding.
- [ ] On completion, restore institution ACTIVE once, refresh counts/landmark and complete the ticket.

### Task 6: Add source guards and regression verification

- [ ] Guard that destruction hooks are academy-only and never directly create buildings.
- [ ] Guard original-tile-first ordering, venue release, landmark dirtying and one-ticket persistence.
- [ ] Run focused tests and full rules tests; expected `Rule tests passed.`
- [ ] Run `git diff --check`; do not compile the main DLL.
