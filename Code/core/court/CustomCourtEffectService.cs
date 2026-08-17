using System;
using System.Collections.Generic;

namespace AncientWarfare3.core.court
{
    public sealed class CustomCourtEffectService
    {
        public IDictionary<CustomCourtEffectId, float> Aggregate(
            IEnumerable<CustomCourtOffice> offices,
            Func<CustomCourtOffice, bool> hasLivingIncumbent)
        {
            var result = new Dictionary<CustomCourtEffectId, float>();
            if (offices == null)
                return result;
            foreach (CustomCourtOffice office in offices)
            {
                if (office == null || hasLivingIncumbent != null &&
                    !hasLivingIncumbent(office))
                    continue;
                foreach (CustomCourtOfficeEffect effect in office.Effects ??
                    new List<CustomCourtOfficeEffect>())
                {
                    if (!CustomCourtEffectRules.IsPreset(effect.Id, effect.Mode,
                            effect.Scope))
                        continue;
                    float current;
                    result.TryGetValue(effect.Id, out current);
                    float value = effect.Mode == CustomCourtEffectMode.Multiply
                        ? current * effect.Value
                        : CustomCourtEffectRules.CombineAdditivePercent(
                            current, effect.Value);
                    result[effect.Id] = CustomCourtEffectRules.ClampValue(
                        effect.Id, value);
                }
            }
            return result;
        }
    }
}
