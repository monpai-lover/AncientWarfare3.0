using AncientWarfare3.content.policies;

namespace AncientWarfare3.core.lineage
{
    internal static class RepublicGovernmentService
    {
        private static readonly System.Random Rng = new System.Random();
        private const int COMMONER_SCAN_LIMIT = 400;

        public static bool IsRepublic(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return false;
            pKingdom.data.get(LineageKeys.POLICY_CLASS_STATE, out string classState, "");
            return RepublicGovernmentRules.IsRepublicClass(classState);
        }

        public static bool IsRepublicLeader(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.REPUBLIC_LEADER, out bool leader, false);
            return leader;
        }

        /// <summary>
        ///     共和国推举首领:无世系继承人时,从本国平民里随机推举一名成年男性作为首领(选举、不世袭)。
        ///     由继承钩子在"无君主候选"时调用;标记 REPUBLIC_LEADER 使 setKing 不误清共和状态、死亡后重新推举。
        ///     无合格平民 → 返回 null(真·无首领,极少见)。
        /// </summary>
        public static Actor ElectCommonerLeader(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return null;
            SetRepublic(pKingdom);
            Actor leader = PickRandomCommoner(pKingdom);
            if (leader?.data == null) return null;
            leader.data.set(LineageKeys.REPUBLIC_LEADER, true);
            return leader;
        }

        /// <summary>本国平民中的均匀随机抽样(蓄水池抽样,单趟有界扫描)。</summary>
        private static Actor PickRandomCommoner(Kingdom pKingdom)
        {
            Actor chosen = null;
            int seen = 0;
            int scanned = 0;
            foreach (Actor unit in pKingdom.getUnits())
            {
                if (scanned++ >= COMMONER_SCAN_LIMIT) break;
                if (!IsEligibleCommoner(unit, pKingdom)) continue;
                seen++;
                if (Rng.Next(seen) == 0) chosen = unit; // 蓄水池:以 1/seen 概率替换
            }
            return chosen;
        }

        private static bool IsEligibleCommoner(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pActor.kingdom != pKingdom) return false;
            bool inSystem = LineageService.IsXia(pActor) || LineageService.UsesAwLineageSystem(pActor);
            return RepublicGovernmentRules.IsEligibleCommonerLeader(
                pInLineageSystem: inSystem,
                pIsMale: pActor.isSexMale(),
                pIsAdult: pActor.isAdult(),
                pIsAlive: !pActor.isRekt() && pActor.isAlive(),
                pIsSlave: SlaveService.IsSlave(pActor),
                pIsKing: pActor.isKing(),
                pIsNoble: ChronicleGate.IsNobleActor(pActor));
        }

        public static void RefreshAfterKingCheck(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return;

            if (pKingdom.hasKing())
            {
                // 共和推举的首领本身是"王",但不结束共和政体;仅世袭/城主复位的君主才清共和。
                if (!IsRepublicLeader(pKingdom.king))
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
