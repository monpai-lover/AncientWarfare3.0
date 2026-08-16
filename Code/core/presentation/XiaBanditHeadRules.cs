using System;

namespace AncientWarfare3.core.presentation
{
    internal static class XiaBanditHeadRules
    {
        internal const string XiaAssetId = "Xia";
        internal const string HeadDirectory = "heads_bandit";
        internal const int HeadCount = 5;

        internal static bool ShouldUse(string pAssetId, bool pBandit,
            bool pSynthetic)
        {
            return string.Equals(pAssetId, XiaAssetId,
                       StringComparison.Ordinal) && (pBandit || pSynthetic);
        }

        internal static int ResolveHeadIndex(long pActorId)
        {
            long value = pActorId < 0 ? -pActorId : pActorId;
            return (int)(value % HeadCount);
        }
    }
}
