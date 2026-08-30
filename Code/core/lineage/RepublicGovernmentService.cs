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

        public static bool IsRepublicLeader(Actor pActor)
        {
            if (pActor?.data == null) return false;
            pActor.data.get(LineageKeys.REPUBLIC_LEADER, out bool leader, false);
            return leader;
        }

        public static bool HasEstablishedMonarchy(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return false;
            pKingdom.data.get(LineageKeys.KINGDOM_MONARCHY_ESTABLISHED, out bool established, false);
            return established;
        }

        public static void MarkMonarchyEstablished(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.KINGDOM_MONARCHY_ESTABLISHED, true);
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

        public static Actor ResolveRulerForVacancy(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || pKingdom.isRekt()) return null;
            bool pending = SuccessionTransitionRules.IsPending(pKingdom.data.timer_new_king);
            if (pending) return null;

            bool wasRepublic = IsRepublic(pKingdom);
            if (wasRepublic)
            {
                Actor registered = GetRegisteredSuccessor(pKingdom);
                Actor elected = registered ??
                                SelectBestCandidate(pKingdom, pExclude: null, out _);
                return PrepareRepublicLeader(pKingdom, elected);
            }

            Actor hereditaryHeir = HeirService.ReconcileHeir(pKingdom, pForce: false);
            if (hereditaryHeir?.data != null) return hereditaryHeir;

            bool monarchyEstablished = HasEstablishedMonarchy(pKingdom);
            if (!monarchyEstablished)
            {
                Actor founder = HeirService.GetLeaderSuccessionCandidate(pKingdom);
                HeirService.MarkLeaderFallbackSuccession(pKingdom, founder);
                return founder;
            }

            Actor houseRuler = AristocraticSuccessionService.SelectRuler(pKingdom);
            if (houseRuler?.data != null)
            {
                HeirService.MarkClanFallbackSuccession(pKingdom, houseRuler);
                return houseRuler;
            }

            Actor leader = SelectBestCandidate(pKingdom, pExclude: null,
                out int electableCount);
            AristocraticVacancyDecision decision = AristocraticSuccessionRules.DecideVacancy(
                successionPending: false,
                hasHereditaryHeir: false,
                hasHouseCandidate: false,
                electableCount: electableCount,
                monarchyEstablished: true);
            if (decision != AristocraticVacancyDecision.ElectRepublic) return null;

            SetRepublic(pKingdom);
            return PrepareRepublicLeader(pKingdom, leader);
        }

        public static void RefreshRepublicSuccessor(Kingdom pKingdom, Actor pCurrentLeader)
        {
            if (!IsRepublic(pKingdom)) return;
            // 上一次一个人都没推举出来的话别每跳重扫 —— 那个状态注定还是找不到人,
            // 而 ShouldRefreshSuccessorDuringStableReign 恰恰会因为「没有继任者」
            // 一直把我们叫回来。见 RepublicElectorateRules.ShouldRescanEmptyElectorate。
            int currentYear = SafeCurrentYear();
            pKingdom.data.get(LineageKeys.REPUBLIC_EMPTY_ELECTORATE_YEAR,
                out int memoYear, int.MinValue);
            if (!RepublicElectorateRules.ShouldRescanEmptyElectorate(
                    memoYear != int.MinValue, memoYear, currentYear)) return;

            Actor successor = SelectBestCandidate(pKingdom, pCurrentLeader, out _);
            if (successor == null)
                pKingdom.data.set(LineageKeys.REPUBLIC_EMPTY_ELECTORATE_YEAR,
                    RepublicElectorateRules.MemoYearFor(currentYear));
            else
                pKingdom.data.set(LineageKeys.REPUBLIC_EMPTY_ELECTORATE_YEAR,
                    int.MinValue);
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
            Actor leader = ResolveRulerForVacancy(pKingdom);
            if (leader?.data == null) return;
            MakeKingAndMoveToCapital(pKingdom, leader);
        }

        private static Actor PrepareRepublicLeader(Kingdom pKingdom, Actor pLeader)
        {
            if (pLeader?.data == null) return null;
            MarkRepublicLeader(pLeader);
            // 换了首领,选民团就变了(新首领本人退出候选),空结果的记忆作废。
            InvalidateElectorateMemo(pKingdom);
            RefreshRepublicSuccessor(pKingdom, pLeader);
            return pLeader;
        }

        /// <summary>
        /// 作废「上次一个人都没推举出来」的记忆,让下一次调用真的重扫一遍。
        /// 政体或首领刚变过就该调它 —— 那两件事都会改变候选集合。
        /// </summary>
        private static void InvalidateElectorateMemo(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.set(LineageKeys.REPUBLIC_EMPTY_ELECTORATE_YEAR, int.MinValue);
        }

        private static int SafeCurrentYear()
        {
            try { return Date.getCurrentYear(); }
            catch { return -1; }
        }

        public static void ClearRepublic(Kingdom pKingdom, string pReason)
        {
            if (pKingdom?.data == null || !IsRepublic(pKingdom)) return;
            ClearRepublicLeader(pKingdom.king);
            pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, KingdomPolicyDefs.ClassDefault);
            MarkMonarchyEstablished(pKingdom);
            HeirService.ClearHeir(pKingdom);
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.RULE_CHANGE,
                HistoryText.Kingdom(pKingdom) +
                HistoryLocalizationRules.H("aw_hist_republic_ended"),
                HistoryTarget.Kingdom(pKingdom));
            if (KingdomTitleService.IsEmperor(pKingdom) && pKingdom.king?.data != null)
                YearNameService.TryStartRestoredMonarchyEra(pKingdom, pKingdom.king);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
        }

        private static void SetRepublic(Kingdom pKingdom)
        {
            if (pKingdom?.data == null || IsRepublic(pKingdom)) return;
            HistoryWriter.RecordKingdom(pKingdom, KingdomEvent.RULE_CHANGE,
                HistoryText.Kingdom(pKingdom) +
                HistoryLocalizationRules.H("aw_hist_republic_established"),
                HistoryTarget.Kingdom(pKingdom));
            pKingdom.data.set(LineageKeys.POLICY_CLASS_STATE, KingdomPolicyDefs.ClassRepublic);
            InvalidateElectorateMemo(pKingdom);
            YearNameService.EndMonarchicalChronology(pKingdom);
            HeirService.ClearHeir(pKingdom);
            RulerAppellationService.RefreshLivingProjection(pKingdom);
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

        /// <summary>
        /// 推举出最优候选。三个调用点要的都只是第一名和「有没有人」,所以这里
        /// 一趟扫出最大值就够了 —— <c>CompareCandidates</c> 末项按唯一的 actor id
        /// 定胜负,是全序,一趟取最大与排完取第一恒等。见
        /// <see cref="RepublicElectorateRules"/>。
        /// </summary>
        private static Actor SelectBestCandidate(Kingdom pKingdom, Actor pExclude,
            out int pElectableCount)
        {
            pElectableCount = 0;
            if (pKingdom?.data == null) return null;
            Actor best = null;
            RepublicCandidateScore bestScore = default;
            foreach (Actor actor in pKingdom.getUnits())
            {
                if (actor == pExclude || !IsEligibleCandidate(actor, pKingdom)) continue;
                pElectableCount++;
                RepublicCandidateScore score = BuildScore(actor);
                if (best == null ||
                    RepublicGovernmentRules.CompareCandidates(score, bestScore) < 0)
                {
                    best = actor;
                    bestScore = score;
                }
            }

            return best;
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
