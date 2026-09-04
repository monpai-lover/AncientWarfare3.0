# Historical Figure Cards API

AncientWarfare3 exposes a historical figure card catalogue, player collection,
and main-thread deployment service. Reference `AncientWarfare3.dll` and use the
public types in `AncientWarfare3.content.figures` and
`AncientWarfare3.core.lineage`.

## Catalogue

The catalogue is read-only and includes the existing automatic historical
figure definitions plus the emperor directory. Historical kingdom names are
stored as short historical names; no geographic prefix is added.

```csharp
using AncientWarfare3.content.figures;

foreach (HistoricalFigureCardDefinition card in
         HistoricalFigureCardCatalog.SortForDisplay(
             HistoricalFigureCardCatalog.All))
{
    string id = card.CardId;
    string name = card.DisplayName;
    int fame = card.FameScore;
    string rarity = card.Rarity.Id;
    string father = card.ParentDisplayName(true);
    string mother = card.ParentDisplayName(false);
}
```

`FameScore` is the display sort key. Rarities, from lowest to highest
probability, are `Gold`, `Red`, `Pink`, `Purple`, and `Blue`. Unknown parents
remain `史料不详` in the player-facing data and are not invented at runtime.

## Historical Crates

The player UI presents six period crates. Their item counts are calculated from
the catalogue. Blue, Purple, Pink, and Red cards are drawn from the selected
period crate; Gold cards are drawn from one shared grand-prize pool containing
all Gold cards in the catalogue. If a period has no cards in a rarity bucket,
the available bucket weights are renormalized, matching the reference case
opening behavior:

```csharp
foreach (HistoricalFigureCardCrate crate in HistoricalFigureCardCrates.All)
{
    IReadOnlyList<HistoricalFigureCardDefinition> cards =
        HistoricalFigureCardCatalog.GetCards(crate.Id);
    string id = crate.Id;
    int itemCount = crate.CardCount;
}
```

The stable crate IDs are `pre_qin_qin`, `han`, `three_six_dynasties`, `sui_tang`,
`five_song`, and `yuan_ming_qing`. A draw can record its source crate while
remaining compatible with the older overload:

```csharp
HistoricalFigureCardRevealResult reveal =
    HistoricalFigureCardDrawService.DrawAndCommit(
        HistoricalFigureCardCatalog.GetCards("han"),
        "han",
        new HistoricalFigureCardCollectionStore());
string sourceCrate = reveal.CrateId;
```

## Collection

The collection is separate from the world save and automatic
`FigureStateStore`. Read a detached snapshot and do not mutate its dictionaries
or lists:

```csharp
using AncientWarfare3.core.lineage;

var snapshot = new HistoricalFigureCardCollectionStore().Snapshot();
int owned = snapshot.ownedCounts.TryGetValue("qin_shihuang", out int count)
    ? count : 0;
```

The game UI uses the same store under
`Application.persistentDataPath/AncientWarfare3/historical_figure_cards.json`.
Draws are committed before the reveal animation starts and writes are atomic.
Other mods should not write this file directly.

The in-game `仓库` view reads `OwnedCounts` from this same store, filters out
zero-count cards, and shows each owned count. It supports sorting by latest
draw, rarity, name, or fame. Selecting a row opens the full card details;
deployment consumes one owned copy after a successful deployment transaction.

## Deploying A Card

Deployment is a main-thread WorldBox operation. The caller supplies either a
living civilization city or buildable unowned land selected from the map. A
deployment creates an adult actor, creates a new kingdom at that location,
makes the actor its king, and uses the card's historical kingdom name. The
deployment consumes one owned card only after the actor, kingdom, lineage,
parentage, and history writes succeed.

```csharp
using AncientWarfare3.core.lineage;

var request = new HistoricalFigureCardDeploymentRequest(
    "qin_shihuang",
    drawId,
    System.Guid.NewGuid().ToString("N"),
    targetCity);

HistoricalFigureCardDeploymentResult result =
    HistoricalFigureCardDeploymentService.TryDeploy(request);
if (result.Succeeded)
{
    long actorId = result.ActorId;
    long kingdomId = result.KingdomId;
}
```

`DeploymentId` is an idempotency key. Reuse it only when retrying the same
confirmation; a completed or active key is rejected. Invoke this method from
the game's main thread because it touches `Actor`, `Kingdom`, and `City`.
Rollback restores the original city ownership when the transaction fails.

Card identities are stored under `HISTORICAL_CARD_ID` and are excluded from the
automatic historical figure spawn pipeline. Deployment writes lineage,
parentage, biography, chronicle, city history, and kingdom history entries.

## Trading Up Cards

The collection supports CS2-style trade-up contracts. Inputs must be cards of
one rarity: ten Blue, Purple, or Pink cards, or five Red cards. The next rarity
is produced; Gold cards cannot be traded up. The output crate is selected with
source-count weighting from the input cards, while Gold output cards come from
the shared Gold pool. The store consumes inputs and records the output in one
atomic write:

```csharp
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

var collection = new HistoricalFigureCardCollectionStore();
IReadOnlyList<string> inputs = new[]
{
    "card_a", "card_b", "card_c", "card_d", "card_e",
    "card_f", "card_g", "card_h", "card_i", "card_j"
};

bool committed = collection.TryRecycle(
    inputs,
    "output_card_id",
    "purple",
    "han",
    System.Guid.NewGuid().ToString("N"));
```

Use `GetRecycleSourceCounts` and
`HistoricalFigureCardRecycleRules.SelectWeightedCrate` when building a custom
UI. The in-game UI already performs this selection and validates the required
count before submitting the transaction.

## History Events

Other history AI mods can subscribe to detached committed events:

```csharp
using AncientWarfare3.api.history;

System.IDisposable subscription = AW3HistoryApi.Subscribe(
    AW3HistorySubscription.ForKingdom(kingdomId),
    item =>
    {
        if (item.EventType == "card_king" ||
            item.EventType == "card_kingdom_founded")
        {
            // Copy the scalar data to an AI queue and return quickly.
        }
    });
```

Event payloads contain IDs and scalar history data only. They do not expose
Unity `Actor`, `Kingdom`, or `City` instances, SQLite connections, or readers.
Callbacks run on the main thread during the normal event drain and should be
short. Dispose subscriptions when the external mod or world stops, and reset
cached IDs when `AW3HistoryApi.RuntimeDatabaseEpoch` changes. Use the public
history API for reads instead of opening AncientWarfare3's SQLite archive.
