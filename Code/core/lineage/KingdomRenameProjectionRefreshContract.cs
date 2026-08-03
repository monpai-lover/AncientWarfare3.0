using System;

namespace AncientWarfare3.core.lineage
{
    internal static class KingdomRenameProjectionRefreshContract
    {
        internal static void Apply(Action pRefreshMandateProjection,
            Action pInvalidateFamilyTreeCaches,
            Action pAdvanceFamilyTreeRevision)
        {
            pRefreshMandateProjection?.Invoke();
            pInvalidateFamilyTreeCaches?.Invoke();
            pAdvanceFamilyTreeRevision?.Invoke();
        }
    }
}
