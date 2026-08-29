using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AncientWarfare3.core.lineage;

namespace AncientWarfare3.core.court
{
    internal static class CivilServiceFormalCandidateQuery
    {
        /// <summary>
        ///     已有正式功名、当前无官职的候选索引 —— 即「官员候选池」。
        /// </summary>
        /// <param name="pLocalLayer">
        ///     取局部层(城/县)的池而不是中央层的池。两点差别:
        ///     局部层按 <c>LocalOfficialCandidateRules.IsLocalQualification</c>
        ///     也接受举人;并且排除**任何**在任官,而不只是中央在任官。
        ///     两个分支都是固定字面量,不接受调用方传入的 SQL 片段。
        /// </param>
        public static List<long> Load(SQLiteConnection pDb,
            string pCandidateTable, string pSessionTable,
            string pArchiveTable, string pOfficerTable, long pKingdomId,
            int pLimit, bool pLocalLayer = false)
        {
            var result = new List<long>();
            if (pDb == null || pKingdomId < 0L || pLimit <= 0 ||
                string.IsNullOrWhiteSpace(pCandidateTable) ||
                string.IsNullOrWhiteSpace(pSessionTable) ||
                string.IsNullOrWhiteSpace(pArchiveTable) ||
                string.IsNullOrWhiteSpace(pOfficerTable)) return result;

            string qualifications = pLocalLayer
                ? "('juren','gongshi','jinshi')"
                : "('gongshi','jinshi')";
            string officerLayer = pLocalLayer ? "" : "AND O.LAYER='central' ";
            try
            {
                using var command = new SQLiteCommand(pDb);
                command.CommandText =
                    "SELECT C.ACTOR_ID FROM " + pCandidateTable + " C JOIN " +
                    pSessionTable + " S ON S.ID=C.SESSION_ID JOIN " +
                    pArchiveTable + " A ON A.ID=C.ACTOR_ID " +
                    "WHERE S.KINGDOM_ID=@kingdom AND C.KINGDOM_ID=@kingdom " +
                    "AND C.QUALIFICATION IN " + qualifications + " " +
                    "AND S.STATUS='completed' " +
                    "AND A.IS_ALIVE=1 AND A.SEX=0 " +
                    "AND IFNULL(A.STATUS,'')<>@slave " +
                    "AND A.KINGDOM_ID=@kingdom AND NOT EXISTS (SELECT 1 FROM " +
                    pOfficerTable + " O WHERE O.ACTOR_ID=C.ACTOR_ID " +
                    "AND O.ACTIVE=1 " + officerLayer + ") " +
                    "AND NOT EXISTS (SELECT 1 FROM " + pCandidateTable +
                    " C2 JOIN " + pSessionTable +
                    " S2 ON S2.ID=C2.SESSION_ID " +
                    "WHERE C2.ACTOR_ID=C.ACTOR_ID " +
                    "AND C2.KINGDOM_ID=C.KINGDOM_ID " +
                    "AND C2.QUALIFICATION IN " + qualifications + " " +
                    "AND S2.STATUS='completed' " +
                    "AND (S2.CYCLE_YEAR>S.CYCLE_YEAR OR " +
                    "(S2.CYCLE_YEAR=S.CYCLE_YEAR AND C2.ID>C.ID))) " +
                    "ORDER BY S.CYCLE_YEAR DESC,C.ENTRY_BONUS DESC,C.ID DESC " +
                    "LIMIT @limit";
                command.Parameters.AddWithValue("@kingdom", pKingdomId);
                command.Parameters.AddWithValue("@slave",
                    LineageStatus.SLAVE);
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
    }
}
