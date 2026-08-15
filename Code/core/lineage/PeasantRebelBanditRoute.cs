using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

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
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return false;
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

            PeasantRebelRouteService.RenameForRoute(pContext.Rebel, Id);
            if (!PeasantRebelRouteService.HasRouteName(
                    pContext.Rebel, Id)) return false;
            PeasantRebelBanditWallService.CaptureAndBuild(pContext.Rebel);
            HistoryWriter.RecordKingdom(pContext.Rebel,
                KingdomEvent.MANDATE_REBELLION,
                HistoryText.Kingdom(pContext.Rebel) +
                HistoryLocalizationRules.H(
                    "aw_hist_rebel_route_bandit"),
                HistoryTarget.Kingdom(pContext.Origin));
            return true;
        }

        public void OnKingdomYear(Kingdom pKingdom)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return;
            if (!TryResolveFoundingCity(pKingdom,
                    out City foundingCity))
            {
                PeasantRebelRouteService.RemoveRuntime(pKingdom);
                return;
            }
            if (SafeCityCount(pKingdom) > 1)
            {
                ModClass.LogWarning("Bandit realm " + pKingdom.id +
                    " owns more than its founding city; acquisition remains locked.");
                return;
            }
            MandateRebelService.RunBanditRouteYear(pKingdom);
            if (TryConvertToFounding(pKingdom, foundingCity))
            {
                MandateRebelService.RunFoundingRouteYear(pKingdom);
                return;
            }
            PeasantRebelBanditWallService.RepairYear(pKingdom,
                IsOriginSuppressionActive(pKingdom));
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

        private static bool IsOriginSuppressionActive(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                out long originId, -1L);
            if (originId < 0) return false;
            try
            {
                foreach (War war in pKingdom.getWars())
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    Kingdom attacker = war.getMainAttacker();
                    Kingdom defender = war.getMainDefender();
                    if (attacker == pKingdom && defender?.id == originId ||
                        defender == pKingdom && attacker?.id == originId)
                        return true;
                }
            }
            catch { }
            return false;
        }

        internal static void RecordDestruction(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !IsOriginSuppressionActive(pKingdom)) return;
            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                out long originId, -1L);
            Kingdom origin = null;
            try { origin = World.world?.kingdoms?.get(originId); }
            catch { }
            HistoryWriter.RecordKingdom(pKingdom,
                KingdomEvent.MANDATE_REBELLION,
                HistoryText.Kingdom(pKingdom) +
                HistoryLocalizationRules.H("aw_hist_bandit_destroyed"),
                HistoryTarget.Kingdom(origin?.data != null
                    ? origin
                    : pKingdom));
        }

        private static bool TryConvertToFounding(Kingdom pKingdom,
            City pFoundingCity)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying ||
                pKingdom?.data == null ||
                !PeasantRebelRouteService.IsBandit(pKingdom)) return false;
            int currentYear = Date.getCurrentYear();
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_ROUTE_LAST_YEAR,
                out int lastYear, int.MinValue);
            if (lastYear == currentYear) return false;
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_ROUTE_LAST_YEAR,
                currentYear);

            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                out long originId, -1L);
            Kingdom origin = null;
            try { origin = World.world?.kingdoms?.get(originId); }
            catch { }
            if (origin?.data == null || origin.isRekt() ||
                !origin.isCiv() ||
                PeasantRebelRouteService.SafeCityCount(origin) == 0)
                return PeasantRebelRouteService.ConvertBanditToFounding(
                    pKingdom, origin);

            pKingdom.data.get(LineageKeys.MANDATE_REBEL_ORIGIN_CITY_COUNT,
                out int originalCityCount, 0);
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_ORIGIN_STRENGTH,
                out int originalStrength, 0);
            int currentCityCount =
                PeasantRebelRouteService.SafeCityCount(origin);
            int currentStrength =
                PeasantRebelRouteService.RealmStrength(origin);
            bool weak = originalCityCount > 0 &&
                        currentCityCount * 2 <= originalCityCount ||
                        originalStrength > 0 &&
                        currentStrength * 2 <= originalStrength;
            bool quarter = originalCityCount > 0 &&
                           currentCityCount * 4 <= originalCityCount ||
                           originalStrength > 0 &&
                           currentStrength * 4 <= originalStrength;

            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_ORIGIN_CAPITAL_ID,
                out long originalCapitalId, -1L);
            City originalCapital = null;
            try
            {
                originalCapital = World.world?.cities?.get(
                    originalCapitalId);
            }
            catch { }
            bool capitalLost = originalCapital?.kingdom != origin;
            int hostileWars =
                PeasantRebelRouteService.CountActiveWars(origin);
            bool turmoil = hostileWars >= 2 || capitalLost ||
                           !origin.hasKing();

            pKingdom.data.get(LineageKeys.MANDATE_REBEL_ROUTE_CREATED_YEAR,
                out int createdYear, currentYear);
            int cityFactor = PeasantRebelRouteService.ComputeCityFactor(
                pFoundingCity, origin);
            int leaderFactor =
                PeasantRebelRouteService.ComputeLeaderFactor(pKingdom.king);
            int age = currentYear - createdYear;
            if (!PeasantRebelRouteRules.CanEvaluateWeakOriginTransition(
                    age, weak, turmoil, cityFactor, leaderFactor))
                return false;

            int chance = PeasantRebelRouteRules.TransitionChance(quarter,
                hostileWars, capitalLost, cityFactor, leaderFactor);
            if (Randy.randomInt(0, 100) >= chance) return false;
            return PeasantRebelRouteService.ConvertBanditToFounding(
                pKingdom, origin);
        }

        private static bool TryResolveFoundingCity(Kingdom pKingdom,
            out City pCity)
        {
            pCity = null;
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_FOUNDING_CITY_ID,
                out long cityId, -1L);
            try { pCity = World.world?.cities?.get(cityId); }
            catch { }
            return pCity?.data != null && !pCity.isRekt() &&
                   pCity.kingdom == pKingdom;
        }
    }
}
