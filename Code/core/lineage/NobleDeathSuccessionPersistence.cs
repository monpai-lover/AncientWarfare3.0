using System;
using System.Data.SQLite;

namespace AncientWarfare3.core.lineage
{
    internal sealed class NobleDeathSuccessionCommittedGrant
    {
        public long GrantId;
        public long KingdomId;
        public int Rank;
        public NobleTitleStyle Style;
        public string TitleName = "";
        public bool Active;
    }

    internal static class NobleDeathSuccessionPersistence
    {
        private const string InheritanceReason = "eldest_son_inheritance";

        public static bool TryReadCommittedInheritance(SQLiteConnection pDb,
            string pTable, long predecessorGrantId,
            long expectedSuccessorActorId, long expectedHolderActorId,
            long expectedKingdomId, int expectedRank,
            NobleTitleStyle expectedStyle,
            out NobleDeathSuccessionCommittedGrant pGrant,
            SQLiteTransaction pTransaction = null)
        {
            pGrant = null;
            if (pDb == null || !IsIdentifier(pTable) ||
                predecessorGrantId < 0L || expectedSuccessorActorId < 0L ||
                expectedHolderActorId < 0L || expectedKingdomId < 0L ||
                expectedRank <= NobleRankRules.RankNone)
                return false;
            using var command = new SQLiteCommand(pDb)
                { Transaction = pTransaction };
            command.CommandText = "SELECT GRANT_ID,KINGDOM_ID,NOBLE_RANK," +
                "TITLE_STYLE,TITLE_NAME,ACTIVE FROM " + pTable +
                " WHERE PREDECESSOR_GRANT_ID=@predecessor" +
                " AND ACTOR_ID=@successor" +
                " AND INHERITED_FROM_ACTOR_ID=@holder" +
                " AND KINGDOM_ID=@kingdom AND NOBLE_RANK=@rank" +
                " AND TITLE_STYLE=@style AND GRANT_REASON=@reason" +
                " ORDER BY GRANT_ID DESC LIMIT 1";
            command.Parameters.AddWithValue("@predecessor", predecessorGrantId);
            command.Parameters.AddWithValue("@successor", expectedSuccessorActorId);
            command.Parameters.AddWithValue("@holder", expectedHolderActorId);
            command.Parameters.AddWithValue("@kingdom", expectedKingdomId);
            command.Parameters.AddWithValue("@rank", expectedRank);
            command.Parameters.AddWithValue("@style", StyleId(expectedStyle));
            command.Parameters.AddWithValue("@reason", InheritanceReason);
            using SQLiteDataReader reader = command.ExecuteReader();
            if (!reader.Read()) return false;
            pGrant = new NobleDeathSuccessionCommittedGrant
            {
                GrantId = Convert.ToInt64(reader.GetValue(0)),
                KingdomId = Convert.ToInt64(reader.GetValue(1)),
                Rank = Convert.ToInt32(reader.GetValue(2)),
                Style = expectedStyle,
                TitleName = reader.IsDBNull(4)
                    ? ""
                    : Convert.ToString(reader.GetValue(4)),
                Active = !reader.IsDBNull(5) &&
                         Convert.ToInt32(reader.GetValue(5)) == 1
            };
            return true;
        }

        private static string StyleId(NobleTitleStyle pStyle)
        {
            return pStyle switch
            {
                NobleTitleStyle.Male => "male",
                NobleTitleStyle.Princess => "princess",
                NobleTitleStyle.SeniorPrincess => "senior_princess",
                NobleTitleStyle.GrandPrincess => "grand_princess",
                _ => ""
            };
        }

        private static bool IsIdentifier(string pValue)
        {
            if (string.IsNullOrWhiteSpace(pValue)) return false;
            for (int i = 0; i < pValue.Length; i++)
            {
                char value = pValue[i];
                if (char.IsLetterOrDigit(value) || value == '_') continue;
                return false;
            }
            return true;
        }
    }
}
