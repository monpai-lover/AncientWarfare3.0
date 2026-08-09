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
        private const int SnapshotVersion = 1;
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

        internal static void OnWarStarted(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                !ZhuluWarService.ShouldEnrollInAw3Systems(pWar)) return;
            EndedWars.Remove(pWar.data.id);
            foreach (Kingdom kingdom in pWar.getAttackers())
                EnqueueKingdom(pWar, kingdom);
            foreach (Kingdom kingdom in pWar.getDefenders())
                EnqueueKingdom(pWar, kingdom);
        }

        internal static void OnKingdomJoinedWar(War pWar, Kingdom pKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pWar.hasEnded()) return;
            EnqueueKingdom(pWar, pKingdom);
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

        internal static void ProcessAuthorityCycle()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
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

        internal static void OnSyntheticMaterialized(Actor pActor)
        {
            if (pActor?.data == null ||
                !SyntheticLevyService.IsSynthetic(pActor)) return;
            pActor.data.get(LineageKeys.SYNTHETIC_LEVY_EMERGENCY_ID,
                out long warId, -1L);
            pActor.data.get(LineageKeys.SYNTHETIC_LEVY_SOURCE_CITY_ID,
                out long cityId, -1L);
            if (!Records.TryGetValue(Key(warId, cityId),
                    out SyntheticMobilizationRecord record) ||
                !record.ActorIds.Add(pActor.data.id)) return;
            record.LiveSynthetic = SaturatingAdd(record.LiveSynthetic, 1);
            AddCitySynthetic(cityId, 1);
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
                    return TryImportLegacySnapshot(Path.Combine(directory,
                        LegacySnapshotFileName), out pError);
                PersistedSnapshot snapshot = JsonConvert.DeserializeObject<
                    PersistedSnapshot>(File.ReadAllText(path));
                if (snapshot?.records == null ||
                    snapshot.version != SnapshotVersion) return false;
                if (snapshot.ended_wars != null)
                    for (int i = 0; i < snapshot.ended_wars.Count; i++)
                        if (snapshot.ended_wars[i] >= 0L)
                            EndedWars.Add(snapshot.ended_wars[i]);
                for (int i = 0; i < snapshot.records.Count; i++)
                    Restore(snapshot.records[i]);
                return true;
            }
            catch (Exception exception)
            {
                ClearRuntime();
                pError = exception.Message;
                return false;
            }
        }

        internal static void ClearRuntime()
        {
            Records.Clear();
            PendingCities.Clear();
            PendingCityKeys.Clear();
            EndedWars.Clear();
            LiveSyntheticByCity.Clear();
            RecordWork.Clear();
            QueuedRecordKeys.Clear();
            ActorBatch.Clear();
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
            if (!IsLivingKingdom(kingdom) || !IsControlledCity(city, kingdom))
                return;

            int knownSynthetic = KnownSyntheticForCity(pending.CityId);
            int population = Math.Max(0, city.getPopulationPeople());
            int percent = CourtConscriptionLawRules.ReservePercent(
                CourtAuxiliaryLawService.GetConscriptionLaw(kingdom));
            int quota = SyntheticMobilizationRules.Quota(population,
                knownSynthetic, percent);
            Records[key] = new SyntheticMobilizationRecord
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
            };
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
            if (!IsLivingKingdom(kingdom) || !IsControlledCity(city, kingdom))
            {
                record.Phase = SyntheticMobilizationPhase.Demobilizing;
                record.ReplacementRemaining = 0;
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

        private static void EnqueueKingdom(War pWar, Kingdom pKingdom)
        {
            if (pWar?.data == null || !IsLivingKingdom(pKingdom) ||
                pKingdom.cities == null) return;
            for (int i = 0; i < pKingdom.cities.Count; i++)
            {
                City city = pKingdom.cities[i];
                if (!IsControlledCity(city, pKingdom)) continue;
                string key = Key(pWar.data.id, city.id);
                if (Records.ContainsKey(key) || !PendingCityKeys.Add(key))
                    continue;
                PendingCities.Enqueue(new PendingCity(pWar.data.id,
                    pKingdom.data.id, city.id));
            }
        }

        private static void MarkDemobilizing(long pWarId,
            long? pKingdomId)
        {
            foreach (SyntheticMobilizationRecord record in Records.Values)
            {
                if (record.WarId != pWarId ||
                    pKingdomId.HasValue &&
                    record.KingdomId != pKingdomId.Value) continue;
                record.ReplacementRemaining = 0;
                if (record.Phase != SyntheticMobilizationPhase.Complete)
                {
                    record.Phase = SyntheticMobilizationPhase.Demobilizing;
                    EnqueueRecord(Key(record.WarId, record.CityId));
                }
            }
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
            Records[Key(record.WarId, record.CityId)] = record;
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
                        Records[Key(record.WarId, record.CityId)] = record;
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
