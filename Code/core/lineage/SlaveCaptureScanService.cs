using System;
using System.Collections.Generic;
using System.Diagnostics;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal enum CaptureTargetSearchState
    {
        Pending,
        Hit,
        Miss
    }

    internal static class SlaveCaptureScanService
    {
        private const int TileRadius = 80;
        private const int ChunkSize = 16;
        private const int UnitBudget = 128;
        private const double MillisecondBudget = 1.0;
        private const double HitTtl = 2.0;
        private const double MissTtl = 5.0;
        private const int PruneThreshold = 256;

        private sealed class ScanState
        {
            public string key;
            public long kingdomId;
            public long islandId;
            public int originX;
            public int originY;
            public int originChunkX;
            public int originChunkY;
            public int chunkIndex;
            public int unitIndex;
            public long bestActorId = -1L;
            public int bestDistance = int.MaxValue;
            public bool complete;
            public double expiresAt;
            public readonly HashSet<long> waitingCityIds = new HashSet<long>();
        }

        private static readonly Dictionary<string, ScanState> States =
            new Dictionary<string, ScanState>(StringComparer.Ordinal);
        private static readonly Queue<string> PendingKeys = new Queue<string>();

        internal static CaptureTargetSearchState FindOrRequest(Kingdom pKingdom, WorldTile pOrigin,
            out Actor pTarget, long pWaitingCityId = -1L)
        {
            pTarget = null;
            if (pKingdom?.data == null || pOrigin?.chunk == null) return CaptureTargetSearchState.Miss;

            long islandId = pOrigin.region?.island?.id ?? -1L;
            string key = BuildKey(pKingdom.id, islandId, pOrigin.chunk.x, pOrigin.chunk.y);
            double now = LineageService.CurTime();
            if (States.TryGetValue(key, out ScanState state))
            {
                if (!state.complete)
                {
                    if (pWaitingCityId >= 0) state.waitingCityIds.Add(pWaitingCityId);
                    return CaptureTargetSearchState.Pending;
                }

                Actor cached = ResolveActor(state.bestActorId);
                bool alive = cached?.data != null && !cached.isRekt() && cached.isAlive();
                bool hostile = alive && cached.kingdom?.data != null && pKingdom.isEnemy(cached.kingdom);
                bool sameIslandAndRadius = alive && cached.current_tile != null &&
                                           pOrigin.isSameIsland(cached.current_tile) &&
                                           Toolbox.SquaredDistTile(pOrigin, cached.current_tile) <= TileRadius * TileRadius;
                if (state.bestActorId < 0 && now < state.expiresAt)
                {
                    RecordCacheHit();
                    return CaptureTargetSearchState.Miss;
                }
                if (SlaveCaptureScanRules.ShouldReuseResult(true, alive, hostile,
                        sameIslandAndRadius, now, state.expiresAt) &&
                    SlaveService.IsCaptureTargetForScan(pKingdom, pOrigin, cached, TileRadius))
                {
                    pTarget = cached;
                    RecordCacheHit();
                    return CaptureTargetSearchState.Hit;
                }
                States.Remove(key);
            }

            var created = new ScanState
            {
                key = key,
                kingdomId = pKingdom.id,
                islandId = islandId,
                originX = pOrigin.x,
                originY = pOrigin.y,
                originChunkX = pOrigin.chunk.x,
                originChunkY = pOrigin.chunk.y
            };
            if (pWaitingCityId >= 0) created.waitingCityIds.Add(pWaitingCityId);
            States[key] = created;
            PendingKeys.Enqueue(key);
            if (States.Count > PruneThreshold) PruneExpired(now);
            return CaptureTargetSearchState.Pending;
        }

        internal static void DrainFrame()
        {
            if (PendingKeys.Count == 0 || World.world?.map_chunk_manager == null) return;
            long start = Stopwatch.GetTimestamp();
            long tickBudget = Math.Max(1L, (long)(Stopwatch.Frequency * MillisecondBudget / 1000.0));
            int checkedUnits = 0;

            while (PendingKeys.Count > 0 &&
                   !SlaveCaptureScanRules.ShouldPause(checkedUnits, UnitBudget,
                       Stopwatch.GetTimestamp() - start, tickBudget))
            {
                string key = PendingKeys.Dequeue();
                if (!States.TryGetValue(key, out ScanState state) || state.complete) continue;
                Advance(state, start, tickBudget, ref checkedUnits);
                if (state.complete)
                    Publish(state);
                else
                    PendingKeys.Enqueue(key);
            }
        }

        internal static void Clear()
        {
            States.Clear();
            PendingKeys.Clear();
        }

        private static void Advance(ScanState pState, long pStart, long pTickBudget, ref int pCheckedUnits)
        {
            int chunkRadius = SlaveCaptureScanRules.ChunkRadius(TileRadius, ChunkSize);
            int chunkCount = SlaveCaptureScanRules.ChunkCount(chunkRadius);
            Kingdom kingdom = ResolveKingdom(pState.kingdomId);
            WorldTile origin = World.world?.GetTile(pState.originX, pState.originY);
            if (kingdom?.data == null || origin == null)
            {
                pState.complete = true;
                return;
            }

            while (pState.chunkIndex < chunkCount)
            {
                SlaveCaptureScanRules.OffsetForIndex(pState.chunkIndex, chunkRadius,
                    out int offsetX, out int offsetY);
                MapChunk chunk = World.world.map_chunk_manager.get(
                    pState.originChunkX + offsetX, pState.originChunkY + offsetY);
                if (chunk == null)
                {
                    pState.chunkIndex++;
                    pState.unitIndex = 0;
                    continue;
                }

                List<Actor> units = chunk.objects.units_all;
                while (pState.unitIndex < units.Count)
                {
                    Actor target = units[pState.unitIndex++];
                    pCheckedUnits++;
                    if (SlaveService.IsCaptureTargetForScan(kingdom, origin, target, TileRadius))
                    {
                        int distance = Toolbox.SquaredDistTile(origin, target.current_tile);
                        if (distance < pState.bestDistance)
                        {
                            pState.bestDistance = distance;
                            pState.bestActorId = target.data.id;
                        }
                    }
                    if (SlaveCaptureScanRules.ShouldPause(pCheckedUnits, UnitBudget,
                            Stopwatch.GetTimestamp() - pStart, pTickBudget)) return;
                }

                pState.chunkIndex++;
                pState.unitIndex = 0;
            }
            pState.complete = true;
        }

        private static void Publish(ScanState pState)
        {
            pState.expiresAt = LineageService.CurTime() +
                               (pState.bestActorId >= 0 ? HitTtl : MissTtl);
            if (pState.bestActorId >= 0)
                foreach (long cityId in pState.waitingCityIds)
                    SlaveService.AssignSlaveCatcherAfterScan(cityId, pState.kingdomId);
            pState.waitingCityIds.Clear();
        }

        private static void PruneExpired(double pNow)
        {
            var expired = new List<string>();
            foreach (KeyValuePair<string, ScanState> entry in States)
                if (entry.Value.complete && entry.Value.expiresAt <= pNow)
                    expired.Add(entry.Key);
            foreach (string key in expired) States.Remove(key);
        }

        private static void RecordCacheHit()
        {
            Bench.bench(CityMaintenanceBenchmarkRules.CaptureCacheHit,
                CityMaintenanceBenchmarkRules.Group);
            Bench.benchEnd(CityMaintenanceBenchmarkRules.CaptureCacheHit,
                CityMaintenanceBenchmarkRules.Group);
        }

        private static string BuildKey(long pKingdomId, long pIslandId, int pChunkX, int pChunkY)
        {
            return pKingdomId + ":" + pIslandId + ":" + pChunkX + ":" + pChunkY;
        }

        private static Actor ResolveActor(long pActorId)
        {
            if (pActorId < 0) return null;
            try { return World.world?.units?.get(pActorId); }
            catch { return null; }
        }

        private static Kingdom ResolveKingdom(long pKingdomId)
        {
            if (pKingdomId < 0) return null;
            try { return World.world?.kingdoms?.get(pKingdomId); }
            catch { return null; }
        }
    }
}
