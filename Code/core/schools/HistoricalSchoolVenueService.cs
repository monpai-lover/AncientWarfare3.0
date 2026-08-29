using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AncientWarfare3.core.schools
{
    internal sealed class HistoricalSchoolVenueClaim
    {
        public HistoricalSchoolVenueClaim(string pOperationKey, long pCityId,
            WorldTile pPrimary, WorldTile pSecondary, Building pAcademy)
        {
            OperationKey = pOperationKey;
            CityId = pCityId;
            Primary = pPrimary;
            Secondary = pSecondary;
            Academy = pAcademy;
        }

        public string OperationKey { get; }
        public long CityId { get; }
        public WorldTile Primary { get; }
        public WorldTile Secondary { get; }
        public Building Academy { get; }
    }

    internal static class HistoricalSchoolVenueService
    {
        private const int MaxCandidates = 48;
        private const int MaxActiveClaims = 12;
        private const int LocalDiameter = 37;
        private const int LocalSearchCount = LocalDiameter * LocalDiameter;
        private static readonly List<WorldTile> NoCandidates = new List<WorldTile>(0);

        private sealed class CityVenueCacheEntry
        {
            public City City;
            public HistoricalSchoolCityCacheStamp Stamp;
            public List<WorldTile> Tiles;
        }

        private static readonly
            HistoricalSchoolActiveReservationBook<string, HistoricalSchoolVenueClaim>
            ByOperation = new HistoricalSchoolActiveReservationBook<string,
                HistoricalSchoolVenueClaim>(MaxActiveClaims);
        private static readonly Dictionary<long, HashSet<long>> OccupiedByCity =
            new Dictionary<long, HashSet<long>>();
        private static readonly HistoricalSchoolFixedLru<long, CityVenueCacheEntry>
            CandidateTilesByCity =
                new HistoricalSchoolFixedLru<long, CityVenueCacheEntry>(128);

        public static bool TryClaimLecture(City pCity, Actor pActor, string pSchoolId,
            string pOperationKey, out HistoricalSchoolVenueClaim pClaim)
        {
            return TryClaim(pCity, pActor, pSchoolId, pOperationKey,
                HistoricalSchoolVenueKind.Lecture, out pClaim);
        }

        public static bool TryClaimDebate(City pCity, Actor pActor, string pSchoolId,
            string pOperationKey, out HistoricalSchoolVenueClaim pClaim)
        {
            return TryClaim(pCity, pActor, pSchoolId, pOperationKey,
                HistoricalSchoolVenueKind.Debate, out pClaim);
        }

        public static void Release(string pOperationKey)
        {
            if (string.IsNullOrEmpty(pOperationKey) ||
                !ByOperation.TryRemove(pOperationKey, out HistoricalSchoolVenueClaim claim))
                return;
            ReleaseOccupied(claim);
        }

        public static void ReleaseCityClaims(long pCityId)
        {
            if (pCityId < 0) return;
            KeyValuePair<string, HistoricalSchoolVenueClaim>[] claims =
                ByOperation.Snapshot();
            for (int index = 0; index < claims.Length; index++)
            {
                HistoricalSchoolVenueClaim claim = claims[index].Value;
                if (claim?.CityId != pCityId) continue;
                Release(claims[index].Key);
            }
            OccupiedByCity.Remove(pCityId);
        }

        public static void InvalidateCity(long pCityId)
        {
            if (pCityId < 0) return;
            CandidateTilesByCity.Remove(pCityId);
        }

        public static void Clear()
        {
            ByOperation.Clear();
            OccupiedByCity.Clear();
            CandidateTilesByCity.Clear();
        }

        internal static bool TryFindPublicVenue(
            City pCity,
            Actor pActor,
            string pSchoolId,
            HistoricalSchoolVenueKind pKind,
            out WorldTile pPrimary,
            out WorldTile pSecondary)
        {
            pPrimary = null;
            pSecondary = null;
            OccupiedByCity.TryGetValue(pCity.data.id, out HashSet<long> occupied);
            // 先用缓存扫一遍。没扫到时只有一种情况需要重建:扫描过程中真的
            // 撞见了失效 tile —— 说明 zone 内部发生了 stamp 盖不住的变化
            // (stamp 只覆盖城市身份/领主/zone 数/中心坐标)。若候选全都有效、
            // 只是被占用或不合该用途,缓存是新鲜的,重建纯属白做。
            List<WorldTile> candidates = CandidatesForCity(pCity);
            pPrimary = ScanForVenue(candidates, pCity, pActor, pSchoolId,
                pKind, occupied, out bool sawStaleTile);
            if (pPrimary == null && sawStaleTile)
            {
                List<WorldTile> rebuilt = CandidatesForCity(pCity,
                    pForceRebuild: true);
                if (!ReferenceEquals(rebuilt, candidates))
                {
                    candidates = rebuilt;
                    pPrimary = ScanForVenue(candidates, pCity, pActor,
                        pSchoolId, pKind, occupied, out _);
                }
            }
            if (pPrimary == null) return false;
            if (pKind != HistoricalSchoolVenueKind.Debate) return true;
            pSecondary = FindSecondary(candidates, pCity, pPrimary, occupied);
            return pSecondary != null;
        }

        /// <summary>
        /// 按稳定起点环形扫描候选,返回第一个可用的。扫描顺序与判定谓词都与
        /// 原实现一致 —— 抽出来只是为了让「缓存未命中就重建重扫」不必复制一遍。
        /// </summary>
        /// <param name="pSawStaleTile">
        /// 扫描中是否撞见过已不再是合法公共候选的 tile。区分「缓存过期」和
        /// 「候选都有效但都不可用」——只有前者才值得重建。
        /// </param>
        private static WorldTile ScanForVenue(List<WorldTile> pCandidates,
            City pCity, Actor pActor, string pSchoolId,
            HistoricalSchoolVenueKind pKind, HashSet<long> pOccupied,
            out bool pSawStaleTile)
        {
            pSawStaleTile = false;
            if (pCandidates == null || pCandidates.Count == 0) return null;
            int start = PositiveModulo(StableHash(pActor, pSchoolId, pKind),
                pCandidates.Count);
            for (int offset = 0; offset < pCandidates.Count; offset++)
            {
                WorldTile candidate =
                    pCandidates[(start + offset) % pCandidates.Count];
                if (!IsPublicCandidate(candidate, pCity))
                {
                    pSawStaleTile = true;
                    continue;
                }

                if (!IsVenueCandidate(candidate, pCity, pActor, pKind) ||
                    pOccupied?.Contains(TileKey(candidate)) == true) continue;
                return candidate;
            }

            return null;
        }

        internal static bool TryFindLocalVenue(
            City pCity,
            Actor pActor,
            string pSchoolId,
            HistoricalSchoolVenueKind pKind,
            out WorldTile pPrimary,
            out WorldTile pSecondary)
        {
            pPrimary = null;
            pSecondary = null;
            WorldTile origin = pActor?.current_tile;
            if (pCity?.data == null || origin == null) return false;
            OccupiedByCity.TryGetValue(pCity.data.id, out HashSet<long> occupied);
            long stableKey = StableHash(pActor, pSchoolId, pKind);
            int probeCount = HistoricalSchoolVenueRules.IdleRoamProbeCount(
                LocalSearchCount);
            for (int probe = 0; probe < probeCount; probe++)
            {
                int index = HistoricalSchoolVenueRules.IdleRoamProbeIndex(
                    stableKey, probe, LocalSearchCount);
                int dx = index % LocalDiameter - 18;
                int dy = index / LocalDiameter - 18;
                int distanceSquared = dx * dx + dy * dy;
                WorldTile candidate = World.world?.GetTile(origin.x + dx, origin.y + dy);
                if (!HistoricalSchoolVenueRules.IsIdleRoamCandidate(
                        candidate?.zone?.city == pCity,
                        IsWalkable(candidate),
                        candidate == pCity.getTile(),
                        IsBorder(candidate, pCity),
                        distanceSquared) ||
                    occupied?.Contains(TileKey(candidate)) == true) continue;
                if (pPrimary == null)
                {
                    pPrimary = candidate;
                    if (pKind != HistoricalSchoolVenueKind.Debate) return true;
                    continue;
                }
                pSecondary = candidate;
                return true;
            }
            return pPrimary != null && pKind != HistoricalSchoolVenueKind.Debate;
        }

        private static bool TryClaim(City pCity, Actor pActor, string pSchoolId,
            string pOperationKey, HistoricalSchoolVenueKind pKind,
            out HistoricalSchoolVenueClaim pClaim)
        {
            pClaim = null;
            if (pCity?.data == null || pCity.isRekt() || pActor?.data == null ||
                string.IsNullOrEmpty(pSchoolId) || string.IsNullOrEmpty(pOperationKey))
                return false;
            if (ByOperation.TryGet(pOperationKey, out pClaim)) return true;
            if (ByOperation.Count >= MaxActiveClaims) return false;
            if (!HistoricalSchoolVenueProvider.TryFind(pCity, pActor, pSchoolId, pKind,
                    out WorldTile primary, out WorldTile secondary,
                    out Building academy)) return false;
            if (!IsVenueCandidate(primary, pCity, pActor, pKind, academy) ||
                pKind == HistoricalSchoolVenueKind.Debate &&
                (!IsVenueCandidate(secondary, pCity, pActor, pKind, academy) ||
                 !HistoricalSchoolVenueRules.IsDebateLayoutValid(
                     academy != null, primary != null, secondary != null,
                     secondary == primary)))
                return false;
            if (!OccupiedByCity.TryGetValue(pCity.data.id, out HashSet<long> occupied))
            {
                occupied = new HashSet<long>();
                OccupiedByCity[pCity.data.id] = occupied;
            }
            if (academy != null && !HistoricalSchoolVenueRules.CanReserveAcademy(
                    HistoricalSchoolAcademyService.IsUsable(academy, pCity),
                    occupied.Contains(TileKey(primary)))) return false;
            if (!occupied.Add(TileKey(primary))) return false;
            if (secondary != null && secondary != primary &&
                !occupied.Add(TileKey(secondary)))
            {
                occupied.Remove(TileKey(primary));
                if (occupied.Count == 0) OccupiedByCity.Remove(pCity.data.id);
                return false;
            }
            pClaim = new HistoricalSchoolVenueClaim(pOperationKey, pCity.data.id,
                primary, secondary, academy);
            if (!ByOperation.TryAdd(pOperationKey, pClaim))
            {
                ReleaseOccupied(pClaim);
                pClaim = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 取城市的公共场地候选 tile。
        ///
        /// 原本缓存命中时还要对全部(最多 48 个)缓存
        /// tile 逐个调 IsPublicCandidate。而一个学派旅行帧要探 24 座城、每城最多
        /// 走 3 个 venue source,于是单帧上千次 IsPublicCandidate —— 实测
        /// travel_frame 单次 0.39~0.61ms、累计 148.6ms,是学派第二大项。
        ///
        /// 那个前置全扫对结果正确性是多余的:TryFindPublicVenue 扫描时对每个
        /// 候选已经调了 IsVenueCandidate(内含 IsPublicCandidate),失效 tile 本来
        /// 就会被跳过。它唯一的作用是决定要不要重建。所以改成 stamp 匹配即直接
        /// 复用,由调用方在「一个可用候选都没扫到」时用 pForceRebuild 重建一次
        /// —— 扫描顺序与判定谓词一字未改,选出的场地不变。
        /// </summary>
        private static List<WorldTile> CandidatesForCity(City pCity,
            bool pForceRebuild = false)
        {
            if (pCity?.data == null || pCity.isRekt()) return EmptyCandidates();
            HistoricalSchoolCityCacheStamp stamp = StampFor(pCity);
            if (!pForceRebuild &&
                CandidateTilesByCity.TryGet(pCity.data.id, out CityVenueCacheEntry cached) &&
                ReferenceEquals(cached.City, pCity) && StampMatches(cached.Stamp, pCity))
                return cached.Tiles;
            List<WorldTile> rebuilt = BuildCandidates(pCity);
            CandidateTilesByCity.Set(pCity.data.id, new CityVenueCacheEntry
            {
                City = pCity,
                Stamp = stamp,
                Tiles = rebuilt
            });
            return rebuilt;
        }

        private static List<WorldTile> BuildCandidates(City pCity)
        {
            var result = new List<WorldTile>(MaxCandidates);
            WorldTile center = pCity.getTile();
            try
            {
                foreach (TileZone zone in pCity.zones)
                {
                    if (zone == null) continue;
                    foreach (WorldTile tile in zone.tiles)
                    {
                        if (!HistoricalSchoolVenueRules.IsPublicCandidate(
                                tile?.zone?.city == pCity, IsWalkable(tile), tile == center))
                            continue;
                        result.Add(tile);
                        if (result.Count >= MaxCandidates) return result;
                    }
                }
            }
            catch { }
            return result;
        }

        private static WorldTile FindSecondary(List<WorldTile> pCandidates, City pCity,
            WorldTile pPrimary, HashSet<long> pOccupied)
        {
            WorldTile result = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < pCandidates.Count; i++)
            {
                WorldTile candidate = pCandidates[i];
                if (candidate == pPrimary || !IsPublicCandidate(candidate, pCity) ||
                    pOccupied?.Contains(TileKey(candidate)) == true) continue;
                int distance = DistanceSquared(pPrimary, candidate);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                result = candidate;
            }
            return result;
        }


        private static bool IsVenueCandidate(WorldTile pTile, City pCity, Actor pActor,
            HistoricalSchoolVenueKind pKind, Building pAcademy = null)
        {
            if (pAcademy != null)
                return HistoricalSchoolAcademyService.IsUsable(pAcademy, pCity) &&
                       ReferenceEquals(pTile, pAcademy.current_tile);
            if (!IsPublicCandidate(pTile, pCity)) return false;
            if (pKind != HistoricalSchoolVenueKind.IdleRoam) return true;
            WorldTile origin = pActor?.current_tile;
            return origin != null && HistoricalSchoolVenueRules.IsIdleRoamCandidate(
                pTile.zone?.city == pCity,
                IsWalkable(pTile),
                pTile == pCity.getTile(),
                IsBorder(pTile, pCity),
                DistanceSquared(origin, pTile));
        }

        private static bool IsPublicCandidate(WorldTile pTile, City pCity)
        {
            return HistoricalSchoolVenueRules.IsPublicCandidate(
                       pTile?.zone?.city == pCity,
                       IsWalkable(pTile),
                       pTile == pCity?.getTile()) &&
                   !IsBorder(pTile, pCity);
        }

        private static bool IsWalkable(WorldTile pTile)
        {
            return pTile?.Type != null && pTile.Type.ground && !pTile.Type.liquid &&
                   !pTile.Type.lava && !pTile.Type.block;
        }

        private static bool IsBorder(WorldTile pTile, City pCity)
        {
            if (pTile == null || pCity == null) return true;
            try
            {
                return World.world?.GetTile(pTile.x - 1, pTile.y)?.zone?.city != pCity ||
                       World.world?.GetTile(pTile.x + 1, pTile.y)?.zone?.city != pCity ||
                       World.world?.GetTile(pTile.x, pTile.y - 1)?.zone?.city != pCity ||
                       World.world?.GetTile(pTile.x, pTile.y + 1)?.zone?.city != pCity;
            }
            catch { return true; }
        }

        private static HistoricalSchoolCityCacheStamp StampFor(City pCity)
        {
            WorldTile center = pCity?.getTile();
            return new HistoricalSchoolCityCacheStamp(
                RuntimeHelpers.GetHashCode(pCity),
                pCity?.kingdom?.data?.id ?? -1L,
                pCity?.zones?.Count ?? 0,
                center?.x ?? int.MinValue,
                center?.y ?? int.MinValue);
        }

        private static bool StampMatches(HistoricalSchoolCityCacheStamp pStamp, City pCity)
        {
            WorldTile center = pCity?.getTile();
            return pStamp.Matches(
                RuntimeHelpers.GetHashCode(pCity),
                pCity?.kingdom?.data?.id ?? -1L,
                pCity?.zones?.Count ?? 0,
                center?.x ?? int.MinValue,
                center?.y ?? int.MinValue);
        }

        private static void ReleaseOccupied(HistoricalSchoolVenueClaim pClaim)
        {
            if (pClaim == null ||
                !OccupiedByCity.TryGetValue(pClaim.CityId, out HashSet<long> occupied))
                return;
            occupied.Remove(TileKey(pClaim.Primary));
            if (pClaim.Secondary != null && pClaim.Secondary != pClaim.Primary)
                occupied.Remove(TileKey(pClaim.Secondary));
            if (occupied.Count == 0) OccupiedByCity.Remove(pClaim.CityId);
        }

        private static List<WorldTile> EmptyCandidates()
        {
            return NoCandidates;
        }

        private static long TileKey(WorldTile pTile)
        {
            return pTile == null ? long.MinValue : ((long)pTile.x << 32) ^ (uint)pTile.y;
        }

        private static long StableHash(Actor pActor, string pSchoolId,
            HistoricalSchoolVenueKind pKind)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                hash = (hash ^ (pActor?.data?.id ?? -1L)) * 1099511628211L;
                foreach (char character in pSchoolId ?? "")
                    hash = (hash ^ character) * 1099511628211L;
                return (hash ^ (int)pKind) * 1099511628211L;
            }
        }

        private static int PositiveModulo(long pValue, int pCount)
        {
            if (pCount <= 0) return 0;
            long remainder = pValue % pCount;
            return (int)(remainder < 0 ? remainder + pCount : remainder);
        }

        private static int DistanceSquared(WorldTile pFirst, WorldTile pSecond)
        {
            if (pFirst == null || pSecond == null) return int.MaxValue;
            int dx = pFirst.x - pSecond.x;
            int dy = pFirst.y - pSecond.y;
            return dx * dx + dy * dy;
        }
    }
}
