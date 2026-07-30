using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class WarMilitaryFactsService
    {
        private static readonly WarMilitaryFactsCache Cache =
            new WarMilitaryFactsCache();

        public static WarMilitaryFacts Build(Kingdom pKingdom, War pWar,
            int pSignedWarScore)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pWar?.data == null || pWar.hasEnded())
                return WarMilitaryFactsRules.Resolve(0,
                    capitalThreatened: false, averageSupply: 0,
                    averageOrganization: 0,
                    signedWarScore: pSignedWarScore);

            long worldDay = CurrentWorldDay();
            if (Cache.TryGet(pWar.data.id, pKingdom.id, worldDay,
                    out WarMilitaryFacts cached, pSignedWarScore))
                return cached;

            ArmyStrategicIdCursor cursor = ArmyFieldIndexService.
                CreateSnapshotCursor(pKingdom);
            IReadOnlyList<long> armyIds = cursor.Take(
                ArmyEstablishmentRules.MaximumFieldArmies);
            int available = 0;
            int supplyTotal = 0;
            int organizationTotal = 0;
            for (int i = 0; i < armyIds.Count; i++)
            {
                Army army = ArmyFieldIndexService.ResolveIndexedArmy(
                    armyIds[i], pKingdom.id);
                if (!IsAvailableFieldArmy(army)) continue;
                ArmyOperationalStateView operational =
                    ArmyLogisticsService.GetOperationalState(army);
                if (operational.Supply <=
                        ArmyLogisticsRules.CriticalSupply ||
                    operational.Organization <
                        ArmyLogisticsRules.RetreatOrganization) continue;
                available++;
                supplyTotal += operational.Supply;
                organizationTotal += operational.Organization;
            }
            int averageSupply = available <= 0
                ? 0
                : supplyTotal / available;
            int averageOrganization = available <= 0
                ? 0
                : organizationTotal / available;
            bool capitalThreatened = IsCapitalThreatened(pKingdom, pWar);
            WarMilitaryFacts result = WarMilitaryFactsRules.Resolve(available,
                capitalThreatened, averageSupply, averageOrganization,
                pSignedWarScore);
            Cache.Store(pWar.data.id, pKingdom.id, worldDay, result,
                pSignedWarScore);
            return result;
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data != null) Cache.RemoveWar(pWar.data.id);
        }

        public static void RebuildRuntime()
        {
            Cache.Clear();
        }

        public static void ClearRuntime()
        {
            Cache.Clear();
        }

        private static bool IsAvailableFieldArmy(Army pArmy)
        {
            if (pArmy?.data == null) return false;
            try
            {
                return pArmy.isAlive() &&
                       pArmy.units != null &&
                       pArmy.units.Count >=
                           ArmyLogisticsRules.MinimumOperationalForce;
            }
            catch { return false; }
        }

        private static bool IsCapitalThreatened(Kingdom pKingdom,
            War pWar)
        {
            City capital = pKingdom?.capital;
            if (capital?.data == null || capital.isRekt()) return true;
            try
            {
                if (capital.isGettingCaptured()) return true;
                return WarScoreService.IsCityFrozenControlledByEnemySide(
                    capital, pKingdom) && pWar.hasKingdom(pKingdom);
            }
            catch { return false; }
        }

        private static long CurrentWorldDay()
        {
            try
            {
                double time = Math.Max(0d,
                    World.world?.getCurWorldTime() ?? 0d);
                double days = Math.Floor(time * 6d);
                return days >= long.MaxValue ? long.MaxValue : (long)days;
            }
            catch { return 0L; }
        }
    }
}
