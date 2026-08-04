# Diplomatic Vassalization One-Rank Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let one-rank-higher realms send diplomatic vassalization demands without a two-times-power preflight while leaving acceptance scoring and AI vassal-war rules unchanged.

**Architecture:** Remove only the directional demand's hard power rejection from `DiplomacyProposalService`. Keep `VassalService.CanSetVassal` as the shared legality gate and keep power inside `DiplomacyProposalRules` acceptance scoring; protect the AI war path with explicit regression tests.

**Tech Stack:** C#, AW3 diplomacy rules, .NET rules tests, PowerShell source guards.

---

### Task 1: Add RED diplomacy and AI-war separation tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/DiplomacyProposalOpportunityRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarAiGoalSelectionRulesTests.cs.txt`

- [ ] Add a failing demand-assessment rule case where requester title is one rank higher and power ratio is below two, expecting the action to remain legally available with an acceptance score.
- [ ] Preserve equal-title rejection and adjacency/subject/war/alliance rejection cases.
- [ ] Assert the existing one-rank AI force-vassal war remains illegal and the existing legal AI case remains legal.
- [ ] Run the focused rules test and confirm RED comes only from the hard power preflight.

### Task 2: Remove only the diplomatic power preflight

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalService.cs`

- [ ] Remove the `requester.power < responder.power * 2` branch from `vassalize_demand` assessment.
- [ ] Keep `VassalService.CanSetVassal(pResponder, pRequester, out reason)` immediately authoritative for title, adjacency, cycle and rebel legality.
- [ ] Do not modify `WarAiGoalSelectionRules`, `WarDecisionService`, `VassalAIService` war selection, or proposal acceptance score weights.
- [ ] Run focused tests GREEN.

### Task 3: Add source separation guard and verify

**Files:**
- Create: `Tests/AncientWarfare3.Rules.Tests/DiplomaticVassalizationRankGateSourceGuard.ps1`

- [ ] Require the demand path to call `CanSetVassal` and reject a two-times-power hard check in that path.
- [ ] Require `CanAiForceVassal` and its existing AI-war tests to remain present.
- [ ] Run the source guard, complete rules tests and `git diff --check`; expected `Rule tests passed.`
- [ ] Do not compile the main mod DLL.
