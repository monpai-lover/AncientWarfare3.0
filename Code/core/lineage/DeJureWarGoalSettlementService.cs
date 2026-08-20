using System;
using System.Collections.Generic;
using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    internal static class DeJureWarGoalSettlementService
    {
        internal static bool HasAffordableGoal(War pWar)
        {
            return TryBuildDraft(pWar, out _, out _);
        }

        internal static bool TryExecuteImmediate(War pWar,
            out WarPeaceExecutionResult pResult)
        {
            pResult = new WarPeaceExecutionResult(false, -1L,
                "de_jure_goal_not_ready");
            if (!TryBuildDraft(pWar, out WarPeaceSettlementDraft draft,
                    out long goalId)) return false;
            pResult = WarPeaceSettlementService.Instance
                .ForceDeJureRegionSettlement(draft, goalId);
            return pResult.Success;
        }

        internal static bool TryBuildDraft(War pWar,
            out WarPeaceSettlementDraft pDraft, out long pGoalId)
        {
            pDraft = null;
            pGoalId = -1L;
            if (pWar?.data == null || pWar.hasEnded()) return false;
            Kingdom attacker = pWar.getMainAttacker();
            Kingdom defender = pWar.getMainDefender();
            if (attacker?.data == null || defender?.data == null ||
                !WarScoreService.TryGetSnapshot(pWar, attacker,
                    out WarScoreSnapshot score) || score.Score <= 0)
                return false;
            WarGoalSettlementSnapshot goal = FindGoal(pWar.data.id);
            if (goal == null || goal.SourceDeJureRegionId < 0L ||
                !WarTerritoryService.TryGetDeJureRegion(
                    goal.SourceDeJureRegionId, out DeJureRegion region))
                return false;

            var cities = new List<City>();
            var candidates = new List<DeJureWarGoalCityCandidate>();
            foreach (long cityId in region.MemberCityIds ??
                     new List<long>())
            {
                City city = World.world?.cities?.get(cityId);
                if (city?.data == null || city.isRekt()) continue;
                bool defenderOwned = city.kingdom == defender;
                bool occupied = IsOccupiedByAttacker(pWar, city,
                    attacker.id);
                int cost = WarPeaceTermsRules.CityCessionCost(
                    WarPeaceSettlementWorld.CityFacts(city, attacker.id,
                        defender.id));
                cities.Add(city);
                candidates.Add(new DeJureWarGoalCityCandidate(city.data.id,
                    cost, pRegionMember: true, defenderOwned, occupied));
            }
            int[] selected = DeJureWarGoalSettlementRules.SelectAffordable(
                candidates, score.Score,
                WarPeaceSettlementValidationRules.MaximumTerms);
            if (selected.Length == 0) return false;

            var draft = new WarPeaceSettlementDraft
            {
                WarId = pWar.data.id,
                RequesterKingdomId = attacker.id,
                ResponderKingdomId = defender.id,
                SignedWarScore = score.Score,
                PlayerInitiated = false
            };
            for (int i = 0; i < selected.Length; i++)
            {
                int index = selected[i];
                draft.Terms.Add(new WarPeaceSettlementTermDraft
                {
                    Kind = WarPeaceTermKind.CedeCity,
                    RequestedCost = candidates[index].Cost,
                    FromKingdomId = defender.id,
                    ToKingdomId = attacker.id,
                    CityId = cities[index].data.id,
                    WarGoalId = goal.WarGoalId
                });
            }
            pDraft = draft;
            pGoalId = goal.WarGoalId;
            return true;
        }

        private static WarGoalSettlementSnapshot FindGoal(long pWarId)
        {
            var db = LineageArchiveManager.Instance?.OperatingDB;
            if (db == null) return null;
            IReadOnlyList<WarGoalSettlementSnapshot> goals =
                WarGoalPersistence.ReadOpenSettlementGoals(db, pWarId);
            for (int i = 0; i < goals.Count; i++)
                if (goals[i]?.GoalType ==
                    WarGoalTypeIds.TakeDeJureRegion) return goals[i];
            return null;
        }

        private static bool IsOccupiedByAttacker(War pWar, City pCity,
            long pAttackerId)
        {
            if (pCity?.data == null) return false;
            return WarScoreService.TryGetFrozenOccupation(pWar.data.id,
                       pCity.data.id, out long dataController) &&
                   dataController == pAttackerId ||
                   WarScoreService.TryGetFrozenOccupation(pWar.data.id,
                       pCity.id, out long objectController) &&
                   objectController == pAttackerId;
        }
    }
}
