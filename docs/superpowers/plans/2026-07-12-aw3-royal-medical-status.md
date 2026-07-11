# AW3 Royal Medical Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace royal medical care's permanent trait with a temporary status refreshed for the current king and heir by the cached imperial physician.

**Architecture:** Register one StatusAsset, keep physician and two-target reconciliation bounded, and let status lifecycle APIs own visual duration while annual treatment owns actual cures.

**Tech Stack:** C# net48, WorldBox StatusAsset API, AW3 court cache, temporary net9 rule harness.

---

## File Structure

- Modify `Code/content/XiaTraits.cs`: remove medical-care trait.
- Modify `Code/content/XiaStatus.cs`: register `aw_royal_medical_care` status.
- Modify `Code/core/court/RoyalMedicalCareRules.cs`: target reconciliation rules.
- Modify `Code/core/court/RoyalMedicalCareService.cs`: status refresh/end and annual cure.
- Modify `Locales/others.csv`: status text in three languages.
- Modify `Locales/trait.csv`: remove obsolete trait text if present.
- Modify `F:/tmp/AW3CourtExpansionRuleTests/*`: temporary regression tests.

### Task 1: Failing Status Tests

- [ ] Add pure assertions for valid physician, at-most-two deduplicated targets, removed-target completion, and material-cure history.
- [ ] Add source assertions requiring `addStatusEffect`, `finishStatusEffect`, `AssetManager.status`, and absence of `addTrait(TRAIT_ROYAL_MEDICAL_CARE)`.
- [ ] Run the court harness and verify failure on current trait implementation.

### Task 2: Register Status

- [ ] Remove `aw_royal_medical_care` ActorTrait registration and stat mutation from `XiaTraits`.
- [ ] Register a StatusAsset in `XiaStatus` with title/description keys, `icondanyao`, health multiplier `0.5f`, lifespan `15f`, and resettable behavior.
- [ ] Add three-language status localization to `Locales/others.csv`.
- [ ] Build and commit with `git commit -m "feat: register royal medical care status"`.

### Task 3: Reconcile Physician Targets

- [ ] Resolve only the cached valid imperial physician from court state.
- [ ] Build a deduplicated target set from current king and current cached heir.
- [ ] Refresh valid targets with `addStatusEffect("aw_royal_medical_care")`.
- [ ] End removed/replaced/wrong-kingdom targets with `finishStatusEffect` on appointment, dismissal, death, king change, heir change, and annual validation.
- [ ] Keep annual health restoration and curable-condition removal; record history only when treatment changed health or removed a condition.
- [ ] Run tests and commit with `git commit -m "fix: reconcile royal medical care targets"`.

### Task 4: Verification

- [ ] Run court and correctness harnesses.
- [ ] Run normal and `DEBUG;TRACE` net48 builds.
- [ ] In game, verify the icon appears in status UI with duration and never in permanent traits.
- [ ] Dismiss/kill/transfer physician and replace king/heir; verify the old status ends and new target receives it.
- [ ] Verify combat/age death remains possible and annual treatment does not scan the royal clan or kingdom population.

