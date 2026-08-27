using System;
using System.Collections.Generic;
using AncientWarfare3.content.schools;
using AncientWarfare3.core.performance;
using AncientWarfare3.core.schools;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRtsSuccessionRecoveryService
    {
        private sealed class Work
        {
            internal long KingId;
            internal long AfterArmyId = -1L;
        }

        private static readonly SortedDictionary<long, Work> Pending =
            new SortedDictionary<long, Work>();
        private static readonly Dictionary<long, long> CompletedKingByKingdom =
            new Dictionary<long, long>();
        private static readonly SortedSet<long> PendingCaptainArmies =
            new SortedSet<long>();
        private static readonly SortedSet<long> PendingLowForceArmies =
            new SortedSet<long>();
        private static readonly HashSet<long> MissingArmyRecoveryQueued =
            new HashSet<long>();
        private static readonly Dictionary<long, long> CaptainRetryAfterCycle =
            new Dictionary<long, long>();
        private static readonly List<long> ArmyIds = new List<long>(
            ArmyRtsSuccessionRecoveryRules.MaximumArmiesPerCycle);
        private static long _authorityCycle;

        internal static void OnKingInstalled(Kingdom pKingdom, Actor pKing,
            bool pFromLoad = false)
        {
            long completedKingId = CompletedKingByKingdom.TryGetValue(
                pKingdom?.id ?? -1L, out long completed) ? completed : -1L;
            bool validKingdom = pKingdom?.data != null &&
                                !pKingdom.isRekt();
            bool validKing = pKing?.data != null && pKing.isAlive() &&
                             !pKing.isRekt();
            if (!ArmyRtsSuccessionRecoveryRules.ShouldEnqueue(validKingdom,
                    validKing, pFromLoad, pKingdom?.king?.data?.id ?? -1L,
                    pKing?.data?.id ?? -1L, completedKingId)) return;
            Pending[pKingdom.id] = new Work { KingId = pKing.data.id };
            KingdomWarDirectorService.Schedule(pKingdom);
        }

        internal static void ProcessAuthorityCycle()
        {
            if (_authorityCycle < long.MaxValue) _authorityCycle++;
            ProcessPendingRecoveries(
                ArmyRtsSuccessionRecoveryRules.MaximumArmiesPerCycle,
                pRequireRuntimeCommit: true);
        }

        internal static int PendingRecoveryUpperBound
        {
            get
            {
                DiscoverMissionContinuity();
                long pending = PendingCaptainArmies.Count + Pending.Count;
                pending += PendingLowForceArmies.Count;
                foreach (KeyValuePair<long, Work> pair in Pending)
                {
                    Kingdom kingdom = FindKingdom(pair.Key);
                    pending += ArmyStrategicIndexService.
                        CreateSnapshotCursor(kingdom).Remaining;
                    if (pending >= int.MaxValue) return int.MaxValue;
                }
                return (int)System.Math.Max(0L, pending);
            }
        }

        internal static int ProcessPendingRecoveries(int pMaximum,
            bool pRequireRuntimeCommit = true)
        {
            if (pRequireRuntimeCommit &&
                !ArmyRtsRuntimeMode.ShouldCommit) return 0;
            DiscoverMissionContinuity();
            int limit = System.Math.Max(0, pMaximum);
            int processed = ProcessPendingCaptains(limit);
            processed += ProcessPendingLowForceArmies(
                System.Math.Max(0, limit - processed));
            int kingdomVisits = Pending.Count;
            var toRemove = new List<long>();
            while (processed < limit && Pending.Count > 0 &&
                   kingdomVisits-- > 0)
            {
                long kingdomId = -1L;
                Work work = null;
                foreach (KeyValuePair<long, Work> pair in Pending)
                {
                    kingdomId = pair.Key;
                    work = pair.Value;
                    break;
                }
                if (kingdomId < 0L || work == null) break;
                Kingdom kingdom = FindKingdom(kingdomId);
                if (!IsCurrent(kingdom, work))
                {
                    toRemove.Add(kingdomId);
                    continue;
                }

                ArmyIds.Clear();
                ArmyStrategicIndexService.CopyArmyIdsAfter(kingdom,
                    work.AfterArmyId, limit - processed,
                    ArmyIds, out bool complete);
                for (int i = 0; i < ArmyIds.Count; i++)
                {
                    long armyId = ArmyIds[i];
                    work.AfterArmyId = armyId;
                    Army army = ArmyStrategicIndexService.
                        ResolveIndexedArmy(armyId, kingdomId);
                    if (army?.data != null)
                    {
                        EnsureNonSyntheticCaptain(army, kingdom);
                        try { army.checkCaptainExistence(); }
                        catch { }
                        ArmyRtsControllerService.
                            RehydrateAfterAuthorityChange(army);
                        ArmyRtsAssignmentReconciliationService.
                            Enqueue(army);
                    }
                    processed++;
                }
                if (!complete) break;
                CompletedKingByKingdom[kingdomId] = work.KingId;
                toRemove.Add(kingdomId);
                KingdomWarDirectorService.QueueArmyChanged(kingdom);
            }
            for (int i = 0; i < toRemove.Count; i++)
                Pending.Remove(toRemove[i]);
            return processed;
        }

        internal static int PendingCaptainCount =>
            PendingCaptainArmies.Count;

        internal static void OnCaptainDied(Army pArmy, long pCaptainId)
        {
            if (pArmy?.data == null || pCaptainId < 0L)
                return;
            CaptainRetryAfterCycle.Remove(pArmy.id);
            PendingCaptainArmies.Add(pArmy.id);
            LogCaptainRecovery("captain_vacancy_enqueued", pArmy,
                "source=death previous=" + pCaptainId);
        }

        internal static void OnCaptainVacated(Army pArmy,
            long pPreviousCaptainId)
        {
            if (pArmy?.data == null || pPreviousCaptainId < 0L)
                return;
            CaptainRetryAfterCycle.Remove(pArmy.id);
            PendingCaptainArmies.Add(pArmy.id);
            LogCaptainRecovery("captain_vacancy_enqueued", pArmy,
                "source=vacated previous=" + pPreviousCaptainId);
        }

        private static void DiscoverMissionContinuity()
        {
            IReadOnlyList<ArmyRtsMission> missions =
                ArmyRtsControllerService.SnapshotMissions();
            for (int i = 0; i < missions.Count; i++)
            {
                ArmyRtsMission mission = missions[i];
                if (mission == null || mission.ArmyId < 0L) continue;
                Army army = FindArmy(mission.ArmyId);
                Kingdom kingdom = FindKingdom(mission.KingdomId);
                if (army?.data == null || kingdom?.data == null ||
                    kingdom.isRekt())
                {
                    TryRequestMissingFieldArmy(mission, kingdom);
                    continue;
                }
                MissingArmyRecoveryQueued.Remove(mission.ArmyId);
                if (!ArmyRtsControllerService.HasActiveMission(army.id))
                    continue;
                int living = SafeUnitCount(army);
                bool captainRecoveryReady =
                    !CaptainRetryAfterCycle.TryGetValue(army.id,
                        out long retryAfter) ||
                    ArmyRtsSuccessionRecoveryRules.
                        ShouldAttemptCaptainRecovery(_authorityCycle,
                            retryAfter);
                if (!HasOperationalCaptain(army, kingdom) && living > 0 &&
                    captainRecoveryReady)
                    PendingCaptainArmies.Add(army.id);
                if (living < ArmyLogisticsRules.MinimumOperationalForce &&
                    mission.ProposalKind != ArmyRtsProposalKind.Retreat)
                    PendingLowForceArmies.Add(army.id);
                else
                    PendingLowForceArmies.Remove(army.id);
            }
        }

        private static int ProcessPendingLowForceArmies(int pMaximum)
        {
            int processed = 0;
            while (processed < pMaximum && PendingLowForceArmies.Count > 0)
            {
                long armyId = PendingLowForceArmies.Min;
                PendingLowForceArmies.Remove(armyId);
                Army army = FindArmy(armyId);
                if (army?.data != null &&
                    ArmyRtsControllerService.HasActiveMission(army.id) &&
                    SafeUnitCount(army) < ArmyLogisticsRules.MinimumOperationalForce)
                {
                    ArmyRetreatService.AssignArmyRetreat(army, -1L,
                        ArmyRtsWithdrawalOrigin.MinimumForce);
                }
                processed++;
            }
            return processed;
        }

        private static void TryRequestMissingFieldArmy(
            ArmyRtsMission pMission, Kingdom pKingdom)
        {
            if (pMission == null || pKingdom?.data == null ||
                pMission.WarId < 0L ||
                !MissingArmyRecoveryQueued.Add(pMission.ArmyId)) return;
            City source = pKingdom.capital;
            if (source?.data == null && pKingdom.cities?.Count > 0)
                source = pKingdom.cities[0];
            if (source?.data == null) return;
            if (ArmyFieldIndexService.TryGetCityArmy(source, out _)) return;
            TemporaryLevyService.RequestOffensiveRecovery(
                pKingdom, source,
                ArmyLogisticsRules.MinimumOperationalForce,
                pForceEstablishment: true);
        }

        internal static int ProcessPendingCaptains(int pMaximum)
        {
            int limit = System.Math.Min(System.Math.Max(0, pMaximum),
                PendingCaptainArmies.Count);
            int processed = 0;
            while (processed < limit && PendingCaptainArmies.Count > 0)
            {
                long armyId = PendingCaptainArmies.Min;
                PendingCaptainArmies.Remove(armyId);
                Army army = FindArmy(armyId);
                Kingdom kingdom = AWArmyService.GetIntendedKingdom(army);
                bool missionActive = ArmyRtsControllerService.
                    HasActiveMission(armyId);
                bool wartimeEmergency = MilitaryEmergencyService.
                    HasAny(kingdom);
                if (army?.data == null || kingdom?.data == null ||
                    !missionActive && !wartimeEmergency)
                {
                    processed++;
                    continue;
                }
                LogCaptainRecovery("captain_recovery_dequeued", army,
                    "mission=" + missionActive + " emergency=" +
                    wartimeEmergency);
                EnsureNonSyntheticCaptain(army, kingdom);
                try { army.checkCaptainExistence(); }
                catch { }
                EnsureNonSyntheticCaptain(army, kingdom);
                ArmyRtsControllerService.
                    RehydrateAfterAuthorityChange(army);
                ArmyRtsAssignmentReconciliationService.Enqueue(army);
                bool captainOperational = HasOperationalCaptain(army,
                    kingdom);
                if (captainOperational)
                    CaptainRetryAfterCycle.Remove(armyId);
                if (ArmyRtsSuccessionRecoveryRules.ShouldRetryCaptainRecovery(
                        armyValid: army?.data != null,
                        captainOperational: captainOperational,
                        liveWarriorCount: SafeUnitCount(army),
                        missionActive: missionActive,
                        wartimeEmergency: wartimeEmergency))
                {
                    PendingCaptainArmies.Add(armyId);
                    CaptainRetryAfterCycle[armyId] = _authorityCycle +
                        ArmyRtsSuccessionRecoveryRules.
                            CaptainRecoveryRetryCooldownCycles;
                    LogCaptainRecovery("captain_recovery_retry", army,
                        "mission=" + missionActive + " emergency=" +
                        wartimeEmergency);
                }
                else
                    LogCaptainRecovery("captain_recovery_complete", army,
                        "operational=" + captainOperational);
                processed++;
            }
            return processed;
        }

        private static void EnsureNonSyntheticCaptain(Army pArmy,
            Kingdom pKingdom)
        {
            if (pArmy?.data == null || pKingdom?.data == null) return;
            Actor current = null;
            try { current = pArmy.getCaptain(); }
            catch { }
            PromoteSyntheticCaptainIfNeeded(current);
            bool currentValid = IsEligibleCaptain(pArmy, pKingdom, current);
            if (currentValid) return;

            if (current?.data != null && !currentValid)
            {
                using (ArmyCaptainDisposalScope.Open(pArmy))
                {
                    try { pArmy.setCaptain(null); }
                    catch { }
                }
            }

            if (TemporaryLevyService.TryPromoteExistingLevyCaptain(pArmy))
                return;

            List<GeneralReadModelEntry> generals = GeneralService.
                GetActiveGeneralsForReadModel(pKingdom,
                    pAllowUnitFallback: false, pLimit: 8);
            for (int i = 0; i < generals.Count; i++)
            {
                Actor general = generals[i]?.Actor;
                if (!IsEligibleGeneral(pArmy, pKingdom, general)) continue;
                if (general.army != null && general.army != pArmy) continue;
                if (general.army != pArmy)
                    AWArmyService.AddToArmy(general, pArmy);
                PromoteSyntheticCaptainIfNeeded(general);
                AWArmyService.SetCaptainIfChanged(pArmy, general);
                try
                {
                    if (pArmy.getCaptain() == general) return;
                }
                catch { }
            }

            Actor replacement = FindStrongestArmyMember(pArmy, pKingdom,
                out string candidateDiagnostics);
            LogCaptainRecovery("captain_candidate_scan", pArmy,
                "selected=" + (replacement?.data?.id ?? -1L) +
                " candidates=" + candidateDiagnostics);
            if (replacement?.data == null) return;
            replacement.data.get(LineageKeys.TEMPORARY_LEVY,
                out bool temporaryLevy, false);
            if (temporaryLevy)
                TemporaryLevyService.PromoteToPermanentMilitary(replacement);
            PromoteSyntheticCaptainIfNeeded(replacement);
            bool candidateEligible = IsEligibleArmyMember(pArmy, pKingdom,
                replacement);
            if (!ArmyRtsSuccessionRecoveryRules.ShouldInstallWarriorCaptain(
                    candidateEligible,
                    HasOperationalCaptain(pArmy, pKingdom))) return;
            AWArmyService.SetCaptainAfterSuccession(pArmy, replacement);
            LogCaptainRecovery("captain_promoted", pArmy,
                "candidate=" + replacement.data.id + " installed=" +
                (CurrentCaptainId(pArmy) == replacement.data.id));
            GeneralService.PromoteToGeneral(replacement);
        }

        private static long CurrentCaptainId(Army pArmy)
        {
            try { return pArmy?.getCaptain()?.data?.id ?? -1L; }
            catch { return -1L; }
        }

        private static void LogCaptainRecovery(string pStage, Army pArmy,
            string pDetails)
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled) return;
            ModClass.LogError("[AW3 RTS succession] stage=" + pStage +
                " army=" + (pArmy?.id ?? -1L) + " captain=" +
                CurrentCaptainId(pArmy) + " units=" + SafeUnitCount(pArmy) +
                " " + pDetails);
        }

        private static bool IsEligibleCaptain(Army pArmy,
            Kingdom pKingdom, Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.kingdom == pKingdom &&
                       pActor.isAlive() && !pActor.isRekt() &&
                       AWArmyService.IsCaptainLeaseEligible(pArmy, pActor,
                           requireMembership: true);
            }
            catch { return false; }
        }

        private static bool IsEligibleGeneral(Army pArmy,
            Kingdom pKingdom, Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.kingdom == pKingdom &&
                       pActor.isAlive() && !pActor.isRekt() &&
                       GeneralService.IsGeneral(pActor) &&
                       AWArmyService.IsCaptainLeaseEligible(pArmy, pActor,
                           requireMembership: false);
            }
            catch { return false; }
        }

        internal static void Reset()
        {
            Pending.Clear();
            CompletedKingByKingdom.Clear();
            PendingCaptainArmies.Clear();
            PendingLowForceArmies.Clear();
            MissingArmyRecoveryQueued.Clear();
            CaptainRetryAfterCycle.Clear();
            ArmyIds.Clear();
            _authorityCycle = 0L;
        }

        private static bool HasLiveCaptain(Army pArmy)
        {
            try
            {
                Actor captain = pArmy?.getCaptain();
                return captain?.data != null && captain.isAlive() &&
                       !captain.isRekt();
            }
            catch { return false; }
        }

        private static bool IsCurrent(Kingdom pKingdom, Work pWork)
        {
            return pKingdom?.data != null && pWork != null &&
                   !pKingdom.isRekt() &&
                   pKingdom.king?.data?.id == pWork.KingId;
        }

        private static Kingdom FindKingdom(long pKingdomId)
        {
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return System.Math.Max(0, pArmy?.countUnits() ?? 0); }
            catch { return 0; }
        }

        private static Actor FindStrongestArmyMember(Army pArmy,
            Kingdom pKingdom, out string pDiagnostics)
        {
            Actor best = null;
            float bestScore = float.MinValue;
            long bestId = -1L;
            var rejected = new List<string>();
            try
            {
                foreach (Actor member in pArmy.getUnits())
                {
                    if (!IsEligibleArmyMember(pArmy, pKingdom, member))
                    {
                        if (rejected.Count < 8)
                            rejected.Add(DescribeIneligibleArmyMember(
                                pArmy, pKingdom, member));
                        continue;
                    }
                    float score = CombatScore(member);
                    long id = member.data.id;
                    if (!ArmyCaptainContinuityRules.ShouldPreferLevyPromotion(
                            bestScore, bestId, score, id)) continue;
                    best = member;
                    bestScore = score;
                    bestId = id;
                }
            }
            catch { }
            pDiagnostics = rejected.Count == 0
                ? "eligible"
                : string.Join(";", rejected);
            return best;
        }

        private static string DescribeIneligibleArmyMember(Army pArmy,
            Kingdom pKingdom, Actor pActor)
        {
            long id = pActor?.data?.id ?? -1L;
            try
            {
                return "id=" + id +
                       ",army=" + (pActor?.army == pArmy) +
                       ",kingdom=" + (pActor?.kingdom == pKingdom) +
                       ",alive=" + (pActor?.isAlive() == true) +
                       ",adult=" + (pActor?.isAdult() == true) +
                       ",profession=" +
                           (pActor?.is_profession_warrior == true) +
                       ",king=" + (pActor?.isKing() == true) +
                       ",city_leader=" +
                           (pActor?.isCityLeader() == true) +
                       ",slave=" + SlaveService.IsSlave(pActor) +
                       ",guard=" + RoyalGuardService.IsRoyalGuard(pActor) +
                       ",garrison=" +
                           WartimeGarrisonService.IsActive(pActor) +
                       ",vanguard=" +
                           TemporarySlaveVanguardService.IsMember(pActor) +
                       ",synthetic=" +
                           SyntheticLevyService.IsSynthetic(pActor);
            }
            catch { return "id=" + id + ",inspection_error=true"; }
        }

        private static bool IsEligibleArmyMember(Army pArmy,
            Kingdom pKingdom, Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.army == pArmy &&
                       pActor.kingdom == pKingdom && pActor.isAlive() &&
                       !pActor.isRekt() && pActor.isAdult() &&
                       pActor.is_profession_warrior && !pActor.isKing() &&
                       !pActor.isCityLeader() && !SlaveService.IsSlave(pActor) &&
                       !RoyalGuardService.IsRoyalGuard(pActor) &&
                       !WartimeGarrisonService.IsActive(pActor) &&
                       !TemporarySlaveVanguardService.IsMember(pActor);
            }
            catch { return false; }
        }

        private static void PromoteSyntheticCaptainIfNeeded(Actor pActor)
        {
            SyntheticLevyService.PromoteToPermanentCommand(pActor);
        }

        private static bool HasOperationalCaptain(Army pArmy,
            Kingdom pKingdom)
        {
            Actor captain = null;
            try { captain = pArmy?.getCaptain(); }
            catch { }
            return IsEligibleCaptain(pArmy, pKingdom, captain);
        }

        private static float CombatScore(Actor pActor)
        {
            if (pActor?.stats == null) return 0f;
            return ReadCombatStat(pActor, "damage") +
                   ReadCombatStat(pActor, "warfare") * 2f +
                   ReadCombatStat(pActor, "health") * 0.1f +
                   ReadCombatStat(pActor, "armor") * 2f +
                   ReadCombatStat(pActor, "speed") * 0.25f;
        }

        private static float ReadCombatStat(Actor pActor, string pStat)
        {
            try { return pActor.stats[pStat]; }
            catch { return 0f; }
        }
    }
}
