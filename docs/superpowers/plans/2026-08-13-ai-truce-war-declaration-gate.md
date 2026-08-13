# AI Truce War Declaration Gate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent AI diplomatic declarations, including mandate wars, from being issued or executed during an active truce or non-aggression pact, and cancel stale declarations when a truce becomes authoritative.

**Architecture:** Add one pure treaty-gate rule that distinguishes notified external declarations from direct internal system wars. Use it at declaration issue time and immediately before engine war start. Reconcile both directed declaration ledgers whenever a truce pair is found or inserted so coalition, separate-peace, and restored-save paths share the same cleanup.

**Tech Stack:** C#/.NET 9 rules executable, net48 mod build, PowerShell source guards, System.Data.SQLite-backed diplomacy persistence.

---

## File Map

- Modify `Code/core/lineage/DiplomaticWarDeclarationLedgerRules.cs`: own the pure live-treaty gate decision.
- Modify `Code/core/lineage/DiplomaticWarDeclarationService.cs`: enforce the gate when issuing a diplomatic declaration and preserve `active_war_blocker` cancellation.
- Modify `Code/core/lineage/WarDecisionService.cs`: enforce the live gate immediately before engine war creation without reopening all locked eligibility checks.
- Modify `Code/core/lineage/DiplomacyProposalService.cs`: cancel pending declarations in both directions after an accepted truce is found or inserted.
- Modify `Tests/AncientWarfare3.Rules.Tests/DiplomaticWarDeclarationLockRulesTests.cs.txt`: cover normal, mandate, independence, and direct-system treaty behavior.
- Modify `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`: add a focused `--diplomatic-war-treaty-gate` test slice.
- Create `Tests/DiplomaticWarTreatyGateSourceGuard.ps1`: verify creation, execution, existing-row recovery, and bilateral cleanup wiring.

### Task 1: Define The Live Treaty Gate With A Failing Rule Test

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/DiplomaticWarDeclarationLockRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Test: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Add the focused runner slice**

Insert near the other early argument slices in `Program.cs.txt`:

```csharp
if (args.Length == 1 &&
    args[0] == "--diplomatic-war-treaty-gate")
{
    DiplomaticWarDeclarationLockRulesTests.Run();
    Console.WriteLine("Diplomatic war treaty gate rules passed.");
    return;
}
```

- [ ] **Step 2: Replace the old blanket-lock expectations with treaty-route expectations**

Keep the existing execution timing and target-city assertions. Add these assertions to `Run()`:

```csharp
True(DiplomaticWarDeclarationLedgerRules
        .ShouldBlockWarWithActiveTreaty(
            activeTreaty: true,
            systemWar: false,
            independenceWar: false,
            declarationLocked: true),
    "a locked ordinary declaration still obeys a live treaty");
True(DiplomaticWarDeclarationLedgerRules
        .ShouldBlockWarWithActiveTreaty(
            activeTreaty: true,
            systemWar: true,
            independenceWar: false,
            declarationLocked: true),
    "a notified mandate system war still obeys a live treaty");
False(DiplomaticWarDeclarationLedgerRules
        .ShouldBlockWarWithActiveTreaty(
            activeTreaty: true,
            systemWar: false,
            independenceWar: true,
            declarationLocked: true),
    "an independence declaration retains its treaty exemption");
False(DiplomaticWarDeclarationLedgerRules
        .ShouldBlockWarWithActiveTreaty(
            activeTreaty: true,
            systemWar: true,
            independenceWar: false,
            declarationLocked: false),
    "a direct internal system war retains its treaty exemption");
False(DiplomaticWarDeclarationLedgerRules
        .ShouldBlockWarWithActiveTreaty(
            activeTreaty: false,
            systemWar: false,
            independenceWar: false,
            declarationLocked: true),
    "an inactive treaty does not block a declaration");
```

Retain the existing `ShouldRevalidateMutableEligibility` assertions because declaration locking must still preserve the other mutable eligibility behavior.

- [ ] **Step 3: Run the focused test and verify RED**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --diplomatic-war-treaty-gate
```

Expected: compilation fails because `ShouldBlockWarWithActiveTreaty` does not exist.

- [ ] **Step 4: Commit only the failing test**

```powershell
git add Tests/AncientWarfare3.Rules.Tests/DiplomaticWarDeclarationLockRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "test: cover live treaty gate for declarations"
```

### Task 2: Enforce The Gate At Declaration Issue And War Start

**Files:**
- Modify: `Code/core/lineage/DiplomaticWarDeclarationLedgerRules.cs`
- Modify: `Code/core/lineage/DiplomaticWarDeclarationService.cs:178`
- Modify: `Code/core/lineage/WarDecisionService.cs:332`
- Test: `Tests/AncientWarfare3.Rules.Tests/DiplomaticWarDeclarationLockRulesTests.cs.txt`

- [ ] **Step 1: Add the pure route rule**

Add to `DiplomaticWarDeclarationLedgerRules`:

```csharp
public static bool ShouldBlockWarWithActiveTreaty(
    bool activeTreaty, bool systemWar, bool independenceWar,
    bool declarationLocked)
{
    if (!activeTreaty || independenceWar) return false;
    return !systemWar || declarationLocked;
}
```

Here `declarationLocked: true` identifies an issued diplomatic declaration. A direct `TryStartSystemWar` call uses `declarationLocked: false`, preserving internal-war behavior.

- [ ] **Step 2: Block mandate declarations at issue time**

In `DiplomaticWarDeclarationService.CanIssue`, after participant validation and before the pending-pair check, add:

```csharp
bool independenceWar = pWarType == "independence_war";
bool activeTreaty = DiplomacyProposalService.HasActiveWarBlocker(
    pAttacker, pDefender);
if (DiplomaticWarDeclarationLedgerRules
        .ShouldBlockWarWithActiveTreaty(activeTreaty,
            systemWar: IsSystemGoal(pGoalType), independenceWar,
            declarationLocked: true))
{
    pFailureReason = "active_war_blocker";
    return false;
}
```

This explicit declaration gate runs before `CanQueueWarPair`, whose `pSystemWar` behavior is retained for non-treaty system eligibility.

- [ ] **Step 3: Make treaty validation independent of mutable eligibility at execution time**

In `WarDecisionService.StartWar`, replace the treaty check guarded by `revalidateMutableEligibility` with:

```csharp
bool independenceWar = type == "independence_war";
bool activeTreaty = DiplomacyProposalService.HasActiveWarBlocker(
    pAttacker, pDefender);
if (DiplomaticWarDeclarationLedgerRules
        .ShouldBlockWarWithActiveTreaty(activeTreaty, pSystemWar,
            independenceWar, pCasusBelliLocked))
{
    pFailureReason = "active_war_blocker";
    return null;
}
```

Do not remove `revalidateMutableEligibility` from mandate-phase, vassal, or alliance checks. Do not change the casus-belli and no-CB lock checks.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run:

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --diplomatic-war-treaty-gate
```

Expected: `Diplomatic war treaty gate rules passed.` and exit code 0.

- [ ] **Step 5: Commit the live gate implementation**

```powershell
git add Code/core/lineage/DiplomaticWarDeclarationLedgerRules.cs Code/core/lineage/DiplomaticWarDeclarationService.cs Code/core/lineage/WarDecisionService.cs
git commit -m "fix: enforce live treaties on war declarations"
```

### Task 3: Specify Bilateral Declaration Cleanup With A Failing Source Guard

**Files:**
- Create: `Tests/DiplomaticWarTreatyGateSourceGuard.ps1`
- Test: `Code/core/lineage/DiplomacyProposalService.cs`

- [ ] **Step 1: Create the source guard**

Create `Tests/DiplomaticWarTreatyGateSourceGuard.ps1`:

```powershell
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$proposal = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'Code/core/lineage/DiplomacyProposalService.cs')
$declaration = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'Code/core/lineage/DiplomaticWarDeclarationService.cs')
$warDecision = Get-Content -Raw -LiteralPath `
    (Join-Path $root 'Code/core/lineage/WarDecisionService.cs')

foreach ($needle in @(
        'ReconcilePendingDeclarationsForActiveTreaty(pFirst, pSecond);',
        'ClearPendingForPair(pFirst, pSecond, "active_war_blocker");',
        'ClearPendingForPair(pSecond, pFirst, "active_war_blocker");')) {
    if (-not $proposal.Contains($needle)) {
        throw "missing truce declaration reconciliation: $needle"
    }
}

$existingRow = $proposal.IndexOf('if (existing.ExecuteScalar() != null)')
$insert = $proposal.IndexOf('DB.Insert(DiplomacyProposalTableItem.GetTableName()')
$notify = $proposal.IndexOf('NotifyPair(pFirst.id, pSecond.id);', $insert)
if ($existingRow -lt 0 -or $insert -lt 0 -or $notify -lt 0) {
    throw 'could not locate truce registration paths'
}
if ($proposal.IndexOf(
        'ReconcilePendingDeclarationsForActiveTreaty(pFirst, pSecond);',
        $existingRow) -gt $insert) {
    throw 'an existing authoritative truce must reconcile before insert path'
}
if ($proposal.IndexOf(
        'ReconcilePendingDeclarationsForActiveTreaty(pFirst, pSecond);',
        $notify) -lt $notify) {
    throw 'a newly inserted truce must reconcile after notification'
}
if (-not $declaration.Contains(
        'ShouldBlockWarWithActiveTreaty(activeTreaty,') -or
    -not $warDecision.Contains(
        'ShouldBlockWarWithActiveTreaty(activeTreaty, pSystemWar,')) {
    throw 'declaration issue and execution must both use the live treaty gate'
}

Write-Host 'Diplomatic war treaty gate source guard passed.'
```

- [ ] **Step 2: Run the source guard and verify RED**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests\DiplomaticWarTreatyGateSourceGuard.ps1
```

Expected: failure reporting the missing reconciliation helper or bilateral clear calls.

- [ ] **Step 3: Commit only the failing guard**

```powershell
git add Tests/DiplomaticWarTreatyGateSourceGuard.ps1
git commit -m "test: guard truce declaration reconciliation"
```

### Task 4: Reconcile Pending Declarations When A Truce Becomes Authoritative

**Files:**
- Modify: `Code/core/lineage/DiplomacyProposalService.cs:1780`
- Test: `Tests/DiplomaticWarTreatyGateSourceGuard.ps1`

- [ ] **Step 1: Add an exception-contained bilateral helper**

Add near `RegisterTrucePair`:

```csharp
private static void ReconcilePendingDeclarationsForActiveTreaty(
    Kingdom pFirst, Kingdom pSecond)
{
    if (pFirst?.data == null || pSecond?.data == null ||
        pFirst == pSecond) return;
    try
    {
        DiplomaticWarDeclarationService.ClearPendingForPair(
            pFirst, pSecond, "active_war_blocker");
        DiplomaticWarDeclarationService.ClearPendingForPair(
            pSecond, pFirst, "active_war_blocker");
    }
    catch (Exception exception)
    {
        ModClass.LogWarning(
            "Diplomacy truce declaration reconciliation failed: " +
            exception.Message);
    }
}
```

- [ ] **Step 2: Reconcile when an adequate accepted row already exists**

Replace the early return in `RegisterTrucePair` with:

```csharp
if (existing.ExecuteScalar() != null)
{
    ReconcilePendingDeclarationsForActiveTreaty(pFirst, pSecond);
    return true;
}
```

- [ ] **Step 3: Reconcile after a new truce is inserted**

Immediately after `NotifyPair(pFirst.id, pSecond.id);`, add:

```csharp
ReconcilePendingDeclarationsForActiveTreaty(pFirst, pSecond);
```

The helper catches its own failures so an already inserted treaty remains successful and the execution-time gate remains authoritative.

- [ ] **Step 4: Run the source guard and verify GREEN**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests\DiplomaticWarTreatyGateSourceGuard.ps1
```

Expected: `Diplomatic war treaty gate source guard passed.` and exit code 0.

- [ ] **Step 5: Run the existing war/peace integration guard**

Run:

```powershell
powershell -ExecutionPolicy Bypass -File Tests\WarPeaceIntegrationTests.ps1
```

Expected: exit code 0 with no thrown guard failure.

- [ ] **Step 6: Commit truce reconciliation**

```powershell
git add Code/core/lineage/DiplomacyProposalService.cs
git commit -m "fix: cancel declarations when truces activate"
```

### Task 5: Full Verification And Review

**Files:**
- Verify: all files changed in Tasks 1-4

- [ ] **Step 1: Run focused treaty tests**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release -- --diplomatic-war-treaty-gate
powershell -ExecutionPolicy Bypass -File Tests\DiplomaticWarTreatyGateSourceGuard.ps1
powershell -ExecutionPolicy Bypass -File Tests\WarPeaceIntegrationTests.ps1
```

Expected: all commands exit 0.

- [ ] **Step 2: Run the complete rules executable**

```powershell
dotnet run --project Tests\AncientWarfare3.Rules.Tests\AncientWarfare3.Rules.Tests.csproj -c Release
```

Expected: exit code 0 and the final all-rules success message.

- [ ] **Step 3: Build the mod target**

```powershell
dotnet build AncientWarfare3.csproj -c Release -f net48 --no-restore
```

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Inspect the final diff**

```powershell
git diff --check master...HEAD
git diff --stat master...HEAD
git status --short
```

Expected: no whitespace errors; only the planned declaration, treaty, test, and plan files appear; worktree is clean after commits.

- [ ] **Step 5: Request code review**

Use `superpowers:requesting-code-review` against `master...HEAD`. Address any correctness findings, rerun the affected focused test, then repeat the full verification commands before integration or deployment.
