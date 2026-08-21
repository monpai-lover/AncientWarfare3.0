using System;

namespace AncientWarfare3.core.policy
{
    internal static class HierarchicalVassalMapClickRules
    {
        internal static bool ShouldIntercept(
            bool pHierarchyActive,
            string pSelectedPowerId,
            string pHierarchyPowerId)
        {
            if (!pHierarchyActive) return false;
            if (string.IsNullOrEmpty(pSelectedPowerId)) return true;
            return string.Equals(pSelectedPowerId, pHierarchyPowerId,
                StringComparison.Ordinal);
        }
    }
}
