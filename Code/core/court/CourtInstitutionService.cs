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
            if (pKingdom?.data == null) return CourtInstitutionId.Zhou;
            pKingdom.data.get(LineageKeys.COURT_INSTITUTION,
                out string institution, "");
            if (CourtInstitutionRules.IsKnown(institution)) return institution;
            return Refresh(pKingdom, pRecordHistory: false);
        }

        public static string Refresh(Kingdom pKingdom, bool pRecordHistory)
        {
            if (pKingdom?.data == null) return CourtInstitutionId.Zhou;
            pKingdom.data.get(LineageKeys.COURT_INSTITUTION,
                out string previous, "");
            if (!CourtInstitutionRules.IsKnown(previous))
            {
                pKingdom.data.get(LineageKeys.COURT_TIER,
                    out string previousTier, CourtTier.EasternZhou);
                previous = CourtInstitutionRules.InstitutionForTier(previousTier);
            }

            string next = CourtInstitutionRules.Resolve(
                HasTech(pKingdom, "aw_tech_official_court"),
                HasTech(pKingdom, "aw_tech_three_departments"),
                HasTech(pKingdom, "aw_tech_song_court"));
            pKingdom.data.set(LineageKeys.COURT_INSTITUTION, next);
            pKingdom.data.set(LineageKeys.COURT_TIER,
                CourtInstitutionRules.TierForInstitution(next));
            if (pRecordHistory &&
                CourtInstitutionRules.IsUpgrade(previous, next))
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
                CourtInstitutionId.Song =>
                    "Policy +15% · Tech/spread +20% · Tax +10% · Direct tribute +10 · Slots -10%",
                _ => "Vassal soft cap 8 · Feudatory maintenance loyalty +1"
            };
            return AW_L10n.Text("aw_court_institution_effect_" +
                                institution, fallback);
        }

        public static string OfficeName(Kingdom pKingdom, string pOfficeId)
        {
            string office = pOfficeId ?? "";
            string fallback = AW_L10n.Text("aw_court_office_" + office,
                office);
            return AW_L10n.Text(
                CourtInstitutionRules.OfficeLocalizationKey(
                    GetInstitution(pKingdom), office), fallback);
        }

        private static bool HasTech(Kingdom pKingdom, string pTechId)
        {
            return KingdomPolicyService.IsCompleted(pKingdom,
                PolicyNodeKind.Tech, pTechId);
        }
    }
}
