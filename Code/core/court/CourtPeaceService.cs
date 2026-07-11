using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.core.db;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CourtPeaceService
    {
        private static readonly Dictionary<long, int> LastCheckYearByWar = new Dictionary<long, int>();
        private static readonly Random Rng = new Random();

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || World.world?.wars == null) return;
            int year = Date.getCurrentYear();
            CleanupOldChecks(year);

            List<War> wars;
            try { wars = pKingdom.getWars().ToList(); }
            catch { return; }

            foreach (War war in wars)
            {
                if (!CanCheck(war, pKingdom, year)) continue;
                LastCheckYearByWar[war.data.id] = year;
                int warYears = GetWarYears(war);
                if (warYears < 10 || IsProtectedWar(war)) continue;

                Kingdom attacker = war.main_attacker;
                Kingdom defender = war.main_defender;
                if (attacker?.data == null || defender?.data == null ||
                    attacker.isRekt() || defender.isRekt()) continue;

                CourtSnapshot attackerCourt = CourtService.GetSnapshot(attacker);
                CourtSnapshot defenderCourt = CourtService.GetSnapshot(defender);
                float attackerPower = Math.Max(1f,
                    VassalService.GetPowerScore(attacker, pIncludeVassals: true));
                float defenderPower = Math.Max(1f,
                    VassalService.GetPowerScore(defender, pIncludeVassals: true));
                float chance = CourtDirectionRules.WhitePeaceChance(
                    warYears,
                    attackerPower / defenderPower,
                    (attackerCourt.peace + defenderCourt.peace) * 0.5f,
                    (attackerCourt.aggression + defenderCourt.aggression) * 0.5f);
                if (chance <= 0f || Rng.NextDouble() >= chance) continue;

                try { World.world.wars.endWar(war, WarWinner.Peace); }
                catch (Exception e) { ModClass.LogWarning("CourtPeaceService: " + e.Message); }
            }
        }

        private static bool CanCheck(War pWar, Kingdom pKingdom, int pYear)
        {
            if (pWar?.data == null || pWar.data.winner != WarWinner.Nobody) return false;
            if (pWar.main_attacker != pKingdom) return false;
            return !LastCheckYearByWar.TryGetValue(pWar.data.id, out int lastYear) || lastYear != pYear;
        }

        private static bool IsProtectedWar(War pWar)
        {
            if (LineageArchiveManager.Instance == null ||
                !LineageArchiveManager.Instance.InitializeSuccessful) return true;
            string type = "";
            try { type = pWar.getAsset()?.id ?? pWar.data.war_type ?? ""; }
            catch { type = pWar.data.war_type ?? ""; }
            if (type == MandateService.WAR_TIANMING ||
                type == MandateService.WAR_TIANMING_REBEL ||
                type == "independence_war" ||
                type == "fief_independence_war" ||
                type == "general_rebellion_war") return true;

            Kingdom attacker = pWar.main_attacker;
            Kingdom defender = pWar.main_defender;
            if (attacker?.data != null && defender?.data != null &&
                MandateService.GetCurrentMandateKingdom() == attacker &&
                WarTerritoryService.CanUseMandateConquest(attacker, defender)) return true;

            return WarTerritoryService.HasOpenGoalType(pWar.data.id,
                WarTerritoryService.GOAL_TAKE_MANDATE,
                WarTerritoryService.GOAL_MANDATE_CONQUEST,
                WarTerritoryService.GOAL_INDEPENDENCE);
        }

        private static int GetWarYears(War pWar)
        {
            try
            {
                double elapsed = World.world.getCurWorldTime() - pWar.data.created_time;
                return elapsed <= 0d ? 0 : Math.Max(0, Date.getYear0(elapsed));
            }
            catch { return 0; }
        }

        private static void CleanupOldChecks(int pYear)
        {
            if (LastCheckYearByWar.Count < 512) return;
            var stale = new List<long>();
            foreach (KeyValuePair<long, int> pair in LastCheckYearByWar)
                if (pair.Value < pYear - 2) stale.Add(pair.Key);
            foreach (long warId in stale) LastCheckYearByWar.Remove(warId);
        }
    }
}
