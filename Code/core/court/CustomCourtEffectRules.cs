using System;

namespace AncientWarfare3.core.court
{
    public readonly struct CustomCourtEffectModifier
    {
        public static readonly CustomCourtEffectModifier Identity =
            new CustomCourtEffectModifier(0f, 0f, 1f, false);

        public CustomCourtEffectModifier(float additiveFlat,
            float additivePercent, float multiplicativeFactor,
            bool hasMultiplier = true)
        {
            AdditiveFlat = additiveFlat;
            AdditivePercent = additivePercent;
            MultiplicativeFactor = multiplicativeFactor;
            HasMultiplier = hasMultiplier;
        }

        public float AdditiveFlat { get; }
        public float AdditivePercent { get; }
        public float MultiplicativeFactor { get; }
        public bool HasMultiplier { get; }

        public bool IsIdentity => Math.Abs(AdditiveFlat) < 0.0001f &&
            Math.Abs(AdditivePercent) < 0.0001f &&
            (!HasMultiplier || Math.Abs(MultiplicativeFactor - 1f) < 0.0001f);

        public float Apply(float baseValue)
        {
            float safeBase = float.IsNaN(baseValue) ||
                             float.IsInfinity(baseValue) ? 0f : baseValue;
            float multiplier = HasMultiplier ? MultiplicativeFactor : 1f;
            return (safeBase + AdditiveFlat) *
                   (1f + AdditivePercent / 100f) * multiplier;
        }
    }

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

        public static CustomCourtEffectModifier Compose(
            System.Collections.Generic.IEnumerable<CustomCourtOfficeEffect>
                effects)
        {
            float flat = 0f;
            float percent = 0f;
            float multiplier = 1f;
            bool hasMultiplier = false;
            foreach (CustomCourtOfficeEffect effect in effects ??
                     Array.Empty<CustomCourtOfficeEffect>())
            {
                if (effect == null || !IsPreset(effect.Id, effect.Mode,
                        effect.Scope) ||
                    !CustomCourtTemplateRules.IsEffectValueValid(effect.Id,
                        effect.Mode, effect.Value)) continue;
                switch (effect.Mode)
                {
                    case CustomCourtEffectMode.AddFlat:
                        flat += effect.Value;
                        break;
                    case CustomCourtEffectMode.AddPercent:
                        percent += effect.Value;
                        break;
                    case CustomCourtEffectMode.Multiply:
                        multiplier *= effect.Value;
                        hasMultiplier = true;
                        break;
                }
            }
            flat = CustomCourtTemplateRules.ClampEffectValue(
                CustomCourtEffectMode.AddFlat, flat);
            percent = CustomCourtTemplateRules.ClampEffectValue(
                CustomCourtEffectMode.AddPercent, percent);
            multiplier = CustomCourtTemplateRules.ClampEffectValue(
                CustomCourtEffectMode.Multiply, multiplier);
            return new CustomCourtEffectModifier(flat, percent, multiplier,
                hasMultiplier);
        }

        public static float ApplyCivilOrder(float unrestRisk,
            CustomCourtEffectModifier modifier)
        {
            float safeUnrest = Math.Max(0f, Math.Min(100f, unrestRisk));
            float order = modifier.Apply(100f - safeUnrest);
            return 100f - Math.Max(0f, Math.Min(100f, order));
        }
    }
}
