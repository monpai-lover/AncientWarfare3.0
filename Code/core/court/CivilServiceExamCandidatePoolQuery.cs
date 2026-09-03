using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CivilServiceExamCandidatePoolQuery
    {
        private static readonly string[] IneligibleCentralOfficeIds =
        {
            CourtOfficeId.TaiZai,
            CourtOfficeId.SiTu,
            CourtOfficeId.ZongBo,
            CourtOfficeId.SiMa,
            CourtOfficeId.SiKou,
            CourtOfficeId.SiKong,
            CourtOfficeId.Chancellor,
            CourtOfficeId.Marshal,
            CourtOfficeId.Censor,
            CourtOfficeId.Zhongshu,
            CourtOfficeId.Menxia,
            CourtOfficeId.Shangshu,
            CourtOfficeId.Justice,
            CourtOfficeId.Steward,
            CourtOfficeId.Erudite,
            CourtOfficeId.Libu,
            CourtOfficeId.Hubu,
            CourtOfficeId.Ribu,
            CourtOfficeId.Bingbu,
            CourtOfficeId.Xingbu,
            CourtOfficeId.Gongbu
        };

        public static List<long> LoadLocal(SQLiteConnection pDb,
            string pMembershipTable, string pArchiveTable,
            string pOfficerTable, string pCareerTable,
            string pInstitutionTable, string pCandidateTable,
            string pSessionTable, long pKingdomId, string pSocialOrigin,
            int pYear, bool pTributeMode, int pLimit)
        {
            var result = new List<long>();
            if (pDb == null || pKingdomId < 0L || pLimit <= 0 ||
                Missing(pMembershipTable, pArchiveTable, pOfficerTable,
                    pCareerTable, pInstitutionTable, pCandidateTable,
                    pSessionTable)) return result;

            // 门第不再由 SQL 判定。旧的 CASE 表达式只看 STATUS/EVER_NOBLE_BLOOD/
            // LINEAGE_ID，与 SocialStandingService 的宗族口径完全不同 —— 名单按
            // 旧口径分桶取人、界面按新口径显示，两边对不上。这里退化为「不按门第
            // 过滤」，由调用方取回全池后用同一个分类器分桶。
            bool filterByOrigin = !string.IsNullOrEmpty(pSocialOrigin);
            string originClause = filterByOrigin
                ? "AND CASE WHEN A.STATUS='noble' THEN 'noble' " +
                  "WHEN A.EVER_NOBLE_BLOOD=1 OR A.LINEAGE_ID>=0 " +
                  "THEN 'declined_noble' ELSE 'commoner' END=@socialOrigin "
                : "";

            try
            {
                using var command = new SQLiteCommand(pDb);
                var ineligibleOfficeParameters = new List<string>(
                    IneligibleCentralOfficeIds.Length);
                for (int index = 0;
                     index < IneligibleCentralOfficeIds.Length; index++)
                {
                    string parameter = "@ineligibleOffice" + index;
                    ineligibleOfficeParameters.Add(parameter);
                    command.Parameters.AddWithValue(parameter,
                        IneligibleCentralOfficeIds[index]);
                }
                command.CommandText =
                    "SELECT M.ACTOR_ID FROM " + pMembershipTable + " M " +
                    "JOIN " + pArchiveTable + " A ON A.ID=M.ACTOR_ID " +
                    "LEFT JOIN " + pOfficerTable + " O ON " +
                    "O.ACTOR_ID=M.ACTOR_ID AND O.KINGDOM_ID=@kingdom " +
                    "AND O.ACTIVE=1 LEFT JOIN " + pCareerTable + " C ON " +
                    "C.ACTOR_ID=M.ACTOR_ID AND C.KINGDOM_ID=@kingdom " +
                    "LEFT JOIN " + pInstitutionTable + " I ON " +
                    "I.SCHOOL_ID=M.SCHOOL_ID AND I.ACTIVE=1 " +
                    "WHERE M.ACTIVE=1 AND M.START_YEAR<@year AND " +
                    "A.IS_ALIVE=1 AND A.SEX=0 AND " +
                    "IFNULL(A.STATUS,'')<>@slave AND " +
                    "A.KINGDOM_ID=@kingdom " + originClause +
                    "AND NOT EXISTS (SELECT 1 FROM " +
                    pOfficerTable + " SO WHERE SO.ACTOR_ID=M.ACTOR_ID " +
                    "AND SO.KINGDOM_ID=@kingdom AND SO.ACTIVE=1 AND " +
                    "SO.LAYER=@centralLayer AND SO.OFFICE_ID IN (" +
                    string.Join(",", ineligibleOfficeParameters) + ")) " +
                    "AND NOT EXISTS (SELECT 1 FROM " +
                    pCandidateTable + " E JOIN " + pSessionTable +
                    " S ON S.ID=E.SESSION_ID WHERE E.ACTOR_ID=M.ACTOR_ID " +
                    "AND E.KINGDOM_ID=@kingdom AND S.STATUS='completed' " +
                    "AND (E.QUALIFICATION='jinshi' OR " +
                    "(@tribute=1 AND E.QUALIFICATION='gongshi'))) " +
                    "GROUP BY M.ACTOR_ID ORDER BY COALESCE((SELECT " +
                    "MAX(SR.CYCLE_YEAR) FROM " + pCandidateTable +
                    " ER JOIN " + pSessionTable +
                    " SR ON SR.ID=ER.SESSION_ID WHERE " +
                    "ER.ACTOR_ID=M.ACTOR_ID AND ER.KINGDOM_ID=@kingdom " +
                    "AND SR.STATUS='completed'),-1),MIN(M.START_YEAR)," +
                    "CASE WHEN MAX(CASE WHEN O.ACTOR_ID IS NOT NULL THEN 1 " +
                    "ELSE 0 END)=1 THEN 0 WHEN MAX(CASE WHEN C.ACTOR_ID " +
                    "IS NOT NULL THEN 1 ELSE 0 END)=1 THEN 1 WHEN " +
                    "MAX(CASE WHEN I.INSTITUTION_ID IS NOT NULL THEN 1 " +
                    "ELSE 0 END)=1 THEN 2 ELSE 3 END,M.ACTOR_ID " +
                    "LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@year", pYear);
                command.Parameters.AddWithValue("@slave",
                    LineageStatus.SLAVE);
                if (filterByOrigin)
                    command.Parameters.AddWithValue("@socialOrigin",
                        pSocialOrigin);
                command.Parameters.AddWithValue("@centralLayer",
                    CourtOfficeLayer.Central);
                command.Parameters.AddWithValue("@tribute",
                    pTributeMode ? 1 : 0);
                command.Parameters.AddWithValue("@limit", pLimit);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read() && result.Count < pLimit)
                    result.Add(Convert.ToInt64(reader.GetValue(0)));
            }
            catch
            {
                result.Clear();
            }
            return result;
        }

        public static List<long> LoadForeignResidents(SQLiteConnection pDb,
            string pMembershipTable, string pArchiveTable,
            string pAffiliationTable, string pOfficerTable,
            string pCandidateTable, string pSessionTable,
            IReadOnlyList<long> pHostCityIds, long pHostKingdomId,
            int pYear, string pResidentState, bool pTributeMode, int pLimit)
        {
            var result = new List<long>();
            if (pDb == null || pHostCityIds == null ||
                pHostCityIds.Count == 0 || pHostKingdomId < 0L ||
                pLimit <= 0 || Missing(pMembershipTable, pArchiveTable,
                    pAffiliationTable, pOfficerTable, pCandidateTable,
                    pSessionTable)) return result;

            try
            {
                using var command = new SQLiteCommand(pDb);
                var cityParameters = new List<string>(pHostCityIds.Count);
                for (int index = 0; index < pHostCityIds.Count; index++)
                {
                    string parameter = "@city" + index;
                    cityParameters.Add(parameter);
                    command.Parameters.AddWithValue(parameter,
                        pHostCityIds[index]);
                }
                command.CommandText =
                    "SELECT R.ACTOR_ID FROM " + pAffiliationTable + " R " +
                    "JOIN " + pMembershipTable + " M ON M.ACTOR_ID=R.ACTOR_ID " +
                    "JOIN " + pArchiveTable + " A ON A.ID=R.ACTOR_ID " +
                    "WHERE M.ACTIVE=1 AND M.START_YEAR<@year AND " +
                    "A.IS_ALIVE=1 AND A.SEX=0 AND " +
                    "R.RESIDENCE_CITY_ID IN (" +
                    string.Join(",", cityParameters) + ") AND " +
                    "R.LIFECYCLE_STATE=@resident AND " +
                    "R.SERVICE_KINGDOM_ID<0 AND " +
                    "R.HOME_KINGDOM_ID<>@kingdom AND NOT EXISTS " +
                    "(SELECT 1 FROM " + pOfficerTable + " O WHERE " +
                    "O.ACTOR_ID=R.ACTOR_ID AND O.ACTIVE=1) AND NOT EXISTS " +
                    "(SELECT 1 FROM " + pCandidateTable + " E JOIN " +
                    pSessionTable + " S ON S.ID=E.SESSION_ID WHERE " +
                    "E.ACTOR_ID=R.ACTOR_ID AND E.KINGDOM_ID=@kingdom AND " +
                    "S.STATUS='completed' AND (E.QUALIFICATION='jinshi' " +
                    "OR (@tribute=1 AND E.QUALIFICATION='gongshi'))) " +
                    "GROUP BY R.ACTOR_ID ORDER BY COALESCE((SELECT " +
                    "MAX(SR.CYCLE_YEAR) FROM " + pCandidateTable +
                    " ER JOIN " + pSessionTable +
                    " SR ON SR.ID=ER.SESSION_ID WHERE " +
                    "ER.ACTOR_ID=R.ACTOR_ID AND ER.KINGDOM_ID=@kingdom " +
                    "AND SR.STATUS='completed'),-1),MIN(M.START_YEAR)," +
                    "MAX(M.REPUTATION) DESC,R.ACTOR_ID LIMIT @limit";
                command.Parameters.AddWithValue("@year", pYear);
                command.Parameters.AddWithValue("@resident",
                    pResidentState ?? "");
                command.Parameters.AddWithValue("@kingdom", pHostKingdomId);
                command.Parameters.AddWithValue("@tribute",
                    pTributeMode ? 1 : 0);
                command.Parameters.AddWithValue("@limit", pLimit);
                using SQLiteDataReader reader = command.ExecuteReader();
                while (reader.Read() && result.Count < pLimit)
                    result.Add(Convert.ToInt64(reader.GetValue(0)));
            }
            catch
            {
                result.Clear();
            }
            return result;
        }

        private static bool Missing(params string[] pTableNames)
        {
            if (pTableNames == null || pTableNames.Length == 0) return true;
            for (int index = 0; index < pTableNames.Length; index++)
                if (string.IsNullOrWhiteSpace(pTableNames[index])) return true;
            return false;
        }
    }
}
