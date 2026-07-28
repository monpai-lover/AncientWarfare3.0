# Civil-Service Youth Pipeline And Appointment Rank Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Maintain at least 24 domestic exam-ready students per sitting and make a successful appointment grant its office rank instead of requiring that rank beforehand.

**Architecture:** Extend the existing bounded elite-enrollment planner with a deficit-derived admission limit and age-priority facts. Separate appointment eligibility from appointment rank projection so the existing atomic career persistence remains the single commit point.

**Tech Stack:** C# 10, .NET Framework 4.8 mod assembly, .NET 9 rule tests, System.Data.SQLite.

---

### Task 1: Lock The Admission And Rank Semantics In Tests

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoricalSchoolEliteEnrollmentRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CivilServiceExamRulesTests.cs.txt`

- [ ] Add a failing test showing an exam-pipeline deficit increases the annual realm admission target without exceeding the hard cap.
- [ ] Add a failing test showing young adults sort before older candidates inside the same education priority.
- [ ] Add a failing test showing a formally qualified unranked candidate can enter a vacant office of any grade.
- [ ] Run `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Debug` and confirm the new assertions fail for the missing behavior.

### Task 2: Implement Demand-Driven Young Admissions

**Files:**
- Modify: `Code/core/schools/HistoricalSchoolEliteEnrollmentRules.cs`
- Modify: `Code/core/schools/HistoricalSchoolEliteEnrollmentService.cs`
- Modify: `Code/core/court/CivilServiceExamCandidateQuery.cs`

- [ ] Add pure rules for pipeline deficit, adaptive annual admission limit, and age priority.
- [ ] Count the realm's exam-ready local pipeline with the existing indexed query.
- [ ] Apply the computed admission limit while retaining one realm preparation and one candidate attempt per frame.
- [ ] Select young nobles, declined nobles, and academy commoners before older actors at the same social priority.
- [ ] Run the focused rule test and confirm it passes.

### Task 3: Make Appointment And Rank Grant One Operation

**Files:**
- Modify: `Code/core/court/OfficialCareerRankRules.cs`
- Modify: `Code/core/court/CivilServiceQualificationService.cs`
- Verify: `Code/core/court/OfficialCareerStateService.cs`
- Verify: `Code/core/court/OfficialCareerPersistence.cs`

- [ ] Separate competitive promotion requirements from vacant-office appointment eligibility.
- [ ] Permit an educated, formally qualified, unranked actor to fill a vacancy.
- [ ] Keep `ResolveVacancyPromotionRank` as the rank floor applied by the existing atomic appointment transaction.
- [ ] Run the focused rule test and confirm it passes.

### Task 4: Verify Long-Run Supply And Vacancy Filling

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/CivilServiceCenturySimulationSqlTests.cs.txt`

- [ ] Extend the simulation with age cohorts and unranked graduates.
- [ ] Assert every 25-year period has stable new candidates and no zero-candidate sitting.
- [ ] Assert qualified graduates fill all accumulated vacancies and receive appointment rank floors.
- [ ] Run all civil-service source guards and rule tests.
- [ ] Build the mod in Debug and Release with zero errors.

### Task 5: Deploy And Validate Autosave Runtime

**Files:**
- Deploy only the production files changed by Tasks 2 and 3 to the installed mod.

- [ ] Load an autosave, never `save8`.
- [ ] Advance at least three examination cycles and inspect the runtime SQLite database.
- [ ] Confirm local candidate counts, age distribution, qualifications, filled offices, and rank-at-appointment rows.
- [ ] Continue the same simulation toward 100 years and confirm candidate supply does not collapse.

