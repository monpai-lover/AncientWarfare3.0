using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct WarNoForceSideFacts
    {
        public WarNoForceSideFacts(int activeOperationalSoldiers,
            int reserveSoldiers, int recruitableSoldiers,
            int operationalArmyCount)
        {
            ActiveOperationalSoldiers = Math.Max(0,
                activeOperationalSoldiers);
            ReserveSoldiers = Math.Max(0, reserveSoldiers);
            RecruitableSoldiers = Math.Max(0, recruitableSoldiers);
            OperationalArmyCount = Math.Max(0, operationalArmyCount);
        }

        public int ActiveOperationalSoldiers { get; }
        public int ReserveSoldiers { get; }
        public int RecruitableSoldiers { get; }
        public int OperationalArmyCount { get; }
        public int TotalPotential
        {
            get
            {
                long total = (long)ActiveOperationalSoldiers +
                             ReserveSoldiers + RecruitableSoldiers;
                return total >= int.MaxValue ? int.MaxValue : (int)total;
            }
        }

        public bool HasForce => WarNoForceSurrenderRules.IsNoForce(
            ActiveOperationalSoldiers, ReserveSoldiers,
            RecruitableSoldiers, OperationalArmyCount) == false;
    }

    /// <summary>
    /// Builds a side-level force picture from every participant.  The old
    /// settlement code examined only the main kingdom's field armies, which
    /// made allied reserve and mobilizable population invisible.
    /// </summary>
    internal static class WarNoForceMilitaryService
    {
        public static WarNoForceSideFacts BuildSideFacts(War pWar,
            bool pAttackers)
        {
            if (pWar?.data == null || pWar.hasEnded()) return default;
            var seen = new HashSet<long>();
            int active = 0;
            int reserve = 0;
            int recruitable = 0;
            int armyCount = 0;
            IEnumerable<Kingdom> side;
            try { side = pAttackers ? pWar.getAttackers() :
                pWar.getDefenders(); }
            catch { return default; }
            try
            {
                foreach (Kingdom kingdom in side)
                {
                    if (kingdom?.data == null || kingdom.isRekt() ||
                        !seen.Add(kingdom.id)) continue;
                    AddKingdom(kingdom, ref active, ref reserve,
                        ref recruitable, ref armyCount);
                }
            }
            catch { }
            return new WarNoForceSideFacts(active, reserve, recruitable,
                armyCount);
        }

        public static bool TryGetOutcome(War pWar,
            out WarNoForceSideFacts pAttackers,
            out WarNoForceSideFacts pDefenders)
        {
            pAttackers = BuildSideFacts(pWar, pAttackers: true);
            pDefenders = BuildSideFacts(pWar, pAttackers: false);
            return pWar?.data != null && !pWar.hasEnded();
        }

        private static void AddKingdom(Kingdom pKingdom, ref int pActive,
            ref int pReserve, ref int pRecruitable, ref int pArmyCount)
        {
            ArmyStrategicIdCursor cursor = ArmyFieldIndexService.
                CreateSnapshotCursor(pKingdom);
            while (!cursor.IsComplete)
            {
                IReadOnlyList<long> ids = cursor.Take(
                    ArmyEstablishmentRules.MaximumFieldArmies);
                if (ids == null || ids.Count == 0) break;
                for (int index = 0; index < ids.Count; index++)
                {
                    Army army = ArmyFieldIndexService.ResolveIndexedArmy(
                        ids[index], pKingdom.id);
                    if (!IsOperationalArmy(army)) continue;
                    pArmyCount = AddSaturating(pArmyCount, 1);
                    int units = 0;
                    try { units = Math.Max(0, army.countUnits()); }
                    catch { }
                    pActive = AddSaturating(pActive, units);
                }
            }
            pReserve = AddSaturating(pReserve,
                CityReservePoolService.CountAvailable(pKingdom));
            pRecruitable = AddSaturating(pRecruitable,
                WartimeMilitaryPotentialService.
                    CountForceRecruitablePopulation(pKingdom));
        }

        private static bool IsOperationalArmy(Army pArmy)
        {
            if (pArmy?.data == null) return false;
            try
            {
                return pArmy.isAlive() && pArmy.units != null &&
                       pArmy.units.Count >=
                           ArmyLogisticsRules.MinimumOperationalForce;
            }
            catch { return false; }
        }

        private static int AddSaturating(int pLeft, int pRight)
        {
            long total = (long)Math.Max(0, pLeft) + Math.Max(0, pRight);
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }
    }
}
