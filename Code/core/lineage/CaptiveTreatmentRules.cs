using AncientWarfare3.core.court;

namespace AncientWarfare3.core.lineage
{
    public enum CaptiveTreatmentAction
    {
        KeepAsSlave,
        SettleAsNobleDependent,
        ExecuteCaptive
    }

    public static class CaptiveTreatmentRules
    {
        public static CaptiveTreatmentAction Decide(string pDominantSchool, bool wasKing, bool wasLeader,
            bool captorAtWar, float hostilePowerRatio)
        {
            if (!wasKing && !wasLeader) return CaptiveTreatmentAction.KeepAsSlave;

            switch (pDominantSchool ?? "")
            {
                case CourtSchoolId.Legalist:
                    if (wasKing) return CaptiveTreatmentAction.ExecuteCaptive;
                    return captorAtWar || hostilePowerRatio >= 0.75f
                        ? CaptiveTreatmentAction.ExecuteCaptive
                        : CaptiveTreatmentAction.SettleAsNobleDependent;

                case CourtSchoolId.Military:
                    return captorAtWar
                        ? CaptiveTreatmentAction.ExecuteCaptive
                        : CaptiveTreatmentAction.SettleAsNobleDependent;

                case CourtSchoolId.Logician:
                    return hostilePowerRatio >= 1.5f || (captorAtWar && hostilePowerRatio >= 1.0f)
                        ? CaptiveTreatmentAction.ExecuteCaptive
                        : CaptiveTreatmentAction.SettleAsNobleDependent;

                case CourtSchoolId.YinYang:
                    return wasKing && hostilePowerRatio >= 2.0f
                        ? CaptiveTreatmentAction.ExecuteCaptive
                        : CaptiveTreatmentAction.SettleAsNobleDependent;

                case CourtSchoolId.Ru:
                case CourtSchoolId.Mohist:
                case CourtSchoolId.Dao:
                case CourtSchoolId.Diplomat:
                case CourtSchoolId.Agrarian:
                case CourtSchoolId.PrimitiveMinister:
                case CourtSchoolId.None:
                default:
                    return CaptiveTreatmentAction.SettleAsNobleDependent;
            }
        }

        public static string SchoolLabel(string pDominantSchool)
        {
            switch (pDominantSchool ?? "")
            {
                case CourtSchoolId.Ru: return HistoryLocalizationRules.Text("aw_court_school_ru");
                case CourtSchoolId.Legalist: return HistoryLocalizationRules.Text("aw_court_school_fa");
                case CourtSchoolId.Dao: return HistoryLocalizationRules.Text("aw_court_school_dao");
                case CourtSchoolId.Mohist: return HistoryLocalizationRules.Text("aw_court_school_mo");
                case CourtSchoolId.Military: return HistoryLocalizationRules.Text("aw_court_school_bing");
                case CourtSchoolId.Diplomat: return HistoryLocalizationRules.Text("aw_court_school_zongheng");
                case CourtSchoolId.Agrarian: return HistoryLocalizationRules.Text("aw_court_school_nong");
                case CourtSchoolId.YinYang: return HistoryLocalizationRules.Text("aw_court_school_yinyang");
                case CourtSchoolId.Logician: return HistoryLocalizationRules.Text("aw_court_school_ming");
                case CourtSchoolId.PrimitiveMinister: return HistoryLocalizationRules.Text("aw_court_school_primitive");
                default: return HistoryLocalizationRules.Text("aw_court_school_none");
            }
        }
    }
}
