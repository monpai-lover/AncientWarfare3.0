using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal sealed class PeasantRebelBanditRoute :
        IPeasantRebelRouteBehavior
    {
        public string Id => PeasantRebelRouteIds.Bandit;
        public string RulerTitleKey => "aw_bandit_ruler_title";
        public string HeirTitleKey => "aw_bandit_heir_title";

        public bool Enter(PeasantRebelRouteEntryContext pContext)
        {
            if (pContext.Rebel?.data == null ||
                pContext.Origin?.data == null ||
                pContext.FoundingCity?.data == null) return false;

            foreach (City city in new List<City>(
                         pContext.Rebel.getCities()))
            {
                if (city == pContext.FoundingCity) continue;
                try
                {
                    city.joinAnotherKingdom(pContext.Origin,
                        pCaptured: false, pRebellion: true);
                }
                catch (Exception e)
                {
                    ModClass.LogWarning(
                        "Bandit route city retention failed: " + e.Message);
                }
            }

            using (var wars = new ListPool<War>())
            {
                foreach (War war in pContext.Rebel.getWars())
                    wars.Add(war);
                for (int i = 0; i < wars.Count; i++)
                {
                    War war = wars[i];
                    if (war?.data == null || war.hasEnded()) continue;
                    World.world.wars.endWar(war, WarWinner.Peace);
                }
            }

            if (pContext.FoundingCity.kingdom != pContext.Rebel ||
                SafeCityCount(pContext.Rebel) != 1 ||
                HasActiveWar(pContext.Rebel)) return false;

            pContext.Rebel.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                out string root, pContext.Rebel.name ?? "");
            return PeasantRebelRouteService.TryApplyRouteName(
                pContext.Rebel, ComposeStateName(root));
        }

        public void OnKingdomYear(Kingdom pKingdom)
        {
            MandateRebelService.RunBanditRouteYear(pKingdom);
        }

        public bool CanDeclareWar(Kingdom pKingdom)
        {
            return false;
        }

        public bool CanReceiveDirectWar(Kingdom pKingdom, Kingdom pAttacker)
        {
            if (pKingdom?.data == null || pAttacker?.data == null)
                return false;
            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                out long originId, -1L);
            return pAttacker.getID() == originId;
        }

        public bool CanAcquireCity(Kingdom pKingdom, City pCity)
        {
            return PeasantRebelRouteRules.CanAcquireCity(true,
                SafeCityCount(pKingdom), pCity?.kingdom == pKingdom);
        }

        public string ComposeStateName(string pRoot)
        {
            return PeasantRebelRouteRules.ComposeName(pRoot, Id);
        }

        public void Exit(Kingdom pKingdom)
        {
        }

        public void OnKingdomDestroying(Kingdom pKingdom)
        {
        }

        private static bool HasActiveWar(Kingdom pKingdom)
        {
            try
            {
                foreach (War war in pKingdom.getWars())
                    if (war?.data != null && !war.hasEnded()) return true;
            }
            catch { return true; }
            return false;
        }

        private static int SafeCityCount(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
        }
    }
}
