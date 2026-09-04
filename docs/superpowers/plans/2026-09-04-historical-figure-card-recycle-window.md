# Historical Figure Card Recycle Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (\`- [ ]\`) syntax for tracking.

**Goal:** Move historical figure card recycling into a dedicated window where selecting one card filters the left list to that card's rarity, and separate card crates into Monarch and Minister deployment roles without breaking existing inventory transactions or dynasty-crate weighting.

**Architecture:** Add a small pure selection-state rules class for quality locking, owned-count limits, and visible-card filtering. Add a role field and top-level crate category filter for Monarch and Minister cards. Add a dedicated Unity window that owns only transient UI selection and delegates validation and persistence to \`HistoricalFigureCardRecycleRules\` and \`HistoricalFigureCardCollectionStore\`. Change the existing draw/inventory window to expose an entry point and remove its embedded recycle controls; route deployment by card role into either kingdom founding or official-candidate registration.

**Tech Stack:** C#, Unity UI (\`AbstractWindow\`, \`ScrollRect\`, \`Button\`, \`Image\`, \`Text\`), Newtonsoft.Json-backed collection store, the existing AW3 localization CSV, and the console-style rules test executable.

---

## File map

- Create \`Code/content/figures/HistoricalFigureCardRecycleSelectionRules.cs\`: pure selection state and filtering rules; no Unity or store writes.
- Create \`Code/ui/windows/HistoricalFigureRecycleWindow.cs\`: dedicated window, card list, input slots, preview, buttons, and service orchestration.
- Create \`Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRecycleSelectionRulesTests.cs.txt\`: tests for the new pure selection behavior.
- Modify \`Code/ui/AW_LineageWindowIds.cs\`: add a unique window ID.
- Modify \`Code/ui/windows/HistoricalFigureDrawWindow.cs\`: replace embedded recycle mode with an entry button and navigation to the dedicated window.
- Modify \`Code/ui/items/HistoricalFigureCardListItem.cs\`: expose an optional owned-count label while preserving existing callers.
- Modify \`Code/core/lineage/HistoricalFigureCardDeploymentService.cs\`: branch deployment by card role.
- Modify \`Code/core/lineage/HistoricalFigureCardDeploymentRules.cs\`: validate Monarch and Minister target requirements.
- Create \`Code/core/lineage/HistoricalFigureCardRoleRules.cs\`: define role-filter and minister candidate-bonus rules.
- Modify \`Code/core/court/CivilServiceQualificationService.cs\`: allow a valid deployed minister through the qualification gate without bypassing identity safety checks.
- Modify \`Code/core/court/LocalCourtAppointmentService.cs\`: apply the minister candidate score bonus consistently in build and reposition paths.
- Modify \`Code/core/court/OfficerCandidateCatalog.cs\`: register deployed ministers in the kingdom candidate catalogue.
- Modify \`Code/core/lineage/LineageKeys.cs\`: add the stable card-role marker used by court scoring.
- Modify \`Code/content/figures/HistoricalFigureCardModels.cs\`: add the Monarch/Minister role value to card definitions.
- Modify \`Code/content/figures/HistoricalFigureCardCatalog.cs\`: assign every card to a role and expose role-filtered period pools.
- Modify \`Code/content/figures/HistoricalFigureCardCrates.cs\`: expose the two top-level crate categories while preserving period IDs.
- Modify \`Code/ui/windows/HistoricalFigureDrawWindow.cs\`: show the two crate categories and role-specific card pools.
- Modify \`Locales/aw3_historical_cards.csv\`: add localized text for the recycle window, slots, preview, reset, errors, and result details.
- Modify \`Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj\`: compile the new pure rules source and test source.
- Modify \`Tests/AncientWarfare3.Rules.Tests/Program.cs.txt\`: run the new selection-rule test suite.
- Create \`Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRoleRulesTests.cs.txt\`: test role filtering, deployment branches, and candidate-score bonus rules.

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

### Task 6: Add Monarch and Minister card roles

**Files:**
- Create: \`Code/core/lineage/HistoricalFigureCardRoleRules.cs\`
- Modify: \`Code/content/figures/HistoricalFigureCardModels.cs\`
- Modify: \`Code/content/figures/HistoricalFigureCardCatalog.cs\`
- Modify: \`Code/content/figures/HistoricalFigureCardCrates.cs\`
- Modify: \`Code/ui/windows/HistoricalFigureDrawWindow.cs\`
- Create: \`Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRoleRulesTests.cs.txt\`
- Modify: \`Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj\`
- Modify: \`Tests/AncientWarfare3.Rules.Tests/Program.cs.txt\`

- [ ] **Step 1: Write failing role and crate-category tests.**

Add a stable role value to each card and verify both top-level categories use
the existing period IDs:

\`\`\`csharp
Equal(HistoricalFigureCardRole.Monarch,
    HistoricalFigureCardCatalog.Get("han_liu_bang").Role,
    "monarch card has monarch role");
Equal(HistoricalFigureCardRole.Minister,
    HistoricalFigureCardCatalog.Get("han_xiao_he").Role,
    "minister card has minister role");
True(HistoricalFigureCardCatalog.GetCards(
    "han", HistoricalFigureCardRole.Monarch).All(
        p => p.Role == HistoricalFigureCardRole.Monarch),
    "monarch crate category filters cards");
True(HistoricalFigureCardCatalog.GetCards(
    "han", HistoricalFigureCardRole.Minister).All(
        p => p.Role == HistoricalFigureCardRole.Minister),
    "minister crate category filters cards");
Equal(50, HistoricalFigureCardRoleRules.MinisterCandidateBonus,
    "minister bonus is stable and positive");
\`\`\`

- [ ] **Step 2: Run the rules executable and verify the role tests fail.**

\`\`\`powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
\`\`\`

Expected: the role property, role-filtered catalog overload, or role rules are
missing. Keep unrelated baseline errors separate.

- [ ] **Step 3: Implement the role model and role-filtered catalog.**

Add \`HistoricalFigureCardRole.Monarch\` and
\`HistoricalFigureCardRole.Minister\`, store the role in
\`HistoricalFigureCardDefinition.Role\`, and assign every catalogue definition
explicitly. Preserve the constructor's existing call compatibility by adding
the role parameter after existing optional biography parameters.

Keep each old period crate ID unchanged. Add the role overload:

\`\`\`csharp
public static IReadOnlyList<HistoricalFigureCardDefinition> GetCards(
    string pCrateId, HistoricalFigureCardRole pRole)
{
    return GetCards(pCrateId).Where(p => p != null && p.Role == pRole).ToArray();
}
\`\`\`

The crate browser renders two top-level category buttons, then the existing
period crates under the selected category. Existing saved source IDs remain
period IDs, not role-prefixed IDs.

- [ ] **Step 4: Implement role rules and register tests.**

Implement explicit role predicates and a fixed positive bonus:

\`\`\`csharp
public static bool IsMonarch(HistoricalFigureCardDefinition pCard);
public static bool IsMinister(HistoricalFigureCardDefinition pCard);
public static int CandidateScoreBonus(HistoricalFigureCardDefinition pCard);
public const int MinisterCandidateBonus = 50;
\`\`\`

Register the production and test files and call
\`HistoricalFigureCardRoleRulesTests.Run()\`. Rerun the command from Step 2 and
expect the new tests to pass.

- [ ] **Step 5: Bind the two categories to the draw window.**

Add a transient selected role to \`HistoricalFigureDrawWindow\`, render Monarch
and Minister category buttons before the period crate list, and pass the
selected role to \`HistoricalFigureCardCatalog.GetCards(crateId, role)\` when
building a crate's draw pool. Keep the role out of persisted period source IDs.
Reset the selected role when returning to the top-level crate list. The shared
gold behavior remains explicit in the catalogue and is not revealed as a
visible gold entry.

- [ ] **Step 6: Commit the role model and crate categories.**

\`\`\`powershell
git add -- Code/core/lineage/HistoricalFigureCardRoleRules.cs Code/content/figures/HistoricalFigureCardModels.cs Code/content/figures/HistoricalFigureCardCatalog.cs Code/content/figures/HistoricalFigureCardCrates.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRoleRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "Separate historical card monarch and minister roles"
\`\`\`

### Task 7: Route deployment by card role

**Files:**
- Modify: \`Code/core/lineage/HistoricalFigureCardDeploymentRules.cs\`
- Modify: \`Code/core/lineage/HistoricalFigureCardDeploymentService.cs\`
- Modify: \`Code/core/court/OfficerCandidateCatalog.cs\`
- Modify: \`Code/core/court/CivilServiceQualificationService.cs\`
- Modify: \`Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardDeploymentRulesTests.cs.txt\`
- Modify: \`Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRoleRulesTests.cs.txt\`

- [ ] **Step 1: Add failing deployment-branch tests.**

Cover these exact facts:

\`\`\`csharp
True(HistoricalFigureCardDeploymentRules.CanDeployMinister(
    hasValidCity: true, hasLivingKingdom: true),
    "minister requires an existing civil city");
False(HistoricalFigureCardDeploymentRules.CanDeployMinister(
    hasValidCity: false, hasLivingKingdom: false),
    "minister cannot deploy to unowned land");
True(HistoricalFigureCardDeploymentRules.IsKingdomFoundingRole(
    HistoricalFigureCardRole.Monarch),
    "monarch founds a kingdom");
False(HistoricalFigureCardDeploymentRules.IsKingdomFoundingRole(
    HistoricalFigureCardRole.Minister),
    "minister never founds a kingdom");
\`\`\`

- [ ] **Step 2: Run the focused rules tests and verify the new assertions fail.**

\`\`\`powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
\`\`\`

Expected: the new role-aware deployment methods are absent or fail.

- [ ] **Step 3: Add role-aware deployment facts and guards.**

Read the card role from the catalogue in
\`HistoricalFigureCardDeploymentService.TryDeploy\`. Monarch cards keep the
current city/unowned-tile kingdom-founding flow. Minister cards require a
living civil kingdom and a valid target city; they use the target city's actor
asset, join the city, and do not call \`makeOwnKingdom\`,
\`makeNewCivKingdom\`, \`newCity\`, \`setCapital\`, or \`setName\`.

Keep minister deployment adult, alive, non-slave, non-king, non-heir, and
no-existing-office. Add the stable role marker before candidate registration.

- [ ] **Step 4: Register ministers in the normal candidate catalogue.**

After the minister actor joins the target city, call the existing lineage
promotion/history path, \`OfficerCandidateCatalog.EnsurePresent\`, and the
event-driven candidate-pool invalidation/request path for the target kingdom.
The operation must be idempotent for the deployment ID. A minister is a
candidate for later vacancy selection, not an automatic appointment.

- [ ] **Step 5: Add role-aware rollback and history.**

For a failed minister deployment, remove only the new actor and restore no
kingdom state because none was created. Record minister deployment and
candidate-registration history using the existing history writer, including
card ID, deployment ID, kingdom ID, and city ID. Consume the card only after
actor, lineage, history, and candidate registration succeed.

- [ ] **Step 6: Make minister qualification eligible without bypassing safety.**

In \`CivilServiceQualificationService\`, allow the card-minister marker to
satisfy the qualification portion of the appointment gate. Continue to reject
dead, underage, slave, king, heir, already-appointed, invalid-affiliation, or
otherwise unsafe actors. Do not change the gate for ordinary actors.

- [ ] **Step 7: Run tests and commit deployment branching.**

\`\`\`powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
git add -- Code/core/lineage/HistoricalFigureCardDeploymentRules.cs Code/core/lineage/HistoricalFigureCardDeploymentService.cs Code/core/court/OfficerCandidateCatalog.cs Code/core/court/CivilServiceQualificationService.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardDeploymentRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRoleRulesTests.cs.txt
git commit -m "Deploy minister cards into official candidate pools"
\`\`\`

### Task 8: Apply the minister candidate bonus consistently

**Files:**
- Modify: \`Code/core/court/LocalCourtAppointmentService.cs\`
- Modify: \`Code/core/lineage/LineageKeys.cs\`
- Create or modify: \`Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRoleRulesTests.cs.txt\`
- Modify: \`Tests/AncientWarfare3.Rules.Tests/Program.cs.txt\`

- [ ] **Step 1: Write failing score tests.**

\`\`\`csharp
int ordinary = LocalOfficialCandidateRules.Score(60, 20, false);
int minister = HistoricalFigureCardRoleRules.ApplyCandidateBonus(
    ordinary, HistoricalFigureCardRole.Minister);
Equal(ordinary + 50, minister,
    "minister receives the fixed candidate score bonus");
Equal(ordinary, HistoricalFigureCardRoleRules.ApplyCandidateBonus(
    ordinary, HistoricalFigureCardRole.Monarch),
    "monarch receives no minister bonus");
\`\`\`

- [ ] **Step 2: Implement the marker and score adjustment.**

Add a stable \`LineageKeys\` key and a helper that reads it without scanning the
catalogue. In both \`BuildCityCandidates\` and \`RankForBehavior\`, apply the
same role bonus immediately after the existing ability/merit score and before
sorting:

\`\`\`csharp
int score = LocalOfficialCandidateRules.Score(
    MainAbility(actor), (int)Math.Max(0f, merit),
    sameNativeCity: false);
score = HistoricalFigureCardRoleRules.ApplyCandidateBonus(
    score, HistoricalFigureCardRoleRules.ReadRole(actor));
\`\`\`

The bonus changes ordering among otherwise eligible candidates. It does not
override office grade, vacancy rules, heir/king exclusion, or an invalid actor.

- [ ] **Step 3: Run the role and court checks, then commit.**

\`\`\`powershell
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
git add -- Code/core/court/LocalCourtAppointmentService.cs Code/core/lineage/LineageKeys.cs Code/core/lineage/HistoricalFigureCardRoleRules.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardRoleRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "Increase deployed minister court candidate priority"
\`\`\`

### Task 9: Build and manual acceptance

**Files:**
- Verify all files from Tasks 1-8.

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

- [ ] **Step 5: Verify Monarch and Minister crate/deployment behavior in game.**

Open the crate browser and verify Monarch and Minister are separate top-level
categories while the existing period crate names remain available beneath each
category. Draw or inspect a Monarch card and deploy it to a city; verify its
historical kingdom is founded and the actor becomes king. Deploy a Minister
card to a city already owned by a civil kingdom; verify no kingdom, capital,
city ownership, or kingdom name changes and the actor appears in that kingdom's
official candidate list. Verify an unowned-tile Minister deployment is
rejected. After a vacancy trigger, verify the Minister ranks with the configured
positive candidate bonus but still respects the normal eligibility gates.

- [ ] **Step 6: Run final verification.**

\`\`\`powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tests/WindowUiRegressionTests.ps1
git status --short --branch
\`\`\`

Expected: window regression checks pass. Feature files are committed and no
unrelated file is staged by feature commits.
