using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.api.multiplayer;
using AncientWarfare3.core.db;

namespace AncientWarfare3.core.lineage
{
    internal static class WarGoalSettlementRuntimeService
    {
        private const string QueuePrefix = "war_goal_settlement:";
        private const int MaximumAttempts = 2;

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
            if (AW3MultiplayerReplicaScope.IsReplicaSession ||
                pWar?.data == null || pWar.hasEnded() ||
                RebellionDirectTerritoryTransferService.
                    BlocksOrdinarySettlement(pWar) ||
                WarPeaceSettlementService.Instance.HasActionableSettlement(
                    pWar.data.id) ||
                !TryBuildPlan(pWar, out _, out var facts,
                    out int expectedGoalCount)) return false;
            if (!WarGoalSettlementRules.TryValidateForceBundle(
                    facts[0].AchievedScore, facts, expectedGoalCount,
                    out _)) return false;
            Enqueue(pWar.data.id, 0);
            return true;
        }

        private static void Enqueue(long pWarId, int pAttempt)
        {
            DeferredRuntimeWorkService.EnqueueCoalesced(
                QueuePrefix + pWarId, DeferredWorkClass.Runtime,
                () => Process(pWarId, pAttempt));
        }

        private static void Process(long pWarId, int pAttempt)
        {
            if (AW3MultiplayerReplicaScope.IsReplicaSession) return;
            War war = WarPeaceSettlementWorld.FindWar(pWarId);
            if (war?.data == null || war.hasEnded() ||
                RebellionDirectTerritoryTransferService.
                    BlocksOrdinarySettlement(war) ||
                WarPeaceSettlementService.Instance.HasActionableSettlement(
                    pWarId) ||
                !TryBuildPlan(war, out WarPeaceSettlementDraft draft,
                    out IReadOnlyList<WarGoalSettlementFacts> facts,
                    out int expectedGoalCount)) return;
            if (!WarGoalSettlementRules.TryValidateForceBundle(
                    draft.SignedWarScore, facts, expectedGoalCount,
                    out _)) return;

            WarPeaceExecutionResult result = WarPeaceSettlementService
                .Instance.ForceGoalSettlement(draft, facts,
                    expectedGoalCount);
            if (result.Success || war?.data == null || war.hasEnded()) return;
            if (pAttempt < MaximumAttempts)
            {
                Enqueue(pWarId, pAttempt + 1);
                return;
            }
            ModClass.LogWarning("War-goal settlement failed war=" +
                                pWarId + " reason=" + result.Reason);
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

            var draft = new WarPeaceSettlementDraft
            {
                WarId = pWar.data.id,
                RequesterKingdomId = attacker.id,
                ResponderKingdomId = defender.id,
                SignedWarScore = score.Score,
                PlayerInitiated = false
            };
            var facts = new List<WarGoalSettlementFacts>(goals.Count);
            for (int i = 0; i < goals.Count; i++)
            {
                WarGoalSettlementSnapshot goal = goals[i];
                if (goal.AttackerKingdomId != attacker.id ||
                    goal.DefenderKingdomId != defender.id ||
                    goal.RequiredWarScore <= 0 || !goal.Completed ||
                    !TryBuildTerm(goal, attacker, defender,
                        out WarPeaceSettlementTermDraft term)) return false;
                draft.Terms.Add(term);
                facts.Add(new WarGoalSettlementFacts(score.Score,
                    goal.WarGoalId, goal.RequiredWarScore,
                    goal.Completed, term.WarGoalId));
            }
            pDraft = draft;
            pFacts = facts;
            pExpectedGoalCount = goals.Count;
            return true;
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
