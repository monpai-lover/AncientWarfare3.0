using System;
using System.Collections.Generic;
using System.Linq;
using AncientWarfare3.api.multiplayer;

namespace AncientWarfare3.core.lineage
{
    internal static class MassUprisingClusterService
    {
        internal const string PhaseClusterUprising = "cluster_uprising";
        internal const string PhaseClusterComplete = "cluster_complete";
        internal const string PhaseCivilWar = "civil_war";
        internal const string PhaseUnification = "unification";
        internal const string PhaseCompleted = "completed";
        internal const string PhaseFailed = "failed";

        private static readonly HashSet<long> ActiveRebels =
            new HashSet<long>();
        private static int _runtimeYear = int.MinValue;
        private const int MaxPersistedPlans = 128;
        private const int MaxPersistedIds = 256;

        private sealed class PlannedCluster
        {
            internal long CultureId;
            internal List<long> CityIds = new List<long>();
            internal List<long> CoreIds = new List<long>();

            internal string Key => MassUprisingClusterRules.ClusterKey(
                CultureId, new MassUprisingCluster(CultureId, CityIds,
                    CoreIds.Count > 0));
        }

        internal static void OnKingdomYear(Kingdom pKingdom)
        {
            if (!CanMutate() || !IsValidOrigin(pKingdom)) return;
            int year = Date.getCurrentYear();
            ProcessAuthorityCycle();
            pKingdom.data.get(
                LineageKeys.MANDATE_REBEL_GREAT_UPRISING_ACTIVE,
                out bool active, false);
            if (!active) return;
            pKingdom.data.get(LineageKeys.MASS_UPRISING_CLUSTER_LAST_YEAR,
                out int lastYear, int.MinValue);
            if (lastYear == year) return;
            pKingdom.data.set(LineageKeys.MASS_UPRISING_CLUSTER_LAST_YEAR,
                year);

            pKingdom.data.get(LineageKeys.MASS_UPRISING_CLUSTER_PLANS,
                out string plansRaw, "");
            List<PlannedCluster> plans = ParsePlans(plansRaw);
            if (plans.Count == 0)
            {
                List<MassUprisingCluster> discovered = BuildClusters(pKingdom,
                    out Dictionary<long, int> discoveredLoyalty);
                plans = discovered.Select(cluster => new PlannedCluster
                {
                    CultureId = cluster.CultureId,
                    CityIds = cluster.CityIds.ToList(),
                    CoreIds = cluster.CityIds.Where(id =>
                        discoveredLoyalty.TryGetValue(id, out int loyalty) &&
                        MassUprisingClusterRules.IsCore(loyalty)).ToList()
                }).ToList();
                if (plans.Count == 0) return;
                pKingdom.data.set(LineageKeys.MASS_UPRISING_CLUSTER_PLANS,
                    SerializePlans(plans));
            }
            pKingdom.data.get(LineageKeys.MASS_UPRISING_CLUSTER_KEYS,
                out string createdRaw, "");
            HashSet<string> created = ParseKeys(createdRaw);
            pKingdom.data.get(LineageKeys.MASS_UPRISING_CLUSTER_CURSOR,
                out int cursor, 0);
            cursor = Normalize(cursor, plans.Count);
            int budget = Math.Min(
                BanditGreatUprisingRules.ConversionBudgetPerYear,
                plans.Count);
            for (int offset = 0; offset < budget; offset++)
            {
                PlannedCluster plan = plans[(cursor + offset) % plans.Count];
                string key = plan.Key;
                if (created.Contains(key)) continue;
                if (!TryCreateCluster(pKingdom, plan,
                        key)) continue;
                created.Add(key);
                break;
            }
            pKingdom.data.set(LineageKeys.MASS_UPRISING_CLUSTER_KEYS,
                string.Join("|", created.OrderBy(value => value)));
            pKingdom.data.set(LineageKeys.MASS_UPRISING_CLUSTER_CURSOR,
                MassUprisingClusterRules.AdvanceCursor(cursor, budget,
                    plans.Count));
        }

        internal static void ProcessAuthorityCycle()
        {
            if (!CanMutate() || World.world?.kingdoms == null) return;
            int year = Date.getCurrentYear();
            if (_runtimeYear == year) return;
            _runtimeYear = year;
            var processed = new HashSet<long>();
            foreach (long rebelId in ActiveRebels.OrderBy(id => id).Take(4)
                         .ToList())
            {
                if (!processed.Add(rebelId)) continue;
                Kingdom rebel = ResolveKingdom(rebelId);
                if (rebel?.data == null || !TryReadCluster(rebel,
                        out string phase, out HashSet<long> targets,
                        out long originId))
                {
                    ActiveRebels.Remove(rebelId);
                    continue;
                }
                if (phase == PhaseFailed || phase == PhaseCompleted ||
                    rebel.isRekt())
                {
                    ActiveRebels.Remove(rebelId);
                    continue;
                }
                if (phase == PhaseClusterUprising && targets.Count > 0 &&
                    targets.Any(id => ResolveCity(id) == null))
                {
                    rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_PHASE,
                        PhaseFailed);
                    ActiveRebels.Remove(rebelId);
                    continue;
                }
                if (phase == PhaseClusterUprising && targets.Count > 0 &&
                    targets.All(id => ResolveCity(id)?.kingdom == rebel))
                {
                    rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_PHASE,
                        PhaseClusterComplete);
                    rebel.data.set(
                        LineageKeys.MASS_UPRISING_CLUSTER_COMPLETED_YEAR,
                        Date.getCurrentYear());
                    phase = PhaseClusterComplete;
                }
                ResolveOriginPhase(originId);
            }
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

        internal static bool CanStartClusterWar(Kingdom pAttacker,
            Kingdom pDefender, out bool pBypassTruce, out string pReason)
        {
            pBypassTruce = false;
            pReason = "";
            if (!TryReadCluster(pAttacker, out string attackerPhase,
                    out _, out long attackerOrigin)) return false;
            if (!TryReadCluster(pDefender, out string defenderPhase,
                    out _, out long defenderOrigin))
            {
                if ((attackerPhase == PhaseClusterUprising ||
                     attackerPhase == PhaseUnification) &&
                    pDefender?.getID() == attackerOrigin)
                {
                    pBypassTruce = true;
                    return true;
                }
                pReason = "mass_uprising_cluster_defender_not_cluster";
                return false;
            }
            if (attackerOrigin != defenderOrigin)
            {
                pReason = "mass_uprising_cluster_origin_mismatch";
                return false;
            }
            if (attackerPhase == PhaseClusterUprising &&
                ResolveKingdom(attackerOrigin) == pDefender)
            {
                pBypassTruce = true;
                return true;
            }
            if (!MassUprisingClusterRules.ShouldDeclareCivilWarPair(
                    ParsePhase(attackerPhase), ParsePhase(defenderPhase),
                    HasActiveWarBetween(pAttacker, pDefender)))
            {
                pReason = "mass_uprising_cluster_war_not_ready";
                return false;
            }
            pBypassTruce = true;
            return true;
        }

        private static void ResolveOriginPhase(long pOriginId)
        {
            Kingdom origin = ResolveKingdom(pOriginId);
            List<Kingdom> rebels = ActiveRebels.Select(ResolveKingdom)
                .Where(rebel => rebel?.data != null &&
                    TryReadCluster(rebel, out _, out _, out long id) &&
                    id == pOriginId && !rebel.isRekt()).OrderBy(
                        rebel => rebel.getID()).ToList();
            if (rebels.Count == 0) return;
            bool allComplete = rebels.All(rebel =>
                TryReadCluster(rebel, out string phase, out _, out _) &&
                (phase == PhaseClusterComplete || phase == PhaseCivilWar ||
                 phase == PhaseUnification));
            if (!allComplete) return;
            if (rebels.Count == 1)
            {
                Kingdom final = rebels[0];
                if (origin == null || origin.isRekt())
                {
                    final.data.set(LineageKeys.MASS_UPRISING_CLUSTER_PHASE,
                        PhaseCompleted);
                    ActiveRebels.Remove(final.getID());
                    return;
                }
                final.data.set(LineageKeys.MASS_UPRISING_CLUSTER_PHASE,
                    PhaseUnification);
                MandateRebelService.StartExistingRebelWar(origin, final);
                return;
            }
            foreach (Kingdom rebel in rebels)
                rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_PHASE,
                    PhaseCivilWar);
            for (int i = 0; i < rebels.Count; i++)
            for (int j = i + 1; j < rebels.Count; j++)
            {
                if (HasActiveWarBetween(rebels[i], rebels[j])) continue;
                try
                {
                    WarDecisionService.TryStartInternalSystemWar(rebels[i],
                        rebels[j], MandateService.WAR_TIANMING_REBEL,
                        "mass_uprising_civil_war");
                }
                catch (Exception error)
                {
                    ModClass.LogWarning("Mass uprising civil war failed: " +
                        error.Message);
                }
            }
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
            PlannedCluster pPlan, string pKey)
        {
            var loyalty = new Dictionary<long, int>();
            foreach (long cityId in pPlan.CoreIds)
            {
                City city = ResolveCity(cityId);
                if (city?.data == null || city.isRekt() ||
                    city.kingdom != pOrigin) continue;
                try { loyalty[cityId] = city.getLoyalty(); }
                catch { }
            }
            long seedId = pPlan.CoreIds
                .Where(loyalty.ContainsKey)
                .OrderBy(id => loyalty[id]).ThenBy(id => id)
                .FirstOrDefault();
            City seed = ResolveCity(seedId);
            if (seed?.data == null || seed.kingdom != pOrigin) return false;
            if (!PeasantRebelBanditStrongholdService.TryCreateDirect(seed,
                    out Kingdom rebel, out _, out _)) return false;
            if (rebel?.data == null) return false;
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_ORIGIN_ID,
                pOrigin.getID());
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_KEY, pKey);
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_CULTURE_ID,
                pPlan.CultureId);
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_CORE_IDS,
                string.Join(",", pPlan.CoreIds));
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_TARGET_IDS,
                string.Join(",", pPlan.CityIds));
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_PHASE,
                PhaseClusterUprising);
            rebel.data.set(LineageKeys.MASS_UPRISING_CLUSTER_COMPLETED_YEAR,
                int.MinValue);
            ActiveRebels.Add(rebel.getID());
            MandateRebelService.StartExistingRebelWar(pOrigin, rebel);
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
            if (!TryReadCluster(pKingdom, out string phase, out _, out _))
                return false;
            return phase != PhaseFailed && phase != PhaseCompleted &&
                   !string.IsNullOrEmpty(phase);
        }

        private static bool HasActiveWarBetween(Kingdom pFirst,
            Kingdom pSecond)
        {
            if (pFirst?.data == null || pSecond?.data == null ||
                pFirst == pSecond) return false;
            try
            {
                foreach (War war in pFirst.getWars())
                {
                    if (war?.data == null || war.hasEnded()) continue;
                    if ((war.isAttacker(pFirst) && war.isDefender(pSecond)) ||
                        (war.isDefender(pFirst) && war.isAttacker(pSecond)))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static MassUprisingPhase ParsePhase(string pPhase)
        {
            if (pPhase == PhaseClusterComplete)
                return MassUprisingPhase.ClusterComplete;
            if (pPhase == PhaseCivilWar)
                return MassUprisingPhase.CivilWar;
            if (pPhase == PhaseUnification)
                return MassUprisingPhase.Unification;
            if (pPhase == PhaseFailed)
                return MassUprisingPhase.Failed;
            if (pPhase == PhaseCompleted)
                return MassUprisingPhase.Completed;
            return MassUprisingPhase.ClusterUprising;
        }

        private static HashSet<string> ParseKeys(string pRaw)
        {
            return new HashSet<string>((pRaw ?? "").Split('|')
                .Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.Ordinal);
        }

        private static List<PlannedCluster> ParsePlans(string pRaw)
        {
            var result = new List<PlannedCluster>();
            foreach (string raw in (pRaw ?? "").Split('|').Take(MaxPersistedPlans))
            {
                string[] parts = raw.Split('~');
                if (parts.Length != 3 || !long.TryParse(parts[0],
                        out long cultureId) || cultureId <= 0L) continue;
                List<long> cities = ParseIds(parts[1]).OrderBy(id => id).ToList();
                List<long> cores = ParseIds(parts[2]).OrderBy(id => id).ToList();
                if (cities.Count == 0 || cores.Count == 0) continue;
                result.Add(new PlannedCluster
                {
                    CultureId = cultureId,
                    CityIds = cities,
                    CoreIds = cores
                });
            }
            return result.OrderBy(plan => plan.Key).ToList();
        }

        private static string SerializePlans(IEnumerable<PlannedCluster> pPlans)
        {
            return string.Join("|", (pPlans ?? Enumerable.Empty<PlannedCluster>())
                .OrderBy(plan => plan.Key)
                .Select(plan => plan.CultureId + "~" +
                    string.Join(",", plan.CityIds.OrderBy(id => id)) + "~" +
                    string.Join(",", plan.CoreIds.OrderBy(id => id))));
        }

        private static HashSet<long> ParseIds(string pRaw)
        {
            var ids = new HashSet<long>();
            foreach (string value in (pRaw ?? "").Split(',').Take(MaxPersistedIds))
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

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            if (pKingdomId <= 0L) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
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
