# Feudatory Founder Branch Button Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make family-tree branch buttons recognize feudatory branches founded by the displayed actor while preserving founder ownership and old-save recovery.

**Architecture:** Write the founder marker at branch creation, then use one shared source/founder predicate across archive writing, bulk recovery and display projection. Recover missing markers from `ShiBranch` for old saves without mutating unrelated heirs.

**Tech Stack:** C#, SQLite query rules, asynchronous lineage projection, .NET rules tests.

---

### Task 1: Add failing founder-source rule tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/LineageProjectionRulesTests.cs.txt` or the closest existing founded-branch test file
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt` only if registration is required

- [ ] Add RED fixtures for `king_founded`, `feudatory founder=self`, `feudatory founder!=self`, unrelated source, and pending `-1` versus recovered positive ID.
- [ ] Run the focused rules test and confirm the feudatory founder case fails.

### Task 2: Centralize valid founded-branch recognition

**Files:**
- Modify: `Code/core/lineage/LineageQuery.cs`
- Modify: `Code/core/lineage/LineageBulkQuery.cs`
- Modify: `Code/core/lineage/LineageArchiveWriter.cs`

- [ ] Add a shared predicate equivalent to:

```csharp
return (source == ShiBranchSourceType.KING_FOUNDED ||
        source == ShiBranchSourceType.FEUDATORY) &&
       branch.founder_actor_id == actorId;
```

- [ ] Use it in display projection and archive/bulk recovery; never accept a feudatory branch founded by another actor.
- [ ] Extend recovery SQL to both sources, order by `CREATED_TIME DESC`, and preserve a recovered positive ID when a pending snapshot contains `-1`.
- [ ] Run focused tests and confirm GREEN.

### Task 3: Persist the founder marker at feudatory branch creation

**Files:**
- Modify: `Code/core/lineage/LineageService.cs:1113-1178`

- [ ] When creating or reusing a feudatory branch, write `FOUNDED_BRANCH_SHI_ID` only if `FOUNDER_ACTOR_ID` equals the current prince actor ID.
- [ ] Keep inherited feudatory branch identity without marking the successor as founder.
- [ ] Run focused and complete rules tests.
- [ ] Run `git diff --check` and commit.

### Task 4: Verify source and navigation guards

- [ ] Verify `FamilyTreeNodeView.BindBranchBadge` receives the restored positive ID and continues to call the existing big-tree navigation path.
- [ ] Run complete rules tests; expected `Rule tests passed.`
- [ ] Do not compile the main mod DLL.
