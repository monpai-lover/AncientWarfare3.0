using System;
using System.Collections.Generic;
using System.Threading;
using AncientWarfare3.core.asyncwork;

namespace AncientWarfare3.core.lineage
{
    internal readonly struct ArmyRtsAsyncPlanningDiagnostics
    {
        internal ArmyRtsAsyncPlanningDiagnostics(long pSnapshots,
            long pScheduled, long pCompleted, long pApplied,
            long pRejectedStale)
        {
            Snapshots = pSnapshots;
            Scheduled = pScheduled;
            Completed = pCompleted;
            Applied = pApplied;
            RejectedStale = pRejectedStale;
        }

        internal long Snapshots { get; }
        internal long Scheduled { get; }
        internal long Completed { get; }
        internal long Applied { get; }
        internal long RejectedStale { get; }
    }

    internal static class ArmyRtsAsyncPlanningService
    {
        private readonly struct PrefetchKey : IEquatable<PrefetchKey>
        {
            internal PrefetchKey(long pKingdomId, long pWarId)
            {
                KingdomId = pKingdomId;
                WarId = pWarId;
            }

            internal long KingdomId { get; }
            internal long WarId { get; }

            public bool Equals(PrefetchKey pOther)
            {
                return KingdomId == pOther.KingdomId && WarId == pOther.WarId;
            }

            public override bool Equals(object pObject)
            {
                return pObject is PrefetchKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((int)KingdomId * 397) ^ (int)WarId;
                }
            }
        }

        private sealed class PrefetchedRank
        {
            internal PrefetchedRank(ArmyRtsAsyncPlanStamp pStamp,
                IReadOnlyList<ArmyRtsAsyncFrontCandidate> pRanked)
            {
                Stamp = pStamp;
                CityIds = CopyCityIds(pRanked);
            }

            internal ArmyRtsAsyncPlanStamp Stamp { get; }
            internal long[] CityIds { get; }
        }

        private static readonly Dictionary<PrefetchKey, PrefetchedRank>
            PrefetchByKey = new Dictionary<PrefetchKey, PrefetchedRank>();
        private static long _snapshots;
        private static long _scheduled;
        private static long _completed;
        private static long _applied;
        private static long _rejectedStale;

        internal static void Schedule(ArmyRtsAsyncPlanStamp pStamp,
            IReadOnlyList<FrontTargetFacts> pTargets)
        {
            ArmyRtsAsyncFrontCandidate[] candidates = CaptureCandidates(
                pTargets);
            Interlocked.Increment(ref _snapshots);
            if (candidates.Length == 0 || !AWAsyncRuntime.AiEnabled) return;

            var runtimeStamp = new AWAsyncStamp(pStamp.WorldGeneration,
                pStamp.DirectorGeneration, pStamp.CityFactsRevision);
            string key = "rts_front:" + pStamp.KingdomId + ":" +
                         pStamp.WarId;
            if (!AWAsyncRuntime.CanSchedule(key, AWAsyncLane.Ai,
                    runtimeStamp)) return;
            var request = new AWAsyncWorkRequest(key, AWAsyncLane.Ai,
                runtimeStamp, token => Plan(token, pStamp, candidates),
                Commit);
            if (AWAsyncRuntime.TrySchedule(request))
                Interlocked.Increment(ref _scheduled);
        }

        internal static IReadOnlyList<FrontTargetFacts> OrderTargets(
            ArmyRtsAsyncPlanStamp pCurrent,
            IReadOnlyList<FrontTargetFacts> pTargets)
        {
            if (pTargets == null || pTargets.Count < 2) return pTargets;
            var key = new PrefetchKey(pCurrent.KingdomId, pCurrent.WarId);
            if (!PrefetchByKey.TryGetValue(key, out PrefetchedRank prefetch))
                return pTargets;
            if (!ArmyRtsAsyncPlanningRules.Accept(prefetch.Stamp,
                    pCurrent.WorldGeneration, pCurrent.KingdomId,
                    pCurrent.DirectorGeneration, pCurrent.WarId,
                    pCurrent.CityFactsRevision))
            {
                PrefetchByKey.Remove(key);
                Interlocked.Increment(ref _rejectedStale);
                return pTargets;
            }

            var byCityId = new Dictionary<long, FrontTargetFacts>(
                pTargets.Count);
            for (int index = 0; index < pTargets.Count; index++)
            {
                FrontTargetFacts target = pTargets[index];
                if (target?.CityId >= 0L)
                    byCityId[target.CityId] = target;
            }
            var ordered = new List<FrontTargetFacts>(pTargets.Count);
            for (int index = 0; index < prefetch.CityIds.Length; index++)
            {
                long cityId = prefetch.CityIds[index];
                if (!byCityId.TryGetValue(cityId,
                        out FrontTargetFacts target)) continue;
                ordered.Add(target);
                byCityId.Remove(cityId);
            }
            for (int index = 0; index < pTargets.Count; index++)
            {
                FrontTargetFacts target = pTargets[index];
                if (target?.CityId >= 0L && byCityId.Remove(target.CityId))
                    ordered.Add(target);
            }
            if (ordered.Count != pTargets.Count) return pTargets;
            Interlocked.Increment(ref _applied);
            return ordered;
        }

        internal static void InvalidateKingdom(long pKingdomId)
        {
            if (pKingdomId < 0L) return;
            RemoveWhere(key => key.KingdomId == pKingdomId);
        }

        internal static void InvalidateWar(long pWarId)
        {
            if (pWarId < 0L) return;
            RemoveWhere(key => key.WarId == pWarId);
        }

        internal static void InvalidateCity(long pCityId)
        {
            if (pCityId < 0L) return;
            var stale = new List<PrefetchKey>();
            foreach (KeyValuePair<PrefetchKey, PrefetchedRank> item in
                     PrefetchByKey)
            {
                long[] cityIds = item.Value?.CityIds;
                if (cityIds == null) continue;
                for (int index = 0; index < cityIds.Length; index++)
                    if (cityIds[index] == pCityId)
                    {
                        stale.Add(item.Key);
                        break;
                    }
            }
            for (int index = 0; index < stale.Count; index++)
                PrefetchByKey.Remove(stale[index]);
        }

        internal static void ClearRuntime()
        {
            PrefetchByKey.Clear();
            Interlocked.Exchange(ref _snapshots, 0L);
            Interlocked.Exchange(ref _scheduled, 0L);
            Interlocked.Exchange(ref _completed, 0L);
            Interlocked.Exchange(ref _applied, 0L);
            Interlocked.Exchange(ref _rejectedStale, 0L);
        }

        internal static ArmyRtsAsyncPlanningDiagnostics SnapshotDiagnostics()
        {
            return new ArmyRtsAsyncPlanningDiagnostics(
                Interlocked.Read(ref _snapshots),
                Interlocked.Read(ref _scheduled),
                Interlocked.Read(ref _completed),
                Interlocked.Read(ref _applied),
                Interlocked.Read(ref _rejectedStale));
        }

        private static object Plan(CancellationToken pToken,
            ArmyRtsAsyncPlanStamp pStamp,
            ArmyRtsAsyncFrontCandidate[] pCandidates)
        {
            pToken.ThrowIfCancellationRequested();
            return new PrefetchedRank(pStamp,
                ArmyRtsAsyncPlanningRules.Rank(pCandidates));
        }

        private static void Commit(object pResult)
        {
            if (!(pResult is PrefetchedRank result)) return;
            PrefetchByKey[new PrefetchKey(result.Stamp.KingdomId,
                result.Stamp.WarId)] = result;
            Interlocked.Increment(ref _completed);
        }

        private static ArmyRtsAsyncFrontCandidate[] CaptureCandidates(
            IReadOnlyList<FrontTargetFacts> pTargets)
        {
            var candidates = new List<ArmyRtsAsyncFrontCandidate>(
                pTargets?.Count ?? 0);
            if (pTargets != null)
                for (int index = 0; index < pTargets.Count; index++)
                {
                    FrontTargetFacts target = pTargets[index];
                    if (target?.CityId < 0L || !target.OperationallyReachable)
                        continue;
                    candidates.Add(new ArmyRtsAsyncFrontCandidate(
                        target.CityId, Score(target)));
                }
            return candidates.ToArray();
        }

        private static int Score(FrontTargetFacts pTarget)
        {
            int tier = pTarget.FrozenFriendly ? 4
                : pTarget.ConnectedCorridor ? 3
                : pTarget.LandReachable ? 2
                : pTarget.TransportReachable ? 1 : 0;
            long score = tier * 1_000_000L;
            if (pTarget.FormalWarGoal) score += 20_000L;
            if (pTarget.EnemyCapital) score += 10_000L;
            if (pTarget.ExposedSecondary) score += 1_000L;
            score -= Math.Min(900_000, pTarget.DistanceSquared);
            return score > int.MaxValue ? int.MaxValue :
                score < int.MinValue ? int.MinValue : (int)score;
        }

        private static long[] CopyCityIds(
            IReadOnlyList<ArmyRtsAsyncFrontCandidate> pRanked)
        {
            var cityIds = new long[pRanked?.Count ?? 0];
            for (int index = 0; index < cityIds.Length; index++)
                cityIds[index] = pRanked[index].CityId;
            return cityIds;
        }

        private static void RemoveWhere(Func<PrefetchKey, bool> pPredicate)
        {
            var stale = new List<PrefetchKey>();
            foreach (PrefetchKey key in PrefetchByKey.Keys)
                if (pPredicate(key)) stale.Add(key);
            for (int index = 0; index < stale.Count; index++)
                PrefetchByKey.Remove(stale[index]);
        }
    }
}
