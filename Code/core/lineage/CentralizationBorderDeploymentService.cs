using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class CentralizationBorderDeploymentService
    {
        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null || pWar.hasEnded()) return;
            Kingdom defender = pWar.getMainDefender();
            if (defender?.data == null || defender.isRekt() ||
                !XiaizationService.CanUsePolicySystem(defender)) return;
            CentralizationSnapshot snapshot = CentralizationService.ReadSnapshot(defender);
            if (snapshot.effective_level < 2) return;

            var owners = new List<Kingdom> { defender };
            var ownerIds = new HashSet<long> { defender.id };
            if (snapshot.effects.IncludesVassalBorderArmies && World.world?.kingdoms != null)
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                {
                    if (kingdom?.data == null || kingdom.isRekt() || kingdom == defender ||
                        VassalService.GetRootSuzerain(kingdom) != defender) continue;
                    bool joinedDefenders = false;
                    try { joinedDefenders = pWar.isDefender(kingdom); } catch { }
                    if (!joinedDefenders || !ownerIds.Add(kingdom.id)) continue;
                    owners.Add(kingdom);
                }
            }

            var targets = new List<City>();
            foreach (Kingdom owner in owners)
            {
                foreach (City city in owner.getCities())
                    if (city?.data != null && !city.isRekt() &&
                        HasExternalLandBorderForRoot(city, defender))
                        targets.Add(city);
            }
            targets.Sort((left, right) => BorderPriority(right).CompareTo(BorderPriority(left)));
            BorderArmyReanchorService.ReanchorExistingArmies(targets, ownerIds);
        }

        internal static bool HasExternalLandBorderForRoot(City pCity,
            Kingdom pDefenderRoot)
        {
            try
            {
                foreach (Kingdom neighbour in pCity.neighbours_kingdoms)
                {
                    if (neighbour?.data == null || neighbour.isNeutral() ||
                        neighbour == pCity.kingdom) continue;
                    if (neighbour == pDefenderRoot ||
                        VassalService.GetRootSuzerain(neighbour) == pDefenderRoot) continue;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static int BorderPriority(City pCity)
        {
            try { return pCity.getPopulationPeople() + pCity.countZones() * 5; }
            catch { return 0; }
        }
    }
}
