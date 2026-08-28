namespace AncientWarfare3.core.court
{
    public static class OfficialCareerBiographyRules
    {
        public static string NormalizeLegacyLocalizationKeys(string pText,
            System.Func<int, string> pRankResolver,
            System.Func<string, string> pHistoryResolver)
        {
            string result = pText ?? "";
            if (pRankResolver != null)
            {
                for (int rank = OfficialCareerRankRules.MaximumRank;
                     rank >= OfficialCareerRankRules.Unranked; rank--)
                {
                    result = result.Replace(
                        OfficialCareerRankRules.RankNameKey(rank),
                        pRankResolver(rank) ?? "");
                }
            }

            if (pHistoryResolver == null) return result;
            string[] historyKeys =
            {
                "aw_hist_official_rank_grant_mid",
                "aw_hist_official_rank_grant_suffix"
            };
            foreach (string key in historyKeys)
                result = result.Replace(key, pHistoryResolver(key) ?? "");
            return result;
        }

        public static bool IsCareerEvent(string pEventType)
        {
            switch (pEventType ?? "")
            {
                case "court_officer_appointed":
                case "court_officer_dismissed":
                case "official_evaluation":
                case "official_appointment_edict":
                case "official_rank_promoted":
                case "official_transferred":
                case "civil_service_qualified":
                case "civil_service_top_ranked":
                case "civil_service_first_appointment":
                    return true;
                default:
                    return false;
            }
        }

        public static bool ShouldRecordRankAdvance(bool hasNineRankSystem,
            bool persistenceCommitted, int previousRank, int nextRank)
        {
            if (!hasNineRankSystem || !persistenceCommitted) return false;
            int previous = OfficialCareerRankRules.ClampRank(previousRank);
            int next = OfficialCareerRankRules.ClampRank(nextRank);
            return next > OfficialCareerRankRules.Unranked && next > previous;
        }

        public static bool ShouldRecordFirstFormalAppointment(
            bool hasExaminationSystem, bool appointmentCommitted,
            bool createdAppointmentEvent, bool actingAppointment,
            bool hasQualification, bool alreadyRecorded)
        {
            return hasExaminationSystem && appointmentCommitted &&
                   createdAppointmentEvent && !actingAppointment &&
                   hasQualification && !alreadyRecorded;
        }
    }
}
