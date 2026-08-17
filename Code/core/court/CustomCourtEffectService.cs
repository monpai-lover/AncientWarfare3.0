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

        public IDictionary<CustomCourtEffectId, CustomCourtEffectModifier>
            AggregateModifiers(IEnumerable<CustomCourtOffice> offices,
                Func<CustomCourtOffice, bool> hasLivingIncumbent)
        {
            var grouped = new Dictionary<CustomCourtEffectId,
                List<CustomCourtOfficeEffect>>();
            foreach (CustomCourtOffice office in offices ??
                     Array.Empty<CustomCourtOffice>())
            {
                if (office == null || hasLivingIncumbent != null &&
                    !hasLivingIncumbent(office)) continue;
                foreach (CustomCourtOfficeEffect effect in office.Effects ??
                         new List<CustomCourtOfficeEffect>())
                {
                    if (effect == null || !CustomCourtEffectRules.IsPreset(
                            effect.Id, effect.Mode, effect.Scope)) continue;
                    if (!grouped.TryGetValue(effect.Id,
                            out List<CustomCourtOfficeEffect> list))
                    {
                        list = new List<CustomCourtOfficeEffect>();
                        grouped.Add(effect.Id, list);
                    }
                    list.Add(effect);
                }
            }

            var result = new Dictionary<CustomCourtEffectId,
                CustomCourtEffectModifier>();
            foreach (KeyValuePair<CustomCourtEffectId,
                         List<CustomCourtOfficeEffect>> pair in grouped)
                result[pair.Key] = CustomCourtEffectRules.Compose(pair.Value);
            return result;
        }
    }
}
