using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    public sealed class HistoricalFigureCardDrawRecord
    {
        public string drawId;
        public string cardId;
        public string rarity;
        public string utc;

        [JsonIgnore]
        public string DrawId => drawId ?? "";
        [JsonIgnore]
        public string CardId => cardId ?? "";
        [JsonIgnore]
        public string Rarity => rarity ?? "";
        [JsonIgnore]
        public string Utc => utc ?? "";
    }

    public sealed class HistoricalFigureCardCollectionSnapshot
    {
        public int schemaVersion = 1;
        public Dictionary<string, int> ownedCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        public List<HistoricalFigureCardDrawRecord> draws =
            new List<HistoricalFigureCardDrawRecord>();
        public string lastUpdatedUtc = "";
    }

    /// <summary>
    /// Player-level card ownership. This file intentionally lives outside the
    /// world save and outside FigureStateStore.
    /// </summary>
    public sealed class HistoricalFigureCardCollectionStore
    {
        private const int SchemaVersion = 1;
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
                _snapshot.draws.Add(new HistoricalFigureCardDrawRecord
                {
                    drawId = pDrawId,
                    cardId = pCardId,
                    rarity = pRarity,
                    utc = pUtc ?? ""
                });
                while (_snapshot.draws.Count > MaximumDrawHistory)
                    _snapshot.draws.RemoveAt(0);
                _snapshot.lastUpdatedUtc = pUtc ?? "";
                if (!TryWriteSnapshot(_snapshot))
                {
                    _snapshot.ownedCounts[pCardId] = count;
                    if (_snapshot.ownedCounts[pCardId] <= 0)
                        _snapshot.ownedCounts.Remove(pCardId);
                    _snapshot.draws.RemoveAll(p => p != null &&
                        string.Equals(p.DrawId, pDrawId, StringComparison.Ordinal));
                    return false;
                }
                return true;
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
                if (loaded == null || loaded.schemaVersion != SchemaVersion)
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
            var validDraws = (pSnapshot.draws ?? new List<HistoricalFigureCardDrawRecord>())
                .Where(p => p != null && !string.IsNullOrEmpty(p.DrawId) &&
                            !string.IsNullOrEmpty(p.CardId))
                .Select(Clone).ToList();
            int firstDraw = Math.Max(0, validDraws.Count - MaximumDrawHistory);
            pSnapshot.draws = validDraws.Skip(firstDraw).ToList();
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
                utc = pRecord.Utc
            };
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
