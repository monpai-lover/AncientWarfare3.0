using System;
using System.Collections;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    internal static class EmptyCityResettlementService
    {
        private const int ScanBudgetPerCycle = 4;
        private static IEnumerator _cities;

        private sealed class NeighbourCandidate
        {
            public Kingdom Kingdom;
            public City SourceCity;
            public int SharedBorders;
        }

        public static void Reset()
        {
            DisposeEnumerator();
        }

        public static void ProcessAuthorityCycle()
        {
            if (World.world?.cities == null) return;
            int remaining = ScanBudgetPerCycle;
            while (remaining-- > 0)
            {
                if (_cities == null)
                    _cities = World.world.cities.GetEnumerator();
                City city;
                try
                {
                    if (!_cities.MoveNext())
                    {
                        DisposeEnumerator();
                        return;
                    }
                    city = _cities.Current as City;
                }
                catch
                {
                    DisposeEnumerator();
                    return;
                }
                TryResettle(city);
            }
        }

        private static bool TryResettle(City pCity)
        {
            if (!CanResettle(pCity)) return false;
            NeighbourCandidate candidate = FindBestNeighbour(pCity);
            if (candidate?.Kingdom?.data == null ||
                candidate.SourceCity?.data == null) return false;
            Actor settler = FindSettler(candidate.SourceCity,
                candidate.Kingdom);
            if (settler?.data == null) return false;

            try
            {
                pCity.setKingdom(candidate.Kingdom);
                settler.stopBeingWarrior();
                settler.joinCity(pCity);
                settler.setMetasFromCity(pCity);
                pCity.setUnitMetas(settler);
                candidate.Kingdom.setUnitMetas(settler);
                settler.cancelAllBeh();
                return true;
            }
            catch { return false; }
        }

        private static bool CanResettle(City pCity)
        {
            try
            {
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

        private static void DisposeEnumerator()
        {
            if (_cities is IDisposable disposable) disposable.Dispose();
            _cities = null;
        }
    }
}
