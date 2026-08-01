using System;
using System.Collections.Generic;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal static class ZhuluAgeDirectorService
    {
        private sealed class RealmSnapshot
        {
            internal Kingdom Root;
            internal readonly List<Kingdom> Members = new List<Kingdom>();
            internal long Score;
        }

        private const int MaximumLoggedFailures = 64;
        private static readonly HashSet<string> LoggedFailures =
            new HashSet<string>();
        private static int _lastProcessedMonthKey = int.MinValue;
        private static bool _runtimeAgeActive;
        private static bool _stateLoaded;

        internal static void ProcessAuthorityCycle()
        {
            if (World.world?.kingdoms == null ||
                World.world.map_stats == null ||
                !ZhuluAgeStatePersistence.IsReady) return;

            bool active = World.world.map_stats.world_age_id ==
                          ZhuluAgeRules.AgeId;
            EnsureStateLoaded(active);
            if (!HandleTransition(active) || !active) return;

            int monthKey = KingdomDecisionMonthlyRules.ToMonthKey(
                Date.getCurrentYear(), Date.getCurrentMonth());
            if (monthKey == _lastProcessedMonthKey) return;
            _lastProcessedMonthKey = monthKey;
            ProcessMonth();
        }

        internal static void RebuildRuntime()
        {
            _lastProcessedMonthKey = int.MinValue;
            LoggedFailures.Clear();
            bool active = World.world?.map_stats?.world_age_id ==
                          ZhuluAgeRules.AgeId;
            bool persisted = ZhuluAgeStatePersistence.IsReady &&
                             ZhuluAgeStatePersistence.ReadEntryActive();
            if (!active && persisted)
            {
                ZhuluAgeStatePersistence.WriteEntryActive(false);
                persisted = false;
            }
            _runtimeAgeActive = active && persisted;
            _stateLoaded = true;
        }

        internal static void Reset()
        {
            _lastProcessedMonthKey = int.MinValue;
            _runtimeAgeActive = false;
            _stateLoaded = false;
            LoggedFailures.Clear();
        }

        private static void EnsureStateLoaded(bool active)
        {
            if (_stateLoaded) return;
            bool persisted = ZhuluAgeStatePersistence.ReadEntryActive();
            if (!active && persisted)
            {
                ZhuluAgeStatePersistence.WriteEntryActive(false);
                persisted = false;
            }
            _runtimeAgeActive = active && persisted;
            _stateLoaded = true;
        }

        private static bool HandleTransition(bool active)
        {
            if (active == _runtimeAgeActive) return true;
            if (!active)
            {
                if (!ZhuluAgeStatePersistence.WriteEntryActive(false))
                    return false;
                _runtimeAgeActive = false;
                _lastProcessedMonthKey = int.MinValue;
                return true;
            }

            if (!ZhuluAgeStatePersistence.ReadEntryActive())
            {
                MandateService.ClearMandate("zhulu_age_entered");
                if (!ZhuluAgeStatePersistence.WriteEntryActive(true))
                    return false;
            }
            _runtimeAgeActive = true;
            _lastProcessedMonthKey = int.MinValue;
            return true;
        }

        private static void ProcessMonth()
        {
            ReleaseBlockingAlliances();
            List<RealmSnapshot> realms = BuildRealmSnapshots();
            if (realms.Count == 0) return;
            realms.Sort(CompareRealmRank);
            TryGrantMandate(realms);
        }

        private static void ReleaseBlockingAlliances()
        {
            if (World.world?.kingdoms == null) return;
            var kingdoms = new List<Kingdom>();
            try
            {
                foreach (Kingdom kingdom in World.world.kingdoms)
                    if (IsScoringRealm(kingdom)) kingdoms.Add(kingdom);
            }
            catch { return; }

            for (int index = 0; index < kingdoms.Count; index++)
            {
                Kingdom source = kingdoms[index];
                Alliance alliance = null;
                try { alliance = source.getAlliance(); }
                catch { }
                if (alliance?.data == null) continue;

                bool hasAllianceTarget = false;
                bool hasNonAllianceTarget = false;
                for (int targetIndex = 0;
                     targetIndex < kingdoms.Count; targetIndex++)
                {
                    Kingdom target = kingdoms[targetIndex];
                    if (!IsPotentialUnificationTarget(source, target,
                            out bool sameAlliance)) continue;
                    if (sameAlliance) hasAllianceTarget = true;
                    else hasNonAllianceTarget = true;
                    if (hasAllianceTarget && hasNonAllianceTarget) break;
                }

                if (!ZhuluAgeRules.ShouldLeaveAllianceForUnification(
                        hasNonAllianceTarget, hasAllianceTarget)) continue;
                try
                {
                    if (alliance.hasKingdom(source)) alliance.leave(source);
                }
                catch (Exception exception)
                {
                    LogFailure(source.id,
                        "leave_alliance:" + exception.Message);
                }
            }
        }

        private static bool IsPotentialUnificationTarget(Kingdom source,
            Kingdom target, out bool sameAlliance)
        {
            sameAlliance = false;
            if (!ZhuluWarService.IsValidRealm(source) ||
                !ZhuluWarService.IsValidRealm(target) || source == target)
                return false;
            try
            {
                Kingdom sourceRoot = VassalService.GetRootSuzerain(source);
                Kingdom targetRoot = VassalService.GetRootSuzerain(target);
                bool sameRoot = sourceRoot?.data != null &&
                                targetRoot?.data != null &&
                                sourceRoot == targetRoot;
                bool alreadyAtWar = World.world.wars?.getWar(source, target,
                    pOnlyMain: false) != null;
                bool blocked = DiplomacyProposalService.HasActiveWarBlocker(
                    source, target);
                sameAlliance = WarTerritoryService.AreInSameAlliance(source,
                    target);
                return ZhuluAgeRules.IsEligibleTarget(
                    new ZhuluAgeTargetFacts(target.id, valid: true,
                        isSelf: false, sameRoot: sameRoot,
                        alreadyAtWar: alreadyAtWar,
                        diplomaticBlocked: blocked,
                        sameAlliance: sameAlliance,
                        directlyAdjacent: false,
                        distanceSquared: 0L, score: 0L));
            }
            catch
            {
                sameAlliance = false;
                return false;
            }
        }

        private static List<RealmSnapshot> BuildRealmSnapshots()
        {
            var living = new List<Kingdom>();
            var byId = new Dictionary<long, Kingdom>();
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (!IsScoringRealm(kingdom)) continue;
                living.Add(kingdom);
                byId[kingdom.id] = kingdom;
            }

            var children = new Dictionary<long, List<Kingdom>>();
            var roots = new List<Kingdom>();
            for (int i = 0; i < living.Count; i++)
            {
                Kingdom kingdom = living[i];
                Kingdom suzerain = VassalService.GetSuzerain(kingdom);
                if (suzerain?.data == null ||
                    !byId.ContainsKey(suzerain.id))
                {
                    roots.Add(kingdom);
                    continue;
                }
                if (!children.TryGetValue(suzerain.id,
                        out List<Kingdom> direct))
                {
                    direct = new List<Kingdom>();
                    children[suzerain.id] = direct;
                }
                direct.Add(kingdom);
            }

            var result = new List<RealmSnapshot>(roots.Count);
            for (int i = 0; i < roots.Count; i++)
            {
                var snapshot = new RealmSnapshot { Root = roots[i] };
                snapshot.Score = ScoreTree(roots[i], children,
                    new HashSet<long>(), snapshot.Members);
                result.Add(snapshot);
            }
            return result;
        }

        private static long ScoreTree(Kingdom kingdom,
            Dictionary<long, List<Kingdom>> children,
            HashSet<long> visited, List<Kingdom> members)
        {
            if (!IsScoringRealm(kingdom) || !visited.Add(kingdom.id))
                return 0L;
            members.Add(kingdom);
            long score = DirectScore(kingdom);
            if (!children.TryGetValue(kingdom.id,
                    out List<Kingdom> direct)) return score;
            direct.Sort((left, right) => left.id.CompareTo(right.id));
            for (int i = 0; i < direct.Count; i++)
            {
                long childScore = ScoreTree(direct[i], children, visited,
                    members);
                score = ZhuluAgeRules.AddScores(score,
                    ZhuluAgeRules.VassalContribution(childScore));
            }
            return score;
        }

        private static long DirectScore(Kingdom kingdom)
        {
            int cities = 0;
            long population = 0L;
            try
            {
                foreach (City city in kingdom.getCities())
                {
                    if (city?.data == null || city.isRekt()) continue;
                    cities++;
                    population += Math.Max(0, city.getPopulationPeople());
                    if (population >= int.MaxValue)
                    {
                        population = int.MaxValue;
                        break;
                    }
                }
            }
            catch { }

            int zones = 0;
            try { zones = Math.Max(0, kingdom.countZones()); }
            catch { }
            int warriors = WartimeMilitaryPotentialService.
                CountPotentialWarriors(kingdom);
            return ZhuluAgeRules.DirectScore(cities, zones,
                (int)population, warriors);
        }

        private static void TryGrantMandate(List<RealmSnapshot> realms)
        {
            RealmSnapshot first = realms[0];
            long second = realms.Count > 1 ? realms[1].Score : 0L;
            if (!ZhuluAgeRules.HasMandateLead(first.Score, second,
                    realms.Count) || MandateService.IsMandateKingdom(
                    first.Root)) return;
            if (!MandateService.TryForceGrantMandateForZhuluAge(
                    first.Root, out string reason))
                LogFailure(first.Root.id, "mandate:" + reason);
        }

        private static bool IsScoringRealm(Kingdom kingdom)
        {
            if (!ZhuluWarService.IsValidRealm(kingdom)) return false;
            try { return kingdom.cities != null && kingdom.cities.Count > 0; }
            catch { return false; }
        }

        private static int CompareRealmRank(RealmSnapshot left,
            RealmSnapshot right)
        {
            int result = right.Score.CompareTo(left.Score);
            return result != 0
                ? result
                : left.Root.id.CompareTo(right.Root.id);
        }

        private static void LogFailure(long kingdomId, string detail)
        {
            if (LoggedFailures.Count >= MaximumLoggedFailures) return;
            string key = kingdomId + ":" + (detail ?? "unknown");
            if (!LoggedFailures.Add(key)) return;
            ModClass.LogWarning("Zhulu age director failed for realm " +
                                kingdomId + ": " + detail);
        }
    }
}
