# Historical Figure Card Recycle Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (\`- [ ]\`) syntax for tracking.

**Goal:** Move historical figure card recycling into a dedicated window where selecting one card filters the left list to that card's rarity, while preserving existing inventory transactions and dynasty-crate weighting.

**Architecture:** Add a small pure selection-state rules class for quality locking, owned-count limits, and visible-card filtering. Add a dedicated Unity window that owns only transient UI selection and delegates validation and persistence to \`HistoricalFigureCardRecycleRules\` and \`HistoricalFigureCardCollectionStore\`. Change the existing draw/inventory window to expose an entry point and remove its embedded recycle controls.

**Tech Stack:** C#, Unity UI (\`AbstractWindow\`, \`ScrollRect\`, \`Button\`, \`Image\`, \`Text\`), Newtonsoft.Json-backed collection store, the existing AW3 localization CSV, and the console-style rules test executable.

---

## File map

- Create \`Code/content/figures/HistoricalFigureCardRecycleSelectionRules.cs\`: pure selection state and filtering rules; no Unity or store writes.
- Create \`Code/ui/windows/HistoricalFigureRecycleWindow.cs\`: dedicated window, card list, input slots, preview, buttons, and service orchestration.
- Create \`Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRecycleSelectionRulesTests.cs.txt\`: tests for the new pure selection behavior.
- Modify \`Code/ui/AW_LineageWindowIds.cs\`: add a unique window ID.
- Modify \`Code/ui/windows/HistoricalFigureDrawWindow.cs\`: replace embedded recycle mode with an entry button and navigation to the dedicated window.
- Modify \`Code/ui/items/HistoricalFigureCardListItem.cs\`: expose an optional owned-count label while preserving existing callers.
- Modify \`Locales/aw3_historical_cards.csv\`: add localized text for the recycle window, slots, preview, reset, errors, and result details.
- Modify \`Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj\`: compile the new pure rules source and test source.
- Modify \`Tests/AncientWarfare3.Rules.Tests/Program.cs.txt\`: run the new selection-rule test suite.

### Task 1: Add the pure selection-state rules

**Files:**
- Create: \`Code/content/figures/HistoricalFigureCardRecycleSelectionRules.cs\`
- Test: \`Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRecycleSelectionRulesTests.cs.txt\`
- Modify: \`Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj\`
- Modify: \`Tests/AncientWarfare3.Rules.Tests/Program.cs.txt\`

- [ ] **Step 1: Write failing tests for initial filtering and first-click locking.**

Construct blue, purple, red, and gold definitions and an owned-count dictionary. Add these assertions:

\`\`\`csharp
IReadOnlyList<HistoricalFigureCardDefinition> initial =
    HistoricalFigureCardRecycleSelectionRules.FilterVisible(
        cards, owned, null);
Equal("blue,purple,red", Ids(initial),
    "initial list excludes gold and cards with zero ownership");

var state = new HistoricalFigureCardRecycleSelectionState();
True(HistoricalFigureCardRecycleSelectionRules.TryAdd(
    state, blue, owned, out string error), error);
Equal(HistoricalFigureCardRarity.Blue, state.LockedRarity,
    "first selected card locks its rarity");
Equal("blue", Ids(HistoricalFigureCardRecycleSelectionRules.FilterVisible(
    cards, owned, state.LockedRarity)),
    "locked list contains only the selected rarity");
\`\`\`

- [ ] **Step 2: Add failing tests for duplicate quantities, mixed qualities, reset, and red count.**

\`\`\`csharp
True(HistoricalFigureCardRecycleSelectionRules.TryAdd(
    state, blue, owned, out _), "same card can fill another slot");
False(HistoricalFigureCardRecycleSelectionRules.TryAdd(
    state, purple, owned, out error), "mixed rarity is rejected");
Equal("recycle_same_rarity", error, "mixed rarity uses localized error key");

owned["blue"] = 2;
False(HistoricalFigureCardRecycleSelectionRules.TryAdd(
    state, blue, owned, out error), "owned count limits repeated selection");

HistoricalFigureCardRecycleSelectionRules.RemoveOne(state, "blue");
True(state.HasInputs, "removing one slot keeps the lock while inputs remain");
HistoricalFigureCardRecycleSelectionRules.Clear(state);
Null(state.LockedRarity, "clear unlocks the rarity");

Equal(5, HistoricalFigureCardRecycleSelectionRules.RequiredCount(
    HistoricalFigureCardRarity.Red), "red uses five inputs");
Equal(10, HistoricalFigureCardRecycleSelectionRules.RequiredCount(
    HistoricalFigureCardRarity.Blue), "blue uses ten inputs");
\`\`\`

- [ ] **Step 3: Run the focused rules executable and verify the new tests fail.**

Run:

\`\`\`powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
\`\`\`

Expected: the new class or methods are missing. Record unrelated pre-existing
missing-type errors separately from the new failure.

- [ ] **Step 4: Implement the minimal pure state API.**

Implement a state object and these exact operations:

\`\`\`csharp
public sealed class HistoricalFigureCardRecycleSelectionState
{
    public HistoricalFigureCardRarity LockedRarity { get; internal set; }
    public IReadOnlyList<string> SlotCardIds { get; internal set; }
    public bool HasInputs { get; }
}

public static IReadOnlyList<HistoricalFigureCardDefinition> FilterVisible(
    IEnumerable<HistoricalFigureCardDefinition> cards,
    IReadOnlyDictionary<string, int> owned,
    HistoricalFigureCardRarity lockedRarity);
public static bool TryAdd(
    HistoricalFigureCardRecycleSelectionState state,
    HistoricalFigureCardDefinition card,
    IReadOnlyDictionary<string, int> owned,
    out string errorKey);
public static void RemoveOne(
    HistoricalFigureCardRecycleSelectionState state, string cardId);
public static void Clear(HistoricalFigureCardRecycleSelectionState state);
public static int RequiredCount(HistoricalFigureCardRarity rarity);
\`\`\`

\`FilterVisible\` excludes null definitions, null rarity, gold rarity, and
owned counts below one. \`TryAdd\` rejects gold, a different locked rarity, an
owned-count overflow, and additions after the required count. It preserves slot
order. \`RemoveOne\` removes only the last matching slot and keeps the lock while
other slots remain; removing the final slot clears the lock.

- [ ] **Step 5: Register the source and test, then rerun.**

Add both source files to the rules test project, call
\`HistoricalFigureCardRecycleSelectionRulesTests.Run()\` from \`Program.cs.txt\`,
and rerun Step 3. Expected: the new selection tests pass; unrelated baseline
errors remain identifiable by their existing type names.

- [ ] **Step 6: Commit the isolated selection rules.**

\`\`\`powershell
git add -- Code/content/figures/HistoricalFigureCardRecycleSelectionRules.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRecycleSelectionRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "Add historical card recycle selection rules"
\`\`\`

### Task 2: Add the dedicated recycle window shell

**Files:**
- Create: \`Code/ui/windows/HistoricalFigureRecycleWindow.cs\`
- Modify: \`Code/ui/AW_LineageWindowIds.cs\`

- [ ] **Step 1: Add the dedicated window ID and lifecycle.**

Add \`HISTORICAL_FIGURE_CARD_RECYCLE = "aw_historical_figure_card_recycle"\`.
The new window exposes \`public static void Open()\`, uses
\`CreateAndInit(AW_LineageWindowIds.HISTORICAL_FIGURE_CARD_RECYCLE)\`, initializes
the runtime service, clears transient selection, loads the store, and refreshes.
Closing or returning clears only transient selection.

- [ ] **Step 2: Build fixed left and right UI containers.**

Use the existing window chrome and sizing conventions. Create a left
\`ScrollRect\` with a permanent vertical scrollbar and a fixed-size right panel.
Create exactly ten visual input slots; for red selection only the first five are
enabled. Add fields for locked rarity, selected/required count, next rarity,
source weights, and output preview. Keep card dimensions fixed.

- [ ] **Step 3: Bind the left list to selection-state rules.**

On refresh, pass \`Store.OwnedCounts\` and the state's \`LockedRarity\` to
\`FilterVisible\`. Each card shows portrait, name, rarity color, historical
kingdom, and owned count. A click calls \`TryAdd\`, refreshes immediately, and
shows the returned error key through the existing localization helper when
rejected.

- [ ] **Step 4: Bind slot removal, reset, and navigation.**

Each occupied slot removes its own card ID and refreshes. Clear and reset remove
all slots, unlock the rarity, and restore the complete eligible list. The
inventory button returns to \`HistoricalFigureDrawWindow.OpenInventory()\` without
changing the collection.

- [ ] **Step 5: Build submit preflight.**

Disable submit unless selected count equals
\`RequiredCount(state.LockedRarity)\`. Before output selection, expand ordered
slot IDs and run \`HistoricalFigureCardRecycleRules.TryCreatePlan\`. On failure,
keep state unchanged and show the localized error key.

- [ ] **Step 6: Commit the window shell.**

\`\`\`powershell
git add -- Code/ui/windows/HistoricalFigureRecycleWindow.cs Code/ui/AW_LineageWindowIds.cs
git commit -m "Add standalone historical card recycle window"
\`\`\`

### Task 3: Connect transaction and result behavior

**Files:**
- Modify: \`Code/ui/windows/HistoricalFigureRecycleWindow.cs\`
- Modify: \`Code/ui/items/HistoricalFigureCardListItem.cs\`

- [ ] **Step 1: Reuse existing source-weight and output selection flow.**

Use this existing service sequence:

\`\`\`csharp
IReadOnlyDictionary<string, int> sources =
    Store.GetRecycleSourceCounts(inputIds);
string outputCrateId = HistoricalFigureCardRecycleRules.SelectWeightedCrate(
    sources, UnityEngine.Random.Range(0, int.MaxValue));
HistoricalFigureCardRarity outputRarity =
    HistoricalFigureCardRecycleRules.NextRarity(inputRarity);
IReadOnlyList<HistoricalFigureCardDefinition> pool =
    outputRarity.Equals(HistoricalFigureCardRarity.Gold)
        ? HistoricalFigureCardCatalog.All
        : HistoricalFigureCardCatalog.GetCards(outputCrateId);
\`\`\`

Filter the pool by exact output rarity, choose one eligible card, and call
\`Store.TryRecycle(inputIds, output.CardId, output.Rarity.Id, outputCrateId,
Guid.NewGuid().ToString("N"))\` once. Never call \`TryConsume\` for inputs.

- [ ] **Step 2: Show the successful result with source metadata.**

After success, clear selection, reload the store, and show output portrait, name,
biography, rarity, historical kingdom, and selected dynasty-crate source. Play
the existing rarity reveal sound. A failed transaction keeps all slots and does
not show a success result.

- [ ] **Step 3: Add owned-count rendering without breaking existing cards.**

Extend \`HistoricalFigureCardListItem\` with an owned-count \`Text\` child and an
optional setter or optional \`SetCard\` argument. Existing draw-track and crate
callers render without an owned label; inventory and recycle callers pass a
localized count string.

- [ ] **Step 4: Commit transaction and result behavior.**

\`\`\`powershell
git add -- Code/ui/windows/HistoricalFigureRecycleWindow.cs Code/ui/items/HistoricalFigureCardListItem.cs
git commit -m "Connect recycle window to card collection transactions"
\`\`\`

### Task 4: Replace embedded inventory recycle mode

**Files:**
- Modify: \`Code/ui/windows/HistoricalFigureDrawWindow.cs\`

- [ ] **Step 1: Add the inventory entry button.**

Add a localized recycle button beside inventory controls. Its action calls
\`HistoricalFigureRecycleWindow.Open()\`. It is visible only while inventory is
idle and is not itself a selection mode.

- [ ] **Step 2: Remove old recycle state and handlers.**

Remove \`_recycleMode\`, \`_recycleSelection\`, old submit/cancel buttons,
\`ToggleRecycleMode\`, \`CancelRecycle\`, \`ToggleRecycleCard\`,
\`SubmitRecycle\`, \`BuildRecycleInputIds\`, \`SelectedRecycleRarity\`, and the
old recycle-specific layout branches. Inventory card clicks must again open card
details. Keep sorting, browsing, deployment, and draw transitions unchanged.

- [ ] **Step 3: Update reset and navigation guards.**

Ensure \`ResetTransientState\`, \`OpenInventory\`, \`BackToCrates\), and collection
card selection do not retain recycle state. The dedicated window owns transient
state; switching back cannot mutate or clear the collection.

- [ ] **Step 4: Run existing window regression checks.**

\`\`\`powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WindowUiRegressionTests.ps1
\`\`\`

Expected: existing window/layout assertions pass and no old recycle control is
required.

- [ ] **Step 5: Commit the entry migration.**

\`\`\`powershell
git add -- Code/ui/windows/HistoricalFigureDrawWindow.cs
git commit -m "Route inventory recycling to dedicated window"
\`\`\`

### Task 5: Add localization and source guards

**Files:**
- Modify: \`Locales/aw3_historical_cards.csv\`
- Create: \`Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRecycleWindowSourceGuardTests.cs.txt\`
- Modify: \`Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj\`
- Modify: \`Tests/AncientWarfare3.Rules.Tests/Program.cs.txt\`

- [ ] **Step 1: Add all localization keys.**

Add Chinese, English, and Traditional Chinese rows for the window title, inventory
entry, selected/required counts, next rarity, source weights, reset, clear,
submit, return, empty state, mixed-quality error, gold error, insufficient
quantity, incomplete selection, missing output, persistence failure, and output
source. Preserve the existing CSV column order and key prefix.

- [ ] **Step 2: Add source guards for ownership boundaries.**

The guard reads both window files and asserts:

\`\`\`csharp
True(recycleWindow.Contains("TryRecycle"),
    "dedicated window owns the recycle transaction");
False(drawWindow.Contains("TryRecycle"),
    "draw window no longer commits recycling");
False(drawWindow.Contains("ToggleRecycleCard"),
    "inventory is not a recycle selection mode");
True(recycleWindow.Contains("FilterVisible"),
    "dedicated window applies the quality filter");
True(drawWindow.Contains("HistoricalFigureRecycleWindow.Open"),
    "inventory exposes the dedicated window");
\`\`\`

- [ ] **Step 3: Register and run the guard.**

Register the guard source and \`Run()\` method, then run the rules executable.
Expected: new source guards pass and unrelated baseline failures remain separate.

- [ ] **Step 4: Commit localization and guards.**

\`\`\`powershell
git add -- Locales/aw3_historical_cards.csv Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRecycleWindowSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "Localize and guard standalone card recycling"
\`\`\`

### Task 6: Build and manual acceptance

**Files:**
- Verify all files from Tasks 1-5.

- [ ] **Step 1: Run source checks and build.**

\`\`\`powershell
git diff --check
dotnet build AncientWarfare3.csproj --no-restore
\`\`\`

Expected: the mod project builds successfully and no whitespace errors are
reported. If the rules project still has known unrelated missing-type failures,
record exact type names and verify the new tests and guards separately.

- [ ] **Step 2: Verify initial and locked lists in game.**

Open the inventory and recycle window. Verify all owned blue, purple, pink, and
red cards are initially visible while gold cards are absent. Click one blue card
and verify every non-blue card disappears immediately. Add the same blue card
again only when its owned count allows it.

- [ ] **Step 3: Verify reset and slot behavior in game.**

Remove one occupied slot and verify the quality lock remains while another slot is
occupied. Clear all slots and verify the complete eligible list returns. Use
reset and verify the same result. Select red cards and verify five slots are
enabled; select blue cards and verify ten are required.

- [ ] **Step 4: Verify transaction and failure behavior in game.**

Submit one valid trade-up and verify input quantities decrease, one output card
appears with its dynasty-crate source, and result details show portrait and
biography. Close or reopen before submit and verify no card is consumed. Trigger
an incomplete or stale selection and verify the localized error leaves selection
intact.

- [ ] **Step 5: Run final verification.**

\`\`\`powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WindowUiRegressionTests.ps1
git status --short --branch
\`\`\`

Expected: window regression checks pass. Feature files are committed and no
unrelated file is staged by feature commits.
