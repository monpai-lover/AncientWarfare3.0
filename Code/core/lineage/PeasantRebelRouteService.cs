using System;
using System.Collections.Generic;

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
                    new PeasantRebelFoundingRoute() }
            };

        [ThreadStatic]
        private static long? _enteringBanditKingdomId;

        internal static string GetRouteId(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return "";

            long kingdomId = pKingdom.getID();
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_ROUTE,
                out string storedRoute, "");
            string route = PeasantRebelRouteRules.ResolvePersistedRoute(
                storedRoute, MandateRebelService.IsRebelKingdom(pKingdom));
            if (route.Length == 0)
            {
                RuntimeByKingdom.Remove(kingdomId);
                return "";
            }

            if (route != storedRoute)
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
            return IsBandit(pKingdom) ||
                   _enteringBanditKingdomId == pKingdom.getID();
        }

        internal static bool InitializeAndEnter(Kingdom pRebel,
            Kingdom pOrigin, City pFoundingCity, Actor pFounder)
        {
            if (pRebel?.data == null || pOrigin?.data == null ||
                pFoundingCity?.data == null || pFounder?.data == null)
                return false;

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

            IPeasantRebelRouteBehavior route =
                Behaviors[PeasantRebelRouteIds.Founding];
            pRebel.data.set(LineageKeys.MANDATE_REBEL_ROUTE, route.Id);
            pRebel.setName(route.ComposeStateName(root), pTrack: false);
            RuntimeByKingdom[pRebel.getID()] = route.Id;

            return route.Enter(new PeasantRebelRouteEntryContext(pRebel,
                pOrigin, pFoundingCity, pFounder));
        }

        internal static void OnKingdomYear(Kingdom pKingdom)
        {
            string routeId = GetRouteId(pKingdom);
            if (!Behaviors.TryGetValue(routeId,
                    out IPeasantRebelRouteBehavior behavior)) return;
            behavior.OnKingdomYear(pKingdom);
        }

        internal static bool ConvertBanditToFounding(Kingdom pKingdom,
            Kingdom pOrigin)
        {
            if (!IsBandit(pKingdom)) return false;
            IPeasantRebelRouteBehavior route =
                Behaviors[PeasantRebelRouteIds.Founding];
            pKingdom.data.get(LineageKeys.MANDATE_REBEL_NAME_ROOT,
                out string root, pKingdom.name ?? "");
            pKingdom.data.set(LineageKeys.MANDATE_REBEL_ROUTE, route.Id);
            pKingdom.setName(route.ComposeStateName(root), pTrack: false);
            RuntimeByKingdom[pKingdom.getID()] = route.Id;
            return true;
        }

        internal static bool CanAcquireCity(Kingdom pRecipient, City pCity)
        {
            string routeId = GetRouteId(pRecipient);
            if (!Behaviors.TryGetValue(routeId,
                    out IPeasantRebelRouteBehavior behavior)) return true;
            return behavior.CanAcquireCity(pRecipient, pCity);
        }

        internal static bool CanStartWar(Kingdom pAttacker,
            Kingdom pDefender, out bool pBypassTruce, out string pReason)
        {
            pBypassTruce = false;
            pReason = "";

            string attackerRoute = GetRouteId(pAttacker);
            if (Behaviors.TryGetValue(attackerRoute,
                    out IPeasantRebelRouteBehavior attackerBehavior) &&
                !attackerBehavior.CanDeclareWar(pAttacker))
            {
                pReason = "bandit_cannot_declare_war";
                return false;
            }

            string defenderRoute = GetRouteId(pDefender);
            if (!Behaviors.TryGetValue(defenderRoute,
                    out IPeasantRebelRouteBehavior defenderBehavior) ||
                defenderBehavior.CanReceiveDirectWar(pDefender, pAttacker))
                return true;

            pReason = "only_origin_can_suppress_bandit";
            return false;
        }

        internal static void ClearRuntime()
        {
            RuntimeByKingdom.Clear();
            _enteringBanditKingdomId = null;
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
