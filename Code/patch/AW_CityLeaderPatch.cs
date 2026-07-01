using AncientWarfare3.core.lineage;
using ai;
using ai.behaviours;
using HarmonyLib;

namespace AncientWarfare3.patch
{
    [HarmonyPatch]
    internal static class AW_CityLeaderPatch
    {
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CityBehCheckLeader), "checkFindLeader")]
        public static bool CheckFindLeader_Prefix(City pCity)
        {
            if (pCity == null) return true;
            if (pCity.hasLeader() || pCity.isGettingCaptured()) return false;

            Kingdom kingdom = pCity.kingdom;
            long heirId = GetHeirId(kingdom);

            Actor actor = TryGetClanLeader(pCity, kingdom, heirId);
            if (actor != null)
            {
                if (actor.city != pCity)
                    actor.stopBeingWarrior();
                actor.joinCity(pCity);
                pCity.setLeader(actor, pNew: true);
                return false;
            }

            int bestScore = 0;
            foreach (Actor unit in pCity.units)
            {
                if (!IsDirectLeaderCandidate(unit, heirId)) continue;

                int dice = 1;
                if (unit.isFavorite()) dice += 2;
                int score = ActorTool.attributeDice(unit, dice);
                if (actor == null || score > bestScore)
                {
                    actor = unit;
                    bestScore = score;
                }
            }

            if (actor != null)
                pCity.setLeader(actor, pNew: true);
            return false;
        }

        private static Actor TryGetClanLeader(City pCity, Kingdom pKingdom, long pHeirId)
        {
            if (pCity == null || pKingdom?.data == null) return null;

            Clan royalClan = null;
            if (pKingdom.data.royal_clan_id.hasValue())
                royalClan = World.world?.clans?.get(pKingdom.data.royal_clan_id);

            using ListPool<Actor> royalCandidates = new ListPool<Actor>();
            using ListPool<Actor> otherCandidates = new ListPool<Actor>();
            foreach (City city in pKingdom.getCities())
            {
                foreach (Actor unit in city.getUnits())
                {
                    if (!IsClanLeaderCandidate(unit, pHeirId)) continue;
                    if (royalClan != null && unit.clan == royalClan)
                        royalCandidates.Add(unit);
                    else
                        otherCandidates.Add(unit);
                }
            }

            Actor royal = PickLeader(royalCandidates, pCity);
            if (royal != null) return royal;
            return PickLeader(otherCandidates, pCity);
        }

        private static Actor PickLeader(ListPool<Actor> pCandidates, City pCity)
        {
            if (pCandidates == null || pCandidates.Count == 0) return null;
            if (pCity.hasCulture())
                return ListSorters.getUnitSortedByAgeAndTraits(pCandidates, pCity.culture);
            pCandidates.Sort(ListSorters.sortUnitByAgeOldFirst);
            return pCandidates[0];
        }

        private static bool IsClanLeaderCandidate(Actor pUnit, long pHeirId)
        {
            if (pUnit?.data == null) return false;
            if (pUnit.data.id == pHeirId) return false;
            return pUnit.isUnitFitToRule() && !pUnit.isKing() && !pUnit.isCityLeader() && pUnit.hasClan();
        }

        private static bool IsDirectLeaderCandidate(Actor pUnit, long pHeirId)
        {
            if (pUnit?.data == null) return false;
            if (pUnit.data.id == pHeirId) return false;
            if (pUnit.isKing() || pUnit.isCityLeader()) return false;
            return pUnit.is_profession_citizen;
        }

        private static long GetHeirId(Kingdom pKingdom)
        {
            Actor heir = HeirService.GetHeir(pKingdom);
            return heir?.data?.id ?? -1L;
        }
    }
}
