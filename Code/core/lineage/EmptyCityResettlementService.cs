using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class EmptyCityResettlementService
    {
        private const int WorkBudgetPerCycle = 4;
        private static readonly Queue<long> PendingOrder = new Queue<long>();
        private static readonly Dictionary<long, PendingCity> Pending =
            new Dictionary<long, PendingCity>();
        private static long _authorityCycle;

        private sealed class PendingCity
        {
            internal int FailureCount;
            internal long DueCycle;
            internal bool Queued;
        }

        private sealed class NeighbourCandidate
        {
            public Kingdom Kingdom;
            public City SourceCity;
            public int SharedBorders;
        }

        private enum AttemptResult
        {
            Cancel,
            Retry,
            Success
        }

        public static void Reset()
        {
            PendingOrder.Clear();
            Pending.Clear();
            _authorityCycle = 0L;
        }

        public static void ObserveLoadedCity(City pCity)
        {
            Enqueue(pCity, true);
        }

        public static void ObserveResidentRemoved(City pCity)
        {
            Enqueue(pCity, true);
        }

        public static void ObserveResidentAdded(City pCity)
        {
            if (pCity?.data == null) return;
            Pending.Remove(pCity.id);
        }

        public static void ObserveOwnershipChanged(City pCity)
        {
            Enqueue(pCity, true);
        }

        public static void ProcessAuthorityCycle()
        {
            _authorityCycle++;
            int remaining = EmptyCityResettlementRules.ResolveScanCount(
                PendingOrder.Count, WorkBudgetPerCycle);
            while (remaining-- > 0 && PendingOrder.Count > 0)
            {
                long cityId = PendingOrder.Dequeue();
                if (!Pending.TryGetValue(cityId, out PendingCity pending))
                    continue;
                pending.Queued = false;
                if (!EmptyCityResettlementRules.IsRetryDue(_authorityCycle,
                        pending.DueCycle))
                {
                    Queue(cityId, pending);
                    continue;
                }

                City city = ResolveCity(cityId);
                AttemptResult result = TryResettle(city);
                if (result == AttemptResult.Retry)
                {
                    pending.FailureCount++;
                    pending.DueCycle = _authorityCycle +
                        EmptyCityResettlementRules.ResolveRetryDelayCycles(
                            pending.FailureCount);
                    Queue(cityId, pending);
                    continue;
                }
                Pending.Remove(cityId);
            }
        }

        private static void Enqueue(City pCity, bool pImmediate)
        {
            if (pCity?.data == null) return;
            long cityId = pCity.id;
            if (!Pending.TryGetValue(cityId, out PendingCity pending))
            {
                pending = new PendingCity();
                Pending[cityId] = pending;
            }
            if (pImmediate)
            {
                pending.FailureCount = 0;
                pending.DueCycle = _authorityCycle;
            }
            Queue(cityId, pending);
        }

        private static void Queue(long pCityId, PendingCity pPending)
        {
            if (pPending == null || pPending.Queued) return;
            pPending.Queued = true;
            PendingOrder.Enqueue(pCityId);
        }

        private static City ResolveCity(long pCityId)
        {
            try { return World.world?.cities?.get(pCityId); }
            catch { return null; }
        }

        private static AttemptResult TryResettle(City pCity)
        {
            if (!CanResettle(pCity)) return AttemptResult.Cancel;
            NeighbourCandidate candidate = FindBestNeighbour(pCity);
            if (candidate?.Kingdom?.data == null ||
                candidate.SourceCity?.data == null) return AttemptResult.Retry;
            Actor settler = FindSettler(candidate.SourceCity,
                candidate.Kingdom);
            if (settler?.data == null) return AttemptResult.Retry;

            try
            {
                pCity.setKingdom(candidate.Kingdom);
                settler.stopBeingWarrior();
                settler.joinCity(pCity);
                settler.setMetasFromCity(pCity);
                pCity.setUnitMetas(settler);
                candidate.Kingdom.setUnitMetas(settler);
                settler.cancelAllBeh();
                return AttemptResult.Success;
            }
            catch { return AttemptResult.Retry; }
        }

        private static bool CanResettle(City pCity)
        {
            try
            {
                if (BanditStrongholdCityDisposalService.IsPending(
                        pCity?.getID() ?? -1L)) return false;
                int population = 0;
                if (pCity != null)
                    foreach (Actor actor in pCity.getUnits())
                        if (actor?.data != null && actor.isAlive() &&
                            !actor.isRekt() && actor.asset?.is_boat != true)
                            population++;
                return EmptyCityResettlementRules.CanResettle(
                    pCity?.data != null, pCity?.isRekt() == true,
                    pCity?.isNeutral() == true, pCity?.zones?.Count ?? 0,
                    population, EmptyCitySurvivalService.HasRazeIntent(pCity),
                    WarScoreService.ShouldHoldFrozenOccupation(pCity));
            }
            catch { return false; }
        }

        private static NeighbourCandidate FindBestNeighbour(City pCity)
        {
            var candidates = new Dictionary<long, NeighbourCandidate>();
            List<TileZone> zones = pCity?.zones;
            if (zones == null) return null;
            for (int i = 0; i < zones.Count; i++)
            {
                TileZone[] neighbours = zones[i]?.neighbours;
                if (neighbours == null) continue;
                for (int j = 0; j < neighbours.Length; j++)
                {
                    City source = neighbours[j]?.city;
                    Kingdom kingdom = source?.kingdom;
                    if (source?.data == null || source == pCity ||
                        kingdom?.data == null || kingdom.isRekt() ||
                        kingdom.isNeutral()) continue;
                    if (!candidates.TryGetValue(kingdom.id,
                            out NeighbourCandidate candidate))
                    {
                        candidate = new NeighbourCandidate
                        {
                            Kingdom = kingdom,
                            SourceCity = source
                        };
                        candidates[kingdom.id] = candidate;
                    }
                    candidate.SharedBorders++;
                }
            }

            NeighbourCandidate best = null;
            foreach (NeighbourCandidate candidate in candidates.Values)
                if (best == null ||
                    candidate.SharedBorders > best.SharedBorders ||
                    candidate.SharedBorders == best.SharedBorders &&
                    candidate.Kingdom.id < best.Kingdom.id)
                    best = candidate;
            return best;
        }

        private static Actor FindSettler(City pSource, Kingdom pKingdom)
        {
            Actor result = null;
            int livingResidents = 0;
            foreach (Actor actor in pSource.getUnits())
            {
                if (actor?.data == null || actor.kingdom != pKingdom ||
                    !actor.isAlive() || actor.isRekt() ||
                    actor.asset?.is_boat == true) continue;
                livingResidents++;
                if (result != null || !actor.isAdult() || actor.isKing() ||
                    actor.isCityLeader() || actor.isArmyGroupLeader() ||
                    actor.hasArmy()) continue;
                result = actor;
            }
            return livingResidents > 1 ? result : null;
        }
    }
}
