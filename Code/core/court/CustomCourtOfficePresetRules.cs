namespace AncientWarfare3.core.court
{
    public static class CustomCourtOfficePresetRules
    {
        public static bool CanApply(CourtOfficeDefinition definition,
            string institutionId)
        {
            return definition != null &&
                definition.AvailableIn(institutionId);
        }

        public static void ApplyDefinition(CustomCourtOffice target,
            CourtOfficeDefinition definition,
            CustomCourtLocalizedText displayName)
        {
            if (target == null || definition == null) return;
            target.Name = new CustomCourtLocalizedText
            {
                Chinese = displayName?.Chinese ?? string.Empty,
                English = displayName?.English ?? string.Empty
            };
            target.Layer = definition.Layer;
            target.Grade = definition.Grade;
            target.PreferredSchoolId = definition.PreferredSchoolId;
            target.MilitaryCapable = definition.MilitaryCapable;
        }
    }
}
