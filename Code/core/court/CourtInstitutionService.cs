using AncientWarfare3.content.policies;
using AncientWarfare3.core.lineage;
using AncientWarfare3.core.policy;
using AncientWarfare3.ui;

namespace AncientWarfare3.core.court
{
    internal static class CourtInstitutionService
    {
        public static string GetInstitution(Kingdom pKingdom)
        {
            ICourtProfile profile = CourtProfileRegistry.For(pKingdom);
            if (pKingdom?.data == null || profile == null)
                return CourtInstitutionId.Zhou;
            pKingdom.data.get(LineageKeys.COURT_INSTITUTION,
                out string institution, "");
            if (CourtInstitutionRules.IsKnown(institution) &&
                InstitutionMatchesProfile(profile, institution) &&
                (profile.Id != CourtProfileId.Western ||
                 CourtInstitutionRules.IsCanonicalWestern(institution) &&
                 institution != CourtInstitutionId.WesternPrimitive))
                return institution;
            return Refresh(pKingdom, pRecordHistory: false);
        }

        public static string Refresh(Kingdom pKingdom, bool pRecordHistory)
        {
            ICourtProfile profile = CourtProfileRegistry.For(pKingdom);
            if (pKingdom?.data == null || profile == null)
                return CourtInstitutionId.Zhou;
            pKingdom.data.get(LineageKeys.COURT_INSTITUTION,
                out string previous, "");
            if (!CourtInstitutionRules.IsKnown(previous) ||
                !InstitutionMatchesProfile(profile, previous))
            {
                pKingdom.data.get(LineageKeys.COURT_TIER,
                    out string previousTier, CourtTier.EasternZhou);
                previous = CourtInstitutionRules.InstitutionForTier(previousTier);
            }

            string next;
            if (profile.Id == CourtProfileId.Western)
            {
                KingdomPolicyEffects effects =
                    KingdomPolicyEffectService.Read(pKingdom);
                string migrated = CourtInstitutionRules.MigrateWesternLegacy(
                    previous, effects.FeudalRetainersUnlocked);
                bool officeSystemUnlocked = effects.WesternCourtUnlocked ||
                    migrated != CourtInstitutionId.WesternPrimitive;
                bool advancedUnlocked = effects.FeudalRetainersUnlocked ||
                    migrated ==
                    CourtInstitutionId.WesternFeudalBureaucratic;
                next = profile.ResolveInstitution(officeSystemUnlocked,
                    advancedUnlocked);
            }
            else
            {
                next = CourtInstitutionRules.Resolve(
                    HasTech(pKingdom, "aw_tech_official_court"),
                    HasTech(pKingdom, "aw_tech_three_departments"),
                    HasTech(pKingdom, "aw_tech_song_court"));
            }
            pKingdom.data.set(LineageKeys.COURT_INSTITUTION, next);
            pKingdom.data.set(LineageKeys.COURT_TIER,
                CourtInstitutionRules.TierForInstitution(next));
            if (pRecordHistory && previous != next)
                ChronicleEvents.OnCourtInstitutionReformed(
                    pKingdom, previous, next);
            return next;
        }

        public static string InstitutionName(Kingdom pKingdom)
        {
            return InstitutionName(GetInstitution(pKingdom));
        }

        public static string InstitutionName(string pInstitution)
        {
            string institution = CourtInstitutionRules.IsKnown(pInstitution)
                ? pInstitution
                : CourtInstitutionId.Zhou;
            return AW_L10n.Text(
                CourtInstitutionRules.InstitutionLocalizationKey(institution),
                institution);
        }

        public static string EffectSummary(Kingdom pKingdom)
        {
            string institution = GetInstitution(pKingdom);
            string fallback = institution switch
            {
                CourtInstitutionId.Han =>
                    "Unrest -4 · Manpower +15% · Slots +10% · Direct autonomy cap -10",
                CourtInstitutionId.Tang =>
                    "Cross-culture opinion +10 · Slots +12% · Domestic spread +10%",
                CourtInstitutionId.WesternPrimitive =>
                    "No formal office system",
                CourtInstitutionId.WesternBureaucratic =>
                    "Standing royal and local offices",
                CourtInstitutionId.WesternFeudalBureaucratic =>
                    "Expanded central, military, and territorial offices",
                CourtInstitutionId.Song =>
                    "Policy +15% · Tech/spread +20% · Tax +10% · Direct tribute +10 · Slots -10%",
                _ => "Vassal soft cap 8 · Feudatory maintenance loyalty +1"
            };
            return AW_L10n.Text("aw_court_institution_effect_" +
                                institution, fallback);
        }

        public static string OfficeName(Kingdom pKingdom, string pOfficeId)
        {
            string office = CourtInstitutionRules.DisplayOfficeId(pOfficeId);
            string customName = CustomCourtRuntime.OfficeDisplayName(
                pKingdom, office);
            if (!string.IsNullOrWhiteSpace(customName)) return customName;
            string fallback = AW_L10n.Text("aw_court_office_" + office,
                office);
            CourtOfficeDefinition definition =
                CourtProfileRegistry.FindOffice(pKingdom, office);
            if (definition != null)
                return AW_L10n.Text(definition.LocalizationKey, fallback);
            return AW_L10n.Text(
                CourtInstitutionRules.OfficeLocalizationKey(
                    GetInstitution(pKingdom), office), fallback);
        }

        private static bool HasTech(Kingdom pKingdom, string pTechId)
        {
            return KingdomPolicyService.IsCompleted(pKingdom,
                PolicyNodeKind.Tech, pTechId);
        }

        private static bool InstitutionMatchesProfile(ICourtProfile pProfile,
            string pInstitution)
        {
            bool western = pInstitution.StartsWith("western_",
                System.StringComparison.Ordinal);
            return pProfile.Id == CourtProfileId.Western
                ? western
                : !western;
        }
    }
}
