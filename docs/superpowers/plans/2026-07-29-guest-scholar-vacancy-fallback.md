# Guest Scholar Vacancy Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let pre-examination courts fill central vacancies with office-free educated scholars as temporary guest officials, without letting one kingdom consume a world-wide appointment quota.

**Architecture:** Extend the existing guest-office eligibility rules instead of adding a second appointment system. Before examinations, the runtime index contributes every educated resident school member; after examinations, the existing teacher/host-qualified restriction remains. Appointment persistence, terms, biography, and cleanup continue through `SchoolGuestOfficeService` and its existing atomic guest-office transaction.

**Tech Stack:** C#, WorldBox runtime APIs, SQLite-backed AW3 court/school persistence, .NET 9 rules test executable, PowerShell source guards.

---

### Task 1: Define Guest Scholar Eligibility And Fair Budget Rules

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/SchoolGuestOfficeRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CivilServiceExamRulesTests.cs.txt`
- Modify: `Code/core/schools/SchoolGuestOfficeRules.cs`
- Modify: `Code/core/court/CivilServiceExamRules.cs`

- [ ] **Step 1: Write failing rule tests**

Add assertions proving that `SchoolGuestOfficeRules.CanInvite` accepts an adult domestic or foreign resident without requiring foreign nationality, rejects minors and non-residents, and that `AppointmentBudgetForHost(9, 4)` returns `4` while `AppointmentBudgetForHost(2, 4)` returns `2`.

Update the guest-index tests to call:

```csharp
CivilServiceExamRules.CanEnterGuestCandidateIndex(
    centralOfficeSexEligible: true,
    hasExaminationSystem: false,
    educatedScholar: true,
    qualifiedTeacher: false,
    hostIssuedQualification: false)
```

and assert that this pre-examination ordinary scholar is accepted, while an uneducated scholar is rejected and a post-examination ordinary unqualified scholar remains rejected.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: compilation fails because the new method signatures and `AppointmentBudgetForHost` do not exist.

- [ ] **Step 3: Implement the minimal pure rules**

Change `SchoolGuestOfficeRules.CanInvite` to take `adult` instead of `foreignHome`, require `adult`, and retain all existing availability, service, sex, reputation, and office-fit gates. Add:

```csharp
public static int AppointmentBudgetForHost(int pVacancyCount, int pMaxPerHost)
{
    return Math.Min(Math.Max(0, pVacancyCount), Math.Max(0, pMaxPerHost));
}
```

Change `CivilServiceExamRules.CanEnterGuestCandidateIndex` to require education and to admit ordinary scholars only before the examination system:

```csharp
return centralOfficeSexEligible && educatedScholar &&
       (!hasExaminationSystem || qualifiedTeacher || hostIssuedQualification);
```

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the same `dotnet run` command. Expected: all rules tests pass.

### Task 2: Wire Resident Scholars Into The Existing Guest Pipeline

**Files:**
- Create: `Tests/GuestScholarVacancyFallbackSourceGuard.ps1`
- Modify: `Code/core/schools/SchoolGuestOfficeService.cs`
- Modify: `Code/core/court/CourtService.cs`

- [ ] **Step 1: Write a failing source guard**

The guard must require all of the following source contracts:

```text
HistoricalSchoolRuntimeIndex.Instance.ResidentIds(city.data.id)
HistoricalSchoolEducationService.IsEducated
CourtService.HasPrimitiveCourt(kingdom)
MaxAppointmentsPerHostPerYear
SchoolGuestOfficeRules.AppointmentBudgetForHost
```

It must reject the old domestic exclusions and the old shared budget:

```text
state.HomeKingdomId == pHost.id
affiliation.HomeKingdomId == pKingdom.id
MaxAppointmentsPerYear
work.AppointmentBudget
```

- [ ] **Step 2: Run the source guard and verify RED**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/GuestScholarVacancyFallbackSourceGuard.ps1
```

Expected: failure reporting the old teacher-only resident index, domestic exclusions, and global appointment budget.

- [ ] **Step 3: Broaden the pre-examination candidate index**

In `BuildCandidateIndex`, use `ResidentIds(city.data.id)` before examinations and retain the existing resident-teacher plus host-qualified foreign sources after examinations. Pass the current year into candidate profiling and require `HistoricalSchoolEducationService.IsEducated`, with qualified teachers/canonical masters retaining their existing exemption behavior.

Remove the domestic-home rejection from both candidate profiling and final invitation checks. Add the missing adult check. Keep residence-in-host, no-office, no-service, role, sex, school, reputation, ability, and office-fit checks.

- [ ] **Step 4: Permit the existing durable guest appointment for domestic scholars**

Remove only `affiliation.HomeKingdomId == pKingdom.id` from `CourtService.CanAppointGuestOfficer`. Keep all residence, education, office vacancy, role, sex, and examination qualification checks.

- [ ] **Step 5: Include primitive courts and replace the shared budget**

Let `HostKingdoms` include realms for which either `HasOfficialCourt` or `HasPrimitiveCourt` is true. Replace `MaxAppointmentsPerYear` and `AnnualGuestWork.AppointmentBudget` with `MaxAppointmentsPerHostPerYear = 4`; compute a local budget from the actual vacancy count inside `ProcessHostAppointments`, and do not stop annual host iteration when one host spends its own allowance.

- [ ] **Step 6: Run the source guard and focused rules test**

Run both commands from Tasks 1 and 2. Expected: both pass.

### Task 3: Verify Regression Safety

**Files:**
- Verify only: `Code/core/schools/GuestOfficePersistence.cs`
- Verify only: `Code/core/schools/GuestOfficeEndPersistence.cs`
- Verify only: `Code/core/court/CourtAffiliationResolver.cs`

- [ ] **Step 1: Run guest and civil-service source guards**

Run:

```powershell
$guards = @(
  'Tests/CivilServiceGuestActingSourceGuard.ps1',
  'Tests/CivilServiceCentralAppointmentSourceGuard.ps1',
  'Tests/CivilServiceCareerGateSourceGuard.ps1',
  'Tests/CivilServiceForeignTalentSourceGuard.ps1',
  'Tests/GuestScholarVacancyFallbackSourceGuard.ps1'
)
foreach ($guard in $guards) {
  powershell -NoProfile -ExecutionPolicy Bypass -File $guard
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
```

Expected: every guard exits zero.

- [ ] **Step 2: Build the mod**

Run:

```powershell
dotnet build AncientWarfare3.csproj --no-restore
```

Expected: exit zero with no new compile errors in the touched files.

- [ ] **Step 3: Review the scoped diff**

Run:

```powershell
git diff -- Code/core/schools/SchoolGuestOfficeRules.cs Code/core/court/CivilServiceExamRules.cs Code/core/schools/SchoolGuestOfficeService.cs Code/core/court/CourtService.cs Tests/AncientWarfare3.Rules.Tests/SchoolGuestOfficeRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/CivilServiceExamRulesTests.cs.txt Tests/GuestScholarVacancyFallbackSourceGuard.ps1
```

Confirm that no existing unrelated court, examination, school-enrollment, or persistence changes were removed.
