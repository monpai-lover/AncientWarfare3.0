using System;

namespace AncientWarfare3.core.lineage
{
    internal static class WarPeaceResourceTransferService
    {
        public static int AvailableStockpileCapacity(City pCity,
            string pResourceId)
        {
            ResourceAsset resource = AssetManager.resources.get(
                pResourceId);
            if (pCity == null || resource == null ||
                pCity.stockpiles == null) return 0;
            long total = 0L;
            for (int i = 0; i < pCity.stockpiles.Count; i++)
            {
                Building stockpile = pCity.stockpiles[i];
                if (stockpile == null || !stockpile.isUsable()) continue;
                int stored = Math.Max(0,
                    stockpile.getResourcesAmount(pResourceId));
                total += Math.Max(0, resource.storage_max - stored);
                if (total >= int.MaxValue) return int.MaxValue;
            }
            return (int)total;
        }

        public static bool TryTransferExact(City pSource, City pTarget,
            string pResourceId, int pAmount, out string pReason)
        {
            pReason = string.Empty;
            if (pSource == null || pTarget == null || pAmount <= 0 ||
                AssetManager.resources.get(pResourceId) == null)
            {
                pReason = "invalid_resource_payment";
                return false;
            }
            int sourceBefore = pSource.getResourcesAmount(pResourceId);
            int targetBefore = pTarget.getResourcesAmount(pResourceId);
            int transferable = WarPeaceResourceTransferRules
                .TransferableAmount(pAmount, sourceBefore,
                    AvailableStockpileCapacity(pTarget, pResourceId));
            if (transferable != pAmount)
            {
                pReason = sourceBefore < pAmount
                    ? "payment_no_longer_available"
                    : "recipient_storage_full";
                return false;
            }

            int accepted = AddResourceAcrossStockpiles(pTarget,
                pResourceId, pAmount);
            if (accepted != pAmount ||
                pTarget.getResourcesAmount(pResourceId) !=
                targetBefore + pAmount)
            {
                RestoreAmount(pTarget, pResourceId, targetBefore);
                pReason = "recipient_storage_full";
                return false;
            }
            pSource.takeResource(pResourceId, pAmount);
            if (pSource.getResourcesAmount(pResourceId) ==
                sourceBefore - pAmount) return true;
            RestoreAmount(pTarget, pResourceId, targetBefore);
            RestoreAmount(pSource, pResourceId, sourceBefore);
            pReason = "payment_debit_failed";
            return false;
        }

        public static int AddResourceAcrossStockpiles(City pCity,
            string pResourceId, int pAmount)
        {
            ResourceAsset resource = AssetManager.resources.get(
                pResourceId);
            if (pCity == null || resource == null || pAmount <= 0 ||
                pCity.stockpiles == null) return 0;
            int remaining = pAmount;
            for (int i = 0; i < pCity.stockpiles.Count && remaining > 0;
                 i++)
            {
                Building stockpile = pCity.stockpiles[i];
                if (stockpile == null || !stockpile.isUsable()) continue;
                int stored = Math.Max(0,
                    stockpile.getResourcesAmount(pResourceId));
                int free = Math.Max(0, resource.storage_max - stored);
                if (free == 0) continue;
                int requested = Math.Min(remaining, free);
                int accepted = Math.Max(0,
                    stockpile.addResources(pResourceId, requested));
                remaining -= Math.Min(requested, accepted);
            }
            int added = pAmount - remaining;
            if (added > 0) pCity._storage_version++;
            return added;
        }

        public static void RestoreAmount(City pCity, string pResourceId,
            int pExpected)
        {
            if (pCity == null || pExpected < 0) return;
            int current = pCity.getResourcesAmount(pResourceId);
            if (current > pExpected)
                pCity.takeResource(pResourceId, current - pExpected);
            else if (current < pExpected)
                AddResourceAcrossStockpiles(pCity, pResourceId,
                    pExpected - current);
        }
    }
}
