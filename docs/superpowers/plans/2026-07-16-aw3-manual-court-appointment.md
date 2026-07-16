# Manual Court Appointment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every vacant central court card open a player-controlled candidate list and appoint any eligible domestic actor without a school requirement.

**Architecture:** Add pure appointment rules, expose a revalidating service API that reuses `SetOfficer`, then add one native list window and one portrait row component. Candidate discovery is on-demand only and uses persisted home-kingdom affiliation as nationality authority.

**Tech Stack:** C# 10, Unity UI, NeoModLoader `AbstractListWindow`, SQLite-backed official career service, PowerShell source gates, .NET rule tests.

---

### Task 1: Restore The Test Baseline

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/PathfindingPerformanceTests.cs.txt`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Remove only the unfinished Task 3 pathfinder coalescing test and source guards (`FinderCoalescesTenThousandRetargets`, `ActorPathSlot`, and pre-allocation reuse); retain the completed lifecycle and stream tests.
- [ ] Run `dotnet run --project .\Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore -c Release`; expect exit code 0.
- [ ] Run `& .\Tests\SourceGuardTests.ps1`; expect `Source guard tests passed.`

### Task 2: Specify Manual Appointment Rules With Failing Tests

**Files:**
- Create: `Code/core/court/CourtManualAppointmentRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] Add RED tests for eligible domestic male actors, schoolless and unmatched-school scoring, matching-school bonus, forbidden king/refugee/slave/mad/dead/already-appointed identities, stable actor-ID tie breaking, and occupied-office commit rejection.
- [ ] Run the rule project and verify it fails because `CourtManualAppointmentRules` and its result types do not exist.
- [ ] Implement the minimal pure rules and result enum, then rerun the rule project to green.

### Task 3: Add The Revalidating Court Service API

**Files:**
- Modify: `Code/core/court/CourtAffiliationResolver.cs`
- Modify: `Code/core/court/CourtService.cs`

- [ ] Expose `IsDomestic(Actor, Kingdom)` using persisted `HomeKingdomId`, falling back to the engine kingdom only when no affiliation record exists.
- [ ] Add `GetManualAppointmentCandidates(Kingdom, string)` that validates the current court tier and vacancy, scans `SafeUnits` once, applies shared eligibility, captures display/stat data, and sorts by score then actor ID.
- [ ] Add `TryManualAppointment(long, string, long)` that resolves all objects again, checks current-tier membership and vacancy again, checks eligibility again, then calls `SetOfficer` with the actor's actual school.
- [ ] Refactor automatic candidate selection to share the same identity eligibility so kings and protected actors cannot slip through a different path.

### Task 4: Add Vacancy Navigation And Candidate UI

**Files:**
- Create: `Code/ui/windows/CourtAppointmentWindow.cs`
- Create: `Code/ui/items/CourtAppointmentCandidateListItem.cs`
- Modify: `Code/ui/items/CourtActorNodeView.cs`
- Modify: `Code/ui/AW_LineageWindowIds.cs`

- [ ] Add the `aw_court_appointment` window ID.
- [ ] Make a valid vacancy card interactable and route it to `CourtAppointmentWindow.Open(kingdomId, officeId)`; keep occupied cards opening actors.
- [ ] Build a native list window with a localized office/kingdom heading, an empty-state row, typed failure feedback, and stable candidate rows.
- [ ] Build each row with a live portrait, name/age, role and school labels, four stats, an actor-inspector card click, and a dedicated localized appointment button.
- [ ] On success call `CourtWindow.Open(kingdomId)` so the pyramid is rebuilt from the committed appointment.

### Task 5: Localize And Guard The Integration

**Files:**
- Modify: `Locales/aw3_court.csv`
- Modify: `Tests/SourceGuardTests.ps1`

- [ ] Add Czech-compatible source text, English, Simplified Chinese, and Traditional Chinese values for title, instructions, button, empty state, role labels, success, and each typed failure.
- [ ] Add source guards for the new window ID, vacancy click route, service call, tier/vacancy revalidation, and absence of a school qualification gate.
- [ ] Run the rule and source-guard suites and fix only failures caused by this slice.

### Task 6: Build, Review, And Deploy

**Files:**
- Review: all files changed by Tasks 1-5

- [ ] Run `dotnet build AncientWarfare3.csproj -c Debug --no-restore -t:Rebuild`; expect zero errors.
- [ ] Run `dotnet build AncientWarfare3.csproj -c Release --no-restore -t:Rebuild`; expect zero errors.
- [ ] Inspect `git diff --check`, `git diff --stat`, and focused diffs to ensure unrelated dirty-tree work was not rewritten.
- [ ] Deploy the built mod to the configured WorldBox mod directory and verify the shipped files contain the appointment window and localization.
- [ ] In game, inspect a court with a vacancy, open the list, verify schoolless/other-school actors appear, appoint one, and verify the card, career history, and office persistence refresh together.

