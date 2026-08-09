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
            if (!ArmyRtsRuntimeMode.ShouldCommit || Pending.Count == 0)
                return;
            using IEnumerator<KeyValuePair<long, Work>> iterator =
                Pending.GetEnumerator();
            if (!iterator.MoveNext()) return;
            long kingdomId = iterator.Current.Key;
            Work work = iterator.Current.Value;
            Kingdom kingdom = FindKingdom(kingdomId);
            if (!IsCurrent(kingdom, work))
            {
                Pending.Remove(kingdomId);
                return;
            }

            ArmyIds.Clear();
            ArmyStrategicIndexService.CopyArmyIdsAfter(kingdom,
                work.AfterArmyId,
                ArmyRtsSuccessionRecoveryRules.MaximumArmiesPerCycle,
                ArmyIds, out bool complete);
            for (int i = 0; i < ArmyIds.Count; i++)
            {
                long armyId = ArmyIds[i];
                Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                    armyId, kingdomId);
                if (army?.data == null) continue;
                EnsureNonSyntheticCaptain(army, kingdom);
                try { army.checkCaptainExistence(); }
                catch { }
                ArmyRtsControllerService.
                    RehydrateAfterAuthorityChange(army);
                ArmyRtsAssignmentReconciliationService.Enqueue(army);
                work.AfterArmyId = armyId;
            }
            if (!complete) return;
            CompletedKingByKingdom[kingdomId] = work.KingId;
            Pending.Remove(kingdomId);
            KingdomWarDirectorService.QueueArmyChanged(kingdom);
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
            ArmyIds.Clear();
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
    }
}
