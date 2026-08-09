# AW3 Authority Cycle Cooperative Scheduling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the monolithic AW3 authority cycle into budget-visible per-service stages without changing native or Cultiway-derived simulation semantics.

**Architecture:** `AWAuthorityCycleService` owns a persistent stage cursor for large scheduling and exposes the next diagnostic phase. `AWCooperativeSimulationRunner` executes one authority stage per governor phase and advances the simulation only when the cursor reports completion. Native execution keeps the existing synchronous service sequence.

**Tech Stack:** C#/.NET Framework 4.8, Harmony integration, PowerShell source guards.

---

### Task 1: Define the regression contract

**Files:**
- Create: `Tests/AuthorityCycleCooperativeSourceGuard.ps1`

- [ ] Add assertions for a cooperative step API, service-specific phase names, completion-gated simulation advancement, reset behavior and unchanged native entry point.
- [ ] Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/AuthorityCycleCooperativeSourceGuard.ps1` and confirm it fails because the cooperative API does not exist.

### Task 2: Implement cooperative authority stages

**Files:**
- Modify: `Code/core/performance/AWAuthorityCycleService.cs`
- Modify: `Code/core/performance/AWCooperativeSimulationRunner.cs`

- [ ] Add the ordered authority stage enum, cursor state and stable phase-name table.
- [ ] Execute exactly one service per cooperative step while preserving the existing order and arguments.
- [ ] Clear cooperative state from `Reset` and after successful completion.
- [ ] Return the next authority phase from the simulation runner and advance to `Complete` only after the final authority stage.
- [ ] Run the new source guard and confirm it passes.

### Task 3: Verify scheduler compatibility

**Files:**
- Test: `Tests/CultiwayPerfSchedulerNonRegressionSourceGuard.ps1`
- Test: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Test: `AncientWarfare3.csproj`

- [ ] Run the Cultiway scheduler non-regression guard.
- [ ] Build the rules project in Release configuration with zero errors.
- [ ] Build the production project in Release configuration with zero errors.
- [ ] Review the diff to confirm Actor, building, maintenance, worker-pool and pathfinding files are unchanged.
