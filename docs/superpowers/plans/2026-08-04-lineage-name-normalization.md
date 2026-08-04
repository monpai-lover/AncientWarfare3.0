# Lineage Name Normalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent complete known family/clan tokens from being stored or displayed twice while preserving legal repeated given names and external naming compatibility.

**Architecture:** Add one pure canonical-given rule and use it in display composition, foreign lineage capture, live actor repair, and archive fallback. Only exact complete affixes are removed; Western/Orc paths remain unchanged.

**Tech Stack:** C# net48 production sources, net9 rules harness, existing lineage and naming rules.

---

### Task 1: Canonical Given Rule And Idempotent Display

**Files:**
- Create: `Code/core/lineage/LineageGivenNameNormalizationRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/LineageGivenNameNormalizationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/NameSystemRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Code/core/lineage/LineageDisplayNameRules.cs`

- [ ] **Step 1: Write failing normalization tests**

Cover prefix removal (`房雨立` + `房`), female suffix removal, compound token
removal, empty remainder no-op, `婷婷` preservation, clean given no-op, and
idempotent repeated calls. Add display assertions for dirty male/integrated and
female values.

- [ ] **Step 2: Verify RED**

Run the rules harness. Expected: missing `LineageGivenNameNormalizationRules`.

- [ ] **Step 3: Implement the pure rule**

Expose `Normalize(given, family, clan, isNoble, isMale, isIntegrated)`. Choose
the same affix that `Build` would compose. Remove exactly one ordinal prefix or
suffix only when a non-empty remainder remains. Return trimmed original
otherwise.

Call this rule at the top of `Build`, then compose normally. This makes display
idempotent without character-level deduplication.

- [ ] **Step 4: Verify GREEN and commit**

Run the rules harness and commit with:

```text
fix: make lineage name composition idempotent
```

### Task 2: Capture-Time And Live Repair Wiring

**Files:**
- Modify: `Code/core/lineage/ForeignPseudoLineageRules.cs`
- Modify: `Code/core/lineage/LineageService.cs`
- Modify: `Code/core/lineage/LineageArchiveWriter.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/LineageGivenNameNormalizationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Create: `Tests/AncientWarfare3.Rules.Tests/LineageGivenNameNormalizationSourceGuard.ps1`

- [ ] **Step 1: Write failing foreign-capture tests and source guard**

Include `ForeignPseudoLineageRules.cs` in the rules project. Assert that a
non-empty dirty existing given is normalized against the resolved family, while
a clean existing given is unchanged. Source guard requires raw capture to
prefer `pActor.data.name`, live repair to write `LineageKeys.GIVEN_NAME`, and
archive fallback to normalize before assigning `given_name`.

- [ ] **Step 2: Verify RED**

Run the rules harness. Expected: dirty existing given remains unchanged or the
source guard fails.

- [ ] **Step 3: Wire normalization**

After family/clan resolution, normalize `existingGiven` in
`ResolveNameParts`. In foreign admission prefer `pActor.data.name` over
`getName()`. In `ApplyDisplayName`, normalize and write back a changed given
before `Build`; update `AWNameDataKeys.GivenName` only when it equals the dirty
old value. In archive capture, normalize both a stored given and the fallback
raw name before assigning the snapshot.

Do not change Western/Orc branches or the three unrelated user-edited naming
resource files.

- [ ] **Step 4: Verify GREEN and commit**

Run the rules harness and source guard, then commit with:

```text
fix: normalize lineage given names at capture
```

### Task 3: Name Verification

- [ ] Run the rules harness.
- [ ] Run `powershell -NoProfile -ExecutionPolicy Bypass -File Tests/LineageDisplayNameRulesTests.ps1`.
- [ ] Run the new source guard.
- [ ] Run `git diff --check`.
