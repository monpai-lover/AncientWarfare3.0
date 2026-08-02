using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyReplenishmentCompletionService
    {
        internal static void Complete(Army pArmy)
        {
            ArmyRtsControllerService.OnReplenishmentOperationCompleted(pArmy);
            if (!IsOrdinaryArmy(pArmy, out Kingdom kingdom)) return;
            KingdomWarDirectorService.QueueArmyChanged(kingdom);
            KingdomWarDirectorService.EnsureOffensiveContinuity(kingdom);
        }

        internal static bool HasViableAttack(Kingdom pKingdom)
        {
            IReadOnlyList<Army> armies = CollectOrdinaryArmies(pKingdom);
            for (int i = 0; i < armies.Count; i++)
            {
                Army army = armies[i];
                if (HasViableAttackAssignment(army, pKingdom,
                        SafeUnitCount(army))) return true;
            }
            return false;
        }

        internal static bool TryPrepareOffensivePrimary(Kingdom pKingdom,
            out Army pPrimary)
        {
            return TryResolveOffensivePrimary(pKingdom,
                pAllowConsolidation: true, out pPrimary);
        }

        internal static bool TrySelectOffensivePrimary(Kingdom pKingdom,
            out Army pPrimary)
        {
            return TryResolveOffensivePrimary(pKingdom,
                pAllowConsolidation: false, out pPrimary);
        }

        private static bool TryResolveOffensivePrimary(Kingdom pKingdom,
            bool pAllowConsolidation, out Army pPrimary)
        {
            pPrimary = null;
            IReadOnlyList<Army> armies = CollectOrdinaryArmies(pKingdom);
            int minimum = ArmyLogisticsRules.MinimumOperationalForce;
            long total = 0L;
            Army bestAttack = null;
            int bestAttackLiving = -1;
            long bestAttackId = long.MaxValue;
            Army bestAvailable = null;
            int bestAvailableLiving = -1;
            long bestAvailableId = long.MaxValue;
            Army bestAny = null;
            int bestLiving = -1;
            long bestId = long.MaxValue;

            for (int i = 0; i < armies.Count; i++)
            {
                Army army = armies[i];
                int living = SafeUnitCount(army);
                total = Math.Min(int.MaxValue, total + living);
                if (living > bestLiving ||
                    living == bestLiving && army.id < bestId)
                {
                    bestAny = army;
                    bestLiving = living;
                    bestId = army.id;
                }
                if (CanRepurpose(army) &&
                    (living > bestAvailableLiving ||
                     living == bestAvailableLiving &&
                     army.id < bestAvailableId))
                {
                    bestAvailable = army;
                    bestAvailableLiving = living;
                    bestAvailableId = army.id;
                }
                if (HasViableAttackAssignment(army, pKingdom, living) &&
                    (living > bestAttackLiving ||
                     living == bestAttackLiving &&
                     army.id < bestAttackId))
                {
                    bestAttack = army;
                    bestAttackLiving = living;
                    bestAttackId = army.id;
                }
            }

            if (bestAttack?.data != null)
            {
                pPrimary = bestAttack;
                return true;
            }
            if (!ArmyReplenishmentOperationRules.MustMaintainAttack(
                    (int)total, minimum, validEnemyTarget: true))
                return false;

            pPrimary = bestAvailable ?? bestAny;
            if (pPrimary?.data == null) return false;
            if (!pAllowConsolidation) return true;
            for (int i = 0; i < armies.Count &&
                            SafeUnitCount(pPrimary) < minimum; i++)
            {
                Army source = armies[i];
                if (source == pPrimary) continue;
                int living = SafeUnitCount(source);
                if (!ArmyReplenishmentOperationRules.ShouldMergeSecondary(
                        living, minimum, ordinary: true,
                        primaryExists: true)) continue;
                AWArmyService.TryMergeOrdinaryArmyInto(source, pPrimary);
            }
            if (ArmyReplenishmentOperationRules.ShouldResumeAttack(
                    SafeUnitCount(pPrimary), minimum)) return true;
            if (bestAny != pPrimary &&
                ArmyReplenishmentOperationRules.ShouldResumeAttack(
                    SafeUnitCount(bestAny), minimum))
            {
                pPrimary = bestAny;
                return true;
            }
            return false;
        }

        private static IReadOnlyList<Army> CollectOrdinaryArmies(
            Kingdom pKingdom)
        {
            var result = new List<Army>();
            if (pKingdom?.data == null || pKingdom.isRekt()) return result;
            ArmyStrategicIdCursor cursor = ArmyStrategicIndexService.
                CreateSnapshotCursor(pKingdom);
            while (cursor != null && !cursor.IsComplete)
            {
                IReadOnlyList<long> ids = cursor.Take(
                    ArmyEstablishmentRules.MaximumFieldArmies);
                if (ids.Count == 0) break;
                for (int i = 0; i < ids.Count; i++)
                {
                    Army army = ArmyStrategicIndexService.ResolveIndexedArmy(
                        ids[i], pKingdom.id);
                    if (IsOrdinaryArmy(army, out Kingdom kingdom) &&
                        kingdom == pKingdom && SafeUnitCount(army) > 0)
                        result.Add(army);
                }
            }
            result.Sort((left, right) => left.id.CompareTo(right.id));
            return result;
        }

        private static bool IsOrdinaryArmy(Army pArmy,
            out Kingdom pKingdom)
        {
            pKingdom = null;
            if (pArmy?.data == null || AWArmyService.IsSpecialArmy(pArmy) ||
                GarrisonSortieService.IsSortieArmy(pArmy) ||
                AWArmyService.IsNonReplacingShell(pArmy)) return false;
            pArmy.data.get(LineageKeys.RESTORATION_UPRISING_ARMY,
                out bool restorationArmy, false);
            if (restorationArmy) return false;
            Actor captain = null;
            try
            {
                if (!pArmy.isAlive()) return false;
                pKingdom = pArmy.getKingdom();
                captain = pArmy.getCaptain();
            }
            catch { return false; }
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   !RoyalGuardService.IsRoyalGuard(captain) &&
                   !TemporarySlaveVanguardService.IsMember(captain) &&
                   !WartimeGarrisonService.IsActive(captain);
        }

        private static bool HasAttackAssignment(Army pArmy)
        {
            return ArmyRtsControllerService.TryGetMission(pArmy,
                       out ArmyRtsMission mission) && mission != null &&
                   mission.ProposalKind == ArmyRtsProposalKind.Attack;
        }

        private static bool HasViableAttackAssignment(Army pArmy,
            Kingdom pKingdom, int pLiving)
        {
            bool attackAssignment = ArmyRtsControllerService.TryGetMission(
                pArmy, out ArmyRtsMission mission) && mission != null &&
                mission.ProposalKind == ArmyRtsProposalKind.Attack;
            War war = FindWar(mission?.WarId ?? -1L);
            City target = FindCity(mission?.TargetCityId ?? -1L);
            bool warActive;
            ArmyRtsObjectiveState objectiveState;
            try
            {
                warActive = war?.data != null && !war.hasEnded() &&
                            war.hasKingdom(pKingdom);
                objectiveState = warActive && target?.data != null
                    ? ArmyRtsObjectiveService.Classify(war, pKingdom, target)
                    : ArmyRtsObjectiveState.Unavailable;
            }
            catch
            {
                warActive = false;
                objectiveState = ArmyRtsObjectiveState.Unavailable;
            }
            int targetStrength = Math.Max(pLiving,
                CityArmyReinforcementService.ApprovedTarget(pArmy,
                    pKingdom));
            return ArmyRtsRules.IsViableAttackAssignment(
                attackAssignment, warActive, objectiveState, pLiving,
                targetStrength, ArmyLogisticsRules.MinimumOperationalForce);
        }

        private static bool CanRepurpose(Army pArmy)
        {
            if (!ArmyRtsControllerService.TryGetMission(pArmy,
                    out ArmyRtsMission mission) || mission == null)
                return true;
            return !mission.PlayerOrder &&
                   mission.Role != ArmyRtsRole.Defense;
        }

        private static int SafeUnitCount(Army pArmy)
        {
            try { return Math.Max(0, pArmy?.countUnits() ?? 0); }
            catch { return 0; }
        }

        private static War FindWar(long pWarId)
        {
            if (pWarId < 0L) return null;
            try { return World.world?.wars?.get(pWarId); }
            catch { return null; }
        }

        private static City FindCity(long pCityId)
        {
            if (pCityId < 0L) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }
    }
}
