using System;

namespace AncientWarfare3.core.presentation
{
    internal static class XiaBanditHeadRules
    {
        internal const string XiaAssetId = "Xia";
        internal const string HeadResourcePrefix =
            "heads_bandit/head_bandit_";
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

        internal static string ResolveHeadPath(long pActorId)
        {
            return HeadResourcePrefix + ResolveHeadIndex(pActorId);
        }
    }
}
