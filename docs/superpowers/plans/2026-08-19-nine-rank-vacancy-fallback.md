# Nine-Rank Vacancy Fallback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep nine-rank progression as the first appointment pass, then fill genuine vacancies with the best hard-valid candidate and grant that actor the target office rank.

**Architecture:** Extend the existing vacancy-promotion flag instead of adding persisted state. Existing qualification and education gates remain authoritative; vacancy fallback bypasses only rank, service-history, and evaluation progression. Central and local reconciliation explicitly run strict selection before fallback selection, and rank projection remains inside the existing career appointment transaction.

**Tech Stack:** C# 10, .NET 8 rules test harness, .NET Framework 4.8.1 production build, SQLite appointment persistence, PowerShell source guards.

---

### Task 1: Lock vacancy fallback and rank-floor behavior with failing rules tests

**Files:**
- Modify: `Code/core/court/CivilServiceExamRules.cs`
- Modify: `Code/core/court/OfficialCareerRankRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/RegionalGovernmentNineRankRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/CivilServiceExamRulesTests.cs.txt`

- [ ] **Step 1: Add failing tests for the approved two-pass boundary**

Add assertions equivalent to:

```csharp
True(CivilServiceExamRules.ShouldUseVacancyFallback(
    officeVacant: true, strictCandidateFound: false,
    appointmentQualificationEligible: true),
    "a real vacancy may bypass progression gates");
False(CivilServiceExamRules.ShouldUseVacancyFallback(
    officeVacant: false, strictCandidateFound: false,
    appointmentQualificationEligible: true),
    "an occupied office never uses fallback");
False(CivilServiceExamRules.ShouldUseVacancyFallback(
    officeVacant: true, strictCandidateFound: true,
    appointmentQualificationEligible: true),
    "strict candidates always win before fallback");
False(CivilServiceExamRules.ShouldUseVacancyFallback(
    officeVacant: true, strictCandidateFound: false,
    appointmentQualificationEligible: false),
    "fallback never bypasses the appointment qualification");

Equal(10, OfficialCareerRankRules.ResolveVacancyPromotionRank(
    currentRank: 0, officeGrade: 20, hasNineRankSystem: true,
    hasFormalQualification: true, vacancyPromotion: true),
    "central fallback grants the floor after qualification was validated upstream");
Equal(6, OfficialCareerRankRules.ResolveLocalVacancyPromotionRank(
    currentRank: 0, officeGrade: 20, hasNineRankSystem: true,
    hasFormalQualification: true, vacancyPromotion: true),
    "local fallback grants the floor after qualification was validated upstream");
```

Keep the existing assertion that a genuinely unqualified actor cannot receive
a formal appointment. The state service converts an accepted local or legacy
credential into an effective appointment-qualification flag before invoking the
pure rank resolver.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --regional-government-nine-rank
```

Expected: compilation fails because `ShouldUseVacancyFallback` and
`ResolveLocalVacancyPromotionRank` do not exist, or assertions fail under the
old local vacancy-rank behavior.

- [ ] **Step 3: Add the minimal pure rules**

Implement these exact semantics:

```csharp
public static bool ShouldUseVacancyFallback(bool officeVacant,
    bool strictCandidateFound, bool appointmentQualificationEligible)
{
    return officeVacant && !strictCandidateFound &&
           appointmentQualificationEligible;
}
```

Keep `ResolveVacancyPromotionRank` qualification-dependent and add
`ResolveLocalVacancyPromotionRank` with identical behavior using
`RequiredRankForLocalOfficeGrade`. The state service passes an effective true
flag for formal, accepted local, legacy, exempt, or pre-examination appointment
paths. A genuinely unqualified actor keeps the unranked result.

- [ ] **Step 4: Run the focused rules slices and verify GREEN**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --regional-government-nine-rank
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --civil-service-exam-slice
```

Expected: both commands exit 0 and report their slice passed.

### Task 2: Separate progression gates from hard appointment validity

**Files:**
- Modify: `Code/core/court/CivilServiceQualificationService.cs`
- Modify: `Code/core/court/OfficialCareerStateService.cs`
- Modify: `Code/core/court/OfficialCareerService.cs`
- Modify: `Tests/RegionalGovernmentNineRankSourceGuard.ps1`

- [ ] **Step 1: Extend the source guard before production edits**

Require the source to contain the new fallback rule, both central and local
vacancy rank resolvers, and continued absence of a persisted
`regional_governor` office:

```powershell
@($qualification, 'ShouldUseVacancyFallback')
@($career, 'ResolveLocalVacancyPromotionRank')
@($career, 'ResolveVacancyPromotionRank')
```

- [ ] **Step 2: Run the source guard and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests\RegionalGovernmentNineRankSourceGuard.ps1
```

Expected: failure reports the missing fallback tokens.

- [ ] **Step 3: Make qualification fallback explicit**

In `CanReceiveFormalCivilAppointment`, retain education and exemption checks as
hard restrictions. Compute formal, accepted local, and legacy credentials
normally. When none is present, reject even when `pAllowVacancyPromotion` is
true. Strict rank, service, and evaluation checks remain unchanged for
`pAllowVacancyPromotion == false`.
The final branch must be equivalent to:

```csharp
if (strictEligible) return true;
return CivilServiceExamRules.ShouldUseVacancyFallback(
    officeVacant: pAllowVacancyPromotion,
    strictCandidateFound: false,
    appointmentQualificationEligible: true);
```

Do not move alive, adult, affiliation, slave, conflicting-office, royal-guard,
or asylum checks into this method; their existing owning services remain the
hard-validity boundary.

- [ ] **Step 4: Grant rank through the staged appointment state**

In `ResolveAppointmentRankFast`, route vacancy appointments to:

```csharp
return localOffice
    ? OfficialCareerRankRules.ResolveLocalVacancyPromotionRank(
        existingRank, officeGrade, true, hasAppointmentQualification, true,
        qualification?.EntryBonus ?? 0)
    : OfficialCareerRankRules.ResolveVacancyPromotionRank(
        existingRank, officeGrade, true, hasAppointmentQualification, true,
        qualification?.EntryBonus ?? 0);
```

Keep `OfficialCareerService.Appoint` staging the state inside
`OfficialCareerPersistence.Appoint`; do not write actor rank before persistence
commits.

- [ ] **Step 5: Run the guard and focused tests**

Run the Task 1 commands plus the source guard. Expected: all exit 0.

### Task 3: Enforce strict-first selection in central and local reconciliation

**Files:**
- Modify: `Code/core/court/CourtService.cs`
- Modify: `Code/core/court/LocalCourtAppointmentService.cs`
- Modify: `Code/core/court/CityGovernorProjectionRepairService.cs`
- Modify: `Code/patch/AW_CityLeaderPatch.cs`
- Modify: `Tests/RegionalGovernmentNineRankSourceGuard.ps1`

- [ ] **Step 1: Add source assertions for two-pass selection**

The guard must require a strict selection call with
`pAllowVacancyPromotion: false`, followed by a fallback call with
`pAllowVacancyPromotion: true`, in both central and local appointment sources.
The local world roster must preserve actor hard-validity checks while deferring
formal-entry checks to the office-aware selector.

- [ ] **Step 2: Run the guard and verify RED**

Expected: local appointment source lacks an office-aware strict pass and a
separate fallback roster.

- [ ] **Step 3: Complete central strict-first selection**

Compare the best indexed-formal strict candidate with the best strict candidate
from the bounded ordinary roster. Only when both strict pools are empty compare
their vacancy-fallback winners. This admits valid legacy credentials without
allowing an indexed fallback candidate to outrank a strict roster candidate.
Acting appointment remains a later compatibility path only when no formal
fallback candidate exists.

Candidate scoring and stable ordering remain `ScoreCandidate`; add no new score
formula and continue marking the selected actor unavailable after commit.

- [ ] **Step 4: Complete local strict-first selection**

Split local candidate validity into the existing hard facts and the examination
progression check. For each empty seat:

```csharp
Actor candidate = SelectCandidate(candidates, pKingdom,
    leaderNativeCityId, officeId, pAllowVacancyPromotion: false);
bool vacancyFallback = candidate == null;
if (vacancyFallback)
    candidate = SelectCandidate(candidates, pKingdom,
        leaderNativeCityId, officeId, pAllowVacancyPromotion: true);
if (candidate != null && CourtService.TryAssignLocalOfficer(candidate,
        pKingdom, pCity, officeId, vacancyFallback))
    retainedCounts[officeId] = current + 1;
```

The combined roster merges the waiting pool with a bounded kingdom-unit scan.
It preserves alive/adult/sex/slave/king/heir/current-office, affiliation,
madness, royal-guard, and asylum hard checks. `SelectCandidate` then calls
`CanReceiveFormalCivilAppointment` with the pass flag, so formal/local/legacy
credentials remain office-aware. Preserve ability, merit, hometown, and
actor-ID tie-breakers. Add optional vacancy flags to local-officer and city-
governor assignment; the city-leader path must also try strict, then vacancy
fallback, then acting appointment.

- [ ] **Step 5: Run rules and source verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests\RegionalGovernmentNineRankSourceGuard.ps1
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --regional-government-nine-rank
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --civil-service-exam-slice
```

Expected: all commands exit 0.

### Task 4: Regression verification and scoped commit

**Files:**
- Verify: `Code/core/court/*.cs` changed by Tasks 1-3
- Verify: `Tests/AncientWarfare3.Rules.Tests/*NineRank*`
- Preserve unstaged: `Code/core/schools/HistoricalSchoolDescentService.cs`

- [ ] **Step 1: Run focused court regression suites**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --regional-government-nine-rank
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --civil-service-exam-slice
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --regional-government
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --regional-government-template
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --regional-government-court
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --custom-local-government
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --custom-court-template
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --custom-court-multiplayer
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --city-administration-mapmode
```

Expected: every command exits 0 and prints its focused pass message.

- [ ] **Step 2: Run source guards**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests\RegionalGovernmentNineRankSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\RegionalGovernmentCourtSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\CustomCourtRegionalEditorSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\CityAdministrationMapModeSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\ReportedLocalizationCoverageSourceGuard.ps1
```

Expected: each guard prints `PASS` and exits 0.

- [ ] **Step 3: Build production target**

Run:

```powershell
dotnet build AncientWarfare3.csproj -c Release -p:TargetFrameworkVersion=v4.8.1 --no-restore
```

Expected: build completes with 0 warnings and 0 errors.

- [ ] **Step 4: Check diff scope**

Run:

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; `HistoricalSchoolDescentService.cs` remains
modified but is not staged.

- [ ] **Step 5: Commit only the nine-rank slice**

Stage the court rule/service files, focused tests, test registration, and source
guard explicitly. Do not stage `HistoricalSchoolDescentService.cs`. Commit with:

```powershell
git commit -m "feat: fill court vacancies under nine-rank rules"
```
