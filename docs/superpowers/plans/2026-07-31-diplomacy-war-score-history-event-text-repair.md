# Diplomacy War Score and History Event Text Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore war-peace negotiation for legacy saves with nullable war-score fields and render royal-marriage history with a localized label and complete sentence.

**Architecture:** Keep save compatibility inside `WarScorePersistence`, where old rows are migrated and nullable numeric reads are tolerated. Keep history compatibility in shared display rules so both synchronous and asynchronous history readers receive the same repaired text without replacing `HistoryListWindow`. Surface snapshot failure through the existing stable `war_score_unavailable` reason.

**Tech Stack:** C#/.NET 9 rules tests, System.Data.SQLite, WorldBox mod runtime, CSV localization.

---

### Task 1: Legacy War Score Compatibility

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/WarScoreBudgetServiceTests.cs.txt`
- Modify: `Code/core/lineage/WarScorePersistence.cs`

- [ ] **Step 1: Write the failing legacy-null test**

Create a legacy `WarScoreSnapshot` table whose reserve-exhaustion columns allow `NULL`, insert an active row with both values null, construct `WarScoreService`, and assert `TryGetSnapshot` succeeds with both values normalized to `0`.

- [ ] **Step 2: Run the focused test and verify RED**

Run: `dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj -c Release -- --war-score-budget-slice`

Expected: FAIL while reading `DBNull` or because the migrated row cannot be loaded.

- [ ] **Step 3: Implement the minimum compatibility repair**

After ensuring the two columns exist, run:

```csharp
Execute("UPDATE " + SnapshotTable +
        " SET ATTACKER_RESERVE_EXHAUSTION=0" +
        " WHERE ATTACKER_RESERVE_EXHAUSTION IS NULL");
Execute("UPDATE " + SnapshotTable +
        " SET DEFENDER_RESERVE_EXHAUSTION=0" +
        " WHERE DEFENDER_RESERVE_EXHAUSTION IS NULL");
```

Read compatibility integers with a helper that returns the supplied fallback for `null`/`DBNull`, and use it for migration-era numeric columns.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the same `--war-score-budget-slice`; expected PASS.

### Task 2: Royal Marriage Label and Complete History Text

**Files:**
- Modify: `Tests/AncientWarfare3.Rules.Tests/HistoryLocalizationRulesTests.cs.txt`
- Modify: `Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj`
- Modify: `Tests/AncientWarfare3.Rules.Tests/Program.cs.txt`
- Modify: `Code/core/lineage/WarDisplayLabelRules.cs`
- Modify: `Code/core/lineage/HistoryLocalizationRules.cs`
- Modify: `Code/core/lineage/HistoryQuery.cs`
- Modify: `Code/core/lineage/DiplomaticMarriageService.cs`
- Modify: `Locales/aw3_diplomacy.csv`

- [ ] **Step 1: Write failing display-rule tests**

Assert `EventLabel("royal_marriage", "cz")` returns `宗室婚盟`; assert legacy `甲与乙` becomes `甲与乙缔结婚盟`; assert already complete text is unchanged; assert English text is unchanged because its middle fragment already forms a sentence.

- [ ] **Step 2: Run the focused test and verify RED**

Add `--history-marriage-slice` to the test program and run it. Expected: FAIL because the event label leaks `royal_marriage` and the suffix normalizer does not exist.

- [ ] **Step 3: Implement shared localized normalization**

Map `royal_marriage` to `aw_hist_event_royal_marriage`. Add `aw_hist_royal_marriage_suffix` with simplified Chinese `缔结婚盟`, empty English, and traditional Chinese `締結婚盟`. Add an idempotent history-content normalizer in `WarDisplayLabelRules`; call it from all `HistoryQuery` finalization paths for both `content` and `content_rich`.

- [ ] **Step 4: Write complete new records**

Append `aw_hist_royal_marriage_suffix` in `DiplomaticMarriageService.RecordHistory`, so new rows are complete at write time while old rows are repaired only for display.

- [ ] **Step 5: Run the focused test and verify GREEN**

Run `--history-marriage-slice`; expected PASS.

### Task 3: Explicit Negotiation Failure and Verification

**Files:**
- Create: `Tests/WarPeaceNegotiationFailureReasonSourceGuard.ps1`
- Modify: `Code/ui/windows/WarPeaceNegotiationController.cs`

- [ ] **Step 1: Write and run the failing source guard**

Require the failed `WarScoreService.TryGetSnapshot` branch to assign `pReason = "war_score_unavailable"` before returning false. Run the script and verify it fails against the current controller.

- [ ] **Step 2: Implement the explicit reason and verify GREEN**

Set `pReason` in that branch, rerun the guard, and expect PASS.

- [ ] **Step 3: Run regression verification**

Run both focused slices, the source guard, and `dotnet build AncientWarfare3.csproj -c Release`. Expected: zero errors and zero warnings.

- [ ] **Step 4: Compare and deploy only safe files**

Compare every changed production/localization file against the installed mod. Merge only the scoped hunks, do not replace `HistoryListWindow.cs`, and verify deployed SHA-256 hashes.
