using System;

namespace AncientWarfare3.core.lineage
{
    public static class WarPeaceResourceTransferRules
    {
        public static int TransferableAmount(int pRequestedAmount,
            int pSourceAmount, int pRecipientFreeCapacity)
        {
            if (pRequestedAmount <= 0 || pSourceAmount <= 0 ||
                pRecipientFreeCapacity <= 0) return 0;
            return Math.Min(pRequestedAmount,
                Math.Min(pSourceAmount, pRecipientFreeCapacity));
        }
    }
}
