using System;
using System.Collections.Generic;
using System.Linq;

namespace AncientWarfare3.core.court
{
    public static class CustomCourtOfficeSettingsRules
    {
        private static readonly string[] Layers =
        {
            CourtOfficeLayer.Primitive,
            CourtOfficeLayer.Central,
            CourtOfficeLayer.City,
            CourtOfficeLayer.Military,
            CourtOfficeLayer.Censor,
            CourtOfficeLayer.Feudatory
        };

        public static CustomCourtOffice CloneOffice(CustomCourtOffice office)
        {
            if (office == null) return null;
            return new CustomCourtOffice
            {
                Id = office.Id ?? string.Empty,
                Name = new CustomCourtLocalizedText
                {
                    Chinese = office.Name?.Chinese ?? string.Empty,
                    English = office.Name?.English ?? string.Empty
                },
                Layer = office.Layer ?? string.Empty,
                Grade = office.Grade,
                Slots = office.Slots,
                MilitaryCapable = office.MilitaryCapable,
                PreferredSchoolId = office.PreferredSchoolId ?? string.Empty,
                Layout = new CustomCourtOfficeLayout
                {
                    X = office.Layout?.X ?? 0f,
                    Y = office.Layout?.Y ?? 0f,
                    Lane = office.Layout?.Lane ?? 0
                },
                Requirements = new CustomCourtOfficeRequirement
                {
                    MinimumRank = office.Requirements?.MinimumRank ?? 0,
                    RequiredSchoolId = office.Requirements?.RequiredSchoolId ??
                        string.Empty,
                    RequiredTraitId = office.Requirements?.RequiredTraitId ??
                        string.Empty,
                    RequiredOfficeId = office.Requirements?.RequiredOfficeId ??
                        string.Empty
                },
                Effects = NormalizeEffects(office.Effects)
            };
        }

        public static void CopyEditableSettings(CustomCourtOffice target,
            CustomCourtOffice draft)
        {
            if (target == null || draft == null) return;
            target.Name = new CustomCourtLocalizedText
            {
                Chinese = draft.Name?.Chinese ?? string.Empty,
                English = draft.Name?.English ?? string.Empty
            };
            target.Layer = draft.Layer ?? CourtOfficeLayer.Central;
            target.Grade = draft.Grade;
            target.Slots = draft.Slots;
            target.MilitaryCapable = draft.MilitaryCapable;
            target.PreferredSchoolId = draft.PreferredSchoolId ?? string.Empty;
            target.Requirements = new CustomCourtOfficeRequirement
            {
                MinimumRank = draft.Requirements?.MinimumRank ?? 0,
                RequiredSchoolId = draft.Requirements?.RequiredSchoolId ??
                    string.Empty,
                RequiredTraitId = draft.Requirements?.RequiredTraitId ??
                    string.Empty,
                RequiredOfficeId = draft.Requirements?.RequiredOfficeId ??
                    string.Empty
            };
            target.Effects = NormalizeEffects(draft.Effects);
        }

        public static List<CustomCourtOfficeEffect> NormalizeEffects(
            IEnumerable<CustomCourtOfficeEffect> effects)
        {
            var byId = new Dictionary<CustomCourtEffectId,
                CustomCourtOfficeEffect>();
            foreach (CustomCourtOfficeEffect effect in effects ??
                     Array.Empty<CustomCourtOfficeEffect>())
            {
                if (effect == null || !Enum.IsDefined(
                        typeof(CustomCourtEffectId), effect.Id)) continue;
                byId[effect.Id] = new CustomCourtOfficeEffect
                {
                    Id = effect.Id,
                    Mode = effect.Mode,
                    Scope = effect.Scope,
                    Value = CustomCourtTemplateRules.ClampEffectValue(
                        effect.Mode, effect.Value)
                };
            }
            return byId.OrderBy(pair => pair.Key)
                .Select(pair => pair.Value).ToList();
        }

        public static IReadOnlyList<CustomCourtEffectScope> AllowedScopes(
            CustomCourtEffectId id)
        {
            return Enum.GetValues(typeof(CustomCourtEffectScope))
                .Cast<CustomCourtEffectScope>()
                .Where(scope => CustomCourtEffectRules.CanApplyToScope(id,
                    scope)).ToArray();
        }

        public static string NextLayer(string current)
        {
            return NextValue(Layers, current);
        }

        public static CustomCourtEffectMode NextMode(
            CustomCourtEffectMode current)
        {
            CustomCourtEffectMode[] values =
                (CustomCourtEffectMode[])Enum.GetValues(
                    typeof(CustomCourtEffectMode));
            return NextValue(values, current);
        }

        public static CustomCourtEffectScope NextScope(CustomCourtEffectId id,
            CustomCourtEffectScope current)
        {
            CustomCourtEffectScope[] values = AllowedScopes(id).ToArray();
            return values.Length == 0
                ? CustomCourtEffectScope.Kingdom
                : NextValue(values, current);
        }

        public static CustomCourtTemplateValidationError ValidateDraft(
            CustomCourtOffice office)
        {
            return CustomCourtTemplateRules.ValidateOffice(office,
                new HashSet<string>(StringComparer.Ordinal));
        }

        private static T NextValue<T>(IReadOnlyList<T> values, T current)
        {
            if (values == null || values.Count == 0) return current;
            int index = -1;
            for (int i = 0; i < values.Count; i++)
                if (EqualityComparer<T>.Default.Equals(values[i], current))
                {
                    index = i;
                    break;
                }
            return values[(index + 1) % values.Count];
        }
    }
}
