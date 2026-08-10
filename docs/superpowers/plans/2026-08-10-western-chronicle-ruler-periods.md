# Western Chronicle Ruler Periods Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Record and render western political rulers and institutional changes in kingdom chronicles without treating them as hereditary Xia monarchs.

**Architecture:** `ChronicleEvents` branches before Xia-only reign and dynasty persistence and emits an idempotent `ruler_change` event. `HistoryQuery` recognizes it as a non-regnal ruler-period boundary. `CourtInstitutionService` records every requested canonical institution change, including same-rank legacy migration.

**Tech Stack:** C#, Harmony, SQLite-backed history projections, .NET 9 rules tests, PowerShell source guards.

---

### Task 1: Classify western ruler periods

**Files:**
- Create: `Code/core/lineage/HistoryRulerPeriodRules.cs`
- Create: `Tests/AncientWarfare3.Rules.Tests/WesternChronicleRulerRulesTests.cs.txt`
- Modify: `Code/core/lineage/ChronicleKeys.cs`, `HistoryQuery.cs`, `LineageDTO.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Write the failing test**

```csharp
Equal(true, HistoryRulerPeriodRules.IsRulerTransition("ruler_change"),
    "western ruler transition opens a period");
Equal(false, HistoryRulerPeriodRules.IsRegnalPeriod("ruler_change"),
    "western ruler transition has no regnal chronology");
Equal(true, HistoryRulerPeriodRules.IsRegnalPeriod("rule_change"),
    "monarchical accession remains regnal");
```

- [ ] **Step 2: Verify red**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore`

Expected: compilation fails because `HistoryRulerPeriodRules` is absent.

- [ ] **Step 3: Add the minimal classifier and consume it**

```csharp
public static bool IsRulerTransition(string eventType) =>
    eventType == KingdomEvent.RULE_CHANGE || eventType == KingdomEvent.RULER_CHANGE;
public static bool IsRegnalPeriod(string eventType) => eventType == KingdomEvent.RULE_CHANGE;
```

Add `RULER_CHANGE = "ruler_change"`; have `GetKingdomReigns` use the classifier and set `ReignPeriod.is_ruler_period` for the dedicated event.

- [ ] **Step 4: Verify green**

Run the command from step 2. Expected: exit 0.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage Tests/AncientWarfare3.Rules.Tests
git commit -m "fix: classify western chronicle ruler periods"
```

### Task 2: Write and render the western leader transition

**Files:**
- Modify: `Code/core/lineage/ChronicleEvents.cs:19-101`
- Modify: `Code/core/court/CourtService.cs:414-421`
- Modify: `Code/ui/windows/HistoryListWindow.cs:1068-1092`
- Modify: `Locales/others.csv`
- Create: `Tests/WesternChronicleRulerSourceGuard.ps1`

- [ ] **Step 1: Write the failing guard**

```powershell
if ($chronicle -notmatch 'RecordWesternRulerChanged') { throw 'missing western writer' }
if ($chronicle -notmatch 'KingdomEvent\.RULER_CHANGE') { throw 'missing ruler event' }
if ($court -notmatch 'ChronicleEvents\.EnsureCurrentRulerRecorded\(pKingdom\)') { throw 'missing recovery' }
if ($historyUi -notmatch 'pReign\.is_ruler_period') { throw 'missing ruler rendering' }
```

- [ ] **Step 2: Verify red**

Run: `powershell -ExecutionPolicy Bypass -File Tests/WesternChronicleRulerSourceGuard.ps1`

Expected: the guard fails for the missing writer.

- [ ] **Step 3: Implement the idempotent branch**

At the start of `OnKingChanged`, route a non-Xia kingdom or ruler to a writer that returns for the same `CHRONICLE_LAST_KING_ID`, otherwise writes this actor-targeted event:

```csharp
HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.RULER_CHANGE,
    HistoryText.Actor(pRuler, pRuler.getName()) + H("aw_hist_western_ruler_ascended"),
    HistoryTarget.Actor(pRuler));
```

Add `EnsureCurrentRulerRecorded` after CourtService's operational guard. It may only recover a living current ruler with a different last id. Do not call `ReignRecordWriter`, dynasty writing, accession books, or `YearNameService` from this branch. In `BuildReignTitle`, present `is_ruler_period` as ruler name plus `YearSpan`, without regnal chronology.

- [ ] **Step 4: Verify green**

```powershell
powershell -ExecutionPolicy Bypass -File Tests/WesternChronicleRulerSourceGuard.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
```

Expected: both exit 0.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/ChronicleEvents.cs Code/core/court/CourtService.cs Code/ui/windows/HistoryListWindow.cs Locales Tests/WesternChronicleRulerSourceGuard.ps1
git commit -m "fix: record western political ruler changes"
```

### Task 3: Record canonical western institution migration

**Files:**
- Modify: `Code/core/court/CourtInstitutionService.cs:58-70`
- Modify: `Code/core/lineage/ChronicleEvents.cs:809-824`
- Modify: `Code/core/policy/KingdomPolicyService.cs:1463-1469`
- Create: `Tests/WesternChronicleInstitutionSourceGuard.ps1`

- [ ] **Step 1: Write the failing guard**

```powershell
if ($institution -notmatch 'pRecordHistory && previous != next') { throw 'same-rank migration not recorded' }
if ($chronicle -notmatch 'pPrevious == pNext') { throw 'event rejects changed institution' }
if ($policy -notmatch 'CourtInstitutionService\.Refresh\(pKingdom, pRecordHistory: true\)') { throw 'policy does not record refresh' }
```

- [ ] **Step 2: Verify red**

Run: `powershell -ExecutionPolicy Bypass -File Tests/WesternChronicleInstitutionSourceGuard.ps1`

Expected: it fails because the implementation only accepts rank upgrades.

- [ ] **Step 3: Implement changed-pair recording**

Call `OnCourtInstitutionReformed` for `pRecordHistory && previous != next`; make the event reject only a null kingdom or identical pair. In `ApplyEffect`, request `Refresh(..., true)` after a policy that can change western court effects, using a focused policy-id rule rather than broad unrelated decisions.

- [ ] **Step 4: Verify green and build**

```powershell
powershell -ExecutionPolicy Bypass -File Tests/WesternChronicleInstitutionSourceGuard.ps1
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: all commands exit 0.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/court/CourtInstitutionService.cs Code/core/lineage/ChronicleEvents.cs Code/core/policy/KingdomPolicyService.cs Tests/WesternChronicleInstitutionSourceGuard.ps1
git commit -m "fix: record western institution migrations"
```

### Task 4: Final review

**Files:** all files above.

- [ ] **Step 1: Inspect scope**

Run: `git diff master...HEAD --check; git diff --stat master...HEAD`

Expected: only chronicle, court, UI, locale, tests, and documentation files changed.

- [ ] **Step 2: Repeat verification**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
dotnet build AncientWarfare3.csproj -c Release --no-restore
```

Expected: both exit 0 with no compile errors.
