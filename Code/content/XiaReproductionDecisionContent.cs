using AncientWarfare3.core.lineage;

namespace AncientWarfare3.content
{
    internal static class XiaReproductionDecisionContent
    {
        private static DecisionActionWeight _originalWeight;
        private static bool _initialized;

        public static void Init()
        {
            if (_initialized) return;
            DecisionAsset decision = AssetManager.decisions_library
                .get("sexual_reproduction_try");
            if (decision == null) return;
            _originalWeight = decision.weight_calculate_custom;
            decision.weight_calculate_custom = pActor =>
            {
                float original = _originalWeight != null
                    ? _originalWeight(pActor)
                    : decision.weight;
                return DynasticReproductionService
                    .ReproductionDecisionWeight(pActor, original);
            };
            decision.has_weight_custom = true;
            _initialized = true;
        }
    }
}
