using System;
using System.Collections.Generic;
using UnityEngine;

namespace AncientWarfare3.core.lineage
{
    internal static class FeudatorySelectionService
    {
        public static bool CanExecuteGreatEnfeoffment(Kingdom pKingdom)
        {
            if (!IsMandateRealm(pKingdom)) return false;
            List<Actor> princes = GetPrinceCandidates(pKingdom);
            if (princes.Count == 0) return false;
            List<FeudatoryCityCandidate> cities = BuildCityCandidates(
                pKingdom, out _);
            return FeudatorySelectionRules.SelectSeat(cities) >= 0;
        }

        public static int ExecuteGreatEnfeoffment(Kingdom pKingdom)
        {
            if (!IsMandateRealm(pKingdom)) return 0;
            List<Actor> princes = GetPrinceCandidates(pKingdom);
            if (princes.Count == 0) return 0;
            List<FeudatoryCityCandidate> baseCandidates = BuildCityCandidates(
                pKingdom, out Dictionary<long, City> cityById);
            if (baseCandidates.Count == 0) return 0;

            var reserved = new HashSet<long>();
            int established = 0;
            for (int princeIndex = 0; princeIndex < princes.Count; princeIndex++)
            {
                List<FeudatoryCityCandidate> available =
                    BuildAvailableCandidates(baseCandidates, reserved);
                long seatId = FeudatorySelectionRules.SelectSeat(available);
                if (seatId < 0) break;

                int remainingCities = CountEligible(available);
                int remainingPrinces = princes.Count - princeIndex;
                int targetSize = Mathf.Clamp(
                    remainingCities / Math.Max(1, remainingPrinces), 1,
                    FeudatoryRules.MaximumCities);
                long[] selectedIds = FeudatorySelectionRules.SelectConnected(
                    available, seatId, targetSize);
                if (selectedIds.Length == 0) continue;

                var selectedCities = new List<City>(selectedIds.Length);
                for (int i = 0; i < selectedIds.Length; i++)
                    if (cityById.TryGetValue(selectedIds[i], out City city))
                        selectedCities.Add(city);
                if (selectedCities.Count == 0) continue;

                if (!FeudatoryService.TryEstablish(pKingdom, princes[princeIndex],
                        selectedCities, "great_enfeoffment", out _))
                    continue;
                established++;
                for (int i = 0; i < selectedIds.Length; i++)
                    reserved.Add(selectedIds[i]);
            }
            return established;
        }

        private static bool IsMandateRealm(Kingdom pKingdom)
        {
            return pKingdom?.data != null && !pKingdom.isRekt() &&
                   pKingdom.hasKing() &&
                   MandateService.GetCurrentMandateKingdom() == pKingdom;
        }

        private static List<Actor> GetPrinceCandidates(Kingdom pKingdom)
        {
            var result = new List<Actor>(
                FeudatoryRules.MaximumPrincesPerDecision);
            Actor king = pKingdom?.king;
            if (king?.data == null) return result;

            foreach (Actor child in king.getChildren(false))
            {
                if (result.Count >= FeudatoryRules.MaximumPrincesPerDecision)
                    break;
                if (child?.data == null || child.isRekt() ||
                    child.kingdom != pKingdom)
                    continue;
                bool alreadyPrince = FeudatoryService.TryGetByPrince(
                    child.data.id, out _);
                if (!FeudatoryRules.IsEligiblePrince(
                        pIsMandateDynast: true,
                        pAdult: child.isAdult(),
                        pMale: child.isSexMale(),
                        pKing: child.isKing(),
                        pHeir: HeirService.IsCurrentHeir(pKingdom, child),
                        pAlreadyPrince: alreadyPrince,
                        pValidRestorationState: true))
                    continue;
                result.Add(child);
            }
            result.Sort((left, right) => left.data.id.CompareTo(right.data.id));
            return result;
        }

        private static List<FeudatoryCityCandidate> BuildCityCandidates(
            Kingdom pKingdom, out Dictionary<long, City> pCityById)
        {
            pCityById = new Dictionary<long, City>();
            var result = new List<FeudatoryCityCandidate>();
            City capital = pKingdom?.capital;
            var capitalAdjacent = new HashSet<long>();
            if (capital?.neighbours_cities != null)
                foreach (City adjacent in capital.neighbours_cities)
                    if (adjacent?.data != null)
                        capitalAdjacent.Add(adjacent.id);

            foreach (City city in pKingdom?.getCities() ?? new List<City>())
            {
                if (city?.data == null) continue;
                pCityById[city.id] = city;
                bool assigned = FeudatoryService.TryGetByCity(city.id, out _);
                bool eligible = FeudatoryRules.CanAssignCity(
                    city.kingdom == pKingdom, !city.isRekt() && city.isAlive(),
                    city == capital, capitalAdjacent.Contains(city.id), assigned,
                    pConnected: true, pSelectedCount: 0);
                var neighborIds = new List<long>();
                if (city.neighbours_cities != null)
                    foreach (City neighbor in city.neighbours_cities)
                        if (neighbor?.data != null && neighbor.kingdom == pKingdom)
                            neighborIds.Add(neighbor.id);
                result.Add(new FeudatoryCityCandidate(city.id, eligible,
                    FrontierScore(city, pKingdom), neighborIds));
            }
            return result;
        }

        private static int FrontierScore(City pCity, Kingdom pKingdom)
        {
            int score = 0;
            if (pCity?.neighbours_kingdoms != null)
                foreach (Kingdom neighbor in pCity.neighbours_kingdoms)
                    if (neighbor?.data != null && !neighbor.isNeutral() &&
                        neighbor != pKingdom)
                    {
                        score += 1000;
                        break;
                    }
            try
            {
                score += Mathf.RoundToInt(Toolbox.DistVec2(
                    pKingdom.capital.getTile().pos, pCity.getTile().pos));
            }
            catch { }
            return score;
        }

        private static List<FeudatoryCityCandidate> BuildAvailableCandidates(
            IReadOnlyList<FeudatoryCityCandidate> pCandidates,
            HashSet<long> pReserved)
        {
            var result = new List<FeudatoryCityCandidate>(pCandidates.Count);
            for (int i = 0; i < pCandidates.Count; i++)
            {
                FeudatoryCityCandidate candidate = pCandidates[i];
                result.Add(new FeudatoryCityCandidate(candidate.CityId,
                    candidate.Eligible && !pReserved.Contains(candidate.CityId),
                    candidate.FrontierScore, candidate.NeighborIds));
            }
            return result;
        }

        private static int CountEligible(
            IReadOnlyList<FeudatoryCityCandidate> pCandidates)
        {
            int count = 0;
            for (int i = 0; i < pCandidates.Count; i++)
                if (pCandidates[i]?.Eligible == true) count++;
            return count;
        }
    }
}
