using System;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtEffectRules
    {
        public static float CombineAdditivePercent(float current,
            float addition)
        {
            if (float.IsNaN(current) || float.IsInfinity(current)) current = 0f;
            if (float.IsNaN(addition) || float.IsInfinity(addition)) addition = 0f;
            return current + addition;
        }

        public static float ClampValue(CustomCourtEffectId id, float value)
        {
            float bound = BoundFor(id);
            if (float.IsNaN(value) || float.IsNegativeInfinity(value))
                return -bound;
            if (float.IsPositiveInfinity(value))
                return bound;
            return Math.Max(-bound, Math.Min(bound, value));
        }

        public static bool CanApplyToScope(CustomCourtEffectId id,
            CustomCourtEffectScope scope)
        {
            switch (id)
            {
                case CustomCourtEffectId.TaxIncome:
                case CustomCourtEffectId.FoodProduction:
                case CustomCourtEffectId.CivilOrder:
                    return scope == CustomCourtEffectScope.Kingdom ||
                        scope == CustomCourtEffectScope.City;
                case CustomCourtEffectId.ArmyMorale:
                    return scope == CustomCourtEffectScope.Army ||
                        scope == CustomCourtEffectScope.Kingdom;
                case CustomCourtEffectId.CourtInfluence:
                    return scope == CustomCourtEffectScope.Court ||
                        scope == CustomCourtEffectScope.Kingdom;
                default:
                    return false;
            }
        }

        public static float BoundFor(CustomCourtEffectId id)
        {
            switch (id)
            {
                case CustomCourtEffectId.TaxIncome:
                case CustomCourtEffectId.FoodProduction:
                    return 25f;
                case CustomCourtEffectId.ArmyMorale:
                case CustomCourtEffectId.CivilOrder:
                    return 50f;
                case CustomCourtEffectId.CourtInfluence:
                    return 100f;
                default:
                    return 0f;
            }
        }

        public static bool IsPreset(CustomCourtEffectId id,
            CustomCourtEffectMode mode, CustomCourtEffectScope scope)
        {
            return Enum.IsDefined(typeof(CustomCourtEffectId), id) &&
                Enum.IsDefined(typeof(CustomCourtEffectMode), mode) &&
                CanApplyToScope(id, scope) &&
                (mode == CustomCourtEffectMode.AddPercent ||
                 mode == CustomCourtEffectMode.AddFlat ||
                 mode == CustomCourtEffectMode.Multiply);
        }
    }
}
