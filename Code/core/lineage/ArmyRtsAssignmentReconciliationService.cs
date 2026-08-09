using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyRtsAssignmentReconciliationService
    {
        private const int MaximumRecordsPerCycle = 8;
        private static int _cursor;

        public static void ProcessAuthorityCycle()
        {
            if (!ArmyRtsRuntimeMode.ShouldCommit) return;
            IReadOnlyList<ArmyRtsWarLifecycleRecord> records =
                ArmyRtsWarLifecycleService.Snapshot();
            int count = records?.Count ?? 0;
            if (count == 0)
            {
                _cursor = 0;
                return;
            }

            int start = _cursor % count;
            if (start < 0) start += count;
            int maximum = Math.Min(count, MaximumRecordsPerCycle);
            double now = LineageService.CurTime();
            for (int i = 0; i < maximum; i++)
                ProcessOne(records[(start + i) % count], now);
            _cursor = (start + maximum) % count;
        }

        public static void Reset()
        {
            _cursor = 0;
        }

        public static void Enqueue(Army pArmy)
        {
            if (pArmy?.data == null) return;
            ArmyRtsControllerService.OnArmyRosterChanged(pArmy);
            KingdomWarDirectorService.QueueArmyChanged(
                AWArmyService.GetIntendedKingdom(pArmy));
        }

        private static void ProcessOne(ArmyRtsWarLifecycleRecord pRecord,
            double pNow)
        {
            if (pRecord == null) return;
            Army army = FindArmy(pRecord.ArmyId);
            War war = FindWar(pRecord.WarId);
            Kingdom kingdom = AWArmyService.GetIntendedKingdom(army);
            bool eligible = IsEligible(army, war, kingdom);
            bool hasMission = eligible &&
                              ArmyRtsControllerService.HasActiveMission(
                                  pRecord.ArmyId);
            bool ownsTacticalActors = ArmyRtsWarLifecycleRules.
                OwnsTacticalActors(pRecord.Phase);
            bool expectedCaptainTask = !hasMission ||
                !ownsTacticalActors || ArmyRtsControllerService.
                    HasExpectedCaptainTask(army);
            ArmyRtsAssignmentReconciliationAction action =
                ArmyRtsAssignmentReconciliationRules.Resolve(eligible,
                    WarArmyReturnService.IsActive(army), hasMission,
                    ownsTacticalActors, expectedCaptainTask,
                    pRecord.WaitReason, pRecord.WaitDeadline, pNow);
            if (action == ArmyRtsAssignmentReconciliationAction.
                    RepairCaptainTask)
            {
                ArmyRtsControllerService.OnArmyRosterChanged(army);
                return;
            }
            if (action != ArmyRtsAssignmentReconciliationAction.
                    QueueAssignment) return;

            CoalitionWarTaskService.ReleaseArmyClaim(pRecord.ArmyId);
            ArmyRtsWarLifecycleService.MarkWaiting(pRecord.WarId, army,
                "director_assignment_queued", pNow +
                ArmyRtsAssignmentReconciliationRules.
                    AssignmentRetryWorldSeconds);
            KingdomWarDirectorService.QueueArmyChanged(kingdom);
        }

        private static bool IsEligible(Army pArmy, War pWar,
            Kingdom pKingdom)
        {
            if (!ArmyNativeNameService.IsOrdinaryArmy(pArmy) ||
                pKingdom?.data == null || pWar?.data == null) return false;
            try
            {
                return pArmy.isAlive() && !pKingdom.isRekt() &&
                       pKingdom.isAlive() && !pWar.hasEnded() &&
                       pWar.hasKingdom(pKingdom);
            }
            catch { return false; }
        }

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static War FindWar(long pWarId)
        {
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }
    }
}
