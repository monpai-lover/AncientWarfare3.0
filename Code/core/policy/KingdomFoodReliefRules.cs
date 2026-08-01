using System;

namespace AncientWarfare3.core.policy
{
    public static class KingdomFoodReliefRules
    {
        public const int MaxReliefCitiesPerYear = 8;
        public const int MaxKingdomTransferPerYear = 512;
        public const int MaxSingleTransfer = 128;

        public static int EmergencyTarget(int pPopulation)
        {
            return Math.Max(12, Math.Max(0, pPopulation) / 2);
        }

        public static int EmergencyTarget(int pPopulation,
            float pFamineResilience)
        {
            float resilience = Math.Max(0f,
                Math.Min(0.8f, pFamineResilience));
            int target = (int)Math.Ceiling(
                EmergencyTarget(pPopulation) * (1f - resilience));
            return Math.Max(12, target);
        }

        public static int DonorReserve(int pPopulation)
        {
            return Math.Max(24, Math.Max(0, pPopulation));
        }

        public static int TransferAmount(int receiverFood,
            int receiverPopulation, int donorFood, int donorPopulation,
            int remainingKingdomBudget,
            float pFamineResilience = 0f)
        {
            int deficit = EmergencyTarget(receiverPopulation,
                              pFamineResilience) -
                          Math.Max(0, receiverFood);
            int surplus = Math.Max(0, donorFood) -
                          DonorReserve(donorPopulation);
            return Math.Max(0, Math.Min(Math.Min(deficit, surplus),
                Math.Min(MaxSingleTransfer,
                    Math.Max(0, remainingKingdomBudget))));
        }

        public static int TransferBudget(int pBaseBudget,
            float pStorageMultiplier)
        {
            float multiplier = Math.Max(1f,
                Math.Min(1.5f, pStorageMultiplier));
            return Math.Max(0, (int)Math.Round(
                Math.Max(0, pBaseBudget) * multiplier));
        }
    }
}
