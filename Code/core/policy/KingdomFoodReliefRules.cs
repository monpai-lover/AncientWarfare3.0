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

        public static int DonorReserve(int pPopulation)
        {
            return Math.Max(24, Math.Max(0, pPopulation));
        }

        public static int TransferAmount(int receiverFood,
            int receiverPopulation, int donorFood, int donorPopulation,
            int remainingKingdomBudget)
        {
            int deficit = EmergencyTarget(receiverPopulation) -
                          Math.Max(0, receiverFood);
            int surplus = Math.Max(0, donorFood) -
                          DonorReserve(donorPopulation);
            return Math.Max(0, Math.Min(Math.Min(deficit, surplus),
                Math.Min(MaxSingleTransfer,
                    Math.Max(0, remainingKingdomBudget))));
        }
    }
}
