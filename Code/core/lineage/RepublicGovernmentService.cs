using AncientWarfare3.content.policies;

namespace AncientWarfare3.core.lineage
{
    internal static class RepublicGovernmentService
    {
        public static bool IsRepublic(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            pKingdom.data.get(LineageKeys.POLICY_CLASS_STATE, out string classState, "");
            return RepublicGovernmentRules.IsRepublicClass(classState);
        }

        public static void RefreshAfterKingCheck(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return;

            if (pKingdom.hasKing())
            {
                ClearRepublic(pKingdom, "king_restored");
                return;
            }

            bool rebel = MandateRebelService.IsRebelKingdom(pKingdom);
            bool hasCandidate = HasMonarchyCandidate(pKingdom);
            if (!RepublicGovernmentRules.ShouldBecomeRepublic(
                    pIsCiv: true,
                    pIsRekt: false,
                    pHasKing: false,
                    pHasMonarchyCandidate: hasCandidate,
                    pIsRebelGovernment: rebel))
                return;

            SetRepublic(pKingdom);
        }

        public static void ClearRepublic(Kingdom pKingdom, string pReason)
        {
            if (pKingdom?.data == null || !IsRepublic(pKingdom)) return;
            pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, KingdomPolicyDefs.ClassDefault);
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.RULE_CHANGE,
                HistoryText.Kingdom(pKingdom) +
                HistoryText.PlainText(" \u91cd\u65b0\u62e5\u7acb\u541b\u4e3b\uff0c\u7ed3\u675f\u5171\u548c\u653f\u4f53"),
                HistoryTarget.Kingdom(pKingdom));
        }

        private static void SetRepublic(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || IsRepublic(pKingdom)) return;
            pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, KingdomPolicyDefs.ClassRepublic);
            HeirService.ClearHeir(pKingdom);
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.RULE_CHANGE,
                HistoryText.Kingdom(pKingdom) +
                HistoryText.PlainText(" \u5df2\u65e0\u53ef\u7acb\u4e4b\u541b\uff0c\u6539\u4e3a\u5171\u548c\u653f\u4f53"),
                HistoryTarget.Kingdom(pKingdom));
        }

        private static bool HasMonarchyCandidate(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            if (HeirService.GetHeir(pKingdom) != null) return true;
            return HeirService.GetLeaderSuccessionCandidate(pKingdom) != null;
        }
    }
}
