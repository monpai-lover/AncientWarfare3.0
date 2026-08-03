# War Declaration Failure Reasons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve and display the real reason a war declaration or individual war goal is unavailable from initial UI rendering through authoritative submission.

**Architecture:** Add a pure availability aggregation rule used by both declaration windows, preserve the pair-level failure reason through goal validation, and return the same stable reason from the authoritative command handler. Blocked goals remain visible as disabled rows with localized tooltips.

**Tech Stack:** C#, NCMS/Unity UI, AW3 multiplayer command facade, CSV localization, `AncientWarfare3.Rules.Tests` console test harness.

---

### Task 1: Preserve pair-level failure reasons

**Files:**
- Modify: `Code/core/lineage/WarDecisionQueueRules.cs`
- Modify: `Code/core/lineage/DiplomaticWarDeclarationService.cs`
- Test: `Tests/AncientWarfare3.Rules.Tests/DiplomaticWarAvailabilityRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`

- [ ] **Step 1: Add the failing reason-preservation test**

Create `DiplomaticWarAvailabilityRulesTests.cs.txt` with a `Run()` method that calls `WarDecisionQueueRules.CanQueueGoal` for `invalid`, `non_aggression_pact`, `same_alliance`, `vassal_external_war_blocked`, and `already_at_war`, passing each value as `pBasicFailureReason`, then asserts the returned reason is unchanged.

```csharp
foreach (string expected in new[]
         {
             "invalid", "non_aggression_pact", "same_alliance",
             "vassal_external_war_blocked", "already_at_war"
         })
{
    bool allowed = WarDecisionQueueRules.CanQueueGoal(
        pGoalType: "take_core_city", pBasicAllowed: false,
        pBasicFailureReason: expected, pHasNormalCb: false,
        pCanForceNoCb: false, pHasCoreTarget: true,
        pHasClaimTarget: false, pCanForceVassal: false,
        pCanForceTributary: false, pIsIndependenceTarget: false,
        pHasRestorationTarget: false, pCanReunifySuccession: false,
        out string actual);
    Equal(false, allowed, "blocked pair remains blocked");
    Equal(expected, actual, "pair failure reason survives goal validation");
}
```

Link the test file in the test csproj, add `--diplomatic-war-availability-slice` to `Program.cs.txt`, and call `DiplomaticWarAvailabilityRulesTests.Run()` from both that slice and the default suite.

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --diplomatic-war-availability-slice
```

Expected: compile failure because `CanQueueGoal` does not accept `pBasicFailureReason`.

- [ ] **Step 3: Thread the original pair reason through goal validation**

Add `string pBasicFailureReason` to the full `CanQueueGoal` overload and replace the `basic_blocked` assignment with:

```csharp
if (!pBasicAllowed)
{
    pReason = string.IsNullOrWhiteSpace(pBasicFailureReason)
        ? "invalid"
        : pBasicFailureReason;
    return false;
}
```

Keep the compatibility overload and pass `"invalid"`. In `DiplomaticWarDeclarationService.CanQueueCurrentGoal`, capture `pairFailureReason` from `CanQueueWarPair` and pass it as `pBasicFailureReason`.

- [ ] **Step 4: Run the slice and verify GREEN**

Run the slice command from Step 2.

Expected: `Diplomatic war availability rules passed.`

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/WarDecisionQueueRules.cs Code/core/lineage/DiplomaticWarDeclarationService.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "fix: preserve war declaration failure reasons"
```

### Task 2: Add pure availability aggregation and selection rules

**Files:**
- Create: `Code/core/lineage/DiplomaticWarAvailabilityRules.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DiplomaticWarAvailabilityRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`

- [ ] **Step 1: Add failing aggregation and selection tests**

Define test inputs as `DiplomaticWarAvailabilityCandidate` values and assert:

```csharp
Equal("war_preparation",
    DiplomaticWarAvailabilityRules.Resolve(true, new[]
    {
        new DiplomaticWarAvailabilityCandidate(true, "")
    }).FailureReason,
    "pending declaration has highest priority");
Equal("no_war_reasons",
    DiplomaticWarAvailabilityRules.Resolve(false,
        Array.Empty<DiplomaticWarAvailabilityCandidate>()).FailureReason,
    "empty candidate set has a stable reason");
Equal(true,
    DiplomaticWarAvailabilityRules.Resolve(false, new[]
    {
        new DiplomaticWarAvailabilityCandidate(false, "missing_core_target"),
        new DiplomaticWarAvailabilityCandidate(true, "")
    }).Available,
    "one available goal enables the pair");
Equal(1, DiplomaticWarAvailabilityRules.ResolveSelectedGoalIndex(
    new[]
    {
        new DiplomaticWarAvailabilityCandidate(false, "missing_core_target"),
        new DiplomaticWarAvailabilityCandidate(true, "")
    }, 0), "selection moves to the first available goal");
```

Also assert all-blocked returns the first non-empty stable reason and no available row returns `-1`.

- [ ] **Step 2: Run the slice and verify RED**

Expected: compile failure because the availability types do not exist.

- [ ] **Step 3: Implement the pure rule types**

Create immutable candidate/result structs and implement `Resolve` with priority `war_preparation` -> `no_war_reasons` -> any available -> first concrete blocked reason -> `unavailable`. Implement `ResolveSelectedGoalIndex` so a blocked preferred index can never be retained.

- [ ] **Step 4: Run the slice and full rules suite**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --diplomatic-war-availability-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
```

Expected: both pass.

- [ ] **Step 5: Commit**

```powershell
git add Code/core/lineage/DiplomaticWarAvailabilityRules.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "feat: add shared war availability rules"
```

### Task 3: Use shared availability in both declaration windows

**Files:**
- Modify: `Code/core/lineage/DiplomaticWarDeclarationService.cs`
- Modify: `Code/ui/windows/DiplomacyConversationWindow.cs`
- Modify: `Code/ui/windows/DiplomaticWarDeclarationWindow.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DiplomaticWarAvailabilityRulesTests.cs.txt`

- [ ] **Step 1: Add failing source-guard tests**

Read both UI source files and assert:

```csharp
SourceContains("DiplomacyConversationWindow.cs",
    "ResolvePairAvailability");
SourceDoesNotContain("DiplomaticWarDeclarationWindow.cs",
    "options.RemoveAll");
SourceContains("DiplomaticWarDeclarationWindow.cs",
    "FailureReason");
SourceContains("DiplomaticWarDeclarationWindow.cs",
    "pRow.Button.interactable = !_commandPending && pAvailability.Available");
```

- [ ] **Step 2: Run the slice and verify RED**

Expected: source guards fail on the existing Count-only button check and `RemoveAll` filtering.

- [ ] **Step 3: Add service-level availability projection**

Add a service method that builds target options once, calls `CanIssue` for each option, and returns the option together with `Available` and `FailureReason`. Add `ResolvePairAvailability` that invokes the pure aggregation rule and includes `HasPendingForPair`.

- [ ] **Step 4: Update the diplomacy action row**

Replace `BuildTargetOptions(...).Count > 0` with `ResolvePairAvailability`. Set `reason` to the returned failure reason so the existing `TipButton` calls `ProposalFailure(reason)`.

- [ ] **Step 5: Keep blocked goal rows visible**

Remove `options.RemoveAll`. Bind each row with its availability record, disable blocked row buttons, append `ProposalFailure(FailureReason)` to the row tooltip, and use `ResolveSelectedGoalIndex` to choose only available rows. Add a `TipButton` to the final declaration button and bind the aggregate/selected reason.

- [ ] **Step 6: Run the slice and full rules suite**

Expected: source guards and full suite pass.

- [ ] **Step 7: Commit**

```powershell
git add Code/core/lineage/DiplomaticWarDeclarationService.cs Code/ui/windows/DiplomacyConversationWindow.cs Code/ui/windows/DiplomaticWarDeclarationWindow.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "fix: show blocked war goals and reasons"
```

### Task 4: Return authoritative submission reasons

**Files:**
- Modify: `Code/core/lineage/DiplomaticWarDeclarationService.cs`
- Modify: `Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs`
- Modify: `Code/ui/windows/DiplomaticWarDeclarationWindow.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DiplomaticWarAvailabilityRulesTests.cs.txt`

- [ ] **Step 1: Add failing authority source guards**

Assert that `CanIssue` checks pending and returns `war_preparation`, `TryIssue` exposes `out string pFailureReason`, the handler calls `Rejected(failureReason)`, and the window calls `DiplomacyConversationWindow.ProposalFailure(result.MessageKey)`.

- [ ] **Step 2: Run the slice and verify RED**

Expected: current handler contains `Rejected("unavailable")` and the window hard-codes the generic toast.

- [ ] **Step 3: Implement reason-bearing submission**

Add `TryIssue` overloads for raw fields and `WarTargetOption`. Validate participants, pending state, goal availability, and ledger append while assigning stable reasons. Keep existing bool `Issue` overloads as compatibility wrappers that call `TryIssue(..., out _)`.

- [ ] **Step 4: Propagate the host result to UI**

Use `TryIssue` in `AW3DiplomacyCommandHandler.DeclareWar`, pass its reason to `Rejected`, and display `ProposalFailure(result.MessageKey)` in `DiplomaticWarDeclarationWindow.Declare`.

- [ ] **Step 5: Run the slice and full rules suite**

Expected: both pass.

- [ ] **Step 6: Commit**

```powershell
git add Code/core/lineage/DiplomaticWarDeclarationService.cs Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs Code/ui/windows/DiplomaticWarDeclarationWindow.cs Tests/AncientWarfare3.Rules.Tests
git commit -m "fix: return authoritative war rejection reasons"
```

### Task 5: Localize every known war failure reason

**Files:**
- Modify: `Locales/aw3_diplomacy.csv`
- Modify: `Code/ui/windows/DiplomacyConversationWindow.cs`
- Modify: `Tests/AncientWarfare3.Rules.Tests/DiplomaticWarAvailabilityRulesTests.cs.txt`

- [ ] **Step 1: Add a failing localization completeness test**

Parse `aw3_diplomacy.csv` and require non-empty `cz`, `en`, and `ch` values for every goal reason from `WarDecisionQueueRules`, including `vassal_external_war_blocked`, mandate/core/claim/vassal/tributary/independence/restoration/reunification/zhulu/no-CB failures.

- [ ] **Step 2: Run the slice and verify RED**

Expected: first failure is a missing `aw_diplomacy_failure_vassal_external_war_blocked` row.

- [ ] **Step 3: Add three-language rows and mappings**

Reuse existing keys for `already_at_war`, `same_alliance`, `non_aggression_pact`, pending, unknown, and no reasons. Add explicit `ProposalFailure` mappings and CSV rows for every missing reason. Do not add a `basic_blocked` translation.

- [ ] **Step 4: Run the slice and full rules suite**

Expected: every known reason resolves without raw reason text.

- [ ] **Step 5: Commit**

```powershell
git add Locales/aw3_diplomacy.csv Code/ui/windows/DiplomacyConversationWindow.cs Tests/AncientWarfare3.Rules.Tests/DiplomaticWarAvailabilityRulesTests.cs.txt
git commit -m "fix: localize war declaration blockers"
```

### Task 6: Final verification

**Files:**
- Verify only

- [ ] **Step 1: Run targeted and full rules tests**

```powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -- --diplomatic-war-availability-slice
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
git diff --check HEAD~5..HEAD
```

Expected: both test commands pass and `git diff --check` emits no errors.

- [ ] **Step 2: Verify no forbidden fallback remains**

```powershell
rg -n 'basic_blocked|Rejected\("unavailable"\)|options\.RemoveAll' Code/core/lineage Code/core/multiplayer/commands/AW3DiplomacyCommandHandler.cs Code/ui/windows
```

Expected: no declaration-path match remains.
