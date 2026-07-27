using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class EnclosedUnownedZoneRepairService
    {
        private const int MaxCandidatesPerCycle = 8;
        private const int MaxSweepZonesPerCycle = 64;
        private const int MaxCityBoundaryZonesPerCycle = 16;
        private const int MaxCityBoundaryRecordsPerCycle = 4;

        private sealed class CityBoundaryScan
        {
            public City city;
            public long cityId;
            public int zoneIndex;
            public bool rescanRequested;
        }

        private static readonly Queue<long> PendingCoordinates =
            new Queue<long>();
        private static readonly HashSet<long> PendingCoordinateSet =
            new HashSet<long>();
        private static readonly Queue<CityBoundaryScan>
            PendingCityBoundaryScans = new Queue<CityBoundaryScan>();
        private static readonly Dictionary<long, CityBoundaryScan>
            PendingBoundaryScansByCityId =
                new Dictionary<long, CityBoundaryScan>();
        private static int _sweepCursor = -1;

        public static void ObserveOwnershipChange(TileZone pZone)
        {
            if (pZone == null || !Config.game_loaded ||
                SmoothLoader.isLoading())
                return;

            Enqueue(pZone);
            TileZone[] neighbours = pZone.neighbours;
            if (neighbours == null) return;
            for (int i = 0; i < neighbours.Length; i++)
                Enqueue(neighbours[i]);
        }

        public static void ObserveCityKingdomChange(City pCity)
        {
            if (pCity?.data == null || pCity.id < 0L ||
                !Config.game_loaded || SmoothLoader.isLoading())
                return;

            if (PendingBoundaryScansByCityId.TryGetValue(pCity.id,
                    out CityBoundaryScan existing))
            {
                existing.city = pCity;
                existing.rescanRequested = true;
                return;
            }

            var scan = new CityBoundaryScan
            {
                city = pCity,
                cityId = pCity.id,
                zoneIndex = 0,
                rescanRequested = false
            };
            PendingBoundaryScansByCityId[pCity.id] = scan;
            PendingCityBoundaryScans.Enqueue(scan);
        }

        public static void BeginInitialSweep()
        {
            PendingCoordinates.Clear();
            PendingCoordinateSet.Clear();
            PendingCityBoundaryScans.Clear();
            PendingBoundaryScansByCityId.Clear();
            _sweepCursor = 0;
        }

        public static void Reset()
        {
            PendingCoordinates.Clear();
            PendingCoordinateSet.Clear();
            PendingCityBoundaryScans.Clear();
            PendingBoundaryScansByCityId.Clear();
            _sweepCursor = -1;
        }

        public static void ProcessAuthorityCycle()
        {
            ZoneCalculator calculator = World.world?.zone_calculator;
            if (calculator == null) return;

            ProcessCityBoundaryScans();
            AdvanceInitialSweep(calculator);
            int count = EnclosedUnownedZoneRules.ResolveDrainCount(
                PendingCoordinates.Count, MaxCandidatesPerCycle);
            for (int i = 0; i < count; i++)
            {
                long coordinate = PendingCoordinates.Dequeue();
                PendingCoordinateSet.Remove(coordinate);
                Decode(coordinate, out int x, out int y);
                TileZone zone = calculator.getZone(x, y);
                if (zone == null) continue;
                try { TryRepair(zone); }
                catch
                {
                    // Ownership may change again while queued; stale work is
                    // safely reconsidered by the next ownership event.
                }
            }
        }

        private static void ProcessCityBoundaryScans()
        {
            int remaining = MaxCityBoundaryZonesPerCycle;
            int recordsRemaining = MaxCityBoundaryRecordsPerCycle;
            while (remaining > 0 && recordsRemaining > 0 &&
                   PendingCityBoundaryScans.Count > 0)
            {
                CityBoundaryScan scan = PendingCityBoundaryScans.Dequeue();
                recordsRemaining--;
                if (scan.rescanRequested)
                {
                    scan.rescanRequested = false;
                    scan.zoneIndex = 0;
                }
                City city = scan.city;
                if (!IsLiveTarget(city) || city.zones == null)
                {
                    PendingBoundaryScansByCityId.Remove(scan.cityId);
                    continue;
                }

                List<TileZone> zones = city.zones;
                while (remaining > 0 && scan.zoneIndex < zones.Count)
                {
                    EnqueueUnownedCardinalNeighbours(
                        zones[scan.zoneIndex]);
                    scan.zoneIndex++;
                    remaining--;
                }

                if (scan.zoneIndex < zones.Count || scan.rescanRequested)
                    PendingCityBoundaryScans.Enqueue(scan);
                else
                    PendingBoundaryScansByCityId.Remove(scan.cityId);
            }
        }

        private static void EnqueueUnownedCardinalNeighbours(TileZone pZone)
        {
            TileZone[] neighbours = pZone?.neighbours;
            if (neighbours == null) return;
            for (int i = 0; i < neighbours.Length; i++)
            {
                TileZone neighbour = neighbours[i];
                if (neighbour?.city == null) Enqueue(neighbour);
            }
        }

        private static void AdvanceInitialSweep(ZoneCalculator pCalculator)
        {
            if (_sweepCursor < 0 || pCalculator?.zones == null) return;
            List<TileZone> zones = pCalculator.zones;
            int count = EnclosedUnownedZoneRules.ResolveSweepCount(
                zones.Count, _sweepCursor, MaxSweepZonesPerCycle);
            int end = _sweepCursor + count;
            for (; _sweepCursor < end; _sweepCursor++)
                Enqueue(zones[_sweepCursor]);
            if (_sweepCursor >= zones.Count) _sweepCursor = -1;
        }

        private static void TryRepair(TileZone pZone)
        {
            TileZone[] neighbours = pZone?.neighbours;
            if (pZone == null || neighbours == null ||
                neighbours.Length != 4)
                return;

            var facts = new[]
            {
                BuildFacts(neighbours[0]),
                BuildFacts(neighbours[1]),
                BuildFacts(neighbours[2]),
                BuildFacts(neighbours[3])
            };
            long targetCityId = EnclosedUnownedZoneRules.SelectTargetCity(
                pZone.city != null, pZone.world_edge,
                pZone.tiles_with_ground, neighbours.Length,
                pZone.x, pZone.y, facts);
            if (targetCityId < 0L) return;

            City pTargetCity = FindTargetCity(neighbours, targetCityId);
            if (!IsLiveTarget(pTargetCity) || pZone.city != null) return;
            pTargetCity.addZone(pZone);
        }

        private static EnclosedZoneNeighbourFacts BuildFacts(TileZone pZone)
        {
            City city = pZone?.city;
            Kingdom kingdom = city?.kingdom;
            TileZone centreZone = null;
            bool live = false;
            try
            {
                centreZone = city?.getTile()?.zone;
                live = city?.data != null && !city.isRekt() &&
                       kingdom?.data != null && !kingdom.isRekt() &&
                       centreZone != null;
            }
            catch { live = false; }

            return new EnclosedZoneNeighbourFacts(
                city != null,
                live,
                city?.id ?? -1L,
                kingdom?.id ?? -1L,
                centreZone?.x ?? 0,
                centreZone?.y ?? 0);
        }

        private static City FindTargetCity(TileZone[] pNeighbours,
            long pTargetCityId)
        {
            if (pNeighbours[0]?.city?.id == pTargetCityId)
                return pNeighbours[0].city;
            if (pNeighbours[1]?.city?.id == pTargetCityId)
                return pNeighbours[1].city;
            if (pNeighbours[2]?.city?.id == pTargetCityId)
                return pNeighbours[2].city;
            if (pNeighbours[3]?.city?.id == pTargetCityId)
                return pNeighbours[3].city;
            return null;
        }

        private static bool IsLiveTarget(City pCity)
        {
            try
            {
                return pCity?.data != null && !pCity.isRekt() &&
                       pCity.kingdom?.data != null &&
                       !pCity.kingdom.isRekt();
            }
            catch { return false; }
        }

        private static void Enqueue(TileZone pZone)
        {
            if (pZone == null) return;
            long coordinate = Encode(pZone.x, pZone.y);
            if (!PendingCoordinateSet.Add(coordinate)) return;
            PendingCoordinates.Enqueue(coordinate);
        }

        private static long Encode(int pX, int pY)
        {
            return ((long)(uint)pX << 32) | (uint)pY;
        }

        private static void Decode(long pCoordinate, out int pX, out int pY)
        {
            pX = (int)(uint)(pCoordinate >> 32);
            pY = (int)(uint)pCoordinate;
        }
    }
}
