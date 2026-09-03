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

## Deploying A Card

Deployment is a main-thread WorldBox operation. The caller supplies a living
civilization city selected from the map. A deployment creates an adult actor,
creates a new kingdom at that city, makes the actor its king, and uses the
card's historical kingdom name. The collection count is not decremented.

```csharp
using AncientWarfare3.core.lineage;

var request = new HistoricalFigureCardDeploymentRequest(
    cardId: "qin_shihuang",
    pDrawId: drawId,
    pDeploymentId: System.Guid.NewGuid().ToString("N"),
    pTargetCity: targetCity);

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
