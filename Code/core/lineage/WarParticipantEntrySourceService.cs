using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    public sealed class WarParticipantEntrySourceRecord
    {
        public long EntryId { get; set; } = -1;
        public long WarId { get; set; } = -1;
        public long KingdomId { get; set; } = -1;
        public string SourceKindId { get; set; } = "unknown";
        public WarParticipantEntrySourceKind SourceKind { get; set; }
        public long SourceKingdomId { get; set; } = -1;
        public bool Active { get; set; }
        public double CreatedTime { get; set; } = -1;
        public double EndedTime { get; set; } = -1;
    }

    public sealed class WarParticipantEntrySourceService
    {
        private const int MaximumReadLimit = 64;
        private const int MaximumPendingSources = 1024;
        private const string SeparatePeaceExitKind = "separate_peace_exit";
        private readonly SQLiteConnection _db;
        private readonly object _runtimeLock = new object();
        private readonly Dictionary<string, PendingSource> _pendingSources =
            new Dictionary<string, PendingSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingClosure> _pendingClosures =
            new Dictionary<string, PendingClosure>(StringComparer.Ordinal);
        private readonly Dictionary<long, PendingWarClosure>
            _pendingWarClosures = new Dictionary<long, PendingWarClosure>();
        private readonly HashSet<string> _knownExitMarkers =
            new HashSet<string>(StringComparer.Ordinal);

        private sealed class PendingSource
        {
            public long WarId;
            public long KingdomId;
            public WarParticipantEntrySourceKind Kind;
            public long SourceKingdomId;
            public double CreatedTime;
            public double EndedTime = -1;
        }

        private sealed class PendingClosure
        {
            public long WarId;
            public long KingdomId;
            public double EndedTime;
        }

        private sealed class PendingWarClosure
        {
            public long WarId;
            public double EndedTime;
        }

        public static WarParticipantEntrySourceService Instance { get; } =
            new WarParticipantEntrySourceService();

        public WarParticipantEntrySourceService()
        {
        }

        public WarParticipantEntrySourceService(SQLiteConnection pDb)
        {
            _db = pDb;
        }

        private SQLiteConnection DB =>
            _db ?? LineageArchiveManager.Instance?.OperatingDB;

        public bool TryRecordSource(long pWarId, long pKingdomId,
            WarParticipantEntrySourceKind pSourceKind,
            long pSourceKingdomId, double pCreatedTime)
        {
            if (pSourceKind == WarParticipantEntrySourceKind.Unknown)
                return false;
            if (DB == null && CanUseStartupFallback())
                return QueuePendingSource(pWarId, pKingdomId, pSourceKind,
                    pSourceKingdomId, pCreatedTime);
            bool recorded = TryRecord(pWarId, pKingdomId,
                SourceId(pSourceKind), pSourceKingdomId, pCreatedTime,
                out Exception failure);
            if (!recorded && pSourceKind ==
                    WarParticipantEntrySourceKind.MainBelligerent &&
                IsBusyOrLocked(failure))
                return QueuePendingSource(pWarId, pKingdomId, pSourceKind,
                    pSourceKingdomId, pCreatedTime);
            if (recorded && pSourceKind ==
                WarParticipantEntrySourceKind.SeparatePeaceExit)
                RememberExitMarker(pWarId, pKingdomId);
            return recorded;
        }

        public bool TryEndSource(long pWarId, long pKingdomId,
            WarParticipantEntrySourceKind pSourceKind,
            long pSourceKingdomId, double pEndedTime)
        {
            EndPendingSource(pWarId, pKingdomId, pSourceKind,
                pSourceKingdomId, pEndedTime);
            SQLiteConnection db = DB;
            if (db == null && CanUseStartupFallback()) return true;
            if (db == null || pWarId < 0 || pKingdomId < 0 ||
                pSourceKind == WarParticipantEntrySourceKind.Unknown)
                return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "UPDATE " +
                    WarParticipantEntrySourceTableItem.GetTableName() +
                    " SET ACTIVE=0,ENDED_TIME=@ended WHERE WAR_ID=@war " +
                    "AND KINGDOM_ID=@kingdom AND SOURCE_KIND=@kind AND " +
                    "SOURCE_KINGDOM_ID=@source AND ACTIVE=1";
                command.Parameters.AddWithValue("@ended", pEndedTime);
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@kind", SourceId(pSourceKind));
                command.Parameters.AddWithValue("@source", pSourceKingdomId);
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("War participant source end failed: " +
                                    error.Message);
                return false;
            }
        }

        public IReadOnlyList<WarParticipantEntrySourceRecord>
            ReadActiveSources(long pWarId, long pKingdomId, int pLimit)
        {
            var result = new List<WarParticipantEntrySourceRecord>();
            if (pLimit <= 0 || !TryReadActiveSources(pWarId, pKingdomId,
                    out IReadOnlyList<WarParticipantEntrySourceRecord> rows))
                return result;
            int limit = Math.Min(MaximumReadLimit, pLimit);
            for (int i = 0; i < rows.Count && i < limit; i++)
                result.Add(rows[i]);
            return result;
        }

        public bool TryReadActiveSources(long pWarId, long pKingdomId,
            out IReadOnlyList<WarParticipantEntrySourceRecord> pSources)
        {
            pSources = Array.Empty<WarParticipantEntrySourceRecord>();
            SQLiteConnection db = DB;
            if (db == null || pWarId < 0 || pKingdomId < 0) return false;
            var result = new List<WarParticipantEntrySourceRecord>();
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT ENTRY_ID,WAR_ID,KINGDOM_ID," +
                    "SOURCE_KIND,SOURCE_KINGDOM_ID,ACTIVE,CREATED_TIME," +
                    "ENDED_TIME FROM " +
                    WarParticipantEntrySourceTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND KINGDOM_ID=@kingdom AND " +
                    "ACTIVE=1 AND SOURCE_KIND<>@exit ORDER BY " +
                    "CREATED_TIME ASC,ENTRY_ID ASC LIMIT @limit";
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@exit", SeparatePeaceExitKind);
                command.Parameters.AddWithValue("@limit",
                    MaximumReadLimit + 1);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (result.Count >= MaximumReadLimit) return false;
                    result.Add(ReadRecord(reader));
                }
                pSources = result;
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("War participant sources read failed: " +
                                    error.Message);
                return false;
            }
        }

        public bool TryMarkSeparatePeaceExit(long pWarId, long pKingdomId,
            double pCreatedTime)
        {
            return TryRecordSource(pWarId, pKingdomId,
                WarParticipantEntrySourceKind.SeparatePeaceExit, -1,
                pCreatedTime);
        }

        public bool HasSeparatePeaceExit(long pWarId, long pKingdomId)
        {
            return !TryHasSeparatePeaceExit(pWarId, pKingdomId,
                out bool exited) || exited;
        }

        public bool TryHasSeparatePeaceExit(long pWarId, long pKingdomId,
            out bool pExited)
        {
            pExited = false;
            SQLiteConnection db = DB;
            if (pWarId < 0 || pKingdomId < 0) return false;
            if (db == null && CanUseStartupFallback())
            {
                if (!HasKnownExitMarker(pWarId, pKingdomId)) return false;
                pExited = true;
                return true;
            }
            if (db == null) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT 1 FROM " +
                    WarParticipantEntrySourceTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND KINGDOM_ID=@kingdom AND " +
                    "SOURCE_KIND=@kind AND ACTIVE=1 LIMIT 1";
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@kind", SeparatePeaceExitKind);
                pExited = command.ExecuteScalar() != null;
                if (pExited) RememberExitMarker(pWarId, pKingdomId);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Separate peace exit lookup failed: " +
                                    error.Message);
                pExited = false;
                return false;
            }
        }

        public bool TryCanJoinWar(long pWarId, long pKingdomId,
            out bool pCanJoin)
        {
            pCanJoin = false;
            if (!TryHasSeparatePeaceExit(pWarId, pKingdomId,
                    out bool exited)) return false;
            pCanJoin = !exited;
            return true;
        }

        public bool TryReadActiveSourceFingerprint(long pWarId,
            long pKingdomId, out string pFingerprint)
        {
            pFingerprint = "unknown";
            if (!TryReadActiveSources(pWarId, pKingdomId,
                    out IReadOnlyList<WarParticipantEntrySourceRecord>
                        sources)) return false;
            var facts = new List<WarParticipantSourceFact>(sources.Count);
            for (int i = 0; i < sources.Count; i++)
            {
                WarParticipantEntrySourceRecord source = sources[i];
                facts.Add(new WarParticipantSourceFact(source.SourceKind,
                    source.SourceKingdomId));
            }
            pFingerprint = WarParticipantRosterRules.
                BuildSourceFingerprint(facts);
            return true;
        }

        public bool TryEndAllActiveSources(long pWarId, long pKingdomId,
            double pEndedTime)
        {
            EndPendingSources(pWarId, pKingdomId, pEndedTime);
            SQLiteConnection db = DB;
            if (pWarId < 0 || pKingdomId < 0) return false;
            if (db == null)
                return QueuePendingClosure(pWarId, pKingdomId, pEndedTime);
            if (TryCloseActiveSources(db, pWarId, pKingdomId,
                    pEndedTime)) return true;
            return QueuePendingClosure(pWarId, pKingdomId, pEndedTime);
        }

        public bool TryEndAllActiveSourcesForWar(long pWarId,
            double pEndedTime)
        {
            if (pWarId < 0) return false;
            EndPendingSourcesForWar(pWarId, pEndedTime);
            SQLiteConnection db = DB;
            if (db == null)
                return QueuePendingWarClosure(pWarId, pEndedTime);
            if (TryCloseActiveSourcesForWar(db, pWarId, pEndedTime,
                    out Exception failure)) return true;
            return IsBusyOrLocked(failure) &&
                   QueuePendingWarClosure(pWarId, pEndedTime);
        }

        private static bool TryCloseActiveSources(SQLiteConnection pDb,
            long pWarId, long pKingdomId, double pEndedTime)
        {
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "UPDATE " +
                    WarParticipantEntrySourceTableItem.GetTableName() +
                    " SET ACTIVE=0,ENDED_TIME=@ended WHERE WAR_ID=@war " +
                    "AND KINGDOM_ID=@kingdom AND ACTIVE=1 AND " +
                    "SOURCE_KIND<>@exit AND CREATED_TIME<=@ended";
                command.Parameters.AddWithValue("@ended", pEndedTime);
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@exit", SeparatePeaceExitKind);
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("War participant source close failed: " +
                                    error.Message);
                return false;
            }
        }

        public bool TryReadSeparatePeaceExit(long pWarId, long pKingdomId,
            out WarParticipantEntrySourceRecord pRecord)
        {
            pRecord = null;
            SQLiteConnection db = DB;
            if (db == null || pWarId < 0 || pKingdomId < 0) return false;
            try
            {
                using var command = new SQLiteCommand(db);
                command.CommandText = "SELECT ENTRY_ID,WAR_ID,KINGDOM_ID," +
                    "SOURCE_KIND,SOURCE_KINGDOM_ID,ACTIVE,CREATED_TIME," +
                    "ENDED_TIME FROM " +
                    WarParticipantEntrySourceTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND KINGDOM_ID=@kingdom AND " +
                    "SOURCE_KIND=@kind AND ACTIVE=1 ORDER BY ENTRY_ID DESC " +
                    "LIMIT 1";
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@kind", SeparatePeaceExitKind);
                using SQLiteDataReader reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                pRecord = ReadRecord(reader);
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning("Separate peace exit read failed: " +
                                    error.Message);
                return false;
            }
        }

        public int FlushPendingSources(int pBudget = 32)
        {
            if (pBudget <= 0 || DB == null) return 0;
            int flushed = FlushPendingWarClosures(pBudget);
            int closureBudget = pBudget - flushed;
            if (closureBudget > 0)
                flushed += FlushPendingClosures(closureBudget);
            int remainingBudget = pBudget - flushed;
            if (remainingBudget <= 0) return flushed;
            var batch = new List<KeyValuePair<string, PendingSource>>();
            lock (_runtimeLock)
            {
                foreach (KeyValuePair<string, PendingSource> pair in
                         _pendingSources)
                {
                    batch.Add(pair);
                    if (batch.Count >= remainingBudget) break;
                }
            }

            for (int i = 0; i < batch.Count; i++)
            {
                PendingSource source = batch[i].Value;
                bool recorded = source.EndedTime >= 0
                    ? TryRecordClosed(source.WarId, source.KingdomId,
                        SourceId(source.Kind), source.SourceKingdomId,
                        source.CreatedTime, source.EndedTime)
                    : TryRecord(source.WarId, source.KingdomId,
                        SourceId(source.Kind), source.SourceKingdomId,
                        source.CreatedTime);
                if (!recorded) continue;
                lock (_runtimeLock)
                    _pendingSources.Remove(batch[i].Key);
                if (source.Kind ==
                    WarParticipantEntrySourceKind.SeparatePeaceExit)
                    RememberExitMarker(source.WarId, source.KingdomId);
                flushed++;
            }
            return flushed;
        }

        private int FlushPendingWarClosures(int pBudget)
        {
            SQLiteConnection db = DB;
            if (pBudget <= 0 || db == null) return 0;
            var batch = new List<KeyValuePair<long, PendingWarClosure>>();
            lock (_runtimeLock)
            {
                foreach (KeyValuePair<long, PendingWarClosure> pair in
                         _pendingWarClosures)
                {
                    batch.Add(new KeyValuePair<long, PendingWarClosure>(
                        pair.Key, new PendingWarClosure
                        {
                            WarId = pair.Value.WarId,
                            EndedTime = pair.Value.EndedTime
                        }));
                    if (batch.Count >= pBudget) break;
                }
            }

            int flushed = 0;
            for (int i = 0; i < batch.Count; i++)
            {
                PendingWarClosure closure = batch[i].Value;
                if (!TryCloseActiveSourcesForWar(db, closure.WarId,
                        closure.EndedTime, out _)) continue;
                lock (_runtimeLock)
                {
                    if (_pendingWarClosures.TryGetValue(batch[i].Key,
                            out PendingWarClosure current) &&
                        current.EndedTime <= closure.EndedTime)
                        _pendingWarClosures.Remove(batch[i].Key);
                }
                flushed++;
            }
            return flushed;
        }

        private int FlushPendingClosures(int pBudget)
        {
            SQLiteConnection db = DB;
            if (pBudget <= 0 || db == null) return 0;
            var batch = new List<KeyValuePair<string, PendingClosure>>();
            lock (_runtimeLock)
            {
                foreach (KeyValuePair<string, PendingClosure> pair in
                         _pendingClosures)
                {
                    batch.Add(new KeyValuePair<string, PendingClosure>(
                        pair.Key, new PendingClosure
                        {
                            WarId = pair.Value.WarId,
                            KingdomId = pair.Value.KingdomId,
                            EndedTime = pair.Value.EndedTime
                        }));
                    if (batch.Count >= pBudget) break;
                }
            }

            int flushed = 0;
            for (int i = 0; i < batch.Count; i++)
            {
                PendingClosure closure = batch[i].Value;
                if (!TryCloseActiveSources(db, closure.WarId,
                        closure.KingdomId, closure.EndedTime)) continue;
                lock (_runtimeLock)
                {
                    if (_pendingClosures.TryGetValue(batch[i].Key,
                            out PendingClosure current) &&
                        current.EndedTime <= closure.EndedTime)
                        _pendingClosures.Remove(batch[i].Key);
                }
                flushed++;
            }
            return flushed;
        }

        public void ClearRuntime()
        {
            lock (_runtimeLock)
            {
                _pendingSources.Clear();
                _pendingClosures.Clear();
                _pendingWarClosures.Clear();
                _knownExitMarkers.Clear();
            }
        }

        private bool QueuePendingSource(long pWarId, long pKingdomId,
            WarParticipantEntrySourceKind pKind, long pSourceKingdomId,
            double pCreatedTime)
        {
            if (pWarId < 0 || pKingdomId < 0 ||
                pKind == WarParticipantEntrySourceKind.Unknown) return false;
            string key = SourceKey(pWarId, pKingdomId, pKind,
                pSourceKingdomId);
            lock (_runtimeLock)
            {
                if (_pendingSources.ContainsKey(key)) return true;
                if (_pendingSources.Count >= MaximumPendingSources)
                    return false;
                _pendingSources[key] = new PendingSource
                {
                    WarId = pWarId,
                    KingdomId = pKingdomId,
                    Kind = pKind,
                    SourceKingdomId = pSourceKingdomId,
                    CreatedTime = pCreatedTime
                };
                if (pKind ==
                    WarParticipantEntrySourceKind.SeparatePeaceExit)
                    _knownExitMarkers.Add(ExitKey(pWarId, pKingdomId));
                return true;
            }
        }

        private void EndPendingSource(long pWarId, long pKingdomId,
            WarParticipantEntrySourceKind pKind, long pSourceKingdomId,
            double pEndedTime)
        {
            lock (_runtimeLock)
            {
                if (_pendingSources.TryGetValue(SourceKey(pWarId,
                        pKingdomId, pKind, pSourceKingdomId),
                        out PendingSource source) &&
                    source.Kind !=
                    WarParticipantEntrySourceKind.SeparatePeaceExit)
                    source.EndedTime = Math.Max(source.EndedTime,
                        pEndedTime);
            }
        }

        private void EndPendingSources(long pWarId, long pKingdomId,
            double pEndedTime)
        {
            string prefix = pWarId + ":" + pKingdomId + ":";
            lock (_runtimeLock)
            {
                foreach (KeyValuePair<string, PendingSource> pair in
                         _pendingSources)
                    if (pair.Key.StartsWith(prefix,
                            StringComparison.Ordinal) &&
                        pair.Value.Kind !=
                            WarParticipantEntrySourceKind.SeparatePeaceExit)
                        pair.Value.EndedTime = Math.Max(
                            pair.Value.EndedTime, pEndedTime);
            }
        }

        private void EndPendingSourcesForWar(long pWarId,
            double pEndedTime)
        {
            string prefix = pWarId + ":";
            lock (_runtimeLock)
            {
                foreach (KeyValuePair<string, PendingSource> pair in
                         _pendingSources)
                    if (pair.Key.StartsWith(prefix,
                            StringComparison.Ordinal) &&
                        pair.Value.Kind !=
                            WarParticipantEntrySourceKind.SeparatePeaceExit)
                        pair.Value.EndedTime = Math.Max(
                            pair.Value.EndedTime, pEndedTime);
            }
        }

        private bool QueuePendingClosure(long pWarId, long pKingdomId,
            double pEndedTime)
        {
            if (pWarId < 0 || pKingdomId < 0) return false;
            string key = ExitKey(pWarId, pKingdomId);
            lock (_runtimeLock)
            {
                if (_pendingClosures.TryGetValue(key,
                        out PendingClosure existing))
                {
                    existing.EndedTime = Math.Max(existing.EndedTime,
                        pEndedTime);
                    return true;
                }
                if (_pendingClosures.Count >= MaximumPendingSources)
                    return false;
                _pendingClosures[key] = new PendingClosure
                {
                    WarId = pWarId,
                    KingdomId = pKingdomId,
                    EndedTime = pEndedTime
                };
                return true;
            }
        }

        private bool QueuePendingWarClosure(long pWarId, double pEndedTime)
        {
            if (pWarId < 0) return false;
            lock (_runtimeLock)
            {
                if (_pendingWarClosures.TryGetValue(pWarId,
                        out PendingWarClosure existing))
                {
                    existing.EndedTime = Math.Max(existing.EndedTime,
                        pEndedTime);
                    return true;
                }
                if (_pendingWarClosures.Count >= MaximumPendingSources)
                    return false;
                _pendingWarClosures[pWarId] = new PendingWarClosure
                {
                    WarId = pWarId,
                    EndedTime = pEndedTime
                };
                return true;
            }
        }

        private bool CanUseStartupFallback()
        {
            if (_db != null) return false;
            LineageArchiveManager manager = LineageArchiveManager.Instance;
            return manager == null || !manager.IsOperational;
        }

        private void RememberExitMarker(long pWarId, long pKingdomId)
        {
            lock (_runtimeLock)
                _knownExitMarkers.Add(ExitKey(pWarId, pKingdomId));
        }

        private bool HasKnownExitMarker(long pWarId, long pKingdomId)
        {
            lock (_runtimeLock)
                return _knownExitMarkers.Contains(
                    ExitKey(pWarId, pKingdomId));
        }

        private static string SourceKey(long pWarId, long pKingdomId,
            WarParticipantEntrySourceKind pKind, long pSourceKingdomId)
        {
            return pWarId + ":" + pKingdomId + ":" + SourceId(pKind) +
                   ":" + pSourceKingdomId;
        }

        private static string ExitKey(long pWarId, long pKingdomId)
        {
            return pWarId + ":" + pKingdomId;
        }

        private bool TryRecord(long pWarId, long pKingdomId,
            string pSourceKind, long pSourceKingdomId, double pCreatedTime)
        {
            return TryRecord(pWarId, pKingdomId, pSourceKind,
                pSourceKingdomId, pCreatedTime, out _);
        }

        private bool TryRecord(long pWarId, long pKingdomId,
            string pSourceKind, long pSourceKingdomId, double pCreatedTime,
            out Exception pFailure)
        {
            pFailure = null;
            SQLiteConnection db = DB;
            if (db == null || pWarId < 0 || pKingdomId < 0 ||
                string.IsNullOrEmpty(pSourceKind)) return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = db.BeginTransaction(IsolationLevel.Serializable);
                long entryId = TableIdAllocator.Next(db, transaction,
                    WarParticipantEntrySourceTableItem.GetTableName(),
                    "ENTRY_ID");
                using var command = new SQLiteCommand(db)
                {
                    Transaction = transaction,
                    CommandText = "INSERT OR IGNORE INTO " +
                        WarParticipantEntrySourceTableItem.GetTableName() +
                        " (ENTRY_ID,WAR_ID,KINGDOM_ID,SOURCE_KIND," +
                        "SOURCE_KINGDOM_ID,ACTIVE,CREATED_TIME,ENDED_TIME) " +
                        "VALUES (@id,@war,@kingdom,@kind,@source,1," +
                        "@created,-1)"
                };
                command.Parameters.AddWithValue("@id", entryId);
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@kind", pSourceKind);
                command.Parameters.AddWithValue("@source", pSourceKingdomId);
                command.Parameters.AddWithValue("@created", pCreatedTime);
                bool recorded = command.ExecuteNonQuery() == 1 ||
                    HasActiveRecord(db, transaction, pWarId, pKingdomId,
                        pSourceKind, pSourceKingdomId);
                if (!recorded) return false;
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                pFailure = error;
                ModClass.LogWarning("War participant source record failed: " +
                                    error.Message);
                try { transaction?.Rollback(); }
                catch { }
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private static bool TryCloseActiveSourcesForWar(SQLiteConnection pDb,
            long pWarId, double pEndedTime, out Exception pFailure)
        {
            pFailure = null;
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText = "UPDATE " +
                    WarParticipantEntrySourceTableItem.GetTableName() +
                    " SET ACTIVE=0,ENDED_TIME=@ended WHERE WAR_ID=@war " +
                    "AND ACTIVE=1 AND SOURCE_KIND<>@exit AND " +
                    "CREATED_TIME<=@ended";
                command.Parameters.AddWithValue("@ended", pEndedTime);
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@exit", SeparatePeaceExitKind);
                command.ExecuteNonQuery();
                return true;
            }
            catch (Exception error)
            {
                pFailure = error;
                ModClass.LogWarning(
                    "Whole-war participant source close failed: " +
                    error.Message);
                return false;
            }
        }

        private static bool IsBusyOrLocked(Exception pFailure)
        {
            for (Exception current = pFailure; current != null;
                 current = current.InnerException)
                if (current is SQLiteException sqlite &&
                    (sqlite.ResultCode == SQLiteErrorCode.Busy ||
                     sqlite.ResultCode == SQLiteErrorCode.Locked))
                    return true;
            return false;
        }

        private bool TryRecordClosed(long pWarId, long pKingdomId,
            string pSourceKind, long pSourceKingdomId,
            double pCreatedTime, double pEndedTime)
        {
            SQLiteConnection db = DB;
            if (db == null || pWarId < 0 || pKingdomId < 0 ||
                string.IsNullOrEmpty(pSourceKind) || pEndedTime < 0)
                return false;
            SQLiteTransaction transaction = null;
            try
            {
                transaction = db.BeginTransaction(IsolationLevel.Serializable);
                if (HasClosedRecord(db, transaction, pWarId, pKingdomId,
                        pSourceKind, pSourceKingdomId, pCreatedTime,
                        pEndedTime))
                {
                    transaction.Commit();
                    return true;
                }
                long entryId = TableIdAllocator.Next(db, transaction,
                    WarParticipantEntrySourceTableItem.GetTableName(),
                    "ENTRY_ID");
                using var command = new SQLiteCommand(db)
                {
                    Transaction = transaction,
                    CommandText = "INSERT INTO " +
                        WarParticipantEntrySourceTableItem.GetTableName() +
                        " (ENTRY_ID,WAR_ID,KINGDOM_ID,SOURCE_KIND," +
                        "SOURCE_KINGDOM_ID,ACTIVE,CREATED_TIME,ENDED_TIME) " +
                        "VALUES (@id,@war,@kingdom,@kind,@source,0," +
                        "@created,@ended)"
                };
                command.Parameters.AddWithValue("@id", entryId);
                command.Parameters.AddWithValue("@war", pWarId);
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@kind", pSourceKind);
                command.Parameters.AddWithValue("@source", pSourceKingdomId);
                command.Parameters.AddWithValue("@created", pCreatedTime);
                command.Parameters.AddWithValue("@ended", pEndedTime);
                if (command.ExecuteNonQuery() != 1) return false;
                transaction.Commit();
                return true;
            }
            catch (Exception error)
            {
                ModClass.LogWarning(
                    "Closed war participant source record failed: " +
                    error.Message);
                try { transaction?.Rollback(); }
                catch { }
                return false;
            }
            finally
            {
                transaction?.Dispose();
            }
        }

        private static bool HasActiveRecord(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pWarId, long pKingdomId,
            string pSourceKind, long pSourceKingdomId)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "SELECT 1 FROM " +
                    WarParticipantEntrySourceTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND KINGDOM_ID=@kingdom AND " +
                    "SOURCE_KIND=@kind AND SOURCE_KINGDOM_ID=@source AND " +
                    "ACTIVE=1 LIMIT 1"
            };
            command.Parameters.AddWithValue("@war", pWarId);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@kind", pSourceKind);
            command.Parameters.AddWithValue("@source", pSourceKingdomId);
            return command.ExecuteScalar() != null;
        }

        private static bool HasClosedRecord(SQLiteConnection pDb,
            SQLiteTransaction pTransaction, long pWarId, long pKingdomId,
            string pSourceKind, long pSourceKingdomId,
            double pCreatedTime, double pEndedTime)
        {
            using var command = new SQLiteCommand(pDb)
            {
                Transaction = pTransaction,
                CommandText = "SELECT 1 FROM " +
                    WarParticipantEntrySourceTableItem.GetTableName() +
                    " WHERE WAR_ID=@war AND KINGDOM_ID=@kingdom AND " +
                    "SOURCE_KIND=@kind AND SOURCE_KINGDOM_ID=@source AND " +
                    "ACTIVE=0 AND CREATED_TIME=@created AND " +
                    "ENDED_TIME=@ended LIMIT 1"
            };
            command.Parameters.AddWithValue("@war", pWarId);
            command.Parameters.AddWithValue("@kingdom", pKingdomId);
            command.Parameters.AddWithValue("@kind", pSourceKind);
            command.Parameters.AddWithValue("@source", pSourceKingdomId);
            command.Parameters.AddWithValue("@created", pCreatedTime);
            command.Parameters.AddWithValue("@ended", pEndedTime);
            return command.ExecuteScalar() != null;
        }

        private static WarParticipantEntrySourceRecord ReadRecord(
            SQLiteDataReader pReader)
        {
            string sourceKind = pReader.IsDBNull(3)
                ? "unknown"
                : pReader.GetString(3);
            return new WarParticipantEntrySourceRecord
            {
                EntryId = pReader.GetInt64(0),
                WarId = pReader.GetInt64(1),
                KingdomId = pReader.GetInt64(2),
                SourceKindId = sourceKind,
                SourceKind = ParseSource(sourceKind),
                SourceKingdomId = pReader.IsDBNull(4)
                    ? -1
                    : pReader.GetInt64(4),
                Active = pReader.GetInt32(5) != 0,
                CreatedTime = pReader.GetDouble(6),
                EndedTime = pReader.IsDBNull(7) ? -1 : pReader.GetDouble(7)
            };
        }

        private static string SourceId(WarParticipantEntrySourceKind pSource)
        {
            return pSource switch
            {
                WarParticipantEntrySourceKind.MainBelligerent =>
                    "main_belligerent",
                WarParticipantEntrySourceKind.AllianceCall => "alliance_call",
                WarParticipantEntrySourceKind.FormalVassalObligation =>
                    "formal_vassal_obligation",
                WarParticipantEntrySourceKind.IndependentDeclaration =>
                    "independent_declaration",
                WarParticipantEntrySourceKind.ScriptedJoin => "scripted_join",
                WarParticipantEntrySourceKind.SeparatePeaceExit =>
                    SeparatePeaceExitKind,
                _ => "unknown"
            };
        }

        private static WarParticipantEntrySourceKind ParseSource(string pSource)
        {
            return pSource switch
            {
                "main_belligerent" =>
                    WarParticipantEntrySourceKind.MainBelligerent,
                "alliance_call" => WarParticipantEntrySourceKind.AllianceCall,
                "formal_vassal_obligation" =>
                    WarParticipantEntrySourceKind.FormalVassalObligation,
                "independent_declaration" =>
                    WarParticipantEntrySourceKind.IndependentDeclaration,
                "scripted_join" => WarParticipantEntrySourceKind.ScriptedJoin,
                SeparatePeaceExitKind =>
                    WarParticipantEntrySourceKind.SeparatePeaceExit,
                _ => WarParticipantEntrySourceKind.Unknown
            };
        }
    }
}
