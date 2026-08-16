using System;
using System.Collections.Generic;
using System.IO;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.court;
using Newtonsoft.Json;

namespace AncientWarfare3.core.lineage
{
    internal enum SyntheticMobilizationPhase
    {
        Mobilizing,
        Active,
        Demobilizing,
        Complete
    }

    internal sealed class SyntheticMobilizationRecord
    {
        internal readonly SortedSet<long> ActorIds = new SortedSet<long>();
        internal long WarId;
        internal long KingdomId;
        internal long CityId;
        internal long ArmyId = -1L;
        internal int PopulationSnapshot;
        internal int LawPercent;
        internal int Quota;
        internal int InitialCreated;
        internal int ReplacementRemaining;
        internal int ReplacementCreated;
        internal int LiveSynthetic;
        internal SyntheticMobilizationPhase Phase;
    }

    internal static class SyntheticMobilizationLedgerService
    {
        private enum LoadReconciliationPhase
        {
            None,
            ResetRecords,
            ScanActors,
            ValidateRecords
        }

        private const int SnapshotVersion = 1;
        private const int LoadActorReconciliationBatchLimit = 64;
        private const int LoadRecordReconciliationBatchLimit = 16;
        private const int LifecycleRecordBatchLimit = 16;
        private const string SnapshotFileName =
            "aw3_synthetic_mobilization.json";
        private const string LegacySnapshotFileName =
            "aw3_city_reserve_pools.json";

        private readonly struct PendingCity
        {
            internal PendingCity(long pWarId, long pKingdomId, long pCityId)
            {
                WarId = pWarId;
                KingdomId = pKingdomId;
                CityId = pCityId;
            }

            internal long WarId { get; }
            internal long KingdomId { get; }
            internal long CityId { get; }
        }

        private sealed class PersistedSnapshot
        {
            public int version = SnapshotVersion;
            public List<PersistedRecord> records = new List<PersistedRecord>();
            public List<long> ended_wars = new List<long>();
        }

        private sealed class PersistedRecord
        {
            public long war_id;
            public long kingdom_id;
            public long city_id;
            public long army_id = -1L;
            public int population_snapshot;
            public int law_percent;
            public int quota;
            public int initial_created;
            public int replacement_remaining;
            public int replacement_created;
            public int live_synthetic;
            public int phase;
            public List<long> actor_ids = new List<long>();
        }

        private sealed class LegacySnapshot
        {
            public List<LegacyKingdom> kingdoms = new List<LegacyKingdom>();
        }

        private sealed class LegacyKingdom
        {
            public long kingdom_id = -1L;
            public List<LegacyCity> cities = new List<LegacyCity>();
        }

        private sealed class LegacyCity
        {
            public long city_id = -1L;
            public int authentic_population = 0;
            public int synthetic_mobilized = 0;
            public int war_reserve_capacity = 0;
            public int war_reserve_consumed = 0;
            public long war_emergency_id = -1L;
        }

        private static readonly Dictionary<string, SyntheticMobilizationRecord>
            Records = new Dictionary<string, SyntheticMobilizationRecord>();
        private static readonly Dictionary<long, List<string>> RecordKeysByWar =
            new Dictionary<long, List<string>>();
        private static readonly Dictionary<long, List<string>> RecordKeysByCity =
            new Dictionary<long, List<string>>();
        private static readonly Queue<PendingCity> PendingCities =
            new Queue<PendingCity>();
        private static readonly HashSet<string> PendingCityKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<long> EndedWars = new HashSet<long>();
        private static readonly Dictionary<long, int> LiveSyntheticByCity =
            new Dictionary<long, int>();
        private static readonly Queue<string> RecordWork =
            new Queue<string>();
        private static readonly HashSet<string> QueuedRecordKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<long> ActorBatch = new List<long>(
            SyntheticMobilizationRules.DemobilizationBatchLimit);
        private static readonly Queue<long> OrphanSyntheticActorIds =
            new Queue<long>();
        private static readonly Queue<PendingParticipantWork>
            PendingParticipantWorks = new Queue<PendingParticipantWork>();
        private static readonly HashSet<string> PendingParticipantWorkKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PendingParticipantWork>
            PendingParticipantWorkByKey =
                new Dictionary<string, PendingParticipantWork>(
                    StringComparer.Ordinal);
        private static readonly Queue<PendingWarRecordWork>
            PendingWarRecordWorks = new Queue<PendingWarRecordWork>();
        private static readonly HashSet<string> PendingWarRecordWorkKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, PendingWarRecordWork>
            PendingWarRecordWorkByKey =
                new Dictionary<string, PendingWarRecordWork>(
                    StringComparer.Ordinal);
        private static readonly Queue<PendingCityRecordWork>
            PendingCityRecordWorks = new Queue<PendingCityRecordWork>();
        private static readonly HashSet<long> PendingCityRecordWorkKeys =
            new HashSet<long>();
        private static readonly Dictionary<long, PendingCityRecordWork>
            PendingCityRecordWorkByKey =
                new Dictionary<long, PendingCityRecordWork>();
        private static bool _loadActorReconciliationPending;
        private static int _loadActorReconciliationCursor;
        private static LoadReconciliationPhase _loadReconciliationPhase;
        private static IEnumerator<KeyValuePair<string,
            SyntheticMobilizationRecord>> _loadRecordEnumerator;
        private static bool _warEnrollmentScanActive;
        private static bool _warEnrollmentScanRequested;
        private static int _warEnrollmentScanCursor;

        internal static void OnWarStarted(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                !ZhuluWarService.ShouldEnrollInAw3Systems(pWar)) return;
            EndedWars.Remove(pWar.data.id);
            EnqueueWarParticipants(pWar.data.id);
        }

        internal static void OnKingdomJoinedWar(War pWar, Kingdom pKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pWar.hasEnded()) return;
            EnqueueParticipant(pWar.data.id, pKingdom?.data?.id ?? -1L);
        }

        internal static void OnKingdomLeftWar(War pWar, Kingdom pKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pKingdom?.data == null) return;
            MarkDemobilizing(pWar.data.id, pKingdom.data.id);
        }

        internal static void OnWarEnded(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null) return;
            EndedWars.Add(pWar.data.id);
            MarkDemobilizing(pWar.data.id, null);
        }

        private sealed class PendingParticipantWork
        {
            internal long WarId;
            internal long KingdomId = -1L;
            internal int Cursor;
            internal bool Defenders;
            internal int ExpectedCityCount;
            internal bool RestartRequested;
        }

        private sealed class PendingWarRecordWork
        {
            internal long WarId;
            internal long? KingdomId;
            internal int Cursor;
            internal int EndExclusive;
        }

        private sealed class PendingCityRecordWork
        {
            internal long CityId;
            internal int Cursor;
            internal int EndExclusive;
        }

        internal static void OnCityKingdomChanged(City pCity,
            Kingdom pPreviousKingdom, Kingdom pCurrentKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pCity?.data == null || pPreviousKingdom == pCurrentKingdom)
                return;
            EnqueueCityRecordWork(pCity.id);
            RequestWarEnrollmentScan();
        }

        internal static void ProcessAuthorityCycle()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            ProcessLoadActorReconciliation();
            if (!SyntheticMobilizationRules.ShouldDeferOrphanScan(
                    _loadActorReconciliationPending))
                ProcessOrphanSyntheticActors();
            ProcessWarEnrollmentScan();
            ProcessPendingParticipantWork();
            ProcessPendingWarRecordWork();
            ProcessPendingCityRecordWork();
            ProcessPendingCity();
            ProcessRecordWork();
        }

        internal static int TryReserveReplacement(long pWarId,
            long pCityId, int pRequested)
        {
            if (pRequested <= 0 || EndedWars.Contains(pWarId) ||
                !Records.TryGetValue(Key(pWarId, pCityId),
                    out SyntheticMobilizationRecord record) ||
                record.Phase == SyntheticMobilizationPhase.Demobilizing ||
                record.Phase == SyntheticMobilizationPhase.Complete)
                return 0;
            int reserved = Math.Min(pRequested,
                Math.Max(0, record.ReplacementRemaining));
            record.ReplacementRemaining -= reserved;
            CityReservePoolService.OnSyntheticLedgerChanged(
                record.CityId, record.KingdomId);
            return reserved;
        }

        internal static void ConfirmReplacementCreated(long pWarId,
            long pCityId, int pCreated)
        {
            if (pCreated <= 0 || !Records.TryGetValue(
                    Key(pWarId, pCityId), out SyntheticMobilizationRecord record))
                return;
            record.ReplacementCreated = SaturatingAdd(
                record.ReplacementCreated, pCreated);
        }

        internal static void ReleaseUncreatedReplacement(long pWarId,
            long pCityId, int pCount)
        {
            if (pCount <= 0 || !Records.TryGetValue(
                    Key(pWarId, pCityId), out SyntheticMobilizationRecord record))
                return;
            record.ReplacementRemaining = Math.Min(record.Quota,
                SaturatingAdd(record.ReplacementRemaining, pCount));
            CityReservePoolService.OnSyntheticLedgerChanged(
                record.CityId, record.KingdomId);
        }

        internal static int AvailableReplacement(long pWarId, long pCityId)
        {
            return Records.TryGetValue(Key(pWarId, pCityId),
                       out SyntheticMobilizationRecord record) &&
                   record.Phase != SyntheticMobilizationPhase.Demobilizing &&
                   record.Phase != SyntheticMobilizationPhase.Complete
                ? Math.Max(0, record.ReplacementRemaining)
                : 0;
        }

        internal static int LiveSyntheticForCity(long pCityId)
        {
            return LiveSyntheticByCity.TryGetValue(pCityId, out int count)
                ? Math.Max(0, count)
                : 0;
        }

        internal static void OnSyntheticMaterialized(Actor pActor)
        {
            if (pActor?.data == null ||
                !SyntheticLevyService.IsSynthetic(pActor)) return;
            pActor.data.get(LineageKeys.SYNTHETIC_LEVY_EMERGENCY_ID,
                out long warId, -1L);
            pActor.data.get(LineageKeys.SYNTHETIC_LEVY_SOURCE_CITY_ID,
                out long cityId, -1L);
            pActor.data.get(LineageKeys.SYNTHETIC_LEVY_SOURCE_KINGDOM_ID,
                out long kingdomId, -1L);
            EnsureRecoveredRecord(warId, cityId, kingdomId);
            if (!Records.TryGetValue(Key(warId, cityId),
                    out SyntheticMobilizationRecord record) ||
                !record.ActorIds.Add(pActor.data.id)) return;
            record.LiveSynthetic = SaturatingAdd(record.LiveSynthetic, 1);
            AddCitySynthetic(cityId, 1);
            CityReservePoolService.OnSyntheticLedgerChanged(cityId,
                record.KingdomId);
        }

        internal static void OnSyntheticRemoved(long pWarId, long pCityId,
            long pActorId, int pCount)
        {
            if (pCount <= 0 || !Records.TryGetValue(
                    Key(pWarId, pCityId), out SyntheticMobilizationRecord record))
                return;
            if (pActorId >= 0L && !record.ActorIds.Remove(pActorId)) return;
            record.LiveSynthetic = Math.Max(0, record.LiveSynthetic - pCount);
            AddCitySynthetic(pCityId, -pCount);
            CityReservePoolService.OnSyntheticLedgerChanged(pCityId,
                record.KingdomId);
        }

        internal static bool TryWriteSnapshot(string pDirectory,
            out string pError)
        {
            pError = string.Empty;
            if (string.IsNullOrWhiteSpace(pDirectory) || World.world == null)
                return false;
            string path = Path.Combine(Path.GetFullPath(pDirectory),
                SnapshotFileName);
            string temporary = path + ".tmp";
            try
            {
                var snapshot = new PersistedSnapshot();
                snapshot.ended_wars.AddRange(EndedWars);
                snapshot.ended_wars.Sort();
                foreach (SyntheticMobilizationRecord record in Records.Values)
                    snapshot.records.Add(ToPersisted(record));
                snapshot.records.Sort((first, second) =>
                {
                    int war = first.war_id.CompareTo(second.war_id);
                    return war != 0
                        ? war
                        : first.city_id.CompareTo(second.city_id);
                });
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temporary,
                    JsonConvert.SerializeObject(snapshot));
                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
                return true;
            }
            catch (Exception exception)
            {
                pError = exception.Message;
                TryDelete(temporary);
                return false;
            }
        }

        internal static bool TryRestoreSnapshot(string pDirectory,
            out string pError)
        {
            pError = string.Empty;
            if (string.IsNullOrWhiteSpace(pDirectory) || World.world == null)
                return false;
            string directory = Path.GetFullPath(pDirectory);
            string path = Path.Combine(directory, SnapshotFileName);
            try
            {
                ClearRuntime();
                if (!File.Exists(path))
                {
                    bool imported = TryImportLegacySnapshot(
                        Path.Combine(directory, LegacySnapshotFileName),
                        out pError);
                    BeginLoadActorReconciliation();
                    return imported;
                }
                PersistedSnapshot snapshot = JsonConvert.DeserializeObject<
                    PersistedSnapshot>(File.ReadAllText(path));
                if (snapshot?.records == null ||
                    snapshot.version != SnapshotVersion)
                {
                    BeginLoadActorReconciliation();
                    return false;
                }
                if (snapshot.ended_wars != null)
                    for (int i = 0; i < snapshot.ended_wars.Count; i++)
                        if (snapshot.ended_wars[i] >= 0L)
                            EndedWars.Add(snapshot.ended_wars[i]);
                for (int i = 0; i < snapshot.records.Count; i++)
                    Restore(snapshot.records[i]);
                BeginLoadActorReconciliation();
                return true;
            }
            catch (Exception exception)
            {
                ClearRuntime();
                BeginLoadActorReconciliation();
                pError = exception.Message;
                return false;
            }
        }

        internal static void ClearRuntime()
        {
            Records.Clear();
            RecordKeysByWar.Clear();
            RecordKeysByCity.Clear();
            PendingCities.Clear();
            PendingCityKeys.Clear();
            EndedWars.Clear();
            LiveSyntheticByCity.Clear();
            RecordWork.Clear();
            QueuedRecordKeys.Clear();
            ActorBatch.Clear();
            OrphanSyntheticActorIds.Clear();
            PendingParticipantWorks.Clear();
            PendingParticipantWorkKeys.Clear();
            PendingParticipantWorkByKey.Clear();
            PendingWarRecordWorks.Clear();
            PendingWarRecordWorkKeys.Clear();
            PendingWarRecordWorkByKey.Clear();
            PendingCityRecordWorks.Clear();
            PendingCityRecordWorkKeys.Clear();
            PendingCityRecordWorkByKey.Clear();
            _warEnrollmentScanActive = false;
            _warEnrollmentScanRequested = false;
            _warEnrollmentScanCursor = 0;
            FinishLoadActorReconciliation();
        }

        private static void BeginLoadActorReconciliation()
        {
            LiveSyntheticByCity.Clear();
            _loadActorReconciliationCursor = 0;
            _loadActorReconciliationPending = true;
            _loadReconciliationPhase = LoadReconciliationPhase.ResetRecords;
            ResetLoadRecordEnumerator();
            RequestWarEnrollmentScan();
        }

        private static void ProcessLoadActorReconciliation()
        {
            if (!_loadActorReconciliationPending) return;
            if (_loadReconciliationPhase ==
                LoadReconciliationPhase.ResetRecords)
            {
                ProcessLoadRecordReset();
                return;
            }
            if (_loadReconciliationPhase ==
                LoadReconciliationPhase.ValidateRecords)
            {
                ProcessLoadRecordValidation();
                return;
            }
            List<Actor> actors = World.world?.units?.getSimpleList();
            if (actors == null)
            {
                FinishLoadActorReconciliation();
                return;
            }
            if (_loadActorReconciliationCursor < 0 ||
                _loadActorReconciliationCursor > actors.Count)
                _loadActorReconciliationCursor = 0;
            int inspected = 0;
            while (_loadActorReconciliationCursor < actors.Count &&
                   inspected < LoadActorReconciliationBatchLimit)
            {
                Actor actor = actors[_loadActorReconciliationCursor++];
                inspected++;
                if (SyntheticLevyService.IsSynthetic(actor))
                {
                    SyntheticLevyService.ReconcileLoadedActor(actor);
                    if (!IsTrackedSynthetic(actor))
                        OrphanSyntheticActorIds.Enqueue(actor.data.id);
                }
            }
            if (_loadActorReconciliationCursor < actors.Count) return;
            _loadReconciliationPhase =
                LoadReconciliationPhase.ValidateRecords;
            ResetLoadRecordEnumerator();
        }

        private static void ProcessLoadRecordReset()
        {
            int processed = 0;
            while (processed < LoadRecordReconciliationBatchLimit &&
                   _loadRecordEnumerator != null &&
                   _loadRecordEnumerator.MoveNext())
            {
                SyntheticMobilizationRecord record =
                    _loadRecordEnumerator.Current.Value;
                record.ActorIds.Clear();
                record.LiveSynthetic = 0;
                processed++;
            }
            if (_loadRecordEnumerator != null && processed >=
                LoadRecordReconciliationBatchLimit) return;
            DisposeLoadRecordEnumerator();
            _loadReconciliationPhase = LoadReconciliationPhase.ScanActors;
        }

        private static void ProcessLoadRecordValidation()
        {
            int processed = 0;
            while (processed < LoadRecordReconciliationBatchLimit &&
                   _loadRecordEnumerator != null &&
                   _loadRecordEnumerator.MoveNext())
            {
                KeyValuePair<string, SyntheticMobilizationRecord> entry =
                    _loadRecordEnumerator.Current;
                SyntheticMobilizationRecord record = entry.Value;
                if (record.Phase != SyntheticMobilizationPhase.Complete &&
                    !IsActiveParticipant(record.WarId,
                        ResolveKingdom(record.KingdomId)))
                {
                    record.ReplacementRemaining = 0;
                    record.Phase = SyntheticMobilizationPhase.Demobilizing;
                    EnqueueRecord(entry.Key);
                }
                processed++;
            }
            if (_loadRecordEnumerator != null && processed >=
                LoadRecordReconciliationBatchLimit) return;
            FinishLoadActorReconciliation();
        }

        private static void FinishLoadActorReconciliation()
        {
            _loadActorReconciliationPending = false;
            _loadActorReconciliationCursor = 0;
            _loadReconciliationPhase = LoadReconciliationPhase.None;
            DisposeLoadRecordEnumerator();
        }

        private static void ResetLoadRecordEnumerator()
        {
            DisposeLoadRecordEnumerator();
            _loadRecordEnumerator = Records.GetEnumerator();
        }

        private static void DisposeLoadRecordEnumerator()
        {
            _loadRecordEnumerator?.Dispose();
            _loadRecordEnumerator = null;
        }

        private static void ProcessOrphanSyntheticActors()
        {
            if (_loadActorReconciliationPending ||
                OrphanSyntheticActorIds.Count == 0) return;
            int count = Math.Min(OrphanSyntheticActorIds.Count,
                SyntheticMobilizationRules.DemobilizationBatchLimit);
            while (count-- > 0)
            {
                Actor actor = ResolveActor(
                    OrphanSyntheticActorIds.Dequeue());
                if (SyntheticLevyService.IsSynthetic(actor))
                    SyntheticLevyService.RemoveWithoutPersonalHistory(actor);
            }
        }

        private static bool IsTrackedSynthetic(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.SYNTHETIC_LEVY_EMERGENCY_ID,
                out long warId, -1L);
            pActor.data.get(LineageKeys.SYNTHETIC_LEVY_SOURCE_CITY_ID,
                out long cityId, -1L);
            return Records.TryGetValue(Key(warId, cityId),
                       out SyntheticMobilizationRecord record) &&
                   record.ActorIds.Contains(pActor.data.id);
        }

        private static void ProcessPendingParticipantWork()
        {
            if (PendingParticipantWorks.Count == 0) return;
            PendingParticipantWork work = PendingParticipantWorks.Dequeue();
            string workKey = ParticipantWorkKey(work.WarId, work.KingdomId);
            War war = ResolveWar(work.WarId);
            if (EndedWars.Contains(work.WarId) || war?.data == null ||
                war.hasEnded())
            {
                CompleteParticipantWork(work, workKey, false);
                return;
            }

            if (work.KingdomId < 0L)
            {
                List<long> participants = work.Defenders
                    ? war.data.list_defenders
                    : war.data.list_attackers;
                if (participants == null || work.Cursor >= participants.Count)
                {
                    if (work.Defenders)
                    {
                        CompleteParticipantWork(work, workKey, true);
                        return;
                    }
                    work.Defenders = true;
                    work.Cursor = 0;
                    participants = war.data.list_defenders;
                    if (participants == null || participants.Count == 0)
                    {
                        CompleteParticipantWork(work, workKey, true);
                        return;
                    }
                }
                long kingdomId = participants[work.Cursor++];
                PendingParticipantWorks.Enqueue(work);
                EnqueueParticipant(work.WarId, kingdomId);
                return;
            }

            Kingdom kingdom = ResolveKingdom(work.KingdomId);
            if (!IsActiveParticipant(work.WarId, kingdom) ||
                kingdom.cities == null)
            {
                CompleteParticipantWork(work, workKey, false);
                return;
            }
            int cityCount = kingdom.cities.Count;
            if (SyntheticMobilizationRules.ShouldRestartCityCursor(
                    work.ExpectedCityCount, cityCount))
            {
                work.ExpectedCityCount = cityCount;
                work.Cursor = 0;
            }
            if (work.Cursor >= cityCount)
            {
                CompleteParticipantWork(work, workKey, true);
                return;
            }
            City city = kingdom.cities[work.Cursor++];
            if (work.Cursor < cityCount)
                PendingParticipantWorks.Enqueue(work);
            else
                CompleteParticipantWork(work, workKey, true);
            if (!IsControlledCity(city, kingdom)) return;
            string recordKey = Key(work.WarId, city.id);
            if (Records.ContainsKey(recordKey) ||
                !PendingCityKeys.Add(recordKey)) return;
            PendingCities.Enqueue(new PendingCity(work.WarId,
                work.KingdomId, city.id));
        }

        private static void ProcessPendingWarRecordWork()
        {
            if (PendingWarRecordWorks.Count == 0) return;
            PendingWarRecordWork work = PendingWarRecordWorks.Dequeue();
            string workKey = WarRecordWorkKey(work.WarId, work.KingdomId);
            if (!RecordKeysByWar.TryGetValue(work.WarId,
                    out List<string> keys))
            {
                PendingWarRecordWorkKeys.Remove(workKey);
                PendingWarRecordWorkByKey.Remove(workKey);
                return;
            }
            int processed = 0;
            int end = Math.Min(work.EndExclusive, keys.Count);
            while (work.Cursor < end &&
                   processed < LifecycleRecordBatchLimit)
            {
                string key = keys[work.Cursor++];
                processed++;
                if (!Records.TryGetValue(key,
                        out SyntheticMobilizationRecord record) ||
                    work.KingdomId.HasValue &&
                    record.KingdomId != work.KingdomId.Value) continue;
                DemobilizeRecord(record, key);
            }
            if (work.Cursor < end)
                PendingWarRecordWorks.Enqueue(work);
            else
            {
                PendingWarRecordWorkKeys.Remove(workKey);
                PendingWarRecordWorkByKey.Remove(workKey);
            }
        }

        private static void ProcessPendingCityRecordWork()
        {
            if (PendingCityRecordWorks.Count == 0) return;
            PendingCityRecordWork work = PendingCityRecordWorks.Dequeue();
            if (!RecordKeysByCity.TryGetValue(work.CityId,
                    out List<string> keys))
            {
                PendingCityRecordWorkKeys.Remove(work.CityId);
                PendingCityRecordWorkByKey.Remove(work.CityId);
                return;
            }
            int processed = 0;
            int end = Math.Min(work.EndExclusive, keys.Count);
            while (work.Cursor < end &&
                   processed < LifecycleRecordBatchLimit)
            {
                string key = keys[work.Cursor++];
                processed++;
                if (!Records.TryGetValue(key,
                        out SyntheticMobilizationRecord record) ||
                    record.Phase == SyntheticMobilizationPhase.Complete)
                    continue;
                record.ReplacementRemaining = 0;
                record.InitialCreated = record.Quota;
                if (record.Phase == SyntheticMobilizationPhase.Mobilizing)
                    record.Phase = SyntheticMobilizationPhase.Active;
                CityReservePoolService.OnSyntheticLedgerChanged(
                    record.CityId, record.KingdomId);
            }
            if (work.Cursor < end)
                PendingCityRecordWorks.Enqueue(work);
            else
            {
                PendingCityRecordWorkKeys.Remove(work.CityId);
                PendingCityRecordWorkByKey.Remove(work.CityId);
            }
        }

        private static void ProcessPendingCity()
        {
            if (PendingCities.Count == 0) return;
            PendingCity pending = PendingCities.Dequeue();
            string key = Key(pending.WarId, pending.CityId);
            PendingCityKeys.Remove(key);
            if (EndedWars.Contains(pending.WarId) ||
                Records.ContainsKey(key)) return;

            Kingdom kingdom = ResolveKingdom(pending.KingdomId);
            City city = ResolveCity(pending.CityId);
            if (!IsActiveParticipant(pending.WarId, kingdom) ||
                !IsControlledCity(city, kingdom))
                return;

            int knownSynthetic = KnownSyntheticForCity(pending.CityId);
            int population = Math.Max(0, city.getPopulationPeople());
            int percent = CourtConscriptionLawRules.ReservePercent(
                CourtAuxiliaryLawService.GetConscriptionLaw(kingdom));
            int quota = SyntheticMobilizationRules.Quota(population,
                knownSynthetic, percent);
            StoreRecord(key, new SyntheticMobilizationRecord
            {
                WarId = pending.WarId,
                KingdomId = pending.KingdomId,
                CityId = pending.CityId,
                PopulationSnapshot = population,
                LawPercent = percent,
                Quota = quota,
                ReplacementRemaining = quota,
                Phase = quota > 0
                    ? SyntheticMobilizationPhase.Mobilizing
                    : SyntheticMobilizationPhase.Active
            });
            CityReservePoolService.OnSyntheticLedgerChanged(
                pending.CityId, pending.KingdomId);
            if (quota > 0) EnqueueRecord(key);
        }

        private static void ProcessRecordWork()
        {
            if (RecordWork.Count == 0) return;
            string key = RecordWork.Dequeue();
            QueuedRecordKeys.Remove(key);
            if (!Records.TryGetValue(key,
                    out SyntheticMobilizationRecord record) ||
                record.Phase == SyntheticMobilizationPhase.Complete) return;
            if (record.Phase == SyntheticMobilizationPhase.Demobilizing)
            {
                ProcessDemobilization(record, key);
                return;
            }
            if (record.Phase != SyntheticMobilizationPhase.Mobilizing ||
                EndedWars.Contains(record.WarId)) return;
            Kingdom kingdom = ResolveKingdom(record.KingdomId);
            City city = ResolveCity(record.CityId);
            if (!IsActiveParticipant(record.WarId, kingdom))
            {
                record.Phase = SyntheticMobilizationPhase.Demobilizing;
                record.ReplacementRemaining = 0;
                EnqueueRecord(key);
                return;
            }
            if (!IsControlledCity(city, kingdom))
            {
                record.ReplacementRemaining = 0;
                record.InitialCreated = record.Quota;
                record.Phase = SyntheticMobilizationPhase.Active;
                CityReservePoolService.OnSyntheticLedgerChanged(
                    record.CityId, record.KingdomId);
                return;
            }

            Army army = ResolveOrBindArmy(record, kingdom, city);
            if (army?.data == null)
            {
                EnqueueRecord(key);
                return;
            }
            int pending = Math.Max(0, record.Quota - record.InitialCreated);
            int requested = SyntheticMobilizationRules.Batch(pending,
                SyntheticMobilizationRules.SpawnBatchLimit);
            int created = SyntheticLevyService.CreateBatch(city, kingdom,
                army, requested, record.WarId);
            record.InitialCreated = Math.Min(record.Quota,
                SaturatingAdd(record.InitialCreated, created));
            if (created > 0)
                KingdomWarDirectorService.QueueArmyChanged(kingdom);
            if (record.InitialCreated >= record.Quota)
                record.Phase = SyntheticMobilizationPhase.Active;
            else
                EnqueueRecord(key);
        }

        private static void ProcessDemobilization(
            SyntheticMobilizationRecord pRecord, string pKey)
        {
            ActorBatch.Clear();
            int limit = SyntheticMobilizationRules.DemobilizationBatch(
                pRecord.ActorIds.Count);
            foreach (long actorId in pRecord.ActorIds)
            {
                ActorBatch.Add(actorId);
                if (ActorBatch.Count >= limit) break;
            }
            for (int i = 0; i < ActorBatch.Count; i++)
            {
                long actorId = ActorBatch[i];
                Actor actor = ResolveActor(actorId);
                if (!MatchesRecord(actor, pRecord))
                {
                    if (pRecord.ActorIds.Remove(actorId))
                    {
                        pRecord.LiveSynthetic = Math.Max(0,
                            pRecord.LiveSynthetic - 1);
                        AddCitySynthetic(pRecord.CityId, -1);
                    }
                    continue;
                }
                bool insideFriendlySafeCity = WarArmyReturnService.
                    IsInsideFriendlySafeCity(actor);
                if (insideFriendlySafeCity)
                    SyntheticLevyService.ConfirmReturnArrivalIfSafe(actor);
                if (SyntheticMobilizationRules.ShouldDeferDemobilization(
                        SyntheticLevyService.IsSynthetic(actor),
                        SyntheticLevyService.HasReturnArrivalConfirmed(
                            actor),
                        WarArmyReturnService.IsActive(actor.army),
                        ActiveMilitaryLifecycleService
                            .HasWartimeMilitaryLock(actor),
                        insideFriendlySafeCity))
                    continue;
                SyntheticLevyService.RemoveWithoutPersonalHistory(actor);
            }
            if (pRecord.ActorIds.Count > 0)
            {
                EnqueueRecord(pKey);
                return;
            }
            AddCitySynthetic(pRecord.CityId, -pRecord.LiveSynthetic);
            pRecord.LiveSynthetic = 0;
            pRecord.Phase = SyntheticMobilizationPhase.Complete;
        }

        private static Army ResolveOrBindArmy(
            SyntheticMobilizationRecord pRecord, Kingdom pKingdom,
            City pCity)
        {
            Army army = ResolveArmy(pRecord.ArmyId);
            if (IsLiveOrdinaryArmy(army, pKingdom)) return army;
            pRecord.ArmyId = -1L;
            if (ArmyFieldIndexService.TryGetCityArmy(pCity, out army) &&
                IsLiveOrdinaryArmy(army, pKingdom))
            {
                pRecord.ArmyId = army.id;
                return army;
            }

            List<GeneralReadModelEntry> generals = GeneralService.
                GetActiveGeneralsForReadModel(pKingdom,
                    pAllowUnitFallback: false, pLimit: 8);
            for (int i = 0; i < generals.Count; i++)
            {
                Actor general = generals[i]?.Actor;
                if (!IsEligibleGeneral(general, pKingdom)) continue;
                if (IsLiveOrdinaryArmy(general.army, pKingdom) &&
                    AWArmyService.GetAnchorCityId(general.army) == pCity.id)
                {
                    pRecord.ArmyId = general.army.id;
                    return general.army;
                }
                try { army = World.world?.armies?.newArmy(general, pCity); }
                catch { army = null; }
                if (!IsLiveOrdinaryArmy(army, pKingdom)) continue;
                ArmyStrategicIndexService.OnArmyRegistered(army);
                AWArmyService.EnsureOrdinaryNativeName(army, pKingdom, pCity);
                pRecord.ArmyId = army.id;
                return army;
            }
            return null;
        }

        private static void EnqueueRecord(string pKey)
        {
            if (string.IsNullOrEmpty(pKey) || !QueuedRecordKeys.Add(pKey))
                return;
            RecordWork.Enqueue(pKey);
        }

        private static void EnsureRecoveredRecord(long pWarId,
            long pCityId, long pKingdomId)
        {
            if (pWarId < 0L || pCityId < 0L || pKingdomId < 0L ||
                Records.ContainsKey(Key(pWarId, pCityId))) return;
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            bool active = IsActiveParticipant(pWarId, kingdom);
            var record = new SyntheticMobilizationRecord
            {
                WarId = pWarId,
                KingdomId = pKingdomId,
                CityId = pCityId,
                Quota = 0,
                InitialCreated = 0,
                ReplacementRemaining = 0,
                Phase = active
                    ? SyntheticMobilizationPhase.Active
                    : SyntheticMobilizationPhase.Demobilizing
            };
            string key = Key(pWarId, pCityId);
            StoreRecord(key, record);
            if (!active) EnqueueRecord(key);
        }

        private static int KnownSyntheticForCity(long pCityId)
        {
            return LiveSyntheticByCity.TryGetValue(pCityId, out int total)
                ? Math.Max(0, total)
                : 0;
        }

        private static void AddCitySynthetic(long pCityId, int pDelta)
        {
            if (pCityId < 0L || pDelta == 0) return;
            int current = KnownSyntheticForCity(pCityId);
            int next = pDelta > 0
                ? SaturatingAdd(current, pDelta)
                : (int)Math.Max(0L, (long)current + pDelta);
            if (next > 0)
                LiveSyntheticByCity[pCityId] = next;
            else
                LiveSyntheticByCity.Remove(pCityId);
        }

        private static void EnqueueWarParticipants(long pWarId)
        {
            if (pWarId < 0L) return;
            string key = ParticipantWorkKey(pWarId, -1L);
            if (PendingParticipantWorkByKey.TryGetValue(key,
                    out PendingParticipantWork existing))
            {
                existing.RestartRequested = true;
                return;
            }
            if (!PendingParticipantWorkKeys.Add(key)) return;
            var work = new PendingParticipantWork
            {
                WarId = pWarId
            };
            PendingParticipantWorkByKey[key] = work;
            PendingParticipantWorks.Enqueue(work);
        }

        private static void EnqueueParticipant(long pWarId, long pKingdomId)
        {
            if (pWarId < 0L || pKingdomId < 0L) return;
            string key = ParticipantWorkKey(pWarId, pKingdomId);
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            int cityCount = kingdom?.cities?.Count ?? 0;
            if (PendingParticipantWorkByKey.TryGetValue(key,
                    out PendingParticipantWork existing))
            {
                existing.RestartRequested = true;
                return;
            }
            if (!PendingParticipantWorkKeys.Add(key)) return;
            var work = new PendingParticipantWork
            {
                WarId = pWarId,
                KingdomId = pKingdomId,
                ExpectedCityCount = cityCount
            };
            PendingParticipantWorkByKey[key] = work;
            PendingParticipantWorks.Enqueue(work);
        }

        private static void CompleteParticipantWork(
            PendingParticipantWork pWork, string pKey, bool pAllowRestart)
        {
            if (pAllowRestart && pWork?.RestartRequested == true)
            {
                pWork.RestartRequested = false;
                pWork.Cursor = 0;
                pWork.Defenders = false;
                if (pWork.KingdomId >= 0L)
                    pWork.ExpectedCityCount = ResolveKingdom(
                        pWork.KingdomId)?.cities?.Count ?? 0;
                PendingParticipantWorks.Enqueue(pWork);
                return;
            }
            PendingParticipantWorkKeys.Remove(pKey);
            PendingParticipantWorkByKey.Remove(pKey);
        }

        private static void RequestWarEnrollmentScan()
        {
            _warEnrollmentScanRequested = true;
        }

        private static void ProcessWarEnrollmentScan()
        {
            if (!_warEnrollmentScanActive)
            {
                if (!_warEnrollmentScanRequested) return;
                _warEnrollmentScanRequested = false;
                _warEnrollmentScanActive = true;
                _warEnrollmentScanCursor = 0;
            }
            List<War> wars = World.world?.wars?.list;
            if (wars == null || _warEnrollmentScanCursor >= wars.Count)
            {
                _warEnrollmentScanActive = false;
                _warEnrollmentScanCursor = 0;
                return;
            }
            War war = wars[_warEnrollmentScanCursor++];
            if (war?.data != null && !war.hasEnded() &&
                ZhuluWarService.ShouldEnrollInAw3Systems(war))
                EnqueueWarParticipants(war.data.id);
            if (_warEnrollmentScanCursor >= wars.Count)
            {
                _warEnrollmentScanActive = false;
                _warEnrollmentScanCursor = 0;
            }
        }

        private static void MarkDemobilizing(long pWarId,
            long? pKingdomId)
        {
            if (pWarId < 0L) return;
            string key = WarRecordWorkKey(pWarId, pKingdomId);
            int end = RecordKeysByWar.TryGetValue(pWarId,
                out List<string> keys) ? keys.Count : 0;
            if (PendingWarRecordWorkByKey.TryGetValue(key,
                    out PendingWarRecordWork existing))
            {
                existing.EndExclusive = SyntheticMobilizationRules.
                    ExpandLifecycleEnd(existing.EndExclusive, end);
                return;
            }
            if (end <= 0 || !PendingWarRecordWorkKeys.Add(key)) return;
            var work = new PendingWarRecordWork
            {
                WarId = pWarId,
                KingdomId = pKingdomId,
                EndExclusive = end
            };
            PendingWarRecordWorkByKey[key] = work;
            PendingWarRecordWorks.Enqueue(work);
        }

        private static void EnqueueCityRecordWork(long pCityId)
        {
            if (pCityId < 0L) return;
            int end = RecordKeysByCity.TryGetValue(pCityId,
                out List<string> keys) ? keys.Count : 0;
            if (PendingCityRecordWorkByKey.TryGetValue(pCityId,
                    out PendingCityRecordWork existing))
            {
                existing.EndExclusive = SyntheticMobilizationRules.
                    ExpandLifecycleEnd(existing.EndExclusive, end);
                return;
            }
            if (end <= 0 || !PendingCityRecordWorkKeys.Add(pCityId)) return;
            var work = new PendingCityRecordWork
            {
                CityId = pCityId,
                EndExclusive = end
            };
            PendingCityRecordWorkByKey[pCityId] = work;
            PendingCityRecordWorks.Enqueue(work);
        }

        private static void DemobilizeRecord(
            SyntheticMobilizationRecord pRecord, string pKey)
        {
            pRecord.ReplacementRemaining = 0;
            CityReservePoolService.OnSyntheticLedgerChanged(
                pRecord.CityId, pRecord.KingdomId);
            if (pRecord.Phase == SyntheticMobilizationPhase.Complete) return;
            pRecord.Phase = SyntheticMobilizationPhase.Demobilizing;
            EnqueueRecord(pKey);
        }

        private static void StoreRecord(string pKey,
            SyntheticMobilizationRecord pRecord)
        {
            if (string.IsNullOrEmpty(pKey) || pRecord == null) return;
            if (!Records.ContainsKey(pKey))
            {
                AddRecordIndex(RecordKeysByWar, pRecord.WarId, pKey);
                AddRecordIndex(RecordKeysByCity, pRecord.CityId, pKey);
            }
            Records[pKey] = pRecord;
            if (EndedWars.Contains(pRecord.WarId))
                DemobilizeRecord(pRecord, pKey);
        }

        private static void AddRecordIndex(
            Dictionary<long, List<string>> pIndex, long pId, string pKey)
        {
            if (!pIndex.TryGetValue(pId, out List<string> keys))
            {
                keys = new List<string>();
                pIndex[pId] = keys;
            }
            keys.Add(pKey);
        }

        private static string ParticipantWorkKey(long pWarId,
            long pKingdomId)
        {
            return pWarId + ":" + pKingdomId;
        }

        private static string WarRecordWorkKey(long pWarId,
            long? pKingdomId)
        {
            return pWarId + ":" + (pKingdomId.HasValue
                ? pKingdomId.Value.ToString()
                : "all");
        }

        private static PersistedRecord ToPersisted(
            SyntheticMobilizationRecord pRecord)
        {
            return new PersistedRecord
            {
                war_id = pRecord.WarId,
                kingdom_id = pRecord.KingdomId,
                city_id = pRecord.CityId,
                army_id = pRecord.ArmyId,
                population_snapshot = pRecord.PopulationSnapshot,
                law_percent = pRecord.LawPercent,
                quota = pRecord.Quota,
                initial_created = pRecord.InitialCreated,
                replacement_remaining = pRecord.ReplacementRemaining,
                replacement_created = pRecord.ReplacementCreated,
                live_synthetic = pRecord.LiveSynthetic,
                phase = (int)pRecord.Phase,
                actor_ids = new List<long>(pRecord.ActorIds)
            };
        }

        private static void Restore(PersistedRecord pPersisted)
        {
            if (pPersisted == null || pPersisted.war_id < 0L ||
                pPersisted.city_id < 0L) return;
            int quota = Math.Max(0, pPersisted.quota);
            var record = new SyntheticMobilizationRecord
            {
                WarId = pPersisted.war_id,
                KingdomId = pPersisted.kingdom_id,
                CityId = pPersisted.city_id,
                ArmyId = pPersisted.army_id,
                PopulationSnapshot = Math.Max(0,
                    pPersisted.population_snapshot),
                LawPercent = Math.Max(0, Math.Min(100,
                    pPersisted.law_percent)),
                Quota = quota,
                InitialCreated = Clamp(pPersisted.initial_created, quota),
                ReplacementRemaining = Clamp(
                    pPersisted.replacement_remaining, quota),
                ReplacementCreated = Clamp(pPersisted.replacement_created,
                    quota),
                Phase = Enum.IsDefined(typeof(SyntheticMobilizationPhase),
                    pPersisted.phase)
                    ? (SyntheticMobilizationPhase)pPersisted.phase
                    : SyntheticMobilizationPhase.Demobilizing
            };
            int maximumLive = (int)Math.Min(int.MaxValue,
                (long)record.InitialCreated + record.ReplacementCreated);
            record.LiveSynthetic = Clamp(pPersisted.live_synthetic,
                maximumLive);
            if (pPersisted.actor_ids != null)
                for (int i = 0; i < pPersisted.actor_ids.Count; i++)
                    if (pPersisted.actor_ids[i] >= 0L)
                        record.ActorIds.Add(pPersisted.actor_ids[i]);
            record.LiveSynthetic = Math.Min(record.LiveSynthetic,
                record.ActorIds.Count);
            StoreRecord(Key(record.WarId, record.CityId), record);
            AddCitySynthetic(record.CityId, record.LiveSynthetic);
            if (record.Phase == SyntheticMobilizationPhase.Mobilizing ||
                record.Phase == SyntheticMobilizationPhase.Demobilizing)
                EnqueueRecord(Key(record.WarId, record.CityId));
        }

        private static bool TryImportLegacySnapshot(string pPath,
            out string pError)
        {
            pError = string.Empty;
            if (!File.Exists(pPath)) return false;
            try
            {
                LegacySnapshot snapshot = JsonConvert.DeserializeObject<
                    LegacySnapshot>(File.ReadAllText(pPath));
                if (snapshot?.kingdoms == null) return false;
                for (int k = 0; k < snapshot.kingdoms.Count; k++)
                {
                    LegacyKingdom kingdom = snapshot.kingdoms[k];
                    if (kingdom?.cities == null) continue;
                    for (int c = 0; c < kingdom.cities.Count; c++)
                    {
                        LegacyCity city = kingdom.cities[c];
                        if (city == null || city.war_emergency_id < 0L ||
                            city.city_id < 0L) continue;
                        int quota = Math.Max(0, city.war_reserve_capacity);
                        int consumed = Clamp(city.war_reserve_consumed,
                            quota);
                        var record = new SyntheticMobilizationRecord
                        {
                            WarId = city.war_emergency_id,
                            KingdomId = kingdom.kingdom_id,
                            CityId = city.city_id,
                            PopulationSnapshot = Math.Max(0,
                                city.authentic_population),
                            Quota = quota,
                            InitialCreated = Math.Min(consumed,
                                Math.Max(0, city.synthetic_mobilized)),
                            ReplacementRemaining = quota - consumed,
                            LiveSynthetic = Math.Min(consumed,
                                Math.Max(0, city.synthetic_mobilized)),
                            Phase = SyntheticMobilizationPhase.Active
                        };
                        StoreRecord(Key(record.WarId, record.CityId), record);
                        AddCitySynthetic(record.CityId,
                            record.LiveSynthetic);
                        EnqueueRecord(Key(record.WarId, record.CityId));
                    }
                }
                return Records.Count > 0;
            }
            catch (Exception exception)
            {
                ClearRuntime();
                pError = exception.Message;
                return false;
            }
        }

        private static int Clamp(int pValue, int pMaximum)
        {
            return Math.Max(0, Math.Min(Math.Max(0, pMaximum), pValue));
        }

        private static string Key(long pWarId, long pCityId)
        {
            return pWarId + ":" + pCityId;
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static War ResolveWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }

        private static bool IsActiveParticipant(long pWarId,
            Kingdom pKingdom)
        {
            if (!IsLivingKingdom(pKingdom)) return false;
            War war = ResolveWar(pWarId);
            try
            {
                return war?.data != null && !war.hasEnded() &&
                       (war.isAttacker(pKingdom) ||
                        war.isDefender(pKingdom));
            }
            catch { return false; }
        }

        private static City ResolveCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static Actor ResolveActor(long pActorId)
        {
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static bool MatchesRecord(Actor pActor,
            SyntheticMobilizationRecord pRecord)
        {
            if (pActor?.data == null || pRecord == null ||
                !SyntheticLevyService.IsSynthetic(pActor)) return false;
            pActor.data.get(LineageKeys.SYNTHETIC_LEVY_EMERGENCY_ID,
                out long warId, -1L);
            pActor.data.get(LineageKeys.SYNTHETIC_LEVY_SOURCE_CITY_ID,
                out long cityId, -1L);
            return warId == pRecord.WarId && cityId == pRecord.CityId;
        }

        private static Army ResolveArmy(long pArmyId)
        {
            if (pArmyId < 0L) return null;
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static bool IsLiveOrdinaryArmy(Army pArmy,
            Kingdom pKingdom)
        {
            try
            {
                Actor captain = pArmy?.getCaptain();
                return pArmy?.data != null && pArmy.isAlive() &&
                       !AWArmyService.IsSpecialArmy(pArmy) &&
                       ArmyNativeNameService.IsOrdinaryArmy(pArmy) &&
                       AWArmyService.GetIntendedKingdom(pArmy) == pKingdom &&
                       IsEligibleCaptain(captain, pKingdom);
            }
            catch { return false; }
        }

        private static bool IsEligibleCaptain(Actor pActor,
            Kingdom pKingdom)
        {
            try
            {
                return pActor?.data != null && pActor.isAlive() &&
                       !pActor.isRekt() && pActor.kingdom == pKingdom &&
                       !SyntheticLevyService.IsSynthetic(pActor);
            }
            catch { return false; }
        }

        private static bool IsEligibleGeneral(Actor pActor,
            Kingdom pKingdom)
        {
            try
            {
                return pActor?.data != null && pActor.isAlive() &&
                       !pActor.isRekt() && pActor.kingdom == pKingdom &&
                       GeneralService.IsGeneral(pActor) &&
                       IsEligibleCaptain(pActor, pKingdom);
            }
            catch { return false; }
        }

        private static int SaturatingAdd(int pCurrent, int pDelta)
        {
            return (int)Math.Min(int.MaxValue,
                (long)Math.Max(0, pCurrent) + Math.Max(0, pDelta));
        }

        private static bool IsLivingKingdom(Kingdom pKingdom)
        {
            try
            {
                return pKingdom?.data != null && !pKingdom.isRekt() &&
                       !pKingdom.isNeutral();
            }
            catch { return false; }
        }

        private static bool IsControlledCity(City pCity, Kingdom pKingdom)
        {
            try
            {
                return pCity?.data != null && !pCity.isRekt() &&
                       pCity.kingdom == pKingdom;
            }
            catch { return false; }
        }

        private static void TryDelete(string pPath)
        {
            try
            {
                if (!string.IsNullOrEmpty(pPath) && File.Exists(pPath))
                    File.Delete(pPath);
            }
            catch { }
        }
    }
}
