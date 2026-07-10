using System.Collections.Generic;
using AncientWarfare3.content.policies;

namespace AncientWarfare3.core.lineage
{
    internal static class RepublicGovernmentService
    {
        private sealed class RankedCandidate
        {
            public Actor Actor;
            public RepublicCandidateScore Score;
        }

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

        public static bool IsRegisteredRepublicSuccessor(Kingdom pKingdom, Actor pActor)
        {
            if (!IsRepublic(pKingdom) || pActor?.data == null) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE, out string mode, SuccessionMode.NONE);
            return mode == SuccessionMode.REPUBLIC_ELECTIVE && heirId == pActor.data.id;
        }

        public static Actor GetRegisteredSuccessor(Kingdom pKingdom)
        {
            if (!IsRepublic(pKingdom)) return null;
            pKingdom.data.get(LineageKeys.KINGDOM_HEIR_ID, out long heirId, -1L);
            pKingdom.data.get(LineageKeys.KINGDOM_SUCCESSION_MODE, out string mode, SuccessionMode.NONE);
            if (heirId < 0 || mode != SuccessionMode.REPUBLIC_ELECTIVE) return null;
            Actor successor = World.world?.units?.get(heirId);
            return IsEligibleCandidate(successor, pKingdom) ? successor : null;
        }

        public static Actor ElectLeaderForVacancy(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return null;
            bool pending = SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king);
            bool wasRepublic = IsRepublic(pKingdom);
            Actor registered = wasRepublic ? GetRegisteredSuccessor(pKingdom) : null;
            List<RankedCandidate> ranked = RankCandidates(pKingdom, pExclude: null);

            if (!wasRepublic)
            {
                bool hasMonarchyHeir = HeirService.FindHeirReadOnly(pKingdom)?.data != null;
                if (!RepublicGovernmentRules.ShouldEnterRepublic(pending, hasMonarchyHeir, ranked.Count))
                    return null;
                SetRepublic(pKingdom);
            }
            else if (pending)
            {
                return null;
            }

            Actor leader = registered ?? (ranked.Count > 0 ? ranked[0].Actor : null);
            if (leader?.data == null) return null;
            MarkRepublicLeader(leader);
            RefreshRepublicSuccessor(pKingdom, leader);
            return leader;
        }

        public static void RefreshRepublicSuccessor(Kingdom pKingdom, Actor pCurrentLeader)
        {
            if (!IsRepublic(pKingdom)) return;
            List<RankedCandidate> ranked = RankCandidates(pKingdom, pCurrentLeader);
            Actor successor = ranked.Count > 0 ? ranked[0].Actor : null;
            HeirService.StoreSelectedHeir(pKingdom, successor, SuccessionMode.REPUBLIC_ELECTIVE);
        }

        public static void MarkRepublicLeader(Actor pActor)
        {
            if (pActor?.data == null) return;
            pActor.data.set(LineageKeys.REPUBLIC_LEADER, true);
        }

        public static void ClearRepublicLeader(Actor pActor)
        {
            if (pActor?.data == null || !IsRepublicLeader(pActor)) return;
            pActor.data.set(LineageKeys.REPUBLIC_LEADER, false);
        }

        public static void RefreshAfterKingCheck(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt() || !pKingdom.isCiv() || pKingdom.isNeutral()) return;
            if (SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king)) return;

            if (pKingdom.hasKing())
            {
                if (!IsRepublic(pKingdom)) return;
                if (!IsRepublicLeader(pKingdom.king))
                {
                    ClearRepublic(pKingdom, "king_restored");
                    return;
                }
                Actor successor = GetRegisteredSuccessor(pKingdom);
                if (RepublicGovernmentRules.ShouldRefreshSuccessorDuringStableReign(
                        successor?.data != null))
                    RefreshRepublicSuccessor(pKingdom, pKingdom.king);
                return;
            }

            if (MandateRebelService.IsRebelKingdom(pKingdom)) return;
            Actor leader = ElectLeaderForVacancy(pKingdom);
            if (leader?.data == null) return;
            MakeKingAndMoveToCapital(pKingdom, leader);
        }

        public static void ClearRepublic(Kingdom pKingdom, string pReason)
        {
            if (pKingdom?.data == null || !IsRepublic(pKingdom)) return;
            ClearRepublicLeader(pKingdom.king);
            pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, KingdomPolicyDefs.ClassDefault);
            HeirService.ClearHeir(pKingdom);
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

        private static void MakeKingAndMoveToCapital(Kingdom pKingdom, Actor pLeader)
        {
            if (pKingdom?.data == null || pLeader?.data == null) return;
            if (pLeader.hasCity())
            {
                pLeader.stopBeingWarrior();
                if (pLeader.isCityLeader()) pLeader.city.removeLeader();
            }
            if (pKingdom.hasCapital() && pLeader.city != pKingdom.capital)
                pLeader.joinCity(pKingdom.capital);
            pKingdom.setKing(pLeader);
            WorldLog.logNewKing(pKingdom);
        }

        private static List<RankedCandidate> RankCandidates(Kingdom pKingdom, Actor pExclude)
        {
            var result = new List<RankedCandidate>();
            if (pKingdom?.data == null) return result;
            foreach (Actor actor in pKingdom.getUnits())
            {
                if (actor == pExclude || !IsEligibleCandidate(actor, pKingdom)) continue;
                result.Add(new RankedCandidate
                {
                    Actor = actor,
                    Score = BuildScore(actor)
                });
            }
            result.Sort((a, b) => RepublicGovernmentRules.CompareCandidates(a.Score, b.Score));
            return result;
        }

        private static bool IsEligibleCandidate(Actor pActor, Kingdom pKingdom)
        {
            if (pActor?.data == null || pActor.kingdom != pKingdom) return false;
            bool inSystem = LineageService.IsXia(pActor) || LineageService.UsesAwLineageSystem(pActor);
            return RepublicGovernmentRules.IsEligibleLeader(
                pInLineageSystem: inSystem,
                pIsMale: pActor.isSexMale(),
                pIsAdult: pActor.isAdult(),
                pIsAlive: !pActor.isRekt() && pActor.isAlive(),
                pIsSlave: SlaveService.IsSlave(pActor),
                pIsKing: pActor.isKing());
        }

        private static RepublicCandidateScore BuildScore(Actor pActor)
        {
            return new RepublicCandidateScore(
                pActor.data.id,
                pActor.diplomacy,
                pActor.warfare,
                pActor.stewardship,
                pActor.level,
                CombatScore(pActor),
                SafeAge(pActor));
        }

        private static float CombatScore(Actor pActor)
        {
            if (pActor?.stats == null) return 0f;
            return SafeStat(pActor, "damage") + SafeStat(pActor, "warfare") * 2f +
                   SafeStat(pActor, "health") * 0.1f + SafeStat(pActor, "armor") * 2f +
                   SafeStat(pActor, "speed") * 0.25f;
        }

        private static float SafeStat(Actor pActor, string pKey)
        {
            try { return pActor.stats[pKey]; }
            catch { return 0f; }
        }

        private static int SafeAge(Actor pActor)
        {
            try { return pActor.getAge(); }
            catch { return 0; }
        }
    }
}
