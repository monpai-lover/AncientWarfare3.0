namespace AncientWarfare3.core.lineage
{
    internal static class VassalCaptureService
    {
        public static Kingdom ResolveCaptureRecipient(City pCity,
            Kingdom pCapturer)
        {
            if (pCapturer?.data == null) return pCapturer;
            Kingdom root = VassalService.GetRootSuzerain(pCapturer);
            if (root?.data == null || root == pCapturer) return pCapturer;
            bool formerOwnerIsSuzerain = pCity?.kingdom == root;
            bool independence = IsActiveIndependenceWar(pCapturer, root);
            return VassalCaptureRules.ShouldRedirectToRootSuzerain(
                    capturerIsVassal: true, formerOwnerIsSuzerain,
                    independence)
                ? root
                : pCapturer;
        }

        private static bool IsActiveIndependenceWar(Kingdom pVassal,
            Kingdom pSuzerain)
        {
            if (pVassal?.data == null || pSuzerain?.data == null) return false;
            pVassal.data.get(LineageKeys.VASSAL_INDEPENDENCE_WAR_ID,
                out long warId, -1L);
            if (warId < 0) return false;
            try
            {
                War war = World.world?.wars?.get(warId);
                return war?.data != null && !war.hasEnded() &&
                       war.isInWarWith(pVassal, pSuzerain);
            }
            catch { return false; }
        }
    }
}
