using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class MassUprisingClusterService
    {
        internal const string PhaseClusterUprising = "cluster_uprising";
        internal const string PhaseCivilWar = "civil_war";
        internal const string PhaseUnification = "unification";
        internal const string PhaseFailed = "failed";

        private static readonly HashSet<long> ActiveRebels =
            new HashSet<long>();
        private static int _runtimeYear = int.MinValue;

        internal static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!CanMutate() || !IsValidOrigin(pKingdom)) return;
            int year = Date.getCurrentYear();
            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_ACTIVE,
                out bool active, false);
            if (!active) return;
            pKingdom.data.get(LineageKeys.MASS_UPRISING_CLUSTER_LAST_YEAR,
                out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.MASS_UPRISING_CLUSTER_LAST_YEAR,
                year);

            List<MassUprisingCluster> clusters = BuildClusters(pKingdom,
                out Dictionary<long, int> loyaltyByCity);
            if (clusters.Count == 0) return;
            pKingdom.data.get(LineageKeys.MASS_UPRISING_CLUSTER_KEYS,
                out string createdRaw, "");
            HashSet<string> created = ParseKeys(createdRaw);
            pKingdom.data.get(LineageKeys.MASS_UPRISING_CLUSTER_CURSOR,
                out int cursor, 0);
            cursor = Normalize(cursor, clusters.Count);
            int budget = Math.Min(
                BanditGreatUprisingRules.ConversionBudgetPerYear,
                clusters.Count);
            for (int offset = 0; offset < budget; offset++)
            {
                MassUprisingCluster cluster =
                    clusters[(cursor + offset) % clusters.Count];
                string key = MassUprisingClusterRules.ClusterKey(
                    cluster.CultureId, cluster);
                if (created.Contains(key)) continue;
                if (!TryCreateCluster(pKingdom, cluster, loyaltyByCity,
                        key)) continue;
                created.Add(key);
                break;
            }
            pKingdom.data.set(LineageKeys.MASS_UPRISING_CLUSTER_KEYS,
                string.Join("|", created.OrderBy(value => value)));
            pKingdom.data.set(LineageKeys.MASS_UPRISING_CLUSTER_CURSOR,
                MassUprisingClusterRules.AdvanceCursor(cursor, budget,
                    clusters.Count));
        }

        internal static void ClearRuntime()
        {
            ActiveRebels.Clear();
            _runtimeYear = int.MinValue;
        }

        internal static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.kingdoms == null) return;
            _runtimeYear = Date.getCurrentYear();
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (HasClusterMetadata(kingdom))
                    ActiveRebels.Add(kingdom.getID());
            }
        }

        internal static bool IsClusterRebel(Kingdom pKingdom)
        {
            return HasClusterMetadata(pKingdom);
        }

        internal static bool CanAcquireClusterTarget(Kingdom pRebel,
            City pCity)
        {
            if (!TryReadCluster(pRebel, out string phase,
                    out HashSet<long> targets, out _)) return true;
            if (phase != PhaseClusterUprising) return true;
            return pCity?.data != null && targets.Contains(pCity.getID());
        }

        internal static bool TryReadCluster(Kingdom pRebel,
            out string pPhase, out HashSet<long> pTargets,
            out long pOriginId)
        {
            pPhase = "";
            pTargets = new HashSet<long>();
            pOriginId = -1L;
            if (pRebel?.data == null) return false;
            pRebel.data.get(LineageKeys.MASS_UPRISING_CLUSTER_PHASE,
                out pPhase, "");
            pRebel.data.get(LineageKeys.MASS_UPRISING_CLUSTER_ORIGIN_ID,
                out pOriginId, -1L);
            pRebel.data.get(LineageKeys.MASS_UPRISING_CLUSTER_TARGET_IDS,
                out string targetRaw, "");
            pTargets = ParseIds(targetRaw);
            return !string.IsNullOrEmpty(pPhase) && pOriginId > 0L;
        }

        private static bool TryCreateCluster(Kingdom pOrigin,
            MassUprisingCluster pCluster, Dictionary<long, int> pLoyalty,
            string pKey)
        {
            long seedId = pCluster.CityIds
                .Where(id => pLoyalty.ContainsKey(id) &&
                    MassUprisingClusterRules.IsCore(pLoyalty[id]))
                .OrderBy(id => pLoyalty[id]).ThenBy(id => id)
                .FirstOrDefault();
            City seed = ResolveCity(seedId);
            if (seed?.data == null || seed.kingdom != pOrigin) return false;
            if (!PeasantRebelBanditStrongholdService.TryCreateDirect(seed,
                    out Kingdom rebel, out _, out _)) return false;
            if (rebel?.data == null) return false;
            try
            {
                PeasantRebelRouteService.ConvertBanditToFounding(
                    rebel, pOrigin);
            }
            catch { }
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_ORIGIN_ID,
                pOrigin.getID());
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_KEY, pKey);
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_CULTURE_ID,
                pCluster.CultureId);
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_CORE_IDS,
                string.Join(",", pCluster.CityIds.Where(id =>
                    pLoyalty.ContainsKey(id) &&
                    MassUprisingClusterRules.IsCore(pLoyalty[id]))));
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_TARGET_IDS,
                string.Join(",", pCluster.CityIds));
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_PHASE,
                PhaseClusterUprising);
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_COMPLETED_YEAR,
                int.MinValue);
            ActiveRebels.Add(rebel.getID());
            return true;
        }

        private static List<MassUprisingCluster> BuildClusters(
            Kingdom pOrigin, out Dictionary<long, int> pLoyalty)
        {
            pLoyalty = new Dictionary<long, int>();
            var facts = new List<MassUprisingCityFact>();
            City capital = pOrigin?.capital;
            try
            {
                foreach (City city in pOrigin.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    int loyalty;
                    try { loyalty = city.getLoyalty(); }
                    catch { continue; }
                    pLoyalty[city.getID()] = loyalty;
                    bool protectedCity = city == capital ||
                        capital?.neighbours_cities?.Contains(city) == true;
                    long cultureId = city.culture?.id ?? -1L;
                    var neighbours = city.neighbours_cities == null
                        ? Enumerable.Empty<long>()
                        : city.neighbours_cities.Where(item => item?.data != null)
                            .Select(item => item.getID());
                    facts.Add(new MassUprisingCityFact(city.getID(),
                        cultureId, loyalty, protectedCity, neighbours));
                }
            }
            catch { }
            return MassUprisingClusterRules.BuildClusters(facts);
        }

        private static bool HasClusterMetadata(Kingdom pKingdom)
        {
            return TryReadCluster(pKingdom, out _, out _, out _);
        }

        private static HashSet<string> ParseKeys(string pRaw)
        {
            return new HashSet<string>((pRaw ?? "").Split('|')
                .Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal);
        }

        private static HashSet<long> ParseIds(string pRaw)
        {
            var ids = new HashSet<long>();
            foreach (string value in (pRaw ?? "").Split(','))
                if (long.TryParse(value, out long id) && id > 0L)
                    ids.Add(id);
            return ids;
        }

        private static int Normalize(int pValue, int pCount)
        {
            if (pCount <= 0) return 0;
            int value = pValue % pCount;
            return value < 0 ? value + pCount : value;
        }

        private static City ResolveCity(long pCityId)
        {
            if (pCityId <= 0L) return null;
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static bool IsValidOrigin(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.isCiv() && !pKingdom.isNeutral();
        }

        private static bool CanMutate()
        {
            return PeasantRebelRouteRules.CanMutateAuthority(
                       AW3MultiplayerReplicaScope.IsReplicaSession) &&
                   !AW3MultiplayerReplicaScope.IsApplying;
        }
    }
}
