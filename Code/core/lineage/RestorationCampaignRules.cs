using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.lineage
{
    public enum RestorationCampaignAction
    {
        Continue,
        Complete,
        Fail
    }

    public static class RestorationCampaignRules
    {
        public const int AiRetryCooldownYears = 10;
        public const int MaxPersistedCoreIds = 4096;

        public static bool CooldownReady(int currentYear, int lastAttemptYear,
            bool playerRequested)
        {
            if (lastAttemptYear < 0) return true;
            int required = playerRequested ? 1 : AiRetryCooldownYears;
            return currentYear - lastAttemptYear >= required;
        }

        public static string EncodeCoreIds(IEnumerable<long> pCoreIds)
        {
            if (pCoreIds == null) return "";
            var unique = new HashSet<long>();
            foreach (long id in pCoreIds)
            {
                if (id < 0 || unique.Count >= MaxPersistedCoreIds) continue;
                unique.Add(id);
            }
            var ordered = new List<long>(unique);
            ordered.Sort();
            return string.Join(",", ordered);
        }

        public static List<long> DecodeCoreIds(string pRaw)
        {
            var result = new List<long>();
            if (string.IsNullOrEmpty(pRaw)) return result;
            var seen = new HashSet<long>();
            foreach (string part in pRaw.Split(','))
            {
                if (result.Count >= MaxPersistedCoreIds) break;
                if (!long.TryParse(part, out long id) || id < 0 || !seen.Add(id)) continue;
                result.Add(id);
            }
            result.Sort();
            return result;
        }

        public static int NextCoreCursor(int currentCursor, int inspectedCount,
            int totalCoreCount)
        {
            if (totalCoreCount <= 0) return 0;
            int cursor = Math.Max(0, currentCursor) % totalCoreCount;
            int inspected = Math.Max(0, inspectedCount);
            return (cursor + inspected) % totalCoreCount;
        }

        public static RestorationCampaignAction ResolveAction(
            bool restoredKingdomAlive,
            bool claimantAlive,
            bool recoveredThreshold)
        {
            if (!restoredKingdomAlive)
                return RestorationCampaignAction.Fail;
            return recoveredThreshold
                ? RestorationCampaignAction.Complete
                : RestorationCampaignAction.Continue;
        }

        public static int AdjustControlledCoreCount(
            int currentControlled,
            int totalCores,
            bool isCampaignCore,
            bool oldOwnerWasRestored,
            bool newOwnerIsRestored)
        {
            int upper = Math.Max(0, totalCores);
            int current = Math.Max(0, Math.Min(currentControlled, upper));
            if (!isCampaignCore || oldOwnerWasRestored == newOwnerIsRestored)
                return current;
            int adjusted = current + (newOwnerIsRestored ? 1 : -1);
            return Math.Max(0, Math.Min(adjusted, upper));
        }
    }
}
