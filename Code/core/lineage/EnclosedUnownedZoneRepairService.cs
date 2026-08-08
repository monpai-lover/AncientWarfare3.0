using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class EnclosedUnownedZoneRepairService
    {
        private const int MaxCandidatesPerCycle = 8;
        private const int MaxSweepZonesPerCycle = 64;
        private const int MaxCityBoundaryZonesPerCycle = 16;
        private const int MaxCityBoundaryRecordsPerCycle = 4;
        private const int MaxEnclosedComponentZones = 64;

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
        private static readonly HashSet<long> InitialSweepVisited =
            new HashSet<long>();
        private static readonly List<TileZone> ComponentBuffer =
            new List<TileZone>(MaxEnclosedComponentZones);
        private static readonly Queue<TileZone> ComponentFrontier =
            new Queue<TileZone>(MaxEnclosedComponentZones);
        private static readonly HashSet<long> ComponentVisited =
            new HashSet<long>();
        private static readonly List<EnclosedZoneNeighbourFacts>
            BoundaryFactsBuffer =
                new List<EnclosedZoneNeighbourFacts>();
        private static readonly Dictionary<long, City> BoundaryCitiesBuffer =
            new Dictionary<long, City>();
        private static int _sweepCursor = -1;
        private static bool _initialSweepActive;
        private static bool _repairAssignmentActive;

        public static void ObserveOwnershipChange(TileZone pZone, City pOldCity, City pNewCity)
        {
            if (pZone == null || _repairAssignmentActive) return;
            if (ReferenceEquals(pOldCity, pNewCity)) return;
            if (!Config.game_loaded ||
                SmoothLoader.isLoading())
                return;

            InvalidateInitialSweepVisit(pZone);
            if (pZone.city == null) Enqueue(pZone);
            TileZone[] neighbours = pZone.neighbours;
            if (neighbours == null) return;
            for (int i = 0; i < neighbours.Length; i++)
            {
                TileZone neighbour = neighbours[i];
                InvalidateInitialSweepVisit(neighbour);
                if (neighbour?.city == null) Enqueue(neighbour);
            }
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
            InitialSweepVisited.Clear();
            ClearComponentBuffers();
            _sweepCursor = 0;
            _initialSweepActive = true;
        }

        public static void Reset()
        {
            PendingCoordinates.Clear();
            PendingCoordinateSet.Clear();
            PendingCityBoundaryScans.Clear();
            PendingBoundaryScansByCityId.Clear();
            InitialSweepVisited.Clear();
            ClearComponentBuffers();
            _sweepCursor = -1;
            _initialSweepActive = false;
            _repairAssignmentActive = false;
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
                if (_initialSweepActive &&
                    !InitialSweepVisited.Add(coordinate)) continue;
                try
                {
                    TryRepair(zone, _initialSweepActive
                        ? InitialSweepVisited
                        : null);
                }
                catch
                {
                    // Ownership may change again while queued; stale work is
                    // safely reconsidered by the next ownership event.
                }
            }
            if (_initialSweepActive && _sweepCursor < 0 &&
                PendingCoordinates.Count == 0)
            {
                _initialSweepActive = false;
                InitialSweepVisited.Clear();
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

        private static void TryRepair(TileZone pZone,
            ISet<long> pSweepVisited = null)
        {
            if (pZone == null || pZone.city != null) return;
            TileZone[] seedNeighbours = pZone.neighbours;
            if (seedNeighbours == null || seedNeighbours.Length != 4) return;
            int ownedNeighbourCount = 0;
            for (int i = 0; i < seedNeighbours.Length; i++)
                if (seedNeighbours[i]?.city != null)
                    ownedNeighbourCount++;
            if (!EnclosedUnownedZoneRules.CanStartComponentScan(
                    ownedNeighbourCount))
                return;

            ClearComponentBuffers();
            List<TileZone> component = ComponentBuffer;
            Queue<TileZone> frontier = ComponentFrontier;
            HashSet<long> visited = ComponentVisited;
            List<EnclosedZoneNeighbourFacts> boundaryFacts =
                BoundaryFactsBuffer;
            Dictionary<long, City> boundaryCities = BoundaryCitiesBuffer;
            long coordinateSumX = 0L;
            long coordinateSumY = 0L;
            bool touchesWorldEdge = false;
            bool exceededZoneBudget = false;

            frontier.Enqueue(pZone);
            visited.Add(Encode(pZone.x, pZone.y));
            while (frontier.Count > 0)
            {
                if (component.Count >= MaxEnclosedComponentZones)
                {
                    exceededZoneBudget = true;
                    break;
                }

                TileZone current = frontier.Dequeue();
                if (current == null || current.city != null) return;
                pSweepVisited?.Add(Encode(current.x, current.y));
                component.Add(current);
                coordinateSumX += current.x;
                coordinateSumY += current.y;
                if (current.world_edge) touchesWorldEdge = true;
                TileZone[] neighbours = current.neighbours;
                if (neighbours == null || neighbours.Length != 4)
                {
                    touchesWorldEdge = true;
                    continue;
                }

                for (int i = 0; i < neighbours.Length; i++)
                {
                    TileZone neighbour = neighbours[i];
                    if (neighbour == null)
                    {
                        touchesWorldEdge = true;
                        continue;
                    }

                    City city = neighbour.city;
                    if (city == null)
                    {
                        long coordinate = Encode(neighbour.x, neighbour.y);
                        if (visited.Add(coordinate))
                            frontier.Enqueue(neighbour);
                        continue;
                    }

                    EnclosedZoneNeighbourFacts facts = BuildFacts(neighbour);
                    boundaryFacts.Add(facts);
                    if (facts.CityId >= 0L &&
                        !boundaryCities.ContainsKey(facts.CityId))
                        boundaryCities.Add(facts.CityId, city);
                }
            }

            int centerX = component.Count == 0
                ? pZone.x
                : (int)(coordinateSumX / component.Count);
            int centerY = component.Count == 0
                ? pZone.y
                : (int)(coordinateSumY / component.Count);
            long targetCityId =
                EnclosedUnownedZoneRules.SelectComponentTargetCity(
                    touchesWorldEdge, exceededZoneBudget,
                    centerX, centerY, boundaryFacts);
            if (targetCityId < 0L ||
                !boundaryCities.TryGetValue(targetCityId,
                    out City pTargetCity) ||
                !IsLiveTarget(pTargetCity))
                return;

            for (int i = 0; i < component.Count; i++)
            {
                TileZone zone = component[i];
                if (zone == null || zone.city != null) return;
            }
            _repairAssignmentActive = true;
            try
            {
                for (int i = 0; i < component.Count; i++)
                    AssignZone(pTargetCity, component[i]);
            }
            finally
            {
                _repairAssignmentActive = false;
            }
        }

        private static void AssignZone(City pTargetCity, TileZone pZone)
        {
            pTargetCity.addZone(pZone);
        }

        private static void ClearComponentBuffers()
        {
            ComponentBuffer.Clear();
            ComponentFrontier.Clear();
            ComponentVisited.Clear();
            BoundaryFactsBuffer.Clear();
            BoundaryCitiesBuffer.Clear();
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
                       !kingdom.isNeutral() &&
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

        private static bool IsLiveTarget(City pCity)
        {
            try
            {
                return pCity?.data != null && !pCity.isRekt() &&
                       pCity.kingdom?.data != null &&
                       !pCity.kingdom.isRekt() &&
                       !pCity.kingdom.isNeutral();
            }
            catch { return false; }
        }

        private static void Enqueue(TileZone pZone)
        {
            if (pZone == null || pZone.city != null) return;
            long coordinate = Encode(pZone.x, pZone.y);
            if (!PendingCoordinateSet.Add(coordinate)) return;
            PendingCoordinates.Enqueue(coordinate);
        }

        private static void InvalidateInitialSweepVisit(TileZone pZone)
        {
            if (!_initialSweepActive || pZone == null) return;
            InitialSweepVisited.Remove(Encode(pZone.x, pZone.y));
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
