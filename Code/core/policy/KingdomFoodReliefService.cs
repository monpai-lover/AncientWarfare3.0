using System;
using System.Collections.Generic;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.policy
{
    internal static class KingdomFoodReliefService
    {
        private static readonly ResType[] FoodResourceTypes =
        {
            ResType.Food,
            ResType.Ingredient_Food
        };

        private sealed class CityFoodState
        {
            public City City;
            public int Population;
            public int Food;
        }

        private static readonly Dictionary<long, int> LastProcessedYear =
            new Dictionary<long, int>();

        public static void ClearRuntime()
        {
            LastProcessedYear.Clear();
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() ||
                pKingdom.cities == null || pKingdom.cities.Count < 2) return;
            int year = Date.getCurrentYear();
            if (LastProcessedYear.TryGetValue(pKingdom.id, out int lastYear) &&
                lastYear == year) return;
            LastProcessedYear[pKingdom.id] = year;

            var receivers = new List<CityFoodState>();
            var donors = new List<CityFoodState>();
            for (int i = 0; i < pKingdom.cities.Count; i++)
            {
                City city = pKingdom.cities[i];
                if (city?.data == null || city.isRekt() || !city.isAlive() ||
                    !city.hasStorages()) continue;
                if (!OccupiedCitySupplyService.CanProvideToRealm(
                        city, pKingdom)) continue;
                int population;
                int food;
                try
                {
                    population = Math.Max(0, city.getPopulationPeople());
                    food = Math.Max(0, city.countFoodTotal());
                }
                catch
                {
                    continue;
                }

                var state = new CityFoodState
                {
                    City = city,
                    Population = population,
                    Food = food
                };
                if (food < KingdomFoodReliefRules.EmergencyTarget(population))
                    receivers.Add(state);
                else if (food > KingdomFoodReliefRules.DonorReserve(population))
                    donors.Add(state);
            }

            int budget = KingdomFoodReliefRules.MaxKingdomTransferPerYear;
            int relievedCities = 0;
            int donorIndex = 0;
            for (int receiverIndex = 0;
                 receiverIndex < receivers.Count && donorIndex < donors.Count &&
                 budget > 0 && relievedCities <
                 KingdomFoodReliefRules.MaxReliefCitiesPerYear;
                 receiverIndex++)
            {
                CityFoodState receiver = receivers[receiverIndex];
                bool receivedAny = false;
                while (donorIndex < donors.Count && budget > 0)
                {
                    CityFoodState donor = donors[donorIndex];
                    int requested = KingdomFoodReliefRules.TransferAmount(
                        receiver.Food, receiver.Population, donor.Food,
                        donor.Population, budget);
                    if (requested <= 0)
                    {
                        donorIndex++;
                        continue;
                    }

                    int transferred = TransferFood(donor.City,
                        receiver.City, requested);
                    if (transferred <= 0)
                    {
                        donorIndex++;
                        continue;
                    }

                    donor.Food -= transferred;
                    receiver.Food += transferred;
                    budget -= transferred;
                    receivedAny = true;
                    if (receiver.Food >=
                        KingdomFoodReliefRules.EmergencyTarget(
                            receiver.Population)) break;
                    if (donor.Food <=
                        KingdomFoodReliefRules.DonorReserve(donor.Population))
                        donorIndex++;
                }
                if (receivedAny) relievedCities++;
            }
        }

        private static int TransferFood(City pDonor, City pReceiver,
            int pRequested)
        {
            if (pDonor == null || pReceiver == null || pRequested <= 0 ||
                !pReceiver.hasStockpiles()) return 0;
            int remaining = pRequested;
            int transferred = 0;
            using ListPool<CityStorageSlot> slots =
                pDonor.getTotalResourceSlots(FoodResourceTypes);
            for (int i = 0; i < slots.Count && remaining > 0; i++)
            {
                CityStorageSlot slot = slots[i];
                ResourceAsset resource = slot?.asset;
                if (resource == null || !resource.food || slot.amount <= 0)
                    continue;
                int amount = Math.Min(remaining, slot.amount);
                int before = pReceiver.getResourcesAmount(resource.id);
                pReceiver.addResourcesToRandomStockpile(resource.id, amount);
                int accepted = Math.Max(0,
                    pReceiver.getResourcesAmount(resource.id) - before);
                if (accepted <= 0) continue;
                pDonor.takeResource(resource.id, accepted);
                transferred += accepted;
                remaining -= accepted;
            }
            return transferred;
        }
    }
}
