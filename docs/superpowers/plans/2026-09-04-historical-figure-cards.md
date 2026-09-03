# Historical Figure Cards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Add an independent historical-figure card draw, collection, CS2-style reveal flow, and city deployment system without changing automatic historical-figure spawning or the existing Lineage archive contract.

**Architecture:** Keep card definitions and draw decisions in Unity-free rules classes. Persist the player collection in a separate JSON store under Application.persistentDataPath, and expose a service boundary that commits the collection before starting animation. Keep all Unity world mutation on the main thread behind a deployment service; use existing city kingdom creation, lineage, biography, chronicle, and history API entry points. Add a narrow UI window to the existing lineage tab and treat audio and portrait assets as optional presentation resources.

**Tech Stack:** C# 11, .NET Framework 4.8, Unity/WorldBox APIs, Harmony, Newtonsoft.Json, existing ScrollWindow/AbstractWindow, and the AncientWarfare3.Rules.Tests .NET 9 source-inclusion test harness.

---

### Task 1: Add the card domain model and catalogue

**Files:**
- Create: Code/content/figures/HistoricalFigureCardModels.cs
- Create: Code/content/figures/HistoricalFigureCardCatalog.cs
- Test: Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardCatalogRulesTests.cs.txt
- Modify: Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt

- [ ] **Step 1: Write the failing catalogue tests**

Add a test class with real catalogue calls. It must assert the five exact rarity definitions, no duplicate card IDs, valid parent references, no geographic dynasty prefix in a historical kingdom name, the current HistoricalFigureDef.All identity coverage, the emperor snapshot anchors (qin_shihuang, han_wudi, tang_taizong, ming_taizu, qing_xuantong), and fame ordering:

~~~
using System;
using System.Linq;
using AncientWarfare3.content.figures;

internal static class HistoricalFigureCardCatalogRulesTests
{
    public static void Run()
    {
        Equal(0.0026f, HistoricalFigureCardRarity.Gold.Probability,
            "gold probability is stable");
        Equal(0.0064f, HistoricalFigureCardRarity.Red.Probability,
            "red probability is stable");
        Equal(1f, HistoricalFigureCardRarity.TotalProbability,
            "rarity probabilities total one");

        var all = HistoricalFigureCardCatalog.All;
        Equal(all.Count, all.Select(p => p.CardId).Distinct(StringComparer.Ordinal).Count(),
            "card ids are unique");
        True(all.All(p => p.FameScore >= 0 && p.FameScore <= 100),
            "fame scores are bounded");
        True(all.All(p => p.ParentReferencesAreValid(all)),
            "parent references point at cards or are empty");
        True(all.All(p => !HistoricalFigureCardCatalog.HasGeographicPrefix(
            p.HistoricalKingdomName)), "historical names are short kingdom names");
        Equal(HistoricalFigureDef.All.Count, all.Count(p => p.LegacyFigureId != null),
            "every registered historical figure has one card");
        True(all.Any(p => p.CardId == "qin_shihuang") &&
             all.Any(p => p.CardId == "han_wudi") &&
             all.Any(p => p.CardId == "tang_taizong") &&
             all.Any(p => p.CardId == "ming_taizu") &&
             all.Any(p => p.CardId == "qing_xuantong"),
            "emperor catalogue contains snapshot anchors");

        var sorted = HistoricalFigureCardCatalog.SortForDisplay(all);
        True(sorted[0].FameScore >= sorted[1].FameScore,
            "catalogue display order starts with highest fame");
        Equal("史料不详", HistoricalFigureCardCatalog.ParentDisplayName(""),
            "unknown parent has explicit display text");
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException(message + ": expected=" + expected +
                                                ", actual=" + actual);
    }
}
~~~

- [ ] **Step 2: Register the test and run it to verify RED**

Link the production model and catalogue files in the test project, add the test source to the project, add the command --historical-figure-card-catalog to Program.cs.txt, and run:

~~~
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --historical-figure-card-catalog
~~~

Expected result: compilation fails because HistoricalFigureCardRarity, HistoricalFigureCardCatalog, and HistoricalFigureCardDefinition do not exist yet. Do not implement production code before this red result.

- [ ] **Step 3: Implement the minimal model and catalogue**

Implement immutable definitions with CardId, display/name fields, dynasty and short kingdom name, era/years, fame, rarity, sex, biography, nullable parent card/display fields, optional portrait, legacy figure ID/index, health, and traits. Implement HistoricalFigureCardRarity with the exact five probabilities and colors. Build the catalogue by converting each HistoricalFigureDef.All entry once, then append the complete Qin-to-Xuantong emperor directory, merge aliases by stable identity, and validate IDs, parent references, score range, rarity, and forbidden prefixes. Keep the existing historical registry indices untouched and use "史料不详" only at display time for missing parents.

- [ ] **Step 4: Run the catalogue test to verify GREEN**

Run the command from Step 2. Expected result: Historical figure card catalogue rules passed. and exit code 0.

- [ ] **Step 5: Commit only the new catalogue files**

~~~
git add Code/content/figures/HistoricalFigureCardModels.cs Code/content/figures/HistoricalFigureCardCatalog.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardCatalogRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: add historical figure card catalogue"
~~~

### Task 2: Add the independent collection store

**Files:**
- Create: Code/core/lineage/HistoricalFigureCardCollectionStore.cs
- Test: Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardCollectionRulesTests.cs.txt
- Modify: Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt

- [ ] **Step 1: Write failing collection tests**

Use a temporary directory and an injected path, not the Unity path. Cover duplicate count increments, last draw retention, reload across store instances, corrupt-file backup plus empty recovery, and the fact that a new world reset does not clear the store:

~~~
using System;
using System.IO;
using AncientWarfare3.core.lineage;

internal static class HistoricalFigureCardCollectionRulesTests
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "aw3-card-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "historical_figure_cards.json");
        var store = new HistoricalFigureCardCollectionStore(path);
        Equal(0, store.GetOwnedCount("qin_shihuang"), "empty store starts empty");
        True(store.RecordDraw("draw-1", "qin_shihuang", "gold",
            "2026-09-04T00:00:00Z"), "first draw commits");
        True(store.RecordDraw("draw-2", "qin_shihuang", "gold",
            "2026-09-04T00:00:01Z"), "duplicate draw commits");
        Equal(2, store.GetOwnedCount("qin_shihuang"),
            "duplicate draws increment owned count");
        Equal("draw-2", store.LastDraw.DrawId, "last draw is retained");

        var reloaded = new HistoricalFigureCardCollectionStore(path);
        reloaded.Load();
        Equal(2, reloaded.GetOwnedCount("qin_shihuang"),
            "collection survives a new store instance");

        File.WriteAllText(path, "not-json");
        var damaged = new HistoricalFigureCardCollectionStore(path);
        damaged.Load();
        Equal(0, damaged.GetOwnedCount("qin_shihuang"),
            "damaged file loads as an empty collection");
        True(File.Exists(path + ".corrupt"), "damaged file is preserved");

        Directory.Delete(root, true);
    }

    private static void True(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException(message);
    }
}
~~~

- [ ] **Step 2: Run the collection test to verify RED**

~~~
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --historical-figure-card-collection
~~~

Expected result: compilation fails because the collection store is absent.

- [ ] **Step 3: Implement the store**

Use Newtonsoft.Json DTOs containing schemaVersion, Dictionary<string,int> ownedCounts, List<HistoricalFigureCardDrawRecord> draws, and lastUpdatedUtc. The default constructor resolves Application.persistentDataPath/AncientWarfare3/historical_figure_cards.json; the test constructor accepts a path. Guard Load, RecordDraw, and Snapshot with one static lock, create the parent directory, write UTF-8 JSON to path + ".tmp", flush it, replace the destination atomically, and remove the temp file on failure. On parse failure move the original to a unique .corrupt path, log through ModClass.LogWarning only when available, and use an empty snapshot. No method references FigureStateStore.

- [ ] **Step 4: Run the collection test to verify GREEN**

Run the command from Step 2 and confirm the duplicate count, reload, and corrupt-file assertions pass.

- [ ] **Step 5: Commit the collection store**

~~~
git add Code/core/lineage/HistoricalFigureCardCollectionStore.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardCollectionRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: persist historical figure card collection"
~~~

### Task 3: Implement deterministic draw decisions and reveal layout

**Files:**
- Create: Code/core/lineage/HistoricalFigureCardDrawService.cs
- Test: Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardDrawRulesTests.cs.txt
- Modify: Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt

- [ ] **Step 1: Write failing draw tests**

Test the integer probability buckets [0, 26, 90, 410, 2008, 10000), uniform selection within the chosen non-empty rarity, exactly 50 rolling cards, winner index 42, and skip preserving the already committed winner:

~~~
using System;
using AncientWarfare3.content.figures;
using AncientWarfare3.core.lineage;

internal static class HistoricalFigureCardDrawRulesTests
{
    public static void Run()
    {
        Equal(HistoricalFigureCardRarity.Gold,
            HistoricalFigureCardDrawService.RarityForRoll(0), "zero is gold");
        Equal(HistoricalFigureCardRarity.Red,
            HistoricalFigureCardDrawService.RarityForRoll(26), "26 is red");
        Equal(HistoricalFigureCardRarity.Pink,
            HistoricalFigureCardDrawService.RarityForRoll(90), "90 is pink");
        Equal(HistoricalFigureCardRarity.Purple,
            HistoricalFigureCardDrawService.RarityForRoll(410), "410 is purple");
        Equal(HistoricalFigureCardRarity.Blue,
            HistoricalFigureCardDrawService.RarityForRoll(2008), "2008 is blue");

        var result = HistoricalFigureCardDrawService.BuildReveal(
            HistoricalFigureCardCatalog.All, new FixedRandom(2008, 0, 1));
        Equal(50, result.RollingCards.Count, "reveal has fifty cards");
        Equal(42, result.WinnerIndex, "winner is centered at index 42");
        Equal(result.Winner.CardId, result.RollingCards[result.WinnerIndex].CardId,
            "center card is the winner");
        Equal(result.Winner.CardId,
            HistoricalFigureCardDrawService.Skip(result).Winner.CardId,
            "skip preserves the stored winner");
    }

    private sealed class FixedRandom : IHistoricalFigureCardRandom
    {
        private readonly int[] _values;
        private int _index;
        public FixedRandom(params int[] values) { _values = values; }
        public int Next(int maximumExclusive)
        {
            int value = _values[Math.Min(_index++, _values.Length - 1)];
            return value % maximumExclusive;
        }
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual)) throw new InvalidOperationException(message);
    }
}
~~~

- [ ] **Step 2: Run the draw test to verify RED**

~~~
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --historical-figure-card-draw
~~~

Expected result: compilation fails because the draw service and result types are absent.

- [ ] **Step 3: Implement the draw service**

Use an injected IHistoricalFigureCardRandom and a production adapter over System.Random. Convert a random integer in [0,10000) into the exact rarity bucket; fail with a structured HistoricalFigureCardDrawResult when that bucket has no cards instead of falling back. Select one card from that bucket, create 50 non-null rolling entries with the winner only at index 42, and expose Skip as a pure operation that returns the same result. Add DrawAndCommit that generates a drawId, records the draw through the collection store, and only returns a playable result after persistence succeeds. Keep all random/result generation on the calling thread.

- [ ] **Step 4: Run the draw test to verify GREEN**

Run the command from Step 2 and verify all boundary and winner-index assertions pass.

- [ ] **Step 5: Commit draw rules**

~~~
git add Code/core/lineage/HistoricalFigureCardDrawService.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardDrawRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: add deterministic historical figure card draws"
~~~

### Task 4: Add deployment, identity, and parentage rules

**Files:**
- Create: Code/core/lineage/HistoricalFigureCardDeploymentRules.cs
- Create: Code/core/lineage/HistoricalFigureCardIdentityService.cs
- Create: Code/core/lineage/HistoricalFigureCardParentageService.cs
- Test: Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardDeploymentRulesTests.cs.txt
- Test: Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardParentageRulesTests.cs.txt
- Test: Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardSourceGuardTests.cs.txt
- Modify: Code/core/lineage/LineageKeys.cs
- Modify: Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt

- [ ] **Step 1: Write failing deployment and parentage tests**

Cover valid/invalid city facts, adult-before-join ordering, one active deployment per deploymentId, exact short kingdom-name retention, unknown parents producing no synthetic ancestor, and deterministic deploymentId + parentSlot synthetic IDs. Use pure DTOs so no Unity objects are required:

~~~
using System;
using AncientWarfare3.core.lineage;

internal static class HistoricalFigureCardDeploymentRulesTests
{
    public static void Run()
    {
        True(HistoricalFigureCardDeploymentRules.CanDeploy(
            new HistoricalFigureCardDeploymentFacts(true, true, true, true, true,
                false, "汉")), "valid city can be deployed");
        Equal(false, HistoricalFigureCardDeploymentRules.CanDeploy(
            new HistoricalFigureCardDeploymentFacts(false, true, true, true, true,
                false, "汉")), "unowned city is rejected");
        Equal(false, HistoricalFigureCardDeploymentRules.CanDeploy(
            new HistoricalFigureCardDeploymentFacts(true, false, true, true, true,
                false, "汉")), "deleted city is rejected");
        Equal(false, HistoricalFigureCardDeploymentRules.CanDeploy(
            new HistoricalFigureCardDeploymentFacts(true, true, false, true, true,
                false, "汉")), "baby actor plan is rejected");
        Equal(false, HistoricalFigureCardDeploymentRules.CanDeploy(
            new HistoricalFigureCardDeploymentFacts(true, true, true, true, true,
                true, "前汉")), "geographic prefix is rejected");
        True(HistoricalFigureCardDeploymentRules.TryBegin("deployment-1"),
            "first deployment starts");
        Equal(false, HistoricalFigureCardDeploymentRules.TryBegin("deployment-1"),
            "duplicate deployment is rejected");
        HistoricalFigureCardDeploymentRules.End("deployment-1");
    }
}

internal static class HistoricalFigureCardParentageRulesTests
{
    public static void Run()
    {
        Equal("deployment-1:father",
            HistoricalFigureCardParentageService.SyntheticParentId(
                "deployment-1", HistoricalFigureCardParentSlot.Father),
            "father synthetic id is stable");
        Equal(false, HistoricalFigureCardParentageService.ShouldCreateSyntheticParent(""),
            "unknown parent does not create an ancestor");
        True(HistoricalFigureCardParentageService.ShouldCreateSyntheticParent("刘邦"),
            "known display-only parent creates an archive identity");
    }
}
~~~

- [ ] **Step 2: Run the new tests to verify RED**

~~~
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore -- --historical-figure-card-deployment
~~~

Expected result: compilation fails because deployment facts, identity, and parentage rules are absent.

- [ ] **Step 3: Implement the pure rules and identity keys**

Add HISTORICAL_CARD_ID, HISTORICAL_CARD_DRAW_ID, and HISTORICAL_CARD_DEPLOYMENT_ID constants. Implement validation requiring a living civilization city, valid kingdom and archive, an adult actor plan, a known card, no active transaction, and a short historical kingdom name. Implement a lock-backed active-deployment set and a result DTO that separates validation failure from committed success. Implement identity writes to actor data only after successful world creation; keep card identities outside FigureStateStore. Implement parentage with the existing historical ancestor display/synthetic archive conventions and do not infer absent parents.

- [ ] **Step 4: Add source guards and run the deployment tests to verify GREEN**

The source guard must assert that the card implementation contains no FigureStateStore write, no geographic prefix concatenation, no Task.Run, and no background access to World.world. Run the dedicated command and verify all assertions pass.

- [ ] **Step 5: Commit deployment rules and identity**

~~~
git add Code/core/lineage/HistoricalFigureCardDeploymentRules.cs Code/core/lineage/HistoricalFigureCardIdentityService.cs Code/core/lineage/HistoricalFigureCardParentageService.cs Code/core/lineage/LineageKeys.cs Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardDeploymentRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardParentageRulesTests.cs.txt Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "feat: define safe card deployment identity rules"
~~~

### Task 5: Implement the main-thread city deployment transaction

**Files:**
- Create: Code/core/lineage/HistoricalFigureCardDeploymentService.cs
- Modify: Code/core/lineage/HistoricalAncestorService.cs
- Modify: Code/core/lineage/HistoryWriter.cs
- Modify: Code/core/lineage/ChronicleEvents.cs
- Modify: Code/patch/AW_FigurePatch.cs

- [ ] **Step 1: Add the production transaction seam behind the existing pure rules**

Expose TryDeploy(HistoricalFigureCardDeploymentRequest, out HistoricalFigureCardDeploymentResult) and keep it main-thread-only. Capture old city/kingdom/leader/capital IDs, begin the deployment scope, and call the existing APIs in this order: createNewUnit with pAdultAge: true, set card identity and name/sex/health, joinCity, city.makeOwnKingdom(actor, true, false), set capital/name, write card identity and parentage, call existing lineage/biography/chronicle writers, then publish history events. Never create a baby and repair its age afterward.

- [ ] **Step 2: Implement rollback paths**

On failure before kingdom creation, remove the captured actor. After kingdom creation, restore the old city kingdom/leader/capital relationship, dispose the new kingdom and actor, remove only temporary history rows, release the deployment lock, and return a structured error. Use the unique deployment ID to make confirmation and duplicate map clicks idempotent. Do not decrement collection ownership.

- [ ] **Step 3: Add external parentage history entry**

Extend HistoricalAncestorService with a card-specific entry that accepts explicit parent IDs/display names and the deployment ID. It must preserve "史料不详" for missing data, use deploymentId + ":father"/":mother" for display-only synthetic IDs, and leave engine biological parent slots in the existing safe state. Write card_deployed, card_king, and card_kingdom_founded through current HistoryWriter/ChronicleEvents APIs only after the archive transaction commits.

- [ ] **Step 4: Protect automatic spawning**

Update AW_FigurePatch/HistoricalFigureService candidate checks to ignore actors containing HISTORICAL_CARD_ID, while leaving FigureStateStore ordering, mutual exclusion, and save format unchanged. The deployment service's scope must suppress the automatic callback only during the transaction and always dispose in finally.

- [ ] **Step 5: Build and inspect the integration diff**

Run:

~~~
dotnet build AncientWarfare3.csproj --no-restore
git diff --check
~~~

Fix compiler errors in the adapter against the actual WorldBox assemblies without changing the pure rules or unrelated war files. Confirm the diff contains no edits to the current uncommitted law/war files.

- [ ] **Step 6: Commit the deployment service**

~~~
git add Code/core/lineage/HistoricalFigureCardDeploymentService.cs Code/core/lineage/HistoricalAncestorService.cs Code/core/lineage/HistoryWriter.cs Code/core/lineage/ChronicleEvents.cs Code/patch/AW_FigurePatch.cs Code/content/figures/HistoricalFigureService.cs
git commit -m "feat: deploy historical cards as independent kingdoms"
~~~

### Task 6: Add audio, entry-point initialization, and lineage-tab UI

**Files:**
- Create: Code/core/lineage/HistoricalFigureCardAudioService.cs
- Create: Code/patch/AW_HistoricalFigureCardPatch.cs
- Create: Code/ui/windows/HistoricalFigureDrawWindow.cs
- Create: Code/ui/items/HistoricalFigureCardListItem.cs
- Modify: Code/ui/AW_LineageTab.cs
- Modify: Code/ui/AW_LineageWindowIds.cs
- Modify: Code/ModClass.cs or Code/content/XiaContent.cs
- Create: GameResources/sounds/historical_cards/aw3_card_unlock.wav
- Create: GameResources/sounds/historical_cards/aw3_card_unlock_immediate.wav
- Create: GameResources/sounds/historical_cards/aw3_card_scroll.wav
- Create: GameResources/sounds/historical_cards/aw3_card_button_press.wav
- Create: GameResources/sounds/historical_cards/aw3_card_item_hover.wav
- Create: GameResources/sounds/historical_cards/aw3_card_reveal_blue.wav
- Create: GameResources/sounds/historical_cards/aw3_card_reveal_purple.wav
- Create: GameResources/sounds/historical_cards/aw3_card_reveal_pink.wav
- Create: GameResources/sounds/historical_cards/aw3_card_reveal_red.wav
- Create: GameResources/sounds/historical_cards/aw3_card_reveal_gold.wav

- [ ] **Step 1: Add the UI state machine and window ID**

Add HISTORICAL_FIGURE_CARDS to AW_LineageWindowIds, a lineage-tab button, and a window with states Idle, Rolling, Reveal, Details, Placement, PlacementConfirm, and Deploying. The draw button calls DrawAndCommit before setting Rolling; animation always uses winner index 42 and the fixed six-second easing, and skip only seeks to the same stored winner. During Rolling, draw/deploy/close callbacks return without changing state.

- [ ] **Step 2: Add collection and details views**

Render rarity legend, last result, and the fame-sorted collection. Each card row has fixed dimensions and shows owned count, rarity color, name, and historical short kingdom. Details show family/clan/given name, dynasty, era, dates, fame rank, biography, father/mother with "史料不详" fallback, portrait fallback, and a text-plus-icon deploy command. Deployment hides the window while map city selection is active, restores on cancellation, and displays target city/original kingdom/history kingdom before confirmation.

- [ ] **Step 3: Add map selection bridge**

Use the existing map click patch pattern from AW_HierarchicalVassalMapClickPatch to accept only a living civilization city during Placement. A city click only enters confirmation; it does not mutate ownership. Confirmation calls the main-thread deployment service once and returns to details on success or placement on failure.

- [ ] **Step 4: Implement optional audio playback**

HistoricalFigureCardAudioService must resolve the ten WAV names through CustomAudioManager, respect sound settings, and catch missing-resource/playback errors. Trigger button press, unlock, per-card scroll crossing, immediate skip, and rarity-specific reveal sounds at the corresponding animation transitions. Missing WAV files must silently disable only audio and never block draw/deploy.

- [ ] **Step 5: Initialize safely**

Load the catalogue and collection once during the existing mod initialization path. If catalogue validation fails, log a warning and disable draw controls; if collection load fails, use the empty in-memory store. Clear only transient placement/animation state when the world is loaded, switched, or replaced. Do not touch the automatic figure state store.

- [ ] **Step 6: Run focused UI source guards and build**

Add source assertions for state transitions, winner index 42, DrawAndCommit preceding animation, no collection decrement on deployment, and no FigureStateStore usage. Run the focused test switches and dotnet build AncientWarfare3.csproj --no-restore.

- [ ] **Step 7: Commit UI and runtime integration**

~~~
git add Code/core/lineage/HistoricalFigureCardAudioService.cs Code/patch/AW_HistoricalFigureCardPatch.cs Code/ui/windows/HistoricalFigureDrawWindow.cs Code/ui/items/HistoricalFigureCardListItem.cs Code/ui/AW_LineageTab.cs Code/ui/AW_LineageWindowIds.cs Code/ModClass.cs Code/content/XiaContent.cs GameResources/sounds/historical_cards
git commit -m "feat: add historical figure card reveal and deployment UI"
~~~

### Task 7: Add notices, usage documentation, and final verification

**Files:**
- Create or modify: THIRD_PARTY_NOTICES.md
- Create: docs/api/historical-figure-cards.md
- Test: Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardAcceptanceSourceGuardTests.cs.txt
- Modify: Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj
- Modify: Tests/AncientWarfare3.Rules.Tests/Program.cs.txt

- [ ] **Step 1: Document the public integration surface**

Document the player flow and the safe calls available to other history AI mods: read-only card catalogue access, HistoricalFigureCardCollectionStore.Snapshot(), HistoricalFigureCardDeploymentService.TryDeploy(...), and history API event subscription. State that Unity Actor, Kingdom, and City instances are not exposed through the cross-mod event payload, that card identities do not occupy automatic FigureStateStore slots, and that unknown parents remain unknown.

- [ ] **Step 2: Document third-party audio provenance**

Record that the WAV files are derived from the local cs2-case-simulator reference at frontend/assets/audio, list each source filename and converted destination, and state that only the audio files used by this feature are included. Do not copy frontend source, database, or unrelated assets.

- [ ] **Step 3: Add acceptance source guards**

Assert that the implementation contains a 50-card reveal, index 42, explicit Application.persistentDataPath store path, atomic temp replacement, the adult spawn argument, the card identity keys, and history event names. Assert that no unrelated Xia building/person texture files or the current law/war files are added to the feature commit.

- [ ] **Step 4: Run the complete verification set**

Run the focused switches, then:

~~~
dotnet run --project Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj --no-restore
dotnet build AncientWarfare3.csproj --no-restore
git diff --check
git status --short
~~~

Separate any pre-existing rules-test baseline failures from card-feature failures. Before claiming completion, inspect the full diff and verify all existing uncommitted law/war files remain unchanged except for their original worktree state.

- [ ] **Step 5: Commit documentation and tests**

~~~
git add -f docs/superpowers/plans/2026-09-04-historical-figure-cards.md docs/api/historical-figure-cards.md
git add THIRD_PARTY_NOTICES.md Tests/AncientWarfare3.Rules.Tests/HistoricalFigureCardAcceptanceSourceGuardTests.cs.txt Tests/AncientWarfare3.Rules.Tests/AncientWarfare3.Rules.Tests.csproj Tests/AncientWarfare3.Rules.Tests/Program.cs.txt
git commit -m "docs: document historical figure card integration"
~~~
