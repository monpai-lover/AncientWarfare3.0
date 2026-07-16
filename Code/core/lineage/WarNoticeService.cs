using System;
using System.Collections.Generic;
using AncientWarfare3.content;
using AncientWarfare3.core.policy;

namespace AncientWarfare3.core.lineage
{
    internal sealed class WarNoticeState
    {
        public string Signature = "";
        public long AttackerId = -1L;
        public long DefenderId = -1L;
        public string GoalType = "";
        public string WarType = "";
        public long TargetCityId = -1L;
        public int NoticeYear = -1;
        public int EarliestWarYear = -1;
        public int ForcedWarYear = -1;
    }

    internal readonly struct WarPreparationSummary
    {
        public readonly long TargetKingdomId;
        public readonly int NoticeYear;
        public readonly int EarliestWarYear;
        public readonly int ForcedWarYear;
        public readonly int LevyCount;
        public readonly bool DeploymentReady;

        public WarPreparationSummary(long pTargetKingdomId, int pNoticeYear,
            int pEarliestWarYear, int pForcedWarYear, int pLevyCount,
            bool pDeploymentReady)
        {
            TargetKingdomId = pTargetKingdomId;
            NoticeYear = pNoticeYear;
            EarliestWarYear = pEarliestWarYear;
            ForcedWarYear = pForcedWarYear;
            LevyCount = pLevyCount;
            DeploymentReady = pDeploymentReady;
        }
    }

    internal static class WarNoticeService
    {
        private const string DeclareWarDecisionId = "aw_decision_declare_war";
        private static readonly Dictionary<string, WarNoticeState> Notices =
            new Dictionary<string, WarNoticeState>(StringComparer.Ordinal);
        private static readonly Dictionary<long, string> SummaryNoticeByKingdom =
            new Dictionary<long, string>();
        private static readonly Dictionary<long, HashSet<string>> NoticesByKingdom =
            new Dictionary<long, HashSet<string>>();
        private static readonly Dictionary<long, HashSet<string>> IncomingNoticesByKingdom =
            new Dictionary<long, HashSet<string>>();
        private static readonly Dictionary<string, HashSet<string>> NoticeSignaturesByPair =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private static readonly HashSet<long> PendingExpandedArmyIds = new HashSet<long>();

        public static void EnsureCurrentNotice(Kingdom pAttacker)
        {
            if (pAttacker?.data == null || pAttacker.isRekt()) return;
            pAttacker.data.get(LineageKeys.DECISION_CURRENT, out string current, "");
            if (current != DeclareWarDecisionId) return;

            Kingdom defender = DecisionTarget(pAttacker);
            if (defender?.data == null || defender.isRekt()) return;
            pAttacker.data.get(LineageKeys.DECISION_WAR_GOAL_TYPE, out string goalType, "");
            pAttacker.data.get(LineageKeys.DECISION_WAR_TYPE, out string warType, "");
            pAttacker.data.get(LineageKeys.DECISION_WAR_TARGET_CITY_ID, out long targetCityId, -1L);
            pAttacker.data.get(LineageKeys.DECISION_NOTICE_SIGNATURE, out string signature, "");

            if (string.IsNullOrEmpty(signature))
            {
                bool pairAlreadyAtWar = HasWar(pAttacker, defender);
                if (!WarNoticeRules.RequiresNotice(
                        LineageService.IsXiaKingdom(pAttacker),
                        XiaizationService.GetLevel(pAttacker),
                        deliberateDecision: true,
                        goalType,
                        warType,
                        joiningExistingWar: false,
                        pairAlreadyAtWar))
                    return;

                int year = Date.getCurrentYear();
                signature = WarNoticeRules.BuildSignature(pAttacker.id, defender.id, goalType,
                    targetCityId, year);
                pAttacker.data.set(LineageKeys.DECISION_NOTICE_SIGNATURE, signature);
                pAttacker.data.set(LineageKeys.DECISION_NOTICE_YEAR, year);
                pAttacker.data.set(LineageKeys.DECISION_NOTICE_EARLIEST_YEAR,
                    WarNoticeRules.EarliestWarYear(year));
                pAttacker.data.set(LineageKeys.DECISION_NOTICE_FORCED_YEAR,
                    WarNoticeRules.ForcedWarYear(year));
                pAttacker.data.set(LineageKeys.DECISION_NOTICE_RECORDED, false);
            }

            WarNoticeState state = ReadCurrentState(pAttacker, defender);
            if (state == null) return;
            if (Index(state))
            {
                TemporaryLevyService.OnEmergencyChanged(pAttacker);
                TemporaryLevyService.OnEmergencyChanged(defender);
                TemporarySlaveVanguardService.OnEmergencyChanged(pAttacker);
                TemporarySlaveVanguardService.OnEmergencyChanged(defender);
            }
            ArmyDeploymentService.ActivateNotice(state);

            pAttacker.data.get(LineageKeys.DECISION_NOTICE_RECORDED, out bool recorded, false);
            if (!recorded)
            {
                RecordNotice(pAttacker, defender, state);
                pAttacker.data.set(LineageKeys.DECISION_NOTICE_RECORDED, true);
            }
        }

        public static bool CanCompleteCurrentDeclaration(Kingdom pAttacker, float pProgress, float pCost)
        {
            if (pAttacker?.data == null) return true;
            EnsureCurrentNotice(pAttacker);
            pAttacker.data.get(LineageKeys.DECISION_NOTICE_SIGNATURE, out string signature, "");
            if (string.IsNullOrEmpty(signature)) return true;
            if (!Notices.TryGetValue(signature, out WarNoticeState state)) return true;

            Kingdom defender = ResolveKingdom(state.DefenderId);
            bool ready = defender?.data == null || defender.isRekt() ||
                         ArmyDeploymentService.AreAllRequiredArmiesReady(state);
            WarNoticeGate gate = WarNoticeRules.EvaluateGate(pProgress, pCost, Date.getCurrentYear(),
                state.EarliestWarYear, state.ForcedWarYear, ready);
            return gate == WarNoticeGate.Ready || gate == WarNoticeGate.Forced;
        }

        public static bool HasActiveNotice(Kingdom pKingdom)
        {
            return pKingdom?.data != null && NoticesByKingdom.TryGetValue(pKingdom.id, out HashSet<string> set) &&
                   set.Count > 0;
        }

        public static int ActiveNoticeCount(Kingdom pKingdom)
        {
            return pKingdom?.data != null && NoticesByKingdom.TryGetValue(pKingdom.id, out HashSet<string> set)
                ? set.Count
                : 0;
        }

        public static bool TryGetPreparationSummary(Kingdom pKingdom,
            out WarPreparationSummary pSummary)
        {
            pSummary = default;
            if (pKingdom?.data == null ||
                !SummaryNoticeByKingdom.TryGetValue(pKingdom.id, out string signature) ||
                !Notices.TryGetValue(signature, out WarNoticeState state)) return false;
            ArmyDeploymentService.TryGetCachedReadiness(state, out bool deploymentReady);
            long targetKingdomId = state.AttackerId == pKingdom.id
                ? state.DefenderId
                : state.AttackerId;
            pSummary = new WarPreparationSummary(targetKingdomId, state.NoticeYear,
                state.EarliestWarYear, state.ForcedWarYear,
                TemporaryLevyService.ActiveLevyCount(pKingdom), deploymentReady);
            return true;
        }

        public static string IncomingNoticeSignature(Kingdom pKingdom)
        {
            if (pKingdom?.data == null ||
                !IncomingNoticesByKingdom.TryGetValue(pKingdom.id, out HashSet<string> set))
                return "";
            foreach (string signature in set)
                if (Notices.ContainsKey(signature)) return signature;
            return "";
        }

        public static bool IsIncomingAttacker(Kingdom pDefender, long pAttackerId)
        {
            if (pDefender?.data == null ||
                !IncomingNoticesByKingdom.TryGetValue(pDefender.id, out HashSet<string> set))
                return false;
            foreach (string signature in set)
                if (Notices.TryGetValue(signature, out WarNoticeState state) &&
                    state.DefenderId == pDefender.id && state.AttackerId == pAttackerId)
                    return true;
            return false;
        }

        public static void OnArmyChanged(Kingdom pKingdom, Army pArmy, bool pRosterExpanded = true)
        {
            if (pKingdom?.data == null || pArmy?.data == null ||
                !IncomingNoticesByKingdom.TryGetValue(pKingdom.id, out HashSet<string> signatures) ||
                signatures.Count == 0) return;
            ArmyDeploymentService.OnArmyChanged(pKingdom, pArmy, pRosterExpanded);
        }

        public static void QueueArmyChanged(Kingdom pKingdom, Army pArmy,
            bool pRosterExpanded = false)
        {
            if (pKingdom?.data == null || pArmy?.data == null ||
                !IncomingNoticesByKingdom.TryGetValue(pKingdom.id, out HashSet<string> notices) ||
                notices.Count == 0) return;
            long kingdomId = pKingdom.id;
            long armyId = pArmy.id;
            if (pRosterExpanded) PendingExpandedArmyIds.Add(armyId);
            DeferredRuntimeWorkService.EnqueueCoalesced(
                DeferredRuntimeWorkRules.CoalescingKey("deployment_army_changed", armyId),
                DeferredWorkClass.Runtime, () => ProcessArmyChanged(kingdomId, armyId));
        }

        public static void OnArmyInvalidated(Kingdom pKingdom, long pArmyId)
        {
            if (pKingdom?.data == null || pArmyId < 0 ||
                !IncomingNoticesByKingdom.TryGetValue(pKingdom.id, out HashSet<string> signatures) ||
                signatures.Count == 0) return;
            ArmyDeploymentService.OnArmyInvalidated(pKingdom, pArmyId);
        }

        private static void ProcessArmyChanged(long pKingdomId, long pArmyId)
        {
            bool rosterExpanded = PendingExpandedArmyIds.Remove(pArmyId);
            Kingdom kingdom = ResolveKingdom(pKingdomId);
            if (kingdom?.data == null) return;
            Army army = ResolveArmy(pArmyId);
            if (army?.data == null)
            {
                OnArmyInvalidated(kingdom, pArmyId);
                return;
            }
            OnArmyChanged(kingdom, army, rosterExpanded);
        }

        public static string FindSignatureForWar(War pWar)
        {
            if (pWar?.data == null) return "";
            WarNoticeState state = FindPair(pWar.getMainAttacker()?.id ?? -1L,
                pWar.getMainDefender()?.id ?? -1L);
            return state?.Signature ?? "";
        }

        public static void OnKingdomYear(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            EnsureCurrentNotice(pKingdom);
        }

        public static void OnDecisionClearing(Kingdom pKingdom)
        {
            if (pKingdom?.data == null) return;
            pKingdom.data.get(LineageKeys.DECISION_NOTICE_SIGNATURE, out string signature, "");
            if (string.IsNullOrEmpty(signature) || IsQueued(pKingdom, signature)) return;
            Close(signature);
        }

        public static void OnWarStarted(War pWar)
        {
            if (pWar?.data == null) return;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            WarNoticeState state = FindPair(attacker?.id ?? -1L, defender?.id ?? -1L);
            if (state == null) return;
            ArmyDeploymentService.CancelNotice(state.Signature, restoreJobs: true);
            RemoveIndex(state);
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
            if (World.world?.kingdoms == null) return;
            foreach (Kingdom kingdom in World.world.kingdoms)
            {
                if (kingdom?.data == null || kingdom.isRekt()) continue;
                RebuildCurrent(kingdom);
                kingdom.data.get(LineageKeys.DECISION_QUEUE, out string raw, "");
                foreach (KingdomDecisionQueueItem item in KingdomDecisionQueueCodec.Decode(raw))
                {
                    WarNoticeState queued = FromQueueItem(kingdom.id, item);
                    if (queued == null) continue;
                    Index(queued);
                    ArmyDeploymentService.ActivateNotice(queued);
                }
            }
        }

        public static void ClearRuntime()
        {
            Notices.Clear();
            NoticesByKingdom.Clear();
            IncomingNoticesByKingdom.Clear();
            NoticeSignaturesByPair.Clear();
            SummaryNoticeByKingdom.Clear();
            PendingExpandedArmyIds.Clear();
            ArmyDeploymentService.ClearRuntime();
        }

        private static void RebuildCurrent(Kingdom pKingdom)
        {
            pKingdom.data.get(LineageKeys.DECISION_NOTICE_SIGNATURE, out string signature, "");
            if (string.IsNullOrEmpty(signature))
            {
                EnsureCurrentNotice(pKingdom);
                return;
            }
            Kingdom defender = DecisionTarget(pKingdom);
            WarNoticeState state = ReadCurrentState(pKingdom, defender);
            if (state == null) return;
            Index(state);
            ArmyDeploymentService.ActivateNotice(state);
        }

        private static WarNoticeState ReadCurrentState(Kingdom pAttacker, Kingdom pDefender)
        {
            if (pAttacker?.data == null || pDefender?.data == null) return null;
            pAttacker.data.get(LineageKeys.DECISION_NOTICE_SIGNATURE, out string signature, "");
            pAttacker.data.get(LineageKeys.DECISION_NOTICE_YEAR, out int noticeYear, -1);
            pAttacker.data.get(LineageKeys.DECISION_NOTICE_EARLIEST_YEAR, out int earliest, -1);
            pAttacker.data.get(LineageKeys.DECISION_NOTICE_FORCED_YEAR, out int forced, -1);
            pAttacker.data.get(LineageKeys.DECISION_WAR_GOAL_TYPE, out string goal, "");
            pAttacker.data.get(LineageKeys.DECISION_WAR_TYPE, out string warType, "");
            pAttacker.data.get(LineageKeys.DECISION_WAR_TARGET_CITY_ID, out long cityId, -1L);
            if (string.IsNullOrEmpty(signature) || noticeYear < 0 || earliest < 0 || forced < 0) return null;
            return new WarNoticeState
            {
                Signature = signature,
                AttackerId = pAttacker.id,
                DefenderId = pDefender.id,
                GoalType = goal ?? "",
                WarType = warType ?? "",
                TargetCityId = cityId,
                NoticeYear = noticeYear,
                EarliestWarYear = earliest,
                ForcedWarYear = forced
            };
        }

        private static WarNoticeState FromQueueItem(long pAttackerId, KingdomDecisionQueueItem pItem)
        {
            if (pItem == null || string.IsNullOrEmpty(pItem.notice_signature) || pItem.notice_year < 0 ||
                pItem.target_kingdom_id < 0) return null;
            return new WarNoticeState
            {
                Signature = pItem.notice_signature,
                AttackerId = pAttackerId,
                DefenderId = pItem.target_kingdom_id,
                GoalType = pItem.war_goal_type ?? "",
                WarType = pItem.war_type ?? "",
                TargetCityId = pItem.war_target_city_id,
                NoticeYear = pItem.notice_year,
                EarliestWarYear = pItem.earliest_war_year,
                ForcedWarYear = pItem.forced_war_year
            };
        }

        private static bool Index(WarNoticeState pState)
        {
            if (pState == null || string.IsNullOrEmpty(pState.Signature)) return false;
            bool added = !Notices.ContainsKey(pState.Signature);
            Notices[pState.Signature] = pState;
            string pairKey = PairKey(pState.AttackerId, pState.DefenderId);
            if (!NoticeSignaturesByPair.TryGetValue(pairKey, out HashSet<string> pairSignatures))
            {
                pairSignatures = new HashSet<string>(StringComparer.Ordinal);
                NoticeSignaturesByPair[pairKey] = pairSignatures;
            }
            pairSignatures.Add(pState.Signature);
            AddKingdomIndex(pState.AttackerId, pState.Signature);
            AddKingdomIndex(pState.DefenderId, pState.Signature);
            AddIncomingIndex(pState.DefenderId, pState.Signature);
            if (added) RefreshPreparationSummaries(pState);
            return added;
        }

        private static void RefreshPreparationSummaries(WarNoticeState pState)
        {
            if (pState == null) return;
            RefreshSummaryNotice(pState.AttackerId);
            RefreshSummaryNotice(pState.DefenderId);
        }

        private static void AddIncomingIndex(long pKingdomId, string pSignature)
        {
            if (pKingdomId < 0) return;
            if (!IncomingNoticesByKingdom.TryGetValue(pKingdomId, out HashSet<string> set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                IncomingNoticesByKingdom[pKingdomId] = set;
            }
            set.Add(pSignature);
        }

        private static void AddKingdomIndex(long pKingdomId, string pSignature)
        {
            if (pKingdomId < 0) return;
            if (!NoticesByKingdom.TryGetValue(pKingdomId, out HashSet<string> set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                NoticesByKingdom[pKingdomId] = set;
            }
            set.Add(pSignature);
        }

        private static void Close(string pSignature)
        {
            if (string.IsNullOrEmpty(pSignature) || !Notices.TryGetValue(pSignature, out WarNoticeState state))
                return;
            RemoveIndex(state);
            ArmyDeploymentService.CancelNotice(pSignature, restoreJobs: true);
            TemporaryLevyService.OnNoticeClosed(state.AttackerId);
            TemporaryLevyService.OnNoticeClosed(state.DefenderId);
            TemporarySlaveVanguardService.OnEmergencyChanged(ResolveKingdom(state.AttackerId));
            TemporarySlaveVanguardService.OnEmergencyChanged(ResolveKingdom(state.DefenderId));
        }

        private static void RemoveIndex(WarNoticeState pState)
        {
            if (pState == null) return;
            Notices.Remove(pState.Signature);
            string pairKey = PairKey(pState.AttackerId, pState.DefenderId);
            if (NoticeSignaturesByPair.TryGetValue(pairKey, out HashSet<string> pairSignatures))
            {
                pairSignatures.Remove(pState.Signature);
                if (pairSignatures.Count == 0) NoticeSignaturesByPair.Remove(pairKey);
            }
            RemoveKingdomIndex(pState.AttackerId, pState.Signature);
            RemoveKingdomIndex(pState.DefenderId, pState.Signature);
            RemoveIncomingIndex(pState.DefenderId, pState.Signature);
            RefreshSummaryNotice(pState.AttackerId);
            RefreshSummaryNotice(pState.DefenderId);
        }

        private static void RefreshSummaryNotice(long pKingdomId)
        {
            if (!NoticesByKingdom.TryGetValue(pKingdomId, out HashSet<string> signatures) ||
                signatures.Count == 0)
            {
                SummaryNoticeByKingdom.Remove(pKingdomId);
                return;
            }

            WarNoticeState selected = null;
            foreach (string signature in signatures)
            {
                if (!Notices.TryGetValue(signature, out WarNoticeState candidate)) continue;
                if (selected == null || ArmyDeploymentRules.CompareNoticePriority(
                        candidate.EarliestWarYear, candidate.NoticeYear, candidate.Signature,
                        selected.EarliestWarYear, selected.NoticeYear, selected.Signature) < 0)
                    selected = candidate;
            }
            if (selected == null)
                SummaryNoticeByKingdom.Remove(pKingdomId);
            else
                SummaryNoticeByKingdom[pKingdomId] = selected.Signature;
        }

        private static void RemoveIncomingIndex(long pKingdomId, string pSignature)
        {
            if (!IncomingNoticesByKingdom.TryGetValue(pKingdomId, out HashSet<string> set)) return;
            set.Remove(pSignature);
            if (set.Count == 0) IncomingNoticesByKingdom.Remove(pKingdomId);
        }

        private static void RemoveKingdomIndex(long pKingdomId, string pSignature)
        {
            if (!NoticesByKingdom.TryGetValue(pKingdomId, out HashSet<string> set)) return;
            set.Remove(pSignature);
            if (set.Count == 0) NoticesByKingdom.Remove(pKingdomId);
        }

        private static WarNoticeState FindPair(long pAttackerId, long pDefenderId)
        {
            Kingdom attacker = ResolveKingdom(pAttackerId);
            if (attacker?.data != null)
            {
                attacker.data.get(LineageKeys.DECISION_NOTICE_SIGNATURE, out string currentSignature, "");
                if (!string.IsNullOrEmpty(currentSignature) &&
                    Notices.TryGetValue(currentSignature, out WarNoticeState current) &&
                    current.AttackerId == pAttackerId && current.DefenderId == pDefenderId)
                    return current;
            }
            if (!NoticeSignaturesByPair.TryGetValue(
                    PairKey(pAttackerId, pDefenderId), out HashSet<string> signatures)) return null;
            foreach (string signature in signatures)
                if (Notices.TryGetValue(signature, out WarNoticeState state)) return state;
            return null;
        }

        private static string PairKey(long pAttackerId, long pDefenderId)
        {
            return pAttackerId + ":" + pDefenderId;
        }

        private static bool IsQueued(Kingdom pKingdom, string pSignature)
        {
            pKingdom.data.get(LineageKeys.DECISION_QUEUE, out string raw, "");
            foreach (KingdomDecisionQueueItem item in KingdomDecisionQueueCodec.Decode(raw))
                if (item.notice_signature == pSignature) return true;
            return false;
        }

        private static Kingdom DecisionTarget(Kingdom pAttacker)
        {
            if (pAttacker?.data == null) return null;
            pAttacker.data.get(LineageKeys.DECISION_TARGET_KINGDOM_ID, out long targetId, -1L);
            return ResolveKingdom(targetId);
        }

        private static Kingdom ResolveKingdom(long pId)
        {
            if (pId < 0) return null;
            try { return World.world?.kingdoms?.get(pId); }
            catch { return null; }
        }

        private static Army ResolveArmy(long pId)
        {
            if (pId < 0) return null;
            try { return World.world?.armies?.get(pId); }
            catch { return null; }
        }

        private static bool HasWar(Kingdom pAttacker, Kingdom pDefender)
        {
            try { return World.world?.wars?.getWar(pAttacker, pDefender, pOnlyMain: false) != null; }
            catch { return false; }
        }

        private static void RecordNotice(Kingdom pAttacker, Kingdom pDefender, WarNoticeState pState)
        {
            WarMobilizationContent.AnnounceNotice(pAttacker, pDefender);
            HistoryWriter.RecordKingdom(pAttacker, "war_notice_issued",
                HistoryText.Kingdom(pAttacker) + HistoryLocalizationRules.H("aw_hist_war_notice_issued_mid") +
                HistoryText.Kingdom(pDefender), HistoryTarget.Kingdom(pDefender));
            HistoryWriter.RecordKingdom(pDefender, "war_notice_received",
                HistoryText.Kingdom(pDefender) + HistoryLocalizationRules.H("aw_hist_war_notice_received_mid") +
                HistoryText.Kingdom(pAttacker), HistoryTarget.Kingdom(pAttacker));
        }
    }
}
