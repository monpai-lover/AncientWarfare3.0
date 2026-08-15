using System.Collections.Generic;
using AncientWarfare3.ai.behaviours.actor;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using ai.behaviours;

namespace AncientWarfare3.content
{
    internal static class XiaExpansionDecisionContent
    {
        private const string NewCityDecisionId =
            "king_check_new_city_foundation";
        private const string ClaimLandDecisionId = "claim_land";
        private static DecisionAsset _installedNewCityDecision;
        private static DecisionActionWeight _installedNewCityCalculator;
        private static DecisionAsset _installedClaimLandDecision;
        private static DecisionActionWeight _installedClaimLandCalculator;

        public static void Init()
        {
            ConfigureNewCityDecision();
            ConfigureClaimLandDecision();
            ConfigureClaimLandTask();
        }

        private static void ConfigureClaimLandTask()
        {
            BehaviourTaskActor task = AssetManager.tasks_actor.get(
                ClaimLandDecisionId);
            if (task?.list == null)
            {
                ModClass.LogWarning(
                    "[Xia expansion] Missing actor task: " +
                    ClaimLandDecisionId);
                return;
            }

            var actionTypeNames = new List<string>(task.list.Count);
            for (int i = 0; i < task.list.Count; i++)
                actionTypeNames.Add(task.list[i]?.GetType().Name ??
                                    string.Empty);

            int insertionIndex = XiaExpansionDecisionRules
                .ClaimLandGuardInsertionIndex(actionTypeNames);
            if (insertionIndex == XiaExpansionDecisionRules
                    .ClaimLandGuardAlreadyInstalled)
                return;
            if (insertionIndex == XiaExpansionDecisionRules
                    .ClaimLandTaskIncompatible)
            {
                ModClass.LogWarning(
                    "[Xia expansion] Incompatible claim_land task shape; " +
                    "arrival guard not installed.");
                return;
            }

            var guard = new BehCivicLeaderClaimArrival();
            guard.id = guard.GetType().ToString();
            guard.id = guard.id.Replace("ai.behaviours.", string.Empty);
            guard.create();
            task.list.Insert(insertionIndex, guard);
        }

        private static void ConfigureNewCityDecision()
        {
            DecisionAsset decision = AssetManager.decisions_library.get(
                NewCityDecisionId);
            if (decision == null)
            {
                ModClass.LogWarning(
                    "[Xia expansion] Missing decision asset: " +
                    NewCityDecisionId);
                return;
            }

            if (ReferenceEquals(decision, _installedNewCityDecision) &&
                decision.weight_calculate_custom ==
                _installedNewCityCalculator)
                return;

            DecisionActionWeight originalCalculator = decision.weight_calculate_custom;
            float originalWeight = decision.weight;
            bool usedOriginalCalculator = decision.has_weight_custom && originalCalculator != null;

            _installedNewCityCalculator = pActor =>
            {
                float upstreamWeight = usedOriginalCalculator
                    ? originalCalculator(pActor)
                    : originalWeight;
                int cityCount = 0;
                try { cityCount = pActor?.kingdom?.countCities() ?? 0; }
                catch { }
                return XiaExpansionDecisionRules.ApplyWeight(
                    upstreamWeight, LineageService.IsXia(pActor), cityCount);
            };

            decision.weight_calculate_custom = _installedNewCityCalculator;
            decision.has_weight_custom = true;
            _installedNewCityDecision = decision;
        }

        private static void ConfigureClaimLandDecision()
        {
            DecisionAsset decision = AssetManager.decisions_library.get(
                ClaimLandDecisionId);
            if (decision == null)
            {
                ModClass.LogWarning(
                    "[Xia expansion] Missing decision asset: " +
                    ClaimLandDecisionId);
                return;
            }
            if (ReferenceEquals(decision, _installedClaimLandDecision) &&
                decision.weight_calculate_custom ==
                _installedClaimLandCalculator)
                return;

            DecisionActionWeight originalCalculator =
                decision.weight_calculate_custom;
            float originalWeight = decision.weight;
            bool usedOriginalCalculator = decision.has_weight_custom &&
                                          originalCalculator != null;
            _installedClaimLandCalculator = pActor =>
            {
                float upstreamWeight = usedOriginalCalculator
                    ? originalCalculator(pActor)
                    : originalWeight;
                City city = pActor?.city;
                if (city?.data == null)
                    return upstreamWeight;
                bool isXia = LineageService.IsXia(pActor);
                int allowance = CityTechService
                    .GetXiaCityZoneAllowance(city);
                bool belowAllowance = allowance == int.MaxValue ||
                                      city.countZones() < allowance;
                bool civicLeader = city.leader == pActor ||
                                   pActor.isKing();
                return XiaExpansionDecisionRules.ApplyClaimLandWeight(
                    upstreamWeight, isXia, belowAllowance, civicLeader);
            };

            decision.weight_calculate_custom = _installedClaimLandCalculator;
            decision.has_weight_custom = true;
            _installedClaimLandDecision = decision;
        }
    }
}
