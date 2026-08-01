# Boundary Mesh Quality Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the remaining Task 5 geometry, fallback, validation, budget, and authoritative API review findings.

**Architecture:** Keep exact geometry helpers private to mesh and polygon modules. Validate inputs at module boundaries, carry explicit invalid pair state, and make every potentially superlinear operation obey a deterministic budget.

**Tech Stack:** C#, .NET 9 Rules tests, existing boundary raster and draft models.

---

### Task 1: Exact Ribbon Footprints And Effective Raw Curves

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalBoundaryMeshDraftRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryMeshDraftRulesTests.cs.txt`

- [ ] Add RED fixtures for a forbidden-cell corner sliver and accepted/raw tangent divergence.
- [ ] Run focused slice and record both behavioral failures.
- [ ] Replace barycentric samples with AABB cell enumeration and scratch-buffer triangle/cell clipping; allow boundary-only zero-area contact.
- [ ] Build and consistently use an effective raw curve after fallback.
- [ ] Run focused slice to GREEN.

### Task 2: Ribbon Input Contracts

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalBoundaryMeshDraftRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryMeshDraftRulesTests.cs.txt`

- [ ] Add RED tests for null entries, null/empty/closed raw chains, invalid/non-finite/closed curves, and mixed valid/invalid input lists.
- [ ] Make each invalid item increment bounded failure and skip without throwing.
- [ ] Run focused slice to GREEN.

### Task 3: Pair Geometry Allocation, Finite Inputs, And Budgets

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalBoundaryPolygonRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryPolygonRulesTests.cs.txt`

- [ ] Add RED tests for non-finite raw/accepted contours and trace/ear/pair stress budgets.
- [ ] Add explicit invalid pair state and finite-contour gates.
- [ ] Add triangle AABB broadphase, fixed scratch buffers, and one shared validation result.
- [ ] Enforce trace, edge, ear-clip, and pair-comparison budgets with deterministic failure.
- [ ] Run focused slice to GREEN.

### Task 4: Authoritative Fill API Guard

**Files:**
- Modify: `Code/core/policy/HierarchicalVassalBoundaryMeshDraftRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/HierarchicalVassalBoundaryMeshDraftRulesTests.cs.txt`

- [ ] Add a RED source/API guard for explicit authoritative and test-only names.
- [ ] Rename the public runtime and internal compatibility entry points and update all tests.
- [ ] Run focused slice to GREEN.

### Task 5: Final Verification And Commit

- [ ] Run the focused boundary slice.
- [ ] Run the full Rules suite.
- [ ] Run `git diff --check` and `git diff --cached --check`.
- [ ] Selectively commit code, tests, and the preserved two-line documentation correction.
- [ ] Do not compile the mod DLL or deploy.
