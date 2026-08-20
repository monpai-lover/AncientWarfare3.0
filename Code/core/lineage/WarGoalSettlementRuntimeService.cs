using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class WarGoalSettlementRuntimeService
    {
        private static SQLiteConnection DB =>
            LineageArchiveManager.Instance?.OperatingDB;

        public static bool OnCityControlChanged(War pWar, City pCity,
            Kingdom pController)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pWar.hasEnded() ||
                pCity?.data == null || pController?.data == null ||
                !IsAttacker(pWar, pController)) return false;
            Kingdom attacker = MainAttacker(pWar);
            if (attacker?.data == null ||
                !WarScoreService.TryGetSnapshot(pWar, attacker,
                    out WarScoreSnapshot snapshot)) return false;
            SQLiteConnection database = DB;
            if (database == null) return false;
            int completed = WarGoalPersistence.MarkCityControlCompleted(
                database,
                pWar.data.id, pCity.data.id, pCity.id, snapshot.Score,
                CurrentWorldTime(), snapshot.Revision);
            bool queued = QueueIfReady(pWar);
            return completed > 0 || queued;
        }

        public static bool QueueIfReady(War pWar)
        {
            if (!HasAffordableGoal(pWar)) return false;
            return WarTerminalSettlementCoordinator.NotifyWarChanged(pWar);
        }

        internal static bool HasAffordableGoal(War pWar)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pWar.hasEnded() ||
                ZhuluPeaceGuard.BlocksOrdinarySettlement(pWar) ||
                RebellionDirectTerritoryTransferService
                    .BlocksOrdinarySettlement(pWar) ||
                !DeJureWarGoalSettlementService.HasAffordableGoal(pWar) &&
                !TryBuildPlan(pWar, out _, out var facts,
                    out int expectedGoalCount)) return false;
            if (DeJureWarGoalSettlementService.HasAffordableGoal(pWar))
                return true;
            return WarGoalSettlementRules.TryValidateForceBundle(
                facts[0].AchievedScore, facts, expectedGoalCount, out _);
        }

        internal static bool TryExecuteImmediate(War pWar,
            out WarPeaceExecutionResult pResult)
        {
            pResult = new WarPeaceExecutionResult(false, -1,
                "war_goal_bundle_incomplete");
            if (DeJureWarGoalSettlementService.TryExecuteImmediate(pWar,
                    out pResult)) return true;
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pWar.hasEnded() ||
                !TryBuildPlan(pWar, out WarPeaceSettlementDraft draft,
                    out IReadOnlyList<WarGoalSettlementFacts> facts,
                    out int expectedGoalCount)) return false;
            if (!WarGoalSettlementRules.TryValidateForceBundle(
                    draft.SignedWarScore, facts, expectedGoalCount,
                    out _)) return false;

            pResult = WarPeaceSettlementService
                .Instance.ForceGoalSettlement(draft, facts,
                    expectedGoalCount);
            return pResult.Success;
        }

        private static bool TryBuildPlan(War pWar,
            out WarPeaceSettlementDraft pDraft,
            out IReadOnlyList<WarGoalSettlementFacts> pFacts,
            out int pExpectedGoalCount)
        {
            pDraft = null;
            pFacts = Array.Empty<WarGoalSettlementFacts>();
            pExpectedGoalCount = 0;
            SQLiteConnection database = DB;
            Kingdom attacker = MainAttacker(pWar);
            Kingdom defender = MainDefender(pWar);
            if (database == null || attacker?.data == null ||
                defender?.data == null ||
                !WarScoreService.TryGetSnapshot(pWar, attacker,
                    out WarScoreSnapshot score)) return false;
            IReadOnlyList<WarGoalSettlementSnapshot> goals =
                WarGoalPersistence.ReadOpenSettlementGoals(database,
                    pWar.data.id);
            if (goals.Count == 0 ||
                goals.Count > WarGoalSettlementRules.MaximumPersistedGoals)
                return false;

            var allFacts = new List<WarGoalSettlementFacts>(goals.Count);
            for (int i = 0; i < goals.Count; i++)
            {
                WarGoalSettlementSnapshot goal = goals[i];
                bool completed = goal.Completed ||
                    TryRepairCompletionFromFrozenOccupation(database, pWar,
                        goal, attacker, score);
                allFacts.Add(new WarGoalSettlementFacts(score.Score,
                    goal.WarGoalId, goal.RequiredWarScore, completed,
                    goal.WarGoalId));
            }
            int[] selected = WarGoalSettlementRules.
                SelectCompletedAffordableGoalIndices(score.Score, allFacts);
            if (selected.Length == 0) return false;

            var draft = new WarPeaceSettlementDraft
            {
                WarId = pWar.data.id,
                RequesterKingdomId = attacker.id,
                ResponderKingdomId = defender.id,
                SignedWarScore = score.Score,
                PlayerInitiated = false
            };
            var facts = new List<WarGoalSettlementFacts>(selected.Length);
            for (int i = 0; i < selected.Length; i++)
            {
                int goalIndex = selected[i];
                WarGoalSettlementSnapshot goal = goals[goalIndex];
                if (goal.AttackerKingdomId != attacker.id ||
                    goal.DefenderKingdomId != defender.id ||
                    goal.RequiredWarScore <= 0 ||
                    !TryBuildTerm(goal, attacker, defender,
                        out WarPeaceSettlementTermDraft term)) return false;
                draft.Terms.Add(term);
                WarGoalSettlementFacts selectedFacts = allFacts[goalIndex];
                facts.Add(new WarGoalSettlementFacts(score.Score,
                    selectedFacts.WarGoalId, selectedFacts.RequiredScore,
                    selectedFacts.GoalCompleted, term.WarGoalId));
            }
            pDraft = draft;
            pFacts = facts;
            pExpectedGoalCount = facts.Count;
            return true;
        }

        private static bool TryRepairCompletionFromFrozenOccupation(
            SQLiteConnection pDatabase, War pWar,
            WarGoalSettlementSnapshot pGoal, Kingdom pAttacker,
            WarScoreSnapshot pScore)
        {
            if (pGoal == null || pGoal.WarGoalId < 0 ||
                pGoal.TargetCityId < 0 || pAttacker?.data == null ||
                pGoal.CompletionKind != "city_control" &&
                pGoal.CompletionKind != "capital_control") return false;

            long frozenCityId = ResolveFrozenCityObjectId(pWar.data.id,
                pGoal.TargetCityId, pAttacker.id);
            if (frozenCityId < 0) return false;
            bool repaired = WarGoalPersistence.MarkGoalCompleted(pDatabase,
                pWar.data.id, pGoal.WarGoalId, pScore.Score,
                CurrentWorldTime(), pScore.Revision);
            if (repaired) pGoal.Completed = true;
            return repaired;
        }

        private static long ResolveFrozenCityObjectId(long pWarId,
            long pTargetCityId, long pAttackerKingdomId)
        {
            if (WarScoreService.TryGetFrozenOccupation(pWarId,
                    pTargetCityId, out long directController) &&
                directController == pAttackerKingdomId)
                return pTargetCityId;
            try
            {
                foreach (City city in World.world.cities)
                {
                    if (city?.data == null ||
                        city.id != pTargetCityId &&
                        city.data.id != pTargetCityId) continue;
                    if (WarScoreService.TryGetFrozenOccupation(pWarId,
                            city.id, out long controller) &&
                        controller == pAttackerKingdomId) return city.id;
                    break;
                }
            }
            catch { }
            return -1L;
        }

        private static bool TryBuildTerm(WarGoalSettlementSnapshot pGoal,
            Kingdom pAttacker, Kingdom pDefender,
            out WarPeaceSettlementTermDraft pTerm)
        {
            pTerm = null;
            if (!WarGoalSettlementRules.TryGetAutomaticSettlementProfile(
                    pGoal.GoalType, out var profile) ||
                pGoal.TargetCityId < 0) return false;
            WarPeaceTermKind kind;
            switch (profile.Effect)
            {
                case WarGoalAutomaticSettlementEffect.CedeCity:
                    kind = WarPeaceTermKind.CedeCity;
                    break;
                case WarGoalAutomaticSettlementEffect.ForceVassal:
                    kind = WarPeaceTermKind.ForceVassal;
                    break;
                case WarGoalAutomaticSettlementEffect.ForceTributary:
                    kind = WarPeaceTermKind.ForceTributary;
                    break;
                case WarGoalAutomaticSettlementEffect.TakeMandate:
                    kind = WarPeaceTermKind.TakeMandate;
                    break;
                case WarGoalAutomaticSettlementEffect.RestoreKingdom:
                    if (pGoal.SourceClaimId < 0) return false;
                    kind = WarPeaceTermKind.RestoreKingdom;
                    break;
                case WarGoalAutomaticSettlementEffect.Independence:
                    kind = WarPeaceTermKind.Independence;
                    break;
                case WarGoalAutomaticSettlementEffect.ReunifySuccession:
                    kind = WarPeaceTermKind.ReunifySuccession;
                    break;
                case WarGoalAutomaticSettlementEffect.NoCbOutcome:
                    kind = WarPeaceTermKind.NoCbOutcome;
                    break;
                default:
                    return false;
            }
            pTerm = new WarPeaceSettlementTermDraft
            {
                Kind = kind,
                RequestedCost = pGoal.RequiredWarScore,
                FromKingdomId = pDefender.id,
                ToKingdomId = pAttacker.id,
                CityId = pGoal.TargetCityId,
                ClaimId = kind == WarPeaceTermKind.RestoreKingdom
                    ? pGoal.SourceClaimId
                    : -1L,
                WarGoalId = pGoal.WarGoalId
            };
            return true;
        }

        private static bool IsAttacker(War pWar, Kingdom pKingdom)
        {
            try { return pWar.isAttacker(pKingdom); }
            catch { return false; }
        }

        private static Kingdom MainAttacker(War pWar)
        {
            try { return pWar?.getMainAttacker(); }
            catch { return null; }
        }

        private static Kingdom MainDefender(War pWar)
        {
            try { return pWar?.getMainDefender(); }
            catch { return null; }
        }

        private static double CurrentWorldTime()
        {
            try { return World.world?.getCurWorldTime() ?? -1d; }
            catch { return -1d; }
        }
    }
}
