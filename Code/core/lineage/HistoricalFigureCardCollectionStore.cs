using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using AncientWarfare3.content.figures;

namespace AncientWarfare3.core.lineage
{
    public sealed class HistoricalFigureCardDrawRecord
    {
        public string drawId;
        public string cardId;
        public string rarity;
        public string utc;
        public string crateId;

        [JsonIgnore]
        public string DrawId => drawId ?? "";
        [JsonIgnore]
        public string CardId => cardId ?? "";
        [JsonIgnore]
        public string Rarity => rarity ?? "";
        [JsonIgnore]
        public string Utc => utc ?? "";
        [JsonIgnore]
        public string CrateId => crateId ?? "";
    }

    public sealed class HistoricalFigureCardCollectionSnapshot
    {
        public int schemaVersion = 2;
        public Dictionary<string, int> ownedCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, int>> ownedCrateCounts =
            new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        public List<HistoricalFigureCardDrawRecord> draws =
            new List<HistoricalFigureCardDrawRecord>();
        public string lastUpdatedUtc = "";
    }

    /// <summary>
    /// Player-level card ownership. This file intentionally lives outside the
    /// world save and outside the automatic historical spawn state.
    /// </summary>
    public sealed class HistoricalFigureCardCollectionStore
    {
        private const int SchemaVersion = 2;
        private const int MaximumDrawHistory = 100;
        private static readonly object Gate = new object();
        private readonly string _path;
        private HistoricalFigureCardCollectionSnapshot _snapshot;
        private bool _loaded;

        public HistoricalFigureCardCollectionStore()
            : this(DefaultPath())
        {
        }

        public HistoricalFigureCardCollectionStore(string pPath)
        {
            _path = string.IsNullOrWhiteSpace(pPath)
                ? DefaultPath()
                : System.IO.Path.GetFullPath(pPath.Trim());
        }

        public string Path => _path;

        public HistoricalFigureCardDrawRecord LastDraw
        {
            get
            {
                EnsureLoaded();
                lock (Gate)
                {
                    return _snapshot.draws.Count == 0
                        ? null
                        : Clone(_snapshot.draws[_snapshot.draws.Count - 1]);
                }
            }
        }

        public void Load()
        {
            lock (Gate)
            {
                if (_loaded) return;
                _snapshot = ReadSnapshot();
                _loaded = true;
            }
        }

        public int GetOwnedCount(string pCardId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(pCardId)) return 0;
            lock (Gate)
            {
                return _snapshot.ownedCounts.TryGetValue(pCardId, out int count)
                    ? Math.Max(0, count)
                    : 0;
            }
        }

        public int GetOwnedCrateCount(string pCardId, string pCrateId)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(pCardId) || pCrateId == null) return 0;
            lock (Gate)
            {
                return _snapshot.ownedCrateCounts.TryGetValue(pCardId,
                        out Dictionary<string, int> sources) &&
                    sources.TryGetValue(pCrateId, out int count)
                    ? Math.Max(0, count) : 0;
            }
        }

        public IReadOnlyDictionary<string, int> GetRecycleSourceCounts(
            IReadOnlyList<string> pCardIds)
        {
            EnsureLoaded();
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            lock (Gate)
            {
                var remainingSources = new Dictionary<string,
                    Dictionary<string, int>>(StringComparer.Ordinal);
                foreach (string cardId in pCardIds ?? Array.Empty<string>())
                {
                    HistoricalFigureCardDefinition card =
                        HistoricalFigureCardCatalog.Get(cardId);
                    if (card == null || GetOwnedCount(cardId) <= 0) continue;
                    if (!remainingSources.TryGetValue(cardId,
                            out Dictionary<string, int> available))
                    {
                        available = SourcesForCard(_snapshot, card,
                            GetOwnedCount(cardId));
                        remainingSources[cardId] = available;
                    }
                    string source = TakeSource(available);
                    if (source == null) continue;
                    available[source]--;
                    result[source] = result.TryGetValue(source, out int count)
                        ? count + 1 : 1;
                }
            }
            return result;
        }

        public IReadOnlyDictionary<string, int> OwnedCounts
        {
            get
            {
                EnsureLoaded();
                lock (Gate)
                    return new Dictionary<string, int>(_snapshot.ownedCounts,
                        StringComparer.Ordinal);
            }
        }

        public IReadOnlyList<HistoricalFigureCardDrawRecord> Draws
        {
            get
            {
                EnsureLoaded();
                lock (Gate)
                    return _snapshot.draws.Select(Clone).ToArray();
            }
        }

        public HistoricalFigureCardCollectionSnapshot Snapshot()
        {
            EnsureLoaded();
            lock (Gate)
                return Clone(_snapshot);
        }

        public bool RecordDraw(string pDrawId, string pCardId, string pRarity,
            string pUtc)
        {
            return RecordDraw(pDrawId, pCardId, pRarity, pUtc, "");
        }

        public bool RecordDraw(string pDrawId, string pCardId, string pRarity,
            string pUtc, string pCrateId)
        {
            if (string.IsNullOrWhiteSpace(pDrawId) ||
                string.IsNullOrWhiteSpace(pCardId) ||
                string.IsNullOrWhiteSpace(pRarity)) return false;
            EnsureLoaded();
            lock (Gate)
            {
                if (_snapshot.draws.Any(p => p != null &&
                        string.Equals(p.DrawId, pDrawId, StringComparison.Ordinal)))
                    return false;
                if (!_snapshot.ownedCounts.TryGetValue(pCardId, out int count))
                    count = 0;
                _snapshot.ownedCounts[pCardId] = checked(count + 1);
                AddSource(_snapshot, pCardId, pCrateId ?? "", 1);
                _snapshot.draws.Add(new HistoricalFigureCardDrawRecord
                {
                    drawId = pDrawId,
                    cardId = pCardId,
                    rarity = pRarity,
                    utc = pUtc ?? "",
                    crateId = pCrateId ?? ""
                });
                while (_snapshot.draws.Count > MaximumDrawHistory)
                    _snapshot.draws.RemoveAt(0);
                _snapshot.lastUpdatedUtc = pUtc ?? "";
                if (!TryWriteSnapshot(_snapshot))
                {
                    _snapshot.ownedCounts[pCardId] = count;
                    if (_snapshot.ownedCounts[pCardId] <= 0)
                        _snapshot.ownedCounts.Remove(pCardId);
                    RemoveSource(_snapshot, pCardId, pCrateId ?? "", 1);
                    _snapshot.draws.RemoveAll(p => p != null &&
                        string.Equals(p.DrawId, pDrawId, StringComparison.Ordinal));
                    return false;
                }
                return true;
            }
        }

        public bool TryConsume(string pCardId, string pUtc = null)
        {
            if (string.IsNullOrWhiteSpace(pCardId)) return false;
            EnsureLoaded();
            lock (Gate)
            {
                if (!_snapshot.ownedCounts.TryGetValue(pCardId, out int count) ||
                    count <= 0) return false;
                if (count == 1) _snapshot.ownedCounts.Remove(pCardId);
                else _snapshot.ownedCounts[pCardId] = count - 1;
                string source = SelectAnySource(_snapshot, pCardId);
                RemoveSource(_snapshot, pCardId, source, 1);
                string previousUpdatedUtc = _snapshot.lastUpdatedUtc;
                _snapshot.lastUpdatedUtc = pUtc ?? DateTime.UtcNow.ToString("O");
                if (TryWriteSnapshot(_snapshot)) return true;
                _snapshot.ownedCounts[pCardId] = count;
                AddSource(_snapshot, pCardId, source, 1);
                _snapshot.lastUpdatedUtc = previousUpdatedUtc;
                return false;
            }
        }

        public bool TryRecycle(IReadOnlyList<string> pCardIds,
            string pOutputCardId, string pOutputRarity, string pOutputCrateId,
            string pRecycleId, string pUtc = null)
        {
            if (pCardIds == null || string.IsNullOrWhiteSpace(pOutputCardId) ||
                string.IsNullOrWhiteSpace(pOutputRarity) ||
                string.IsNullOrWhiteSpace(pOutputCrateId) ||
                string.IsNullOrWhiteSpace(pRecycleId)) return false;
            EnsureLoaded();
            lock (Gate)
            {
                if (_snapshot.draws.Any(p => p != null &&
                        string.Equals(p.DrawId, pRecycleId,
                            StringComparison.Ordinal))) return false;
                var inputs = new List<HistoricalFigureCardRecycleInput>();
                var remainingSources = new Dictionary<string,
                    Dictionary<string, int>>(StringComparer.Ordinal);
                foreach (string cardId in pCardIds)
                {
                    HistoricalFigureCardDefinition card =
                        HistoricalFigureCardCatalog.Get(cardId);
                    if (card == null || card.Rarity == null ||
                        GetOwnedCount(cardId) <= 0) return false;
                    if (!remainingSources.TryGetValue(cardId,
                            out Dictionary<string, int> available))
                    {
                        available = SourcesForCard(_snapshot, card,
                            GetOwnedCount(cardId));
                        remainingSources[cardId] = available;
                    }
                    string source = TakeSource(available);
                    if (source == null) return false;
                    inputs.Add(new HistoricalFigureCardRecycleInput(cardId,
                        card.Rarity, source));
                    available[source]--;
                }
                if (!HistoricalFigureCardRecycleRules.TryCreatePlan(inputs,
                        out HistoricalFigureCardRecyclePlan plan,
                        out string _)) return false;
                HistoricalFigureCardDefinition output =
                    HistoricalFigureCardCatalog.Get(pOutputCardId);
                if (output?.Rarity == null ||
                    !output.Rarity.Equals(plan.OutputRarity)) return false;
                string outputCrateId = pOutputCrateId == "*"
                    ? HistoricalFigureCardRecycleRules.SelectWeightedCrate(
                        plan.SourceCounts, Environment.TickCount)
                    : pOutputCrateId;
                if (HistoricalFigureCardCrates.Get(outputCrateId) == null)
                    return false;

                HistoricalFigureCardCollectionSnapshot before = Clone(_snapshot);
                foreach (HistoricalFigureCardRecycleInput input in inputs)
                {
                    int owned = _snapshot.ownedCounts[input.CardId];
                    if (owned == 1) _snapshot.ownedCounts.Remove(input.CardId);
                    else _snapshot.ownedCounts[input.CardId] = owned - 1;
                    RemoveSource(_snapshot, input.CardId, input.CrateId, 1);
                }
                _snapshot.ownedCounts[pOutputCardId] =
                    _snapshot.ownedCounts.TryGetValue(pOutputCardId,
                        out int outputCount) ? outputCount + 1 : 1;
                AddSource(_snapshot, pOutputCardId, outputCrateId, 1);
                string utc = pUtc ?? DateTime.UtcNow.ToString("O");
                _snapshot.draws.Add(new HistoricalFigureCardDrawRecord
                {
                    drawId = pRecycleId,
                    cardId = pOutputCardId,
                    rarity = pOutputRarity,
                    utc = utc,
                    crateId = outputCrateId
                });
                while (_snapshot.draws.Count > MaximumDrawHistory)
                    _snapshot.draws.RemoveAt(0);
                _snapshot.lastUpdatedUtc = utc;
                if (TryWriteSnapshot(_snapshot)) return true;
                _snapshot = before;
                return false;
            }
        }

        private static string DefaultPath()
        {
            return System.IO.Path.Combine(Application.persistentDataPath,
                "AncientWarfare3", "historical_figure_cards.json");
        }

        private void EnsureLoaded()
        {
            if (!_loaded) Load();
        }

        private HistoricalFigureCardCollectionSnapshot ReadSnapshot()
        {
            if (!File.Exists(_path)) return EmptySnapshot();
            try
            {
                var loaded = JsonConvert.DeserializeObject<
                    HistoricalFigureCardCollectionSnapshot>(File.ReadAllText(_path));
                if (loaded == null || (loaded.schemaVersion != 1 &&
                    loaded.schemaVersion != SchemaVersion))
                    throw new InvalidDataException("unsupported card collection schema");
                Normalize(loaded);
                return loaded;
            }
            catch (Exception error)
            {
                PreserveCorruptFile();
                TryLogWarning("Historical card collection reset after read failure: " +
                              error.Message);
                return EmptySnapshot();
            }
        }

        private bool TryWriteSnapshot(HistoricalFigureCardCollectionSnapshot pSnapshot)
        {
            string temporary = _path + ".tmp";
            try
            {
                string directory = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                string payload = JsonConvert.SerializeObject(pSnapshot,
                    Formatting.Indented);
                using (var stream = new FileStream(temporary, FileMode.Create,
                           FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(payload);
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(_path)) File.Replace(temporary, _path, null);
                else File.Move(temporary, _path);
                return true;
            }
            catch (Exception error)
            {
                TryDelete(temporary);
                TryLogWarning("Historical card collection write failed: " +
                              error.Message);
                return false;
            }
        }

        private void PreserveCorruptFile()
        {
            try
            {
                if (!File.Exists(_path)) return;
                string backup = _path + ".corrupt";
                if (File.Exists(backup))
                    backup = _path + "." + DateTime.UtcNow.Ticks + ".corrupt";
                File.Move(_path, backup);
            }
            catch (Exception error)
            {
                TryLogWarning("Historical card corrupt-file backup failed: " +
                              error.Message);
            }
        }

        private static HistoricalFigureCardCollectionSnapshot EmptySnapshot()
        {
            return new HistoricalFigureCardCollectionSnapshot
            {
                schemaVersion = SchemaVersion,
                ownedCounts = new Dictionary<string, int>(StringComparer.Ordinal),
                ownedCrateCounts = new Dictionary<string, Dictionary<string, int>>(
                    StringComparer.Ordinal),
                draws = new List<HistoricalFigureCardDrawRecord>(),
                lastUpdatedUtc = ""
            };
        }

        private static void Normalize(HistoricalFigureCardCollectionSnapshot pSnapshot)
        {
            pSnapshot.schemaVersion = SchemaVersion;
            pSnapshot.ownedCounts = pSnapshot.ownedCounts == null
                ? new Dictionary<string, int>(StringComparer.Ordinal)
                : new Dictionary<string, int>(pSnapshot.ownedCounts
                    .Where(p => !string.IsNullOrEmpty(p.Key) && p.Value > 0)
                    .ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal);
            var normalizedSources = new Dictionary<string, Dictionary<string, int>>(
                StringComparer.Ordinal);
            foreach (var pair in pSnapshot.ownedCrateCounts ??
                     new Dictionary<string, Dictionary<string, int>>())
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                Dictionary<string, int> sources = (pair.Value ??
                        new Dictionary<string, int>())
                    .Where(p => p.Key != null && p.Value > 0)
                    .ToDictionary(p => p.Key, p => p.Value,
                        StringComparer.Ordinal);
                if (sources.Count > 0) normalizedSources[pair.Key] = sources;
            }
            bool needsSourceMigration = normalizedSources.Count == 0;
            pSnapshot.ownedCrateCounts = normalizedSources;
            var validDraws = (pSnapshot.draws ?? new List<HistoricalFigureCardDrawRecord>())
                .Where(p => p != null && !string.IsNullOrEmpty(p.DrawId) &&
                            !string.IsNullOrEmpty(p.CardId))
                .Select(Clone).ToList();
            int firstDraw = Math.Max(0, validDraws.Count - MaximumDrawHistory);
            pSnapshot.draws = validDraws.Skip(firstDraw).ToList();
            if (needsSourceMigration)
                foreach (HistoricalFigureCardDrawRecord draw in pSnapshot.draws)
                    if (!string.IsNullOrEmpty(draw.CrateId))
                        AddSource(pSnapshot, draw.CardId, draw.CrateId, 1);
            TrimSourcesToOwnedCounts(pSnapshot);
            pSnapshot.lastUpdatedUtc = pSnapshot.lastUpdatedUtc ?? "";
        }

        private static HistoricalFigureCardCollectionSnapshot Clone(
            HistoricalFigureCardCollectionSnapshot pSnapshot)
        {
            var copy = EmptySnapshot();
            if (pSnapshot == null) return copy;
            copy.lastUpdatedUtc = pSnapshot.lastUpdatedUtc ?? "";
            foreach (var pair in pSnapshot.ownedCounts ??
                     new Dictionary<string, int>())
                if (!string.IsNullOrEmpty(pair.Key) && pair.Value > 0)
                    copy.ownedCounts[pair.Key] = pair.Value;
            foreach (var pair in pSnapshot.ownedCrateCounts ??
                     new Dictionary<string, Dictionary<string, int>>())
                if (!string.IsNullOrEmpty(pair.Key))
                    copy.ownedCrateCounts[pair.Key] = new Dictionary<string, int>(
                        pair.Value ?? new Dictionary<string, int>(),
                        StringComparer.Ordinal);
            copy.draws = (pSnapshot.draws ?? new List<HistoricalFigureCardDrawRecord>())
                .Where(p => p != null).Select(Clone).ToList();
            return copy;
        }

        private static HistoricalFigureCardDrawRecord Clone(
            HistoricalFigureCardDrawRecord pRecord)
        {
            return pRecord == null ? null : new HistoricalFigureCardDrawRecord
            {
                drawId = pRecord.DrawId,
                cardId = pRecord.CardId,
                rarity = pRecord.Rarity,
                utc = pRecord.Utc,
                crateId = pRecord.CrateId
            };
        }

        private static void AddSource(HistoricalFigureCardCollectionSnapshot pSnapshot,
            string pCardId, string pCrateId, int pAmount)
        {
            if (pSnapshot == null || string.IsNullOrEmpty(pCardId) ||
                pAmount <= 0) return;
            if (pSnapshot.ownedCrateCounts == null)
                pSnapshot.ownedCrateCounts = new Dictionary<string,
                    Dictionary<string, int>>(StringComparer.Ordinal);
            if (!pSnapshot.ownedCrateCounts.TryGetValue(pCardId,
                    out Dictionary<string, int> sources))
                pSnapshot.ownedCrateCounts[pCardId] = sources =
                    new Dictionary<string, int>(StringComparer.Ordinal);
            string source = pCrateId ?? "";
            sources[source] = sources.TryGetValue(source, out int count)
                ? checked(count + pAmount) : pAmount;
        }

        private static void RemoveSource(HistoricalFigureCardCollectionSnapshot pSnapshot,
            string pCardId, string pCrateId, int pAmount)
        {
            if (pSnapshot?.ownedCrateCounts == null ||
                string.IsNullOrEmpty(pCardId) ||
                !pSnapshot.ownedCrateCounts.TryGetValue(pCardId,
                    out Dictionary<string, int> sources)) return;
            string source = pCrateId ?? "";
            if (!sources.TryGetValue(source, out int count)) return;
            count -= pAmount;
            if (count > 0) sources[source] = count;
            else sources.Remove(source);
            if (sources.Count == 0) pSnapshot.ownedCrateCounts.Remove(pCardId);
        }

        private static string SelectAnySource(
            HistoricalFigureCardCollectionSnapshot pSnapshot, string pCardId)
        {
            if (pSnapshot?.ownedCrateCounts == null ||
                !pSnapshot.ownedCrateCounts.TryGetValue(pCardId,
                    out Dictionary<string, int> sources)) return "";
            return sources.Where(p => p.Value > 0).OrderBy(p => p.Key,
                StringComparer.Ordinal).Select(p => p.Key).FirstOrDefault() ?? "";
        }

        private static Dictionary<string, int> SourcesForCard(
            HistoricalFigureCardCollectionSnapshot pSnapshot,
            HistoricalFigureCardDefinition pCard, int pOwnedCount)
        {
            var sources = new Dictionary<string, int>(StringComparer.Ordinal);
            if (pCard == null) return sources;
            if (pSnapshot?.ownedCrateCounts != null &&
                pSnapshot.ownedCrateCounts.TryGetValue(pCard.CardId,
                    out Dictionary<string, int> saved))
                foreach (KeyValuePair<string, int> pair in saved)
                    if (pair.Value > 0) sources[pair.Key] = pair.Value;
            int savedCount = sources.Values.Sum();
            int missing = Math.Max(0, pOwnedCount - savedCount);
            if (missing > 0)
            {
                HistoricalFigureCardCrate crate = HistoricalFigureCardCrates.ForYear(
                    pCard.HistoricalYear);
                string fallback = crate?.Id ??
                    HistoricalFigureCardCrates.All.FirstOrDefault()?.Id ?? "";
                sources[fallback] = sources.TryGetValue(fallback,
                    out int count) ? count + missing : missing;
            }
            return sources;
        }

        private static string TakeSource(Dictionary<string, int> pSources)
        {
            return pSources?.Where(p => p.Value > 0).OrderBy(p => p.Key,
                StringComparer.Ordinal).Select(p => p.Key).FirstOrDefault();
        }

        private static void TrimSourcesToOwnedCounts(
            HistoricalFigureCardCollectionSnapshot pSnapshot)
        {
            if (pSnapshot?.ownedCrateCounts == null) return;
            foreach (string cardId in pSnapshot.ownedCrateCounts.Keys.ToArray())
            {
                int remaining = pSnapshot.ownedCounts.TryGetValue(cardId,
                    out int owned) ? owned : 0;
                Dictionary<string, int> sources = pSnapshot.ownedCrateCounts[cardId];
                foreach (string source in sources.Keys.OrderBy(p => p,
                    StringComparer.Ordinal).ToArray())
                {
                    int keep = Math.Min(remaining, Math.Max(0, sources[source]));
                    remaining -= keep;
                    if (keep == 0) sources.Remove(source);
                    else sources[source] = keep;
                }
                if (sources.Count == 0) pSnapshot.ownedCrateCounts.Remove(cardId);
            }
        }

        private static void TryDelete(string pPath)
        {
            try { if (File.Exists(pPath)) File.Delete(pPath); }
            catch { }
        }

        private static void TryLogWarning(string pMessage)
        {
            try
            {
                Type modClass = Type.GetType("AncientWarfare3.ModClass, AncientWarfare3");
                MethodInfo logger = modClass?.GetMethod("LogWarning",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                logger?.Invoke(null, new object[] { pMessage });
            }
            catch { }
        }
    }
}
