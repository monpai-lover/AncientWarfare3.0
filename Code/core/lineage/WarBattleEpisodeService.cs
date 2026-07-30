using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class WarBattleEpisodeService
    {
        internal const int MaximumActiveEpisodes = 64;
        internal const int MaximumFinalizationsPerFrame = 4;
        internal const int MaximumSharedWarsPerKill = 8;
        private const int BucketSize = 40;
        private const int JoinDistanceSquared = BucketSize * BucketSize;
        private const double QuietSeconds = 2.2d;

        private static readonly Dictionary<long, BattleEpisode> Episodes =
            new Dictionary<long, BattleEpisode>();
        private static readonly Dictionary<BucketKey, List<long>> Buckets =
            new Dictionary<BucketKey, List<long>>();
        private static readonly LinkedList<long> ExpiryOrder =
            new LinkedList<long>();
        private static long _nextEpisodeId = 1L;

        public static void RecordMilitaryKill(Actor pKiller, Actor pVictim,
            Kingdom pVictimKingdom)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pKiller?.data == null || pVictim?.data == null ||
                pKiller.kingdom?.data == null ||
                pVictimKingdom?.data == null ||
                pKiller.kingdom == pVictimKingdom ||
                pVictim.current_tile?.data == null ||
                !IsMilitary(pKiller) || !IsMilitary(pVictim)) return;

            Kingdom pKillerKingdom = pKiller.kingdom;
            int acceptedWars = 0;
            try
            {
                foreach (War war in pKillerKingdom.getWars())
                {
                    if (acceptedWars >= MaximumSharedWarsPerKill) break;
                    if (!TryResolveVictimSide(war, pKillerKingdom,
                            pVictimKingdom, out WarScoreSide victimSide))
                        continue;
                    acceptedWars++;
                    RecordDeath(war, pVictim, victimSide, Now());
                }
            }
            catch
            {
                // A transient war-list mutation must not break the kill path.
            }
        }

        public static void ProcessFrame()
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession)
            {
                ClearRuntime();
                return;
            }
            FinalizeExpired(Now(), MaximumFinalizationsPerFrame);
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            long warId = pWar.data.id;
            LinkedListNode<long> node = ExpiryOrder.First;
            while (node != null)
            {
                LinkedListNode<long> next = node.Next;
                if (Episodes.TryGetValue(node.Value,
                        out BattleEpisode episode) && episode.WarId == warId)
                    RemoveEpisode(episode);
                node = next;
            }
        }

        public static void ClearRuntime()
        {
            Episodes.Clear();
            Buckets.Clear();
            ExpiryOrder.Clear();
            _nextEpisodeId = 1L;
        }

        private static void RecordDeath(War pWar, Actor pVictim,
            WarScoreSide pVictimSide, double pNow)
        {
            WorldTile tile = pVictim.current_tile;
            BattleEpisode episode = FindNearest(pWar.data.id, tile.x, tile.y);
            if (episode == null)
            {
                if (Episodes.Count >= MaximumActiveEpisodes)
                    FinalizeExpired(pNow, MaximumFinalizationsPerFrame);
                if (Episodes.Count >= MaximumActiveEpisodes) return;
                episode = CreateEpisode(pWar, pVictim.data.id, tile.x,
                    tile.y, pNow);
            }

            if (pVictimSide == WarScoreSide.Attackers)
                episode.AttackerDeaths++;
            else
                episode.DefenderDeaths++;
            UpdateCentroid(episode, tile.x, tile.y);
            episode.LastEventTime = pNow;
            ExpiryOrder.Remove(episode.ExpiryNode);
            ExpiryOrder.AddLast(episode.ExpiryNode);
        }

        private static BattleEpisode FindNearest(long pWarId, int pX,
            int pY)
        {
            int bucketX = FloorBucket(pX);
            int bucketY = FloorBucket(pY);
            BattleEpisode nearest = null;
            long nearestDistance = long.MaxValue;
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    var key = new BucketKey(pWarId, bucketX + dx,
                        bucketY + dy);
                    if (!Buckets.TryGetValue(key, out List<long> ids))
                        continue;
                    for (int i = 0; i < ids.Count; i++)
                    {
                        if (!Episodes.TryGetValue(ids[i],
                                out BattleEpisode candidate)) continue;
                        long deltaX = (long)Math.Round(candidate.CenterX) - pX;
                        long deltaY = (long)Math.Round(candidate.CenterY) - pY;
                        long distance = deltaX * deltaX + deltaY * deltaY;
                        if (distance > JoinDistanceSquared ||
                            distance >= nearestDistance) continue;
                        nearestDistance = distance;
                        nearest = candidate;
                    }
                }
            return nearest;
        }

        private static BattleEpisode CreateEpisode(War pWar,
            long pFirstVictimActorId, int pX, int pY, double pNow)
        {
            long id = NextEpisodeId();
            var episode = new BattleEpisode
            {
                Id = id,
                War = pWar,
                WarId = pWar.data.id,
                FirstVictimActorId = pFirstVictimActorId,
                CenterX = pX,
                CenterY = pY,
                SampleCount = 0,
                LastEventTime = pNow,
                BucketX = FloorBucket(pX),
                BucketY = FloorBucket(pY)
            };
            episode.ExpiryNode = ExpiryOrder.AddLast(id);
            Episodes[id] = episode;
            AddToBucket(episode);
            return episode;
        }

        private static void UpdateCentroid(BattleEpisode pEpisode, int pX,
            int pY)
        {
            int samples = pEpisode.SampleCount;
            pEpisode.CenterX = (pEpisode.CenterX * samples + pX) /
                               (samples + 1d);
            pEpisode.CenterY = (pEpisode.CenterY * samples + pY) /
                               (samples + 1d);
            pEpisode.SampleCount = samples == int.MaxValue
                ? int.MaxValue
                : samples + 1;
            int bucketX = FloorBucket((int)Math.Round(pEpisode.CenterX));
            int bucketY = FloorBucket((int)Math.Round(pEpisode.CenterY));
            if (bucketX == pEpisode.BucketX &&
                bucketY == pEpisode.BucketY) return;
            RemoveFromBucket(pEpisode);
            pEpisode.BucketX = bucketX;
            pEpisode.BucketY = bucketY;
            AddToBucket(pEpisode);
        }

        private static void FinalizeExpired(double pNow, int pBudget)
        {
            int finalized = 0;
            while (finalized < pBudget && ExpiryOrder.First != null)
            {
                long id = ExpiryOrder.First.Value;
                if (!Episodes.TryGetValue(id, out BattleEpisode episode))
                {
                    ExpiryOrder.RemoveFirst();
                    continue;
                }
                if (pNow - episode.LastEventTime < QuietSeconds) break;
                RemoveEpisode(episode);
                finalized++;
                FinalizeEpisode(episode);
            }
        }

        private static void FinalizeEpisode(BattleEpisode pEpisode)
        {
            War war = pEpisode.War;
            if (war?.data == null || war.hasEnded()) return;
            if (!WarBattleEpisodeRules.TryResolve(pEpisode.AttackerDeaths,
                    pEpisode.DefenderDeaths, out WarScoreSide winner,
                    out int intensity)) return;
            WarScoreService.RecordBattleVictoryRelief(war,
                pEpisode.FirstVictimActorId.ToString(), winner, intensity);
        }

        private static void RemoveEpisode(BattleEpisode pEpisode)
        {
            RemoveFromBucket(pEpisode);
            Episodes.Remove(pEpisode.Id);
            if (pEpisode.ExpiryNode?.List != null)
                ExpiryOrder.Remove(pEpisode.ExpiryNode);
        }

        private static void AddToBucket(BattleEpisode pEpisode)
        {
            var key = new BucketKey(pEpisode.WarId, pEpisode.BucketX,
                pEpisode.BucketY);
            if (!Buckets.TryGetValue(key, out List<long> ids))
            {
                ids = new List<long>();
                Buckets[key] = ids;
            }
            ids.Add(pEpisode.Id);
        }

        private static void RemoveFromBucket(BattleEpisode pEpisode)
        {
            var key = new BucketKey(pEpisode.WarId, pEpisode.BucketX,
                pEpisode.BucketY);
            if (!Buckets.TryGetValue(key, out List<long> ids)) return;
            ids.Remove(pEpisode.Id);
            if (ids.Count == 0) Buckets.Remove(key);
        }

        private static bool TryResolveVictimSide(War pWar,
            Kingdom pKillerKingdom, Kingdom pVictimKingdom,
            out WarScoreSide pVictimSide)
        {
            pVictimSide = WarScoreSide.None;
            try
            {
                if (pWar?.data == null || pWar.hasEnded() ||
                    !pWar.isInWarWith(pKillerKingdom, pVictimKingdom))
                    return false;
                bool killerAttacker = pWar.isAttacker(pKillerKingdom);
                bool victimAttacker = pWar.isAttacker(pVictimKingdom);
                bool killerDefender = pWar.isDefender(pKillerKingdom);
                bool victimDefender = pWar.isDefender(pVictimKingdom);
                if (killerAttacker && victimDefender)
                    pVictimSide = WarScoreSide.Defenders;
                else if (killerDefender && victimAttacker)
                    pVictimSide = WarScoreSide.Attackers;
                return pVictimSide != WarScoreSide.None;
            }
            catch { return false; }
        }

        private static bool IsMilitary(Actor pActor)
        {
            try { return pActor.isWarrior() || pActor.hasArmy(); }
            catch { return false; }
        }

        private static int FloorBucket(int pValue)
        {
            if (pValue >= 0) return pValue / BucketSize;
            return (pValue - BucketSize + 1) / BucketSize;
        }

        private static long NextEpisodeId()
        {
            long result = _nextEpisodeId;
            _nextEpisodeId = _nextEpisodeId == long.MaxValue
                ? 1L
                : _nextEpisodeId + 1L;
            while (Episodes.ContainsKey(result))
            {
                result = _nextEpisodeId;
                _nextEpisodeId = _nextEpisodeId == long.MaxValue
                    ? 1L
                    : _nextEpisodeId + 1L;
            }
            return result;
        }

        private static double Now()
        {
            try { return Time.realtimeSinceStartupAsDouble; }
            catch { return 0d; }
        }

        private sealed class BattleEpisode
        {
            internal long Id;
            internal War War;
            internal long WarId;
            internal long FirstVictimActorId;
            internal int AttackerDeaths;
            internal int DefenderDeaths;
            internal double CenterX;
            internal double CenterY;
            internal int SampleCount;
            internal double LastEventTime;
            internal int BucketX;
            internal int BucketY;
            internal LinkedListNode<long> ExpiryNode;
        }

        private readonly struct BucketKey : IEquatable<BucketKey>
        {
            internal BucketKey(long pWarId, int pX, int pY)
            {
                WarId = pWarId;
                X = pX;
                Y = pY;
            }

            private long WarId { get; }
            private int X { get; }
            private int Y { get; }

            public bool Equals(BucketKey pOther)
            {
                return WarId == pOther.WarId && X == pOther.X &&
                       Y == pOther.Y;
            }

            public override bool Equals(object pObject)
            {
                return pObject is BucketKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = WarId.GetHashCode();
                    hash = hash * 397 ^ X;
                    return hash * 397 ^ Y;
                }
            }
        }
    }
}
