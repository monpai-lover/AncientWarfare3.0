using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AncientWarfare3.content.figures;
using AncientWarfare3.core.lineage;
using Newtonsoft.Json;

internal static class HistoricalFigureCardPersistenceRuntimeTests
{
    public static void Run()
    {
        var failures = new List<string>();
        Check("failed draw preserves the full history and timestamp", FailedDraw, failures);
        Check("failed legacy consume preserves missing provenance", FailedLegacyConsume, failures);
        Check("failed recycle preserves inputs, output and history", FailedRecycle, failures);
        Check("temporary read lock preserves inventory and retries", LockedRead, failures);
        Check("failed corruption backup cannot overwrite the original", FailedCorruptBackup, failures);
        Check("recycle rejects selection beyond owned quantities", ExcessRecycleSelection, failures);
        if (failures.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
    }

    private static void FailedDraw(string path)
    {
        var card = Card(HistoricalFigureCardRarity.Blue);
        Seed(path, card.CardId, 100, history: 100);
        var store = new HistoricalFigureCardCollectionStore(path);
        string before = Snapshot(store);
        int revision = store.Revision;
        string disk = File.ReadAllText(path);
        Directory.CreateDirectory(path + ".tmp");
        var result = HistoricalFigureCardDrawService.DrawAndCommit(new[] { card },
            "han", store, new FirstRandom(), "failed-draw-time");
        Require(!result.Succeeded && !result.IsCommitted,
            "failed storage must not report a committed reveal");
        Equal(before, Snapshot(store), "failed draw must restore every snapshot field");
        Equal(revision, store.Revision, "failed draw does not publish a revision");
        Equal(disk, File.ReadAllText(path), "failed draw leaves disk intact");
        Directory.Delete(path + ".tmp");
        result = HistoricalFigureCardDrawService.DrawAndCommit(new[] { card },
            "han", store, new FirstRandom(), "successful-draw-time");
        Require(result.Succeeded && result.IsCommitted, "retry commits the reveal");
        Equal(101, store.GetOwnedCount(card.CardId), "retry awards one card");
        Equal(100, store.Draws.Count, "successful history remains bounded");
        Equal("old-1", store.Draws[0].DrawId, "only the committed draw evicts the oldest entry");
        Equal(Snapshot(store), Snapshot(new HistoricalFigureCardCollectionStore(path)),
            "successful retry survives reload");
    }

    private static void FailedLegacyConsume(string path)
    {
        var card = Card(HistoricalFigureCardRarity.Blue);
        Seed(path, card.CardId, 1, legacy: true);
        var store = new HistoricalFigureCardCollectionStore(path);
        string before = Snapshot(store);
        int revision = store.Revision;
        Directory.CreateDirectory(path + ".tmp");
        Require(!store.TryConsume(card.CardId, "failed-consume-time"), "consume reports write failure");
        Equal(before, Snapshot(store), "failed consume must not invent a source entry");
        Equal(revision, store.Revision, "failed consume does not publish a revision");
        Directory.Delete(path + ".tmp");
        Require(store.TryConsume(card.CardId, "successful-consume-time"), "consume retry commits");
        Equal(0, store.GetOwnedCount(card.CardId), "consume removes exactly the last card");
        Equal(Snapshot(store), Snapshot(new HistoricalFigureCardCollectionStore(path)),
            "successful consume survives reload");
    }

    private static void FailedRecycle(string path)
    {
        var input = Card(HistoricalFigureCardRarity.Blue);
        var output = Card(HistoricalFigureCardRarity.Purple);
        Seed(path, input.CardId, 10, history: 100);
        var store = new HistoricalFigureCardCollectionStore(path);
        string before = Snapshot(store);
        string disk = File.ReadAllText(path);
        int revision = store.Revision;
        string[] inputs = Enumerable.Repeat(input.CardId, 10).ToArray();
        Directory.CreateDirectory(path + ".tmp");
        Require(!store.TryRecycle(inputs, output.CardId, output.Rarity.Id,
            "han", "recycle", "failed-recycle-time"), "recycle reports write failure");
        Equal(before, Snapshot(store), "failed recycle leaves inputs and history intact");
        Equal(revision, store.Revision, "failed recycle does not publish a revision");
        Equal(disk, File.ReadAllText(path), "failed recycle leaves disk intact");
        Directory.Delete(path + ".tmp");
        Require(store.TryRecycle(inputs, output.CardId, output.Rarity.Id,
            "han", "recycle", "successful-recycle-time"), "same recycle id can retry");
        Equal(0, store.GetOwnedCount(input.CardId), "recycle consumes ten inputs");
        Equal(1, store.GetOwnedCount(output.CardId), "recycle awards one output");
        Equal("old-1", store.Draws[0].DrawId, "successful recycle evicts one history entry");
        Equal(Snapshot(store), Snapshot(new HistoricalFigureCardCollectionStore(path)),
            "recycle survives reload");
    }

    private static void LockedRead(string path)
    {
        var card = Card(HistoricalFigureCardRarity.Blue);
        Seed(path, card.CardId, 4);
        string disk = File.ReadAllText(path);
        var store = new HistoricalFigureCardCollectionStore(path);
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            store.Load();
            Require(File.Exists(path) && !File.Exists(path + ".corrupt"),
                "a valid but locked file must not be renamed as corrupt");
            Require(!store.RecordDraw("after-lock", card.CardId, card.Rarity.Id, "later", "han"),
                "a store that has not read existing inventory cannot overwrite it");
            Equal(0, store.Revision, "failed load has no published revision");
        }
        Equal(disk, File.ReadAllText(path), "read failure preserves original bytes");
        Equal(4, store.GetOwnedCount(card.CardId), "same store retries after the lock clears");
        Require(store.RecordDraw("after-lock", card.CardId, card.Rarity.Id, "later", "han"),
            "draw can retry after recovery");
        Equal(5, new HistoricalFigureCardCollectionStore(path).GetOwnedCount(card.CardId),
            "recovery keeps existing inventory plus the new card");
    }

    private static void FailedCorruptBackup(string path)
    {
        File.WriteAllText(path, "unparseable inventory bytes");
        Directory.CreateDirectory(path + ".corrupt");
        var store = new HistoricalFigureCardCollectionStore(path);
        var card = Card(HistoricalFigureCardRarity.Blue);
        Require(!store.RecordDraw("new", card.CardId, card.Rarity.Id, "later", "han"),
            "failed corruption backup must block destructive replacement");
        Equal("unparseable inventory bytes", File.ReadAllText(path), "original remains recoverable");
        Directory.Delete(path + ".corrupt");
        Require(store.RecordDraw("new", card.CardId, card.Rarity.Id, "later", "han"),
            "retry may reset only after preserving the corrupt file");
        Equal("unparseable inventory bytes", File.ReadAllText(path + ".corrupt"),
            "successful backup retains the original bytes");
        Equal(1, store.GetOwnedCount(card.CardId), "new collection commits after backup");
    }

    private static void ExcessRecycleSelection(string path)
    {
        var input = Card(HistoricalFigureCardRarity.Blue);
        var output = Card(HistoricalFigureCardRarity.Purple);
        Seed(path, input.CardId, 9);
        var store = new HistoricalFigureCardCollectionStore(path);
        string before = Snapshot(store);
        Require(!store.TryRecycle(Enumerable.Repeat(input.CardId, 10).ToArray(),
            output.CardId, output.Rarity.Id, "han", "too-many"),
            "ten duplicate selections cannot consume nine owned cards");
        Equal(before, Snapshot(store), "invalid selection leaves inventory unchanged");
    }

    private static void Seed(string path, string cardId, int count,
        int history = 0, bool legacy = false)
    {
        var snapshot = new HistoricalFigureCardCollectionSnapshot
        {
            schemaVersion = legacy ? 1 : 2,
            lastUpdatedUtc = "original-time"
        };
        snapshot.ownedCounts[cardId] = count;
        if (!legacy) snapshot.ownedCrateCounts[cardId] = new Dictionary<string, int> { ["han"] = count };
        for (int i = 0; i < history; i++) snapshot.draws.Add(new HistoricalFigureCardDrawRecord
        {
            drawId = "old-" + i, cardId = cardId, rarity = "blue", utc = "original-time", crateId = "han"
        });
        File.WriteAllText(path, JsonConvert.SerializeObject(snapshot));
    }

    private static HistoricalFigureCardDefinition Card(HistoricalFigureCardRarity rarity) =>
        HistoricalFigureCardCatalog.GetCards("han").First(card => card.Rarity.Equals(rarity));

    private static string Snapshot(HistoricalFigureCardCollectionStore store) =>
        JsonConvert.SerializeObject(store.Snapshot());

    private static void Check(string name, Action<string> test, List<string> failures)
    {
        string directory = Path.Combine(Path.GetTempPath(), "aw3-card-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { test(Path.Combine(directory, "cards.json")); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures.Add(name + ": " + error.Message); Console.WriteLine("FAIL " + name + ": " + error.Message); }
        finally { Directory.Delete(directory, true); }
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual, string message) =>
        Require(Equals(expected, actual), message);

    private sealed class FirstRandom : IHistoricalFigureCardRandom
    {
        public int Next(int maximum) => maximum == 10000 ? 9999 : 0;
    }
}
