# Guest Office Reform Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** End a guest's affiliation and central career together during court reform, and safely close legacy serving affiliations whose career was already removed.

**Architecture:** Court validation delegates an invalid reform-era guest office to the existing asynchronous guest-end state machine instead of directly clearing its career. The end persistence gains an explicit `career already closed` request shape, guarded by a zero-active-career check both before enqueue and inside the write transaction; this stages only the stale affiliation closure and never touches a newly appointed career.

**Tech Stack:** C# 11, System.Data.SQLite, WorldBox/NeoModLoader, AW3 rules console tests and PowerShell source guards.

---

### Task 1: Define Missing-Career End Rules

**Files:**
- Modify: `Code/core/schools/GuestOfficePersistenceRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/SchoolGuestOfficeRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Write failing tests for career-count decisions**

```csharp
Equal(true, GuestOfficeEndRecoveryRules.CanCloseMissingCareer(0));
Equal(false, GuestOfficeEndRecoveryRules.CanCloseMissingCareer(1));
Equal(false, GuestOfficeEndRecoveryRules.CanCloseMissingCareer(2));
```

The tests must name the behavior: a zero active central-career count permits a legacy affiliation-only close; one or multiple rows remain protected from this recovery path.

- [ ] **Step 2: Run the rules console and verify the new symbol fails**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release --nologo
```

Expected: compilation failure naming `GuestOfficeEndRecoveryRules` or `CanCloseMissingCareer`, before the pre-existing school portrait baseline failure.

- [ ] **Step 3: Add the minimal pure rule**

```csharp
public static class GuestOfficeEndRecoveryRules
{
    public static bool CanCloseMissingCareer(int pActiveCentralCareerCount)
    {
        return pActiveCentralCareerCount == 0;
    }
}
```

- [ ] **Step 4: Re-run the rules console**

Expected: the new assertions pass; record but do not alter the known unrelated school-portrait assertion if the full sequence reaches it.

### Task 2: Make Durable Guest End Support A Previously Closed Career

**Files:**
- Modify: `Code/core/schools/GuestOfficeEndPersistence.cs`
- Modify: `Tests/CivilServiceGuestActingSourceGuard.ps1` or create `Tests/GuestOfficeReformLifecycleSourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard for the orphaned-career path**

Require all of the following source-level contracts:

```powershell
'GuestOfficeEndRecoveryRules.CanCloseMissingCareer('
'CareerAlreadyClosed'
'ReadActiveCentralCareers('
'if (current.CareerAlreadyClosed)'
'StageAffiliation(pDb, pTransaction, current);'
```

The guard must also reject calling `OfficialCareerPersistence.StageClose` when `CareerAlreadyClosed` is true.

- [ ] **Step 2: Run the guard and verify it fails**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\GuestOfficeReformLifecycleSourceGuard.ps1
```

Expected: failure because a `GuestOfficeEndRequest` currently requires a non-null `OfficialCareerCloseToken`.

- [ ] **Step 3: Implement a bounded affiliation-only request**

Change `GuestOfficeEndRequest` so `CareerToken` is nullable only when `CareerAlreadyClosed` is true. In `PrepareEnd`, read central active rows under its transaction before capturing a career:

```csharp
List<GuestOfficeCareerRow> active = ReadActiveCentralCareers(pDb, transaction,
    original.ActorId);
if (GuestOfficeEndRecoveryRules.CanCloseMissingCareer(active.Count))
    return GuestOfficeEndRequest.ForClosedCareer(original, desired,
        pEndedYear, pEndedTime, pEndReason);
if (active.Count != 1) return null;
```

Retain `GuestCareerMatches` for the exactly-one-row path. In `RefreshEndRequestForTransaction`, re-read active rows; a legacy request may continue only while the count remains zero. In `EndInTransaction`, an affiliation-only request must stage `StageAffiliation` and return committed without calling `OfficialCareerPersistence.StageClose`. A legacy request whose affiliation is already desired is an idempotent recovered commit. A new active career appearing after request creation returns `Unknown` and is never closed.

- [ ] **Step 4: Run the source guard and build**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\GuestOfficeReformLifecycleSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release --nologo
```

Expected: guard passes and Release build has zero errors.

### Task 3: Route Court Reform Through The Guest End State Machine

**Files:**
- Modify: `Code/core/court/CourtService.cs`
- Extend: `Tests/GuestOfficeReformLifecycleSourceGuard.ps1`

- [ ] **Step 1: Extend the source guard with the reform ordering rule**

Require the invalid-tier branch to call:

```csharp
if (SchoolGuestOfficeService.EndGuestOfficer(actor, pKingdom,
        "reform", Date.getCurrentYear()))
    continue;
```

and reject a direct `ClearOfficer(actor, "reform")` before the guest end call. The guard must permit direct clear only for actors that are not serving guests.

- [ ] **Step 2: Run the guard and verify the direct-clear code fails it**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\GuestOfficeReformLifecycleSourceGuard.ps1
```

Expected: failure identifying the old direct reform clear.

- [ ] **Step 3: Implement forward-safe reform handling**

At the reform branch in `CourtService.ValidateOfficers`, inspect `HistoricalAffiliationService.Get(actor.data.id)`. For a serving affiliation hosted by `pKingdom`, call `SchoolGuestOfficeService.EndGuestOfficer(actor, pKingdom, "reform", Date.getCurrentYear())` and always `continue`; this preserves the live projection until durable guest end applies it. Non-guests continue to use `ClearOfficer(actor, "reform")`.

- [ ] **Step 4: Run guard, build, and diff check**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests\GuestOfficeReformLifecycleSourceGuard.ps1
dotnet build AncientWarfare3.csproj -c Release --nologo
git diff --check
```

Expected: all focused checks pass and there is no whitespace error.

### Task 4: Verify Real Stale Data Without Mutating It

**Files:**
- Test data: `C:\Users\24908\AppData\LocalLow\mkarpenko\WorldBox\autosaves\1785421267\aw3_lineage_archive.db`

- [ ] **Step 1: Query the legacy mismatch read-only**

Run a read-only SQLite query joining `SchoolAffiliation` to active central `CourtOfficer` rows and confirm the five known stale actor IDs are selected before the fix is deployed.

- [ ] **Step 2: Deploy to the closed game and load the same autosave**

Do not edit the SQLite file. Let the normal guest-end queue close residual affiliations through the authority cycle.

- [ ] **Step 3: Inspect `Player.log` and re-run the read-only query**

Expected: no repeated `Guest office end preparation failed` line; stale serving affiliations decline to zero; no active career is closed for any actor that acquired a new appointment.

- [ ] **Step 4: Commit the implementation**

```powershell
git add Code/core/court/CourtService.cs Code/core/schools/GuestOfficeEndPersistence.cs Code/core/schools/GuestOfficePersistenceRules.cs Tests
git commit -m "fix: close guest affiliations during court reform"
```
