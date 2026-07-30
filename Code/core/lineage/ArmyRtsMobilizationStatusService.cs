#if !AW3_RULES_TESTS
using System;
using AncientWarfare3.content;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct ArmyRtsMobilizationStatusReconciliation
    {
        public ArmyRtsMobilizationStatusReconciliation(int pNextCursor,
            bool pCompletedPass, bool pHasPendingAssembly)
        {
            NextCursor = pNextCursor;
            CompletedPass = pCompletedPass;
            HasPendingAssembly = pHasPendingAssembly;
        }

        public int NextCursor { get; }
        public bool CompletedPass { get; }
        public bool HasPendingAssembly { get; }
    }

    internal static class ArmyRtsMobilizationStatusService
    {
        private const int MaximumMembersPerController = 8;
        private const float StatusDurationSeconds = 20f;

        public static ArmyRtsMobilizationStatusReconciliation Reconcile(
            Army pArmy, ArmyRtsState pState,
            int pCursor)
        {
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            if (count <= 0)
                return new ArmyRtsMobilizationStatusReconciliation(0,
                    pCompletedPass: true, pHasPendingAssembly: false);

            int start = Math.Max(0, Math.Min(pCursor, count));
            int end = Math.Min(count,
                start + MaximumMembersPerController);
            bool hasPendingAssembly = false;
            for (int i = start; i < end; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                if (ReconcileActor(actor, pArmy, pState))
                    hasPendingAssembly = true;
            }
            bool completedPass = end >= count;
            return new ArmyRtsMobilizationStatusReconciliation(
                completedPass ? 0 : end, completedPass,
                hasPendingAssembly);
        }

        public static void Clear(Army pArmy)
        {
            int count;
            try { count = pArmy?.units?.Count ?? 0; }
            catch { count = 0; }
            for (int i = 0; i < count; i++)
            {
                Actor actor;
                try { actor = pArmy.units[i]; }
                catch { continue; }
                try { actor?.finishStatusEffect(
                    ArmyRtsContent.MobilizationSpeedStatusId); }
                catch { }
            }
        }

        private static bool ReconcileActor(Actor pActor, Army pArmy,
            ArmyRtsState pState)
        {
            if (pActor?.data == null) return false;
            bool eligible = pActor.army == pArmy &&
                            !pActor.isRekt() && pActor.isAlive() &&
                            pActor.is_profession_warrior;
            bool assemblyComplete = IsAssemblyComplete(pActor, pArmy);
            bool active = ArmyRtsMobilizationStatusRules.ShouldApplyToMember(
                pState, eligible, assemblyComplete);
            try
            {
                if (active)
                {
                    if (!pActor.hasStatus(
                            ArmyRtsContent.MobilizationSpeedStatusId))
                        pActor.addStatusEffect(
                            ArmyRtsContent.MobilizationSpeedStatusId,
                            StatusDurationSeconds, pColorEffect: false);
                    return true;
                }
                pActor.finishStatusEffect(ArmyRtsContent.MobilizationSpeedStatusId);
            }
            catch { }
            return false;
        }

        private static bool IsAssemblyComplete(Actor pActor, Army pArmy)
        {
            try
            {
                if (pArmy?.getCaptain() == pActor) return true;
            }
            catch { }
            return ArmyFormationService.IsInsideLooseEscort(pActor);
        }
    }
}
#endif
