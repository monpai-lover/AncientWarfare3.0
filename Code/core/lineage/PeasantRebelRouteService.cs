using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class PeasantRebelRouteService
    {
        private static readonly Dictionary<long, string> RuntimeByKingdom =
            new Dictionary<long, string>();
        private static readonly Dictionary<string, IPeasantRebelRouteBehavior>
            Behaviors = new Dictionary<string, IPeasantRebelRouteBehavior>
            {
                { PeasantRebelRouteIds.Founding,
                    new PeasantRebelFoundingRoute() },
                { PeasantRebelRouteIds.Bandit,
                    new PeasantRebelBanditRoute() }
            };

        [ThreadStatic]
        private static long? _enteringBanditKingdomId;

        internal static string GetRouteId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return "";

            long kingdomId = pKingdom.getID();
            if (_enteringBanditKingdomId == kingdomId)
                return PeasantRebelRouteIds.Bandit;
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_ROUTE,
                out string storedRoute, "");
            string route = PeasantRebelRouteRules.ResolvePersistedRoute(
                storedRoute, MandateRebelService.IsRebelKingdom(pKingdom));
            if (route.Length == 0)
            {
                RuntimeByKingdom.Remove(kingdomId);
                return "";
            }

            bool authority = PeasantRebelRouteRules.CanMutateAuthority(
                AW3MultiplayerReplicaScope.IsReplicaSession) &&
                !AW3MultiplayerReplicaScope.IsApplying;
            if (authority && route != storedRoute)
                pKingdom.data.set(LineageKeys.MANDATE_REBEL_ROUTE, route);
            RuntimeByKingdom[kingdomId] = route;
            return route;
        }

        internal static bool IsBandit(Kingdom pKingdom)
        {
            return GetRouteId(pKingdom) == PeasantRebelRouteIds.Bandit;
        }

        internal static bool IsBanditOrEntering(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            return _enteringBanditKingdomId == pKingdom.getID() ||
                   IsBandit(pKingdom);
        }

        internal static bool InitializeAndEnter(Kingdom pRebel,
            Kingdom pOrigin, City pFoundingCity, Actor pFounder)
        {
            if (pRebel?.data == null || pOrigin?.data == null ||
                pFoundingCity?.data == null || pFounder?.data == null)
                return false;
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return false;

            int year = Date.getCurrentYear();
            long seed = pRebel.getID() ^ (pFounder.getID() << 1) ^
                        ((long)year << 32);
            string root = pFounder.generateName(MetaType.Kingdom, seed);
            if (string.IsNullOrWhiteSpace(root)) root = pRebel.name ?? "";
            root = root.Trim();

            pRebel.data.set(LineageKeys.MANDATE_REBEL_NAME_ROOT, root);
            pRebel.data.set(LineageKeys.MANDATE_REBEL_FOUNDING_CITY_ID,
                pFoundingCity.getID());
            pRebel.data.set(LineageKeys.MANDATE_REBEL_ROUTE_CREATED_YEAR,
                year);
            pRebel.data.set(LineageKeys.MANDATE_REBEL_ROUTE_LAST_YEAR,
                int.MinValue);
            pRebel.data.set(LineageKeys.MANDATE_REBEL_ORIGIN_CITY_COUNT,
                SafeCityCount(pOrigin));
            pRebel.data.set(LineageKeys.MANDATE_REBEL_ORIGIN_STRENGTH,
                RealmStrength(pOrigin));
            pRebel.data.set(LineageKeys.MANDATE_REBEL_ORIGIN_CAPITAL_ID,
                pOrigin.capital?.getID() ?? -1L);
            pRebel.data.set(LineageKeys.MANDATE_REBEL_ORIGIN_RULER_ID,
                pOrigin.king?.getID() ?? -1L);

            int leaderFactor = ComputeLeaderFactor(pFounder);
            int cityFactor = ComputeCityFactor(pFoundingCity, pOrigin);
            int originFactor = PeasantRebelRouteRules.OriginStrengthFactor(
                RealmStrength(pOrigin), RealmStrength(pRebel));
            int turmoil = Math.Min(10,
                Math.Max(0, CountActiveWars(pOrigin) - 1) * 5 +
                (pOrigin.capital?.data == null || !pOrigin.hasKing()
                    ? 5 : 0));
            int foundingChance = PeasantRebelRouteRules.FoundingChance(
                leaderFactor, cityFactor, originFactor, turmoil);
            string routeId = PeasantRebelRouteRules.SelectRoute(
                Randy.randomInt(0, 100), foundingChance);
            IPeasantRebelRouteBehavior route = Behaviors[routeId];

            bool entered;
            if (route.Id == PeasantRebelRouteIds.Bandit)
            {
                using (new BanditEntryScope(pRebel.getID()))
                    entered = route.Enter(new PeasantRebelRouteEntryContext(
                        pRebel, pOrigin, pFoundingCity, pFounder));
            }
            else
            {
                RenameForRoute(pRebel, route.Id);
                entered = HasRouteName(pRebel, route.Id) &&
                    route.Enter(new PeasantRebelRouteEntryContext(pRebel,
                        pOrigin, pFoundingCity, pFounder));
            }

            if (!entered) return false;
            pRebel.data.set(LineageKeys.MANDATE_REBEL_ROUTE, route.Id);
            RuntimeByKingdom[pRebel.getID()] = route.Id;
            return true;
        }

        internal static void EnterFoundingFallback(Kingdom pRebel,
            Kingdom pOrigin, City pFoundingCity)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return;
            if (pRebel?.data == null || pOrigin?.data == null ||
                pFoundingCity?.data == null) return;
            pRebel.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                out string root, pRebel.name ?? "");
            IPeasantRebelRouteBehavior route =
                Behaviors[PeasantRebelRouteIds.Founding];
            pRebel.data.set(LineageKeys.MANDATE_REBEL_ROUTE, route.Id);
            RuntimeByKingdom[pRebel.getID()] = route.Id;
            RenameForRoute(pRebel, route.Id);
            MandateRebelService.EnterFoundingRoute(pRebel, pOrigin,
                pFoundingCity);
        }

        internal static bool TryApplyRouteName(Kingdom pKingdom,
            string pName)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return false;
            if (pKingdom?.data == null || string.IsNullOrWhiteSpace(pName))
                return false;
            try
            {
                pKingdom.setName(pName, pTrack: false);
                KingdomRenameProjectionService.Refresh(pKingdom);
                return string.Equals(pKingdom.name, pName,
                    StringComparison.Ordinal);
            }
            catch (Exception e)
            {
                ModClass.LogWarning("Peasant rebel route rename failed: " +
                                    e.Message);
                return false;
            }
        }

        internal static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return;
            string routeId = GetRouteId(pKingdom);
            if (!Behaviors.TryGetValue(routeId,
                    out IPeasantRebelRouteBehavior behavior)) return;
            behavior.OnKingdomYear(pKingdom);
        }

        internal static bool ConvertBanditToFounding(Kingdom pKingdom,
            Kingdom pOrigin)
        {
            if (!IsBandit(pKingdom) ||
                !PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return false;
            Behaviors[PeasantRebelRouteIds.Bandit].Exit(pKingdom);
            IPeasantRebelRouteBehavior route =
                Behaviors[PeasantRebelRouteIds.Founding];
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_ROUTE, route.Id);
            RuntimeByKingdom[pKingdom.getID()] = route.Id;
            RenameForRoute(pKingdom, route.Id);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
            KingdomRenameProjectionService.Refresh(pKingdom);
            PeasantRebelFoundingRoute.RecordTransition(pKingdom, pOrigin);
            if (pOrigin?.data != null && !pOrigin.isRekt())
                MandateRebelService.StartExistingRebelWar(pOrigin,
                    pKingdom);
            return true;
        }

        internal static void RenameForRoute(Kingdom pKingdom,
            string pRoute)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                out string root, "");
            string name = PeasantRebelRouteRules.ComposeName(root, pRoute);
            if (string.IsNullOrWhiteSpace(name)) return;
            TryApplyRouteName(pKingdom, name);
        }

        internal static bool HasRouteName(Kingdom pKingdom, string pRoute)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                out string root, "");
            return string.Equals(pKingdom.name,
                PeasantRebelRouteRules.ComposeName(root, pRoute),
                StringComparison.Ordinal);
        }

        internal static void OnWarStarted(War pWar)
        {
            if (!PeasantRebelRouteRules.CanMutateAuthority(
                    AW3MultiplayerReplicaScope.IsReplicaSession) ||
                AW3MultiplayerReplicaScope.IsApplying) return;
            if (pWar?.data == null) return;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (!IsOriginSuppressionPair(attacker, defender)) return;
            HistoryWriter.RecordKingdom(defender,
                KingdomEvent.MANDATE_REBELLION,
                HistoryText.Kingdom(attacker) +
                HistoryLocalizationRules.H(
                    "aw_hist_bandit_suppression_started") +
                HistoryText.Kingdom(defender),
                HistoryTarget.Kingdom(attacker));
        }

        internal static bool CanAcquireCity(Kingdom pRecipient, City pCity)
        {
            return PeasantRebelBanditTerritoryService.CanAcquire(
                pRecipient, pCity, IsBanditOrEntering(pRecipient));
        }

        internal static bool CanStartWar(Kingdom pAttacker,
            Kingdom pDefender, out bool pBypassTruce, out string pReason)
        {
            pBypassTruce = false;
            pReason = "";
            bool attackerBandit = IsBanditOrEntering(pAttacker);
            bool defenderBandit = IsBanditOrEntering(pDefender);
            bool attackerIsOrigin = defenderBandit &&
                ReadOriginKingdomId(pDefender) ==
                (pAttacker?.id ?? -1L);
            if (!PeasantRebelRouteRules.CanDeclareWar(attackerBandit,
                    defenderBandit, attackerIsOrigin))
            {
                pReason = attackerBandit
                    ? "bandit_cannot_declare_war"
                    : "only_origin_can_suppress_bandit";
                return false;
            }
            pBypassTruce = PeasantRebelRouteRules.ShouldBypassTruce(
                defenderBandit, attackerIsOrigin);
            return true;
        }

        internal static bool IsOriginSuppressionPair(Kingdom pAttacker,
            Kingdom pDefender)
        {
            return IsBanditOrEntering(pDefender) &&
                   ReadOriginKingdomId(pDefender) ==
                   (pAttacker?.id ?? -1L);
        }

        internal static void ClearRuntime()
        {
            RuntimeByKingdom.Clear();
            _enteringBanditKingdomId = null;
        }

        internal static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.kingdoms == null) return;
            bool authority = PeasantRebelRouteRules.CanMutateAuthority(
                AW3MultiplayerReplicaScope.IsReplicaSession) &&
                !AW3MultiplayerReplicaScope.IsApplying;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                string stored = ReadRouteRaw(kingdom);
                string resolved =
                    PeasantRebelRouteRules.ResolvePersistedRoute(stored,
                        MandateRebelService.IsRebelKingdom(kingdom));
                if (authority && stored.Length == 0 &&
                    resolved == PeasantRebelRouteIds.Founding)
                    kingdom.data.set(LineageKeys.MANDATE_REBEL_ROUTE,
                        resolved);
                if (resolved.Length == 0) continue;
                RuntimeByKingdom[kingdom.getID()] = resolved;
                RulerAppellationService.RefreshLivingProjection(kingdom);
            }
        }

        internal static void RemoveRuntime(Kingdom pKingdom)
        {
            if (pKingdom == null) return;
            RuntimeByKingdom.Remove(pKingdom.getID());
            RulerAppellationService.RemoveKingdom(pKingdom.getID());
        }

        internal static void OnKingdomDestroying(Kingdom pKingdom,
            bool pAuthoritative)
        {
            if (pKingdom?.data == null) return;
            bool bandit = string.Equals(ReadRouteRaw(pKingdom),
                PeasantRebelRouteIds.Bandit, StringComparison.Ordinal);
            if (pAuthoritative && bandit)
                PeasantRebelBanditRoute.RecordDestruction(pKingdom);
            if (pAuthoritative)
            {
                try
                {
                    foreach (Actor unit in pKingdom.getUnits())
                    {
                        if (unit?.data == null) continue;
                        unit.data.set(LineageKeys.MANDATE_REBEL_LEADER,
                            false);
                        if (unit.hasTrait("rebel"))
                            unit.removeTrait("rebel");
                    }
                }
                catch (Exception e)
                {
                    ModClass.LogWarning(
                        "Peasant rebel extinction cleanup failed: " +
                        e.Message);
                }
            }
            RemoveRuntime(pKingdom);
        }

        internal static int ComputeLeaderFactor(Actor pActor)
        {
            return PeasantRebelRouteRules.LeaderFactor(
                SafeStat(pActor, "warfare"),
                SafeStat(pActor, "stewardship"),
                SafeStat(pActor, "diplomacy"),
                SafeHasTrait(pActor, "ambitious"),
                SafeHasTrait(pActor, "peaceful") ||
                SafeHasTrait(pActor, "pacifist"));
        }

        internal static int ComputeCityFactor(City pCity, Kingdom pOrigin)
        {
            return PeasantRebelRouteRules.CityFactor(SafePopulation(pCity),
                MedianOriginCityPopulation(pOrigin));
        }

        private static int SafeStat(Actor pActor, string pStat)
        {
            try { return (int)Math.Round(pActor?.stats[pStat] ?? 0f); }
            catch { return 0; }
        }

        private static bool SafeHasTrait(Actor pActor, string pTrait)
        {
            try { return pActor?.hasTrait(pTrait) == true; }
            catch { return false; }
        }

        private static int SafePopulation(City pCity)
        {
            try { return Math.Max(0, pCity?.getPopulationPeople() ?? 0); }
            catch { return 0; }
        }

        private static int MedianOriginCityPopulation(Kingdom pOrigin)
        {
            var populations = new List<int>();
            try
            {
                foreach (City city in pOrigin.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    populations.Add(SafePopulation(city));
                }
            }
            catch { }
            if (populations.Count == 0) return 0;
            populations.Sort();
            int middle = populations.Count / 2;
            return populations.Count % 2 == 1
                ? populations[middle]
                : (populations[middle - 1] + populations[middle]) / 2;
        }

        internal static int RealmStrength(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return 0;
            int population = 0;
            int cities = 0;
            try
            {
                foreach (City city in pKingdom.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    cities++;
                    population += SafePopulation(city);
                }
            }
            catch { }

            int warriors = 0;
            try
            {
                foreach (Actor unit in pKingdom.getUnits())
                    if (unit?.data != null && !unit.isRekt() &&
                        unit.isWarrior()) warriors++;
            }
            catch { }
            return population + warriors * 5 + cities * 50;
        }

        internal static int CountActiveWars(Kingdom pKingdom)
        {
            int count = 0;
            if (pKingdom?.data == null) return count;
            try
            {
                foreach (War war in pKingdom.getWars())
                    if (war?.data != null && !war.hasEnded()) count++;
            }
            catch { }
            return count;
        }

        internal static int SafeCityCount(Kingdom pKingdom)
        {
            try { return pKingdom?.countCities() ?? 0; }
            catch { return 0; }
        }

        private static long ReadOriginKingdomId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return -1L;
            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_ORIGIN_KINGDOM_ID,
                out long originId, -1L);
            return originId;
        }

        private static string ReadRouteRaw(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return "";
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_ROUTE,
                out string route, "");
            return (route ?? "").Trim();
        }

        private sealed class BanditEntryScope : IDisposable
        {
            private readonly long? _previous;

            internal BanditEntryScope(long pKingdomId)
            {
                _previous = _enteringBanditKingdomId;
                _enteringBanditKingdomId = pKingdomId;
            }

            public void Dispose()
            {
                _enteringBanditKingdomId = _previous;
            }
        }
    }
}
