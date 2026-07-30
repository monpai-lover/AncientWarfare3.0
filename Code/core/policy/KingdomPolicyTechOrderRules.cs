using System;

namespace AncientWarfare3.core.policy
{
    public static class KingdomPolicyTechOrderRules
    {
        private static readonly string[] Order =
        {
            "aw_tech_writing",
            "aw_tech_pottery_casting",
            "aw_tech_bronze_casting",
            "aw_tech_well_field_survey",
            "aw_tech_iron_plow",
            "aw_tech_chariot_training",
            "aw_tech_enfeoffment_study",
            "aw_tech_granary_accounting",
            "aw_tech_city_defense",
            "aw_tech_official_court",
            "aw_tech_rites_music",
            "aw_tech_nine_rank_system",
            "aw_tech_civil_service_examination",
            "aw_tech_three_departments",
            "aw_tech_song_court"
        };

        public static int Count => Order.Length;

        public static bool Contains(string pId)
        {
            return Array.IndexOf(Order, pId) >= 0;
        }

        public static bool CanConsider(string pId, bool pOfficialCourtCompleted,
            bool pRitesMusicCompleted, bool pNineRankCompleted)
        {
            if (pId == "aw_tech_rites_music") return pOfficialCourtCompleted;
            if (pId == "aw_tech_civil_service_examination") return pNineRankCompleted;
            if (pId == "aw_tech_three_departments") return pNineRankCompleted;
            return true;
        }

        public static int CivilServiceExaminationContextScore(
            int pCentralVacancies, int pEducatedWithoutQualification,
            int pCityCount, bool pImperial)
        {
            int vacancyPressure = Math.Min(120,
                Math.Max(0, pCentralVacancies) * 24);
            int candidatePressure = Math.Min(100,
                Math.Max(0, pEducatedWithoutQualification) * 5);
            int realmScale = Math.Min(60,
                Math.Max(0, pCityCount - 1) * 10);
            return vacancyPressure + candidatePressure + realmScale +
                   (pImperial ? 60 : 0);
        }

        public static int PreferredIndex(string pId, int pLayoutFallback)
        {
            int index = Array.IndexOf(Order, pId);
            return index >= 0 ? index : Order.Length + Math.Max(0, pLayoutFallback);
        }
    }
}
