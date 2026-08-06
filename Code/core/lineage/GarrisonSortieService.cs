using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AncientWarfare3.core.lineage
{
    public sealed class GarrisonSortieRecord
    {
        private readonly IReadOnlyList<long> _memberIds;
        private int _returnCursor;

        internal GarrisonSortieRecord(long armyId, long kingdomId,
            long originCityId, IReadOnlyList<long> memberIds)
        {
            ArmyId = armyId;
            KingdomId = kingdomId;
            OriginCityId = originCityId;
            _memberIds = memberIds;
        }

        public long ArmyId { get; }
        public long KingdomId { get; }
        public long OriginCityId { get; }
        public int RemainingMemberCount =>
            Math.Max(0, _memberIds.Count - _returnCursor);
        public bool ReturnComplete => _returnCursor >= _memberIds.Count;

        internal IReadOnlyList<long> TakeReturnBatch(int maximum)
        {
            int count = Math.Min(Math.Max(0, maximum),
                RemainingMemberCount);
            if (count == 0) return Array.Empty<long>();
            var result = new long[count];
            for (int i = 0; i < count; i++)
                result[i] = _memberIds[_returnCursor + i];
            _returnCursor += count;
            return result;
        }

        public IReadOnlyList<long> GetRemainingMemberIds()
        {
            int count = RemainingMemberCount;
            if (count == 0) return Array.Empty<long>();
            var result = new long[count];
            for (int i = 0; i < count; i++)
                result[i] = _memberIds[_returnCursor + i];
            return result;
        }
    }

    public sealed class GarrisonSortieRuntimeIndex
    {
        private readonly Dictionary<long, GarrisonSortieRecord> _byArmy =
            new Dictionary<long, GarrisonSortieRecord>();
        private readonly Dictionary<long, long> _armyByOriginCity =
            new Dictionary<long, long>();

        public bool TryBegin(long armyId, long kingdomId,
            long originCityId, IReadOnlyList<long> memberIds)
        {
            if (armyId < 0L || kingdomId < 0L || originCityId < 0L ||
                memberIds == null || memberIds.Count == 0 ||
                _byArmy.ContainsKey(armyId) ||
                _armyByOriginCity.ContainsKey(originCityId)) return false;
            var copy = new List<long>(memberIds.Count);
            var unique = new HashSet<long>();
            for (int i = 0; i < memberIds.Count; i++)
            {
                long memberId = memberIds[i];
                if (memberId >= 0L && unique.Add(memberId))
                    copy.Add(memberId);
            }
            if (!GarrisonSortieRules.CanFormSortie(copy.Count))
                return false;
            var record = new GarrisonSortieRecord(armyId, kingdomId,
                originCityId, copy);
            _byArmy[armyId] = record;
            _armyByOriginCity[originCityId] = armyId;
            return true;
        }

        public bool TryGet(long armyId, out GarrisonSortieRecord record)
        {
            return _byArmy.TryGetValue(armyId, out record);
        }

        public bool ContainsOrigin(long originCityId)
        {
            return _armyByOriginCity.ContainsKey(originCityId);
        }

        public bool TryGetByOrigin(long originCityId,
            out GarrisonSortieRecord record)
        {
            record = null;
            return _armyByOriginCity.TryGetValue(originCityId,
                       out long armyId) &&
                   _byArmy.TryGetValue(armyId, out record);
        }

        public IReadOnlyList<long> TakeReturnBatch(long armyId,
            int maximum)
        {
            return _byArmy.TryGetValue(armyId,
                out GarrisonSortieRecord record)
                ? record.TakeReturnBatch(maximum)
                : Array.Empty<long>();
        }

        public bool Complete(long armyId)
        {
            if (!_byArmy.TryGetValue(armyId,
                    out GarrisonSortieRecord record) ||
                !record.ReturnComplete) return false;
            _byArmy.Remove(armyId);
            _armyByOriginCity.Remove(record.OriginCityId);
            return true;
        }

        public void Clear()
        {
            _byArmy.Clear();
            _armyByOriginCity.Clear();
        }

        public IReadOnlyList<long> GetArmyIds()
        {
            if (_byArmy.Count == 0) return Array.Empty<long>();
            var result = new long[_byArmy.Count];
            _byArmy.Keys.CopyTo(result, 0);
            return result;
        }
    }

#if !AW3_RULES_TESTS
    internal static class GarrisonSortieService
    {
        private const string OriginCityKey =
            "aw_garrison_sortie_origin_city_id";
        private const string KingdomKey =
            "aw_garrison_sortie_kingdom_id";
        private const string MemberIdsKey =
            "aw_garrison_sortie_member_ids";
        private const string MemberArmyKey =
            "aw_garrison_sortie_army_id";
        private const string MemberOriginCityKey =
            "aw_garrison_sortie_member_origin_city_id";
        private const string LastLaunchedWarKey =
            "aw_garrison_sortie_last_launched_war_id";

        private static readonly GarrisonSortieRuntimeIndex Runtime =
            new GarrisonSortieRuntimeIndex();

        public static bool IsSortieArmy(Army pArmy)
        {
            if (pArmy?.data == null) return false;
            if (Runtime.TryGet(pArmy.id, out _)) return true;
            pArmy.data.get(OriginCityKey, out long originCityId, -1L);
            return originCityId >= 0L;
        }

        public static bool TryLaunch(City pCity)
        {
            Kingdom kingdom = pCity?.kingdom;
            if (pCity?.data == null || pCity.isRekt() ||
                kingdom?.data == null || kingdom.isRekt() ||
                Runtime.ContainsOrigin(pCity.id) ||
                !ArmyRtsRuntimeMode.ShouldCommit)
            {
                WartimeGarrisonService.ClearSortieReserve(pCity);
                return false;
            }
            if (!MilitaryEmergencyService.TryGetActiveWarId(kingdom,
                    out long warId))
            {
                WartimeGarrisonService.ClearSortieReserve(pCity);
                return false;
            }
            pCity.data.get(LastLaunchedWarKey,
                out long lastLaunchedWarId, -1L);
            if (!GarrisonSortieRules.CanLaunchForWar(warId,
                    lastLaunchedWarId))
            {
                WartimeGarrisonService.ClearSortieReserve(pCity);
                return false;
            }

            bool capitalThreatened = pCity == kingdom.capital &&
                                     IsThreatened(pCity);
            bool adjacentRecaptureNeeded = FindAdjacentRecapture(pCity,
                kingdom, warId, out City adjacentRecapture);
            bool fieldArmyScanComplete =
                StandingArmyService.TryHasUsableFieldArmy(kingdom,
                    out bool hasUsableFieldArmy);
            if (GarrisonSortieRules.ShouldWaitForFieldArmyScan(
                    fieldArmyScanComplete))
            {
                ScheduleLaunchRetry(pCity.id);
                return false;
            }
            if (!GarrisonSortieRules.ShouldLaunch(capitalThreatened,
                    hasUsableFieldArmy, adjacentRecaptureNeeded,
                    cityAlreadyHasSortie: false))
            {
                WartimeGarrisonService.ClearSortieReserve(pCity);
                return false;
            }

            City target = adjacentRecapture;
            if (target?.data == null && !hasUsableFieldArmy)
                target = FindAdjacentEnemy(pCity, kingdom, warId);
            if (target?.data == null && capitalThreatened) target = pCity;
            if (target?.data == null)
            {
                WartimeGarrisonService.ClearSortieReserve(pCity);
                return false;
            }

            int garrison = WartimeGarrisonService.
                GetIndexedDefenderCount(pCity);
            int minimumDefense = WartimeGarrisonService.
                MinimumDefenseForSortie(pCity);
            int extraction = GarrisonSortieRules.ExtractionSize(garrison,
                minimumDefense);
            if (!GarrisonSortieRules.ShouldAttemptLaunch(garrison,
                    minimumDefense))
            {
                WartimeGarrisonService.RequestSortieReserve(pCity);
                return false;
            }
            IReadOnlyList<Actor> candidates = WartimeGarrisonService.
                CollectSortieMembers(pCity, extraction);
            if (!GarrisonSortieRules.CanFormSortie(candidates.Count))
            {
                WartimeGarrisonService.RequestSortieReserve(pCity);
                return false;
            }

            var released = new List<Actor>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
                if (WartimeGarrisonService.ReleaseForSortie(
                        candidates[i], pCity, kingdom))
                    released.Add(candidates[i]);
            if (!GarrisonSortieRules.CanFormSortie(released.Count))
            {
                RestoreReleased(released, pCity, kingdom);
                WartimeGarrisonService.RequestSortieReserve(pCity);
                return false;
            }

            Army army = AWArmyService.CreateDetachedArmy(kingdom, pCity,
                released[0]);
            if (army?.data == null)
            {
                RestoreReleased(released, pCity, kingdom);
                return false;
            }
            PersistControlledShellIdentity(army, kingdom.id, pCity.id);

            var memberIds = new List<long>(released.Count);
            for (int i = 0; i < released.Count; i++)
            {
                Actor actor = released[i];
                if (actor.army != army) AWArmyService.AddToArmy(actor, army);
                if (actor.army == army)
                    memberIds.Add(actor.data.id);
                else
                    WartimeGarrisonService.ReturnFromSortie(actor, pCity,
                        kingdom);
            }
            if (!Runtime.TryBegin(army.id, kingdom.id, pCity.id,
                    memberIds))
            {
                RestoreReleased(released, pCity, kingdom);
                ArmyInvalidCleanupQueue.ScheduleShell(army, null, kingdom);
                return false;
            }

            pCity.data.set(LastLaunchedWarKey, warId);
            Persist(army, kingdom, pCity, memberIds);
            WartimeGarrisonService.ClearSortieReserve(pCity);
            ArmyFieldIndexService.OnArmyChanged(army);
            ArmyRtsControllerService.AssignMission(army,
                new ArmyRtsMission
                {
                    ArmyId = army.id,
                    KingdomId = kingdom.id,
                    WarId = warId,
                    FrontId = target.id,
                    TargetCityId = target.id,
                    ProposalKind = target == pCity
                        ? ArmyRtsProposalKind.Defend
                        : ArmyRtsProposalKind.Attack,
                    Role = ArmyRtsRole.TemporaryGarrisonSortie,
                    Posture = target == pCity
                        ? ArmyRtsPosture.Defend
                        : ArmyRtsPosture.Automatic,
                    IssuedTime = -1d
                });
            return true;
        }

        public static bool ShouldCompleteMission(Army pArmy,
            ArmyRtsMission pMission, City pTarget, Kingdom pKingdom)
        {
            if (!IsSortieArmy(pArmy) || pMission == null ||
                pKingdom?.data == null) return true;
            long originCityId = ReadOriginCityId(pArmy);
            City origin = ResolveCity(originCityId);
            bool targetIsOrigin = pTarget?.data != null &&
                                  pTarget.id == originCityId;
            bool originThreatened = IsThreatened(origin);
            ArmyRtsObjectiveState objectiveState =
                ArmyRtsObjectiveService.Classify(
                    ResolveWar(pMission.WarId), pKingdom, pTarget);
            bool targetControlled = GarrisonSortieRules.
                IsTargetSecuredForMissionCompletion(objectiveState);
            bool adjacentNeeded;
            if (targetIsOrigin)
                adjacentNeeded = FindAdjacentRecapture(origin, pKingdom,
                    pMission.WarId, out _);
            else
                adjacentNeeded = !targetControlled;
            return GarrisonSortieRules.ShouldCompleteMission(
                targetIsOrigin, originThreatened, adjacentNeeded,
                targetControlled);
        }

        public static bool OnMissionCompleted(Army pArmy)
        {
            if (!IsSortieArmy(pArmy)) return false;
            if (!Runtime.TryGet(pArmy.id, out _) && !TryHydrate(pArmy))
            {
                ArmyInvalidCleanupQueue.ScheduleShell(pArmy,
                    AWArmyService.FindAnchorCity(pArmy),
                    SafeKingdom(pArmy));
                return true;
            }
            ScheduleReturnBatch(pArmy.id);
            return true;
        }

        public static void OnKingdomDestroying(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            IReadOnlyList<long> ids = Runtime.GetArmyIds();
            for (int i = 0; i < ids.Count; i++)
                if (Runtime.TryGet(ids[i], out GarrisonSortieRecord record) &&
                    record.KingdomId == pKingdom.id)
                    ScheduleReturnBatch(ids[i]);
        }

        public static void OnOriginSupplyChanged(City pOrigin)
        {
            if (pOrigin?.data == null ||
                !Runtime.TryGetByOrigin(pOrigin.id,
                    out GarrisonSortieRecord record)) return;
            Kingdom kingdom = ResolveKingdom(record.KingdomId);
            if (kingdom?.data != null &&
                OccupiedCitySupplyService.CanProvideToRealm(pOrigin,
                    kingdom)) return;
            ArmyRtsControllerService.Invalidate(record.ArmyId);
            ScheduleReturnBatch(record.ArmyId);
        }

        public static void RebuildRuntime()
        {
            Runtime.Clear();
            if (World.world?.armies == null) return;
            foreach (Army army in World.world.armies)
            {
                if (!TryHydrate(army))
                {
                    ReclaimInvalidPersistedSortie(army);
                    continue;
                }
                if (!ArmyRtsControllerService.HasActiveMission(army.id))
                    ScheduleReturnBatch(army.id);
            }
        }

        public static void ClearRuntime()
        {
            Runtime.Clear();
        }

        private static void ScheduleReturnBatch(long pArmyId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "garrison_sortie_return", pArmyId),
                DeferredWorkClass.Runtime,
                () => ReturnBatch(pArmyId));
        }

        private static void ScheduleLaunchRetry(long pCityId)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey(
                    "garrison_sortie_launch", pCityId),
                DeferredWorkClass.Runtime,
                () => TryLaunch(ResolveCity(pCityId)));
        }

        private static void ReturnBatch(long pArmyId)
        {
            if (!Runtime.TryGet(pArmyId,
                    out GarrisonSortieRecord record)) return;
            City origin = ResolveCity(record.OriginCityId);
            Kingdom kingdom = ResolveKingdom(record.KingdomId);
            Army army = ResolveArmy(pArmyId);
            IReadOnlyList<long> batch = Runtime.TakeReturnBatch(pArmyId,
                GarrisonSortieRules.MemberMutationBatchSize);
            for (int i = 0; i < batch.Count; i++)
            {
                Actor actor = ResolveActor(batch[i]);
                if (!IsOwnedSortieMember(actor, pArmyId,
                        record.OriginCityId)) continue;
                ClearMemberFields(actor);
                if (origin?.data != null && !origin.isRekt() &&
                    kingdom?.data != null && !kingdom.isRekt() &&
                    origin.kingdom == kingdom && actor.kingdom == kingdom &&
                    !actor.isRekt() && actor.isAlive())
                    WartimeGarrisonService.ReturnFromSortie(actor, origin,
                        kingdom);
                else if (!actor.isRekt() && actor.isAlive())
                    TemporaryMilitaryDemobilizationService.RestoreCivilian(
                        actor);
            }
            PersistRemainingMemberIds(army,
                record.GetRemainingMemberIds());
            if (!record.ReturnComplete)
            {
                ScheduleReturnBatch(pArmyId);
                return;
            }

            Runtime.Complete(pArmyId);
            if (army?.data == null) return;
            using (ArmyCaptainDisposalScope.Open(army))
            {
                PersistControlledShellIdentity(army, record.KingdomId,
                    record.OriginCityId);
                try { army.setCaptain(null); } catch { }
                ArmyInvalidCleanupQueue.ScheduleShell(army, origin, kingdom);
            }
        }

        private static bool TryHydrate(Army pArmy)
        {
            if (pArmy?.data == null) return false;
            long originCityId = ReadOriginCityId(pArmy);
            if (originCityId < 0L) return false;
            pArmy.data.get(KingdomKey, out long kingdomId, -1L);
            pArmy.data.get(MemberIdsKey, out string encoded,
                string.Empty);
            IReadOnlyList<long> memberIds = DecodeMemberIds(encoded);
            return Runtime.TryBegin(pArmy.id, kingdomId, originCityId,
                memberIds);
        }

        private static void ReclaimInvalidPersistedSortie(Army pArmy)
        {
            if (pArmy?.data == null) return;
            long originCityId = ReadOriginCityId(pArmy);
            if (originCityId < 0L) return;
            pArmy.data.get(KingdomKey, out long kingdomId, -1L);
            pArmy.data.get(MemberIdsKey, out string encoded,
                string.Empty);

            City origin = ResolveCity(originCityId);
            Kingdom kingdom = ResolveKingdom(kingdomId);
            IReadOnlyList<long> memberIds = DecodeMemberIds(encoded);
            var processed = new HashSet<long>();
            for (int i = 0; i < memberIds.Count; i++)
            {
                long memberId = memberIds[i];
                if (!processed.Add(memberId)) continue;
                Actor actor = ResolveActor(memberId);
                if (!IsOwnedSortieMember(actor, pArmy.id, originCityId))
                    continue;
                ClearMemberFields(actor);
                if (origin?.data != null && !origin.isRekt() &&
                    kingdom?.data != null && !kingdom.isRekt() &&
                    origin.kingdom == kingdom && actor.kingdom == kingdom &&
                    !actor.isRekt() && actor.isAlive())
                    WartimeGarrisonService.ReturnFromSortie(actor, origin,
                        kingdom);
                else if (!actor.isRekt() && actor.isAlive())
                    TemporaryMilitaryDemobilizationService.RestoreCivilian(
                        actor);
            }

            PersistRemainingMemberIds(pArmy, Array.Empty<long>());
            using (ArmyCaptainDisposalScope.Open(pArmy))
            {
                try { pArmy.setCaptain(null); } catch { }
                ArmyInvalidCleanupQueue.ScheduleShell(pArmy, origin, kingdom);
            }
        }

        private static void Persist(Army pArmy, Kingdom pKingdom,
            City pOrigin, IReadOnlyList<long> pMemberIds)
        {
            pArmy.data.set(OriginCityKey, pOrigin.id);
            pArmy.data.set(KingdomKey, pKingdom.id);
            pArmy.data.set(MemberIdsKey, EncodeMemberIds(pMemberIds));
            for (int i = 0; i < pMemberIds.Count; i++)
            {
                Actor actor = ResolveActor(pMemberIds[i]);
                if (actor?.data == null) continue;
                actor.data.set(MemberArmyKey, pArmy.id);
                actor.data.set(MemberOriginCityKey, pOrigin.id);
            }
        }

        private static void PersistControlledShellIdentity(Army pArmy,
            long pKingdomId, long pOriginCityId)
        {
            pArmy.data.set(OriginCityKey, pOriginCityId);
            pArmy.data.set(KingdomKey, pKingdomId);
            pArmy.data.set(MemberIdsKey, string.Empty);
            ArmyFieldIndexService.OnArmyChanged(pArmy);
        }

        private static void PersistRemainingMemberIds(Army pArmy,
            IReadOnlyList<long> pMemberIds)
        {
            if (pArmy?.data == null) return;
            pArmy.data.set(MemberIdsKey, EncodeMemberIds(pMemberIds));
        }

        private static bool IsOwnedSortieMember(Actor pActor,
            long pArmyId, long pOriginCityId)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(MemberArmyKey, out long armyId, -1L);
            pActor.data.get(MemberOriginCityKey, out long originCityId,
                -1L);
            return armyId == pArmyId && originCityId == pOriginCityId &&
                   pActor.army?.id == pArmyId;
        }

        private static string EncodeMemberIds(IReadOnlyList<long> pIds)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < pIds.Count; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(pIds[i].ToString(
                    CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static IReadOnlyList<long> DecodeMemberIds(string pEncoded)
        {
            if (string.IsNullOrEmpty(pEncoded)) return Array.Empty<long>();
            string[] parts = pEncoded.Split(',');
            var result = new List<long>(parts.Length);
            for (int i = 0; i < parts.Length; i++)
                if (long.TryParse(parts[i], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out long actorId) &&
                    actorId >= 0L)
                    result.Add(actorId);
            return result;
        }

        private static bool FindAdjacentRecapture(City pOrigin,
            Kingdom pKingdom, long pWarId, out City pTarget)
        {
            pTarget = null;
            if (pOrigin?.data == null || pKingdom?.data == null ||
                pOrigin.neighbours_cities == null) return false;
            foreach (City adjacent in pOrigin.neighbours_cities)
            {
                if (adjacent?.data == null || adjacent.isRekt()) continue;
                if (!WarScoreService.IsFriendlySideRecaptureNeeded(
                        pWarId, adjacent, pKingdom)) continue;
                pTarget = adjacent;
                return true;
            }
            return false;
        }

        private static City FindAdjacentEnemy(City pOrigin,
            Kingdom pKingdom, long pWarId)
        {
            War war = ResolveWar(pWarId);
            if (pOrigin?.neighbours_cities == null || war?.data == null)
                return null;
            foreach (City adjacent in pOrigin.neighbours_cities)
            {
                if (adjacent?.data == null || adjacent.isRekt() ||
                    adjacent.kingdom?.data == null) continue;
                try
                {
                    if (war.isInWarWith(pKingdom, adjacent.kingdom))
                        return adjacent;
                }
                catch { }
            }
            return null;
        }

        private static bool IsThreatened(City pCity)
        {
            try
            {
                return pCity?.data != null &&
                       (pCity.isInDanger() || pCity.isGettingCaptured());
            }
            catch { return false; }
        }

        private static void RestoreReleased(IReadOnlyList<Actor> pActors,
            City pOrigin, Kingdom pKingdom)
        {
            for (int i = 0; i < pActors.Count; i++)
                if (pActors[i]?.data != null)
                {
                    ClearMemberFields(pActors[i]);
                    WartimeGarrisonService.ReturnFromSortie(pActors[i],
                        pOrigin, pKingdom);
                }
        }

        private static long ReadOriginCityId(Army pArmy)
        {
            pArmy.data.get(OriginCityKey, out long originCityId, -1L);
            return originCityId;
        }

        private static void ClearMemberFields(Actor pActor)
        {
            pActor.data.removeLong(MemberArmyKey);
            pActor.data.removeLong(MemberOriginCityKey);
        }

        private static Army ResolveArmy(long pId)
        {
            try { return World.world?.armies?.get(pId); }
            catch { return null; }
        }

        private static Actor ResolveActor(long pId)
        {
            try
            {
                if (World.world == null || World.world.units == null)
                    return null;
                return World.world.units.get(pId);
            }
            catch { return null; }
        }

        private static City ResolveCity(long pId)
        {
            try { return World.world?.cities?.get(pId); }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long pId)
        {
            try { return World.world?.kingdoms?.get(pId); }
            catch { return null; }
        }

        private static War ResolveWar(long pId)
        {
            try { return World.world?.wars?.get(pId); }
            catch { return null; }
        }

        private static Kingdom SafeKingdom(Army pArmy)
        {
            try { return pArmy?.getKingdom(); }
            catch { return null; }
        }
    }
#endif
}
