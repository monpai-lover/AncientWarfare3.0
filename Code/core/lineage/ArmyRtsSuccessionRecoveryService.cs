using System.Collections.Generic;

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
        private static readonly List<long> ArmyIds = new List<long>(
            ArmyRtsSuccessionRecoveryRules.MaximumArmiesPerCycle);

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
            ProcessPendingRecoveries(
                ArmyRtsSuccessionRecoveryRules.MaximumArmiesPerCycle,
                pRequireRuntimeCommit: true);
        }

        internal static int PendingRecoveryUpperBound
        {
            get
            {
                long pending = PendingCaptainArmies.Count + Pending.Count;
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
            int limit = System.Math.Max(0, pMaximum);
            int processed = ProcessPendingCaptains(limit);
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
            if (pArmy?.data == null || pCaptainId < 0L ||
                !ArmyRtsControllerService.HasActiveMission(pArmy.id))
                return;
            PendingCaptainArmies.Add(pArmy.id);
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
                if (army?.data == null || kingdom?.data == null ||
                    !ArmyRtsControllerService.HasActiveMission(armyId))
                {
                    processed++;
                    continue;
                }
                EnsureNonSyntheticCaptain(army, kingdom);
                try { army.checkCaptainExistence(); }
                catch { }
                ArmyRtsControllerService.
                    RehydrateAfterAuthorityChange(army);
                ArmyRtsAssignmentReconciliationService.Enqueue(army);
                if (!HasLiveCaptain(army))
                    PendingCaptainArmies.Add(armyId);
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
            bool currentValid = IsEligibleCaptain(pArmy, pKingdom, current);
            if (currentValid) return;

            if (current?.data != null &&
                (SyntheticLevyService.IsSynthetic(current) ||
                 !currentValid))
            {
                using (ArmyCaptainDisposalScope.Open(pArmy))
                {
                    try { pArmy.setCaptain(null); }
                    catch { }
                }
            }

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
                AWArmyService.SetCaptainIfChanged(pArmy, general);
                try
                {
                    if (pArmy.getCaptain() == general) return;
                }
                catch { }
            }
        }

        private static bool IsEligibleCaptain(Army pArmy,
            Kingdom pKingdom, Actor pActor)
        {
            try
            {
                return pActor?.data != null && pActor.kingdom == pKingdom &&
                       pActor.isAlive() && !pActor.isRekt() &&
                       !SyntheticLevyService.IsSynthetic(pActor) &&
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
                       !SyntheticLevyService.IsSynthetic(pActor) &&
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
            ArmyIds.Clear();
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
    }
}
