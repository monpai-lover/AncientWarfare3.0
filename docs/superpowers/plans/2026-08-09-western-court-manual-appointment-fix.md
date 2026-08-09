# Western Court Manual Appointment Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let both Western bureaucratic institutions manually appoint valid offices while preserving real institution-membership checks and distinct error text.

**Architecture:** Put institution permission and vacancy clickability in `CourtManualAppointmentRules`, then make the vacancy card and `CourtService` consume the same rule. Keep office membership independent from appointment permission and add a distinct result for intentionally locked institutions.

**Tech Stack:** C#, Unity UI, CSV localization, .NET rules test project, PowerShell source guards

---

### Task 1: Define the manual-appointment permission rule

**Files:**
- Modify: `Code/core/court/CourtManualAppointmentRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/WesternCourtProfileRulesTests.cs.txt`

- [ ] **Step 1: Add failing rule tests**

Add assertions covering institution permission and unified vacancy clickability:

```csharp
True(CourtManualAppointmentRules.CanUseManualAppointment(
        CourtInstitutionRules.WesternBureaucratic, false),
    "Western bureaucratic offices allow manual appointment");
True(CourtManualAppointmentRules.CanUseManualAppointment(
        CourtInstitutionRules.WesternFeudalBureaucratic, false),
    "Western feudal bureaucratic offices allow manual appointment");
False(CourtManualAppointmentRules.CanUseManualAppointment(
        CourtInstitutionRules.WesternPrimitive, false),
    "primitive Western court remains locked");
True(CourtManualAppointmentRules.CanOpenVacancyAppointment(
        true, true, true),
    "valid vacancy opens appointment");
False(CourtManualAppointmentRules.CanOpenVacancyAppointment(
        true, false, true),
    "office outside the institution stays closed");
False(CourtManualAppointmentRules.CanOpenVacancyAppointment(
        true, true, false),
    "institution permission is respected");
```

- [ ] **Step 2: Run the focused rules project and verify RED**

Run: `dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore`

Expected: compile failure because `CanUseManualAppointment` and `CanOpenVacancyAppointment` do not exist.

- [ ] **Step 3: Add the minimal pure rules and result value**

Add `AppointmentNotAllowed` after `InvalidOffice`, then add:

```csharp
public static bool CanUseManualAppointment(string pInstitution,
    bool pRoyalAppointmentsUnlocked)
{
    if (string.Equals(pInstitution,
            CourtInstitutionRules.WesternBureaucratic,
            StringComparison.Ordinal) ||
        string.Equals(pInstitution,
            CourtInstitutionRules.WesternFeudalBureaucratic,
            StringComparison.Ordinal))
        return true;
    if ((pInstitution ?? string.Empty).StartsWith("western_",
            StringComparison.Ordinal))
        return pRoyalAppointmentsUnlocked;
    return true;
}

public static bool CanOpenVacancyAppointment(bool pIsVacancy,
    bool pOfficeInCurrentInstitution, bool pManualAppointmentAllowed)
{
    return pIsVacancy && pOfficeInCurrentInstitution &&
           pManualAppointmentAllowed;
}
```

- [ ] **Step 4: Run tests and verify GREEN**

Run the same rules command.

Expected: `Rule tests passed.`

- [ ] **Step 5: Commit the pure rule**

```powershell
git add -- Code/core/court/CourtManualAppointmentRules.cs `
  Tests/AncientWarfare3.Rules.Tests/WesternCourtProfileRulesTests.cs.txt
git commit -m "fix: allow western bureaucratic appointments"
```

### Task 2: Unify UI and service validation

**Files:**
- Modify: `Code/core/court/CourtService.cs:1047-1060`
- Modify: `Code/core/court/CourtService.cs:1240-1260`
- Modify: `Code/ui/items/CourtActorNodeView.cs:95-115`
- Test: `Tests/AncientWarfare3.Rules.Tests/WesternCourtManualAppointmentSourceGuard.ps1`

- [ ] **Step 1: Write a failing source guard**

Create a guard requiring the vacancy card and service to share the pure rule:

```powershell
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$rules = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\court\CourtManualAppointmentRules.cs')
$service = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\core\court\CourtService.cs')
$node = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Code\ui\items\CourtActorNodeView.cs')
foreach ($token in @(
    'CourtManualAppointmentResult.AppointmentNotAllowed',
    'CourtManualAppointmentRules.CanUseManualAppointment(',
    'CourtManualAppointmentRules.CanOpenVacancyAppointment('
)) {
    if (-not (($service + $node + $rules).Contains($token))) {
        throw "Western manual appointment integration is missing $token"
    }
}
Write-Output 'Western court manual appointment source guard passed.'
```

- [ ] **Step 2: Run the guard and verify RED**

Run: `powershell -ExecutionPolicy Bypass -File Tests\AncientWarfare3.Rules.Tests\WesternCourtManualAppointmentSourceGuard.ps1`

Expected: FAIL because service/UI integration is absent.

- [ ] **Step 3: Separate office membership from permission in `CourtService`**

Make `IsManualOfficeInCurrentTier` check only policy/profile membership. Add:

```csharp
internal static bool CanUseManualAppointment(Kingdom pKingdom)
{
    if (pKingdom?.data == null) return false;
    return CourtManualAppointmentRules.CanUseManualAppointment(
        CourtInstitutionService.GetInstitution(pKingdom),
        KingdomPolicyEffectService.Read(pKingdom)
            .RoyalAppointmentsUnlocked);
}
```

Validate in this order:

```csharp
bool officeAvailable = IsManualOfficeInCurrentTier(pKingdom, pOfficeId);
if (!officeAvailable)
    return CourtManualAppointmentResult.InvalidOffice;
if (!CanUseManualAppointment(pKingdom))
    return CourtManualAppointmentResult.AppointmentNotAllowed;
```

- [ ] **Step 4: Use the same gate in `CourtActorNodeView`**

Replace vacancy-only clickability with:

```csharp
bool officeAvailable = CourtService.IsManualOfficeInCurrentTier(
    pKingdom, pNode.OfficeId);
bool appointmentAllowed = CourtService.CanUseManualAppointment(pKingdom);
bool canAppoint = CourtManualAppointmentRules.CanOpenVacancyAppointment(
    pNode.IsVacancy, officeAvailable, appointmentAllowed);
```

Pass `officeAvailable && appointmentAllowed` to `ResolveOfficeAction` so the card body and management button cannot diverge.

- [ ] **Step 5: Run the guard and complete rules suite**

Run both:

```powershell
powershell -ExecutionPolicy Bypass -File Tests\AncientWarfare3.Rules.Tests\WesternCourtManualAppointmentSourceGuard.ps1
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: guard passes and rules output `Rule tests passed.`

- [ ] **Step 6: Commit integration**

```powershell
git add -f -- Tests/AncientWarfare3.Rules.Tests/WesternCourtManualAppointmentSourceGuard.ps1
git add -- Code/core/court/CourtService.cs Code/ui/items/CourtActorNodeView.cs
git commit -m "fix: unify western court appointment gates"
```

### Task 3: Add accurate error localization and verify deployment

**Files:**
- Modify: `Code/ui/windows/CourtAppointmentWindow.cs:540`
- Modify: `Locales/aw3_court.csv`

- [ ] **Step 1: Add failing localization assertions**

Extend the source guard to require:

```powershell
$locales = Get-Content -Raw -LiteralPath (Join-Path $root `
    'Locales\aw3_court.csv')
if (-not $locales.Contains('aw_court_appointment_not_allowed')) {
    throw 'Western court appointment permission localization is missing.'
}
```

Run the guard and expect failure for the missing key.

- [ ] **Step 2: Add result rendering and three-column text**

Add the switch case:

```csharp
case CourtManualAppointmentResult.AppointmentNotAllowed:
    return AW_L10n.Text("aw_court_appointment_not_allowed",
        "The current institution does not allow manual appointment.");
```

Add CSV text:

```csv
aw_court_appointment_not_allowed,当前官制不允许手动任命,The current institution does not allow manual appointment.,當前官制不允許手動任命
```

- [ ] **Step 3: Run final verification**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests\AncientWarfare3.Rules.Tests\WesternCourtManualAppointmentSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\AncientWarfare3.Rules.Tests\WesternCourtUiSourceGuard.ps1
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj --no-restore
dotnet build AncientWarfare3.csproj --no-restore -p:TargetFrameworkVersion=v4.8.1
```

Expected: all guards/tests pass and build completes with zero errors; pre-existing warnings may remain.

- [ ] **Step 4: Commit and deploy source only**

```powershell
git add -- Code/ui/windows/CourtAppointmentWindow.cs Locales/aw3_court.csv
git commit -m "fix: localize western appointment permission"
```

Copy the changed `Code`, `Locales`, and test-independent source files to `D:\SteamLibrary\steamapps\common\worldbox\Mods\AncientWarfare3.0`, then compare SHA256 hashes. Do not copy `bin`, `obj`, or DLL files.
